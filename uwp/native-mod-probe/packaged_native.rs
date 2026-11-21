//! Load typed native mods from a same-publisher executable optional package.
use horizon_mod_package::{
    mod_list::{ModList, NATIVE_DIR},
    packages::{self, AdjacentManifest},
    runtime::Runtime,
};
use mod_api::{
    __private::{
        game::Fields,
        native::{BuiltMod, NativeMod},
    },
    ModEntry, Side,
};
use serde::Deserialize;
use sha2::{Digest, Sha256};
use std::{
    alloc::{self, Layout},
    collections::BTreeSet,
    ffi::{CStr, OsString, c_char, c_void},
    fs,
    io::{self, Read},
    os::windows::ffi::{OsStrExt, OsStringExt},
    path::{Path, PathBuf},
    ptr, slice,
};

#[path = "packaged_abi.rs"]
mod abi;
use abi::Allocator;

const GAME_BUILD_ID: &str = env!("HORIZON_GAME_BUILD_ID");
const RUSTC_VERSION: &str = env!("HORIZON_RUSTC_VERSION");
const MIXINS: &str = "e59da9094f0c2fdda6c627fe9eff22dec6a906f0";
const RECORD: &str = "native-mods.json";
const HEAD: u32 = 0x10;
const DIRECT: u32 = 0x20;
const FILTER_OPTIONAL: u32 = 0x20000;
const PROPERTY_OPTIONAL: u32 = 0x8;
const RELATED_SET: u32 = 0x40000;
const INSUFFICIENT_BUFFER: i32 = 122;
const MAX_RECORD_SIZE: u64 = 64 * 1024;

#[cfg(target_arch = "x86")]
const PROCESSOR_ARCHITECTURE: u32 = 0;
#[cfg(target_arch = "x86_64")]
const PROCESSOR_ARCHITECTURE: u32 = 9;
#[cfg(target_arch = "aarch64")]
const PROCESSOR_ARCHITECTURE: u32 = 12;

// appmodel.h declares PACKAGE_ID and PACKAGE_INFO with four-byte packing.
#[repr(C, packed(4))]
struct PackageId {
    reserved: u32,
    architecture: u32,
    version: u64,
    name: *const u16,
    publisher: *const u16,
    resource: *const u16,
    publisher_id: *const u16,
}

#[repr(C, packed(4))]
struct PackageInfo {
    reserved: u32,
    flags: u32,
    path: *const u16,
    full_name: *const u16,
    family_name: *const u16,
    id: PackageId,
}

unsafe extern "system" {
    fn GetCurrentPackageInfo(flags: u32, length: *mut u32, buffer: *mut u8, count: *mut u32)
    -> i32;
    fn LoadPackagedLibrary(name: *const u16, reserved: u32) -> *mut c_void;
    fn GetModuleFileNameW(module: *mut c_void, name: *mut u16, length: u32) -> u32;
    fn GetProcAddress(module: *mut c_void, name: *const c_char) -> *const c_void;
}

struct Package {
    flags: u32,
    architecture: u32,
    name: OsString,
    publisher_id: OsString,
    path: PathBuf,
}

fn package_graph(flags: u32) -> Result<Vec<Package>, String> {
    let mut length = 0;
    let mut count = 0;
    // The package graph and all pointed-to strings are returned in a single caller-owned buffer.
    let status = unsafe { GetCurrentPackageInfo(flags, &mut length, ptr::null_mut(), &mut count) };
    if status != INSUFFICIENT_BUFFER {
        return Err(format!("GetCurrentPackageInfo query: Win32 {status}"));
    }
    let mut words = vec![0_u64; (length as usize).div_ceil(size_of::<u64>())];
    let buffer = words.as_mut_ptr().cast::<u8>();
    let status = unsafe { GetCurrentPackageInfo(flags, &mut length, buffer, &mut count) };
    if status != 0 {
        return Err(format!("GetCurrentPackageInfo read: Win32 {status}"));
    }
    if length as usize > words.len() * size_of::<u64>()
        || count as usize > length as usize / size_of::<PackageInfo>()
    {
        return Err("invalid package graph buffer".into());
    }
    let rows = unsafe { slice::from_raw_parts(buffer.cast::<PackageInfo>(), count as usize) };
    let string = |value: *const u16| -> Result<OsString, String> {
        let offset = value.addr().wrapping_sub(buffer.addr());
        if offset % 2 != 0 || offset >= length as usize {
            return Err("invalid package graph string address".into());
        }
        let units = unsafe { slice::from_raw_parts(value, (length as usize - offset) / 2) };
        let end = units
            .iter()
            .position(|&unit| unit == 0)
            .ok_or("unterminated package graph string")?;
        Ok(OsString::from_wide(&units[..end]))
    };
    rows.iter()
        .map(|row| {
            Ok(Package {
                flags: row.flags,
                architecture: row.id.architecture,
                name: string(row.id.name)?,
                publisher_id: string(row.family_name)?
                    .into_string()
                    .ok()
                    .and_then(|family| {
                        family
                            .rsplit_once('_')
                            .map(|(_, publisher)| OsString::from(publisher))
                    })
                    .ok_or("invalid package family publisher ID")?,
                path: PathBuf::from(string(row.path)?),
            })
        })
        .collect()
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Record {
    libraries: Vec<Library>,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Library {
    #[serde(rename = "mod")]
    id: String,
    identity: String,
    sha256: String,
}

struct Pending {
    built: BuiltMod,
    identity: String,
    path: PathBuf,
}

pub(crate) struct PackagedNative(Vec<Pending>);

fn read_record(path: &Path) -> Result<Record, String> {
    let file = fs::File::open(path).map_err(|e| format!("{}: {e}", path.display()))?;
    let mut bytes = Vec::new();
    file.take(MAX_RECORD_SIZE + 1)
        .read_to_end(&mut bytes)
        .map_err(|e| format!("{}: {e}", path.display()))?;
    if bytes.len() as u64 > MAX_RECORD_SIZE {
        return Err(format!(
            "{} exceeds {MAX_RECORD_SIZE} bytes",
            path.display()
        ));
    }
    serde_json::from_slice(&bytes).map_err(|e| format!("{}: {e}", path.display()))
}

fn valid_id(id: &str) -> bool {
    (1..=64).contains(&id.len())
        && id
            .bytes()
            .all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'_')
}

fn check_manifest(package: &AdjacentManifest) -> Result<(), String> {
    let m = &package.manifest;
    let n = m.native.as_ref().ok_or("missing native declaration")?;
    if m.api != "0.2"
        || n.game != "26.3"
        || GAME_BUILD_ID.rsplit_once('-').is_none_or(|(_, upstream)| n.horizon != upstream)
        || n.mixins != MIXINS
        || !matches!(m.side, Side::Client | Side::Both)
        || m.version.split_whitespace().count() != 1
        || !n.mixin.is_empty()
        || !n.module.is_empty()
        || !n.bridge.is_empty()
        || !n.state.is_empty()
        || !n.r#loop.is_empty()
        || !m.internals.is_empty()
        || !m.inject.is_empty()
    {
        return Err(format!(
            "{}: expected a typed native mod for the pinned Horizon UWP SDK",
            m.id
        ));
    }
    Ok(())
}

fn verify_hash(path: &Path, expected: &str) -> Result<(), String> {
    let mut file = fs::File::open(path).map_err(|e| format!("{}: {e}", path.display()))?;
    let mut digest = Sha256::new();
    io::copy(&mut file, &mut digest).map_err(|e| format!("{}: {e}", path.display()))?;
    if format!("{:x}", digest.finalize()) != expected {
        return Err(format!("{}: native library hash mismatch", path.display()));
    }
    Ok(())
}

pub(crate) fn prepare(game_dir: &Path) -> Result<(Runtime, PackagedNative), String> {
    let head = package_graph(HEAD)?;
    let [head] = head.as_slice() else {
        return Err("the UWP package graph has no unique main package".into());
    };
    let graph = package_graph(HEAD | DIRECT | FILTER_OPTIONAL)?;
    let mut optional_name = head.name.clone();
    optional_name.push(".NativeMods");
    let mut matches = graph.iter().filter(|row| row.name == optional_name);
    let Some(package) = matches.next() else {
        return Ok((Runtime::default(), PackagedNative(Vec::new())));
    };
    if matches.next().is_some()
        || package.flags & (PROPERTY_OPTIONAL | RELATED_SET) != (PROPERTY_OPTIONAL | RELATED_SET)
        || package.architecture != PROCESSOR_ARCHITECTURE
        || head.architecture != PROCESSOR_ARCHITECTURE
        || package.publisher_id != head.publisher_id
    {
        return Err(
            "native modpack must be the same-publisher, same-architecture executable related set"
                .into(),
        );
    }

    let record = read_record(&package.path.join(RECORD))?;
    let settings = ModList::load(game_dir);
    let mut used = BTreeSet::new();
    let mut built = Vec::with_capacity(record.libraries.len());
    let mut pending = Vec::new();
    for row in record.libraries {
        if !valid_id(&row.id) || !used.insert(row.id.clone()) {
            return Err(format!("invalid or duplicate native mod ID {:?}", row.id));
        }
        let folder = package.path.join(NATIVE_DIR).join(&row.id);
        let manifest = packages::read_native(&folder).map_err(|e| e.to_string())?;
        check_manifest(&manifest)?;
        let m = manifest.manifest;
        let identity = format!("{GAME_BUILD_ID} {} {} {RUSTC_VERSION}", m.id, m.version);
        if m.id != row.id || row.identity != identity {
            return Err(format!(
                "{}: expected native build identity {identity:?}",
                row.id
            ));
        }
        let basename = format!("horizon_uwp_mod_{}.dll", row.id);
        let path = package.path.join(&basename);
        verify_hash(&path, &row.sha256)?;
        for other in graph.iter().filter(|other| other.path != package.path) {
            match other.path.join(&basename).try_exists() {
                Ok(false) => {}
                Ok(true) => {
                    return Err(format!(
                        "{}: native DLL name collides in the package graph",
                        row.id
                    ));
                }
                Err(e) => {
                    return Err(format!(
                        "{}: native DLL collision check failed: {e}",
                        row.id
                    ));
                }
            }
        }
        let disabled = settings.disabled.contains(&m.id);
        let item = BuiltMod {
            id: Box::leak(m.id.into_boxed_str()),
            version: Box::leak(m.version.into_boxed_str()),
            side: m.side,
        };
        built.push(item);
        if !disabled {
            pending.push(Pending {
                built: item,
                identity,
                path,
            });
        }
    }
    let runtime = Runtime::load(
        &package.path.join("HorizonUWP.exe"),
        Box::leak(built.into_boxed_slice()),
    )
    .map_err(|e| e.to_string())?;
    Ok((runtime, PackagedNative(pending)))
}

unsafe extern "C" fn game_alloc(size: usize, align: usize) -> *mut u8 {
    let layout = Layout::from_size_align(size, align).expect("native mod passed a valid layout");
    unsafe { alloc::alloc(layout) }
}

unsafe extern "C" fn game_dealloc(pointer: *mut u8, size: usize, align: usize) {
    let layout = Layout::from_size_align(size, align).expect("native mod passed a valid layout");
    unsafe { alloc::dealloc(pointer, layout) }
}

fn load(pending: &Pending) -> Result<&'static NativeMod, String> {
    let basename = pending.path.file_name().ok_or("missing DLL basename")?;
    let name: Vec<_> = basename.encode_wide().chain(Some(0)).collect();
    // Keep the reference acquired by LoadPackagedLibrary for the process lifetime. Rust
    // vtables and even a refused module's callbacks can outlive this startup function.
    let module = unsafe { LoadPackagedLibrary(name.as_ptr(), 0) };
    if module.is_null() {
        return Err(format!(
            "LoadPackagedLibrary: {}",
            io::Error::last_os_error()
        ));
    }
    let mut actual_path = vec![0_u16; 32_768];
    let length =
        unsafe { GetModuleFileNameW(module, actual_path.as_mut_ptr(), actual_path.len() as u32) };
    if length == 0 || length as usize >= actual_path.len() {
        return Err(format!(
            "GetModuleFileNameW: {}",
            io::Error::last_os_error()
        ));
    }
    let actual = PathBuf::from(OsString::from_wide(&actual_path[..length as usize]));
    if fs::canonicalize(actual).map_err(|e| e.to_string())?
        != fs::canonicalize(&pending.path).map_err(|e| e.to_string())?
    {
        return Err("loaded DLL is outside the authenticated modpack".into());
    }
    let symbol = |name: &CStr| -> Result<*const c_void, String> {
        let address = unsafe { GetProcAddress(module, name.as_ptr()) };
        if address.is_null() {
            return Err(format!(
                "{}: {}",
                name.to_string_lossy(),
                io::Error::last_os_error()
            ));
        }
        Ok(address)
    };
    // The related-set package is intentionally trusted as native code. Export signatures,
    // pinned Rust SDK and allocator are supplied by the corresponding Builder profile.
    unsafe {
        let identity: extern "C" fn() -> *const c_char =
            std::mem::transmute(symbol(c"horizon_native_build")?);
        let value = identity();
        if value.is_null() || CStr::from_ptr(value).to_bytes() != pending.identity.as_bytes() {
            return Err("exported native build identity mismatch".into());
        }
        let init: extern "C" fn(Allocator) -> u32 =
            std::mem::transmute(symbol(c"horizon_uwp_native_init_v1")?);
        let entry: fn() -> &'static NativeMod =
            std::mem::transmute(symbol(c"horizon_native_mod_v1")?);
        if init(Allocator {
            alloc: game_alloc,
            dealloc: game_dealloc,
        }) != 1
        {
            return Err("native allocator bootstrap refused".into());
        }
        let native = entry();
        if native.built != [pending.built] {
            return Err("exported native mod inventory differs from its manifest".into());
        }
        Ok(native)
    }
}

impl PackagedNative {
    pub(crate) fn entries(self) -> Result<Vec<ModEntry>, String> {
        let libraries: Vec<_> = self
            .0
            .iter()
            .map(|pending| load(pending).map_err(|e| format!("{}: {e}", pending.built.id)))
            .collect::<Result<_, _>>()?;
        let mut result = Vec::with_capacity(libraries.len());
        for library in libraries {
            let id = library.built[0].id;
            if !(library.slots)().is_empty() || !(library.installs)().is_empty() {
                return Err(format!(
                    "{id}: packaged typed mods cannot install native patches or slots"
                ));
            }
            let mut entries = (library.entries)();
            if entries.len() != 1 || entries[0].fields().id != id {
                return Err(format!("{id}: invalid exported hook registration"));
            }
            result.append(&mut entries);
            log::info!("Packaged native mod: {id} {}", library.built[0].version);
        }
        Ok(result)
    }
}
