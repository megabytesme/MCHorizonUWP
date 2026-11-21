"""Prepare explicitly trusted, typed-hook native mods in a private Builder snapshot."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import stat
import tomllib


UPSTREAM = "0b14f8acfd97fa2c750e42795249dcc981178ca4"
NOV20_UPSTREAM = "0a875da91dbb0fdcb7ba8c42c11341aa229af51a"
MIXINS = "e59da9094f0c2fdda6c627fe9eff22dec6a906f0"
SDK_API = ".horizon/sdk/api/crates/horizon-mod-api"
TARGETS = {"x64": "x86_64-uwp-windows-msvc", "x86": "i686-uwp-windows-msvc",
           "arm64": "aarch64-uwp-windows-msvc", "arm": "thumbv7a-uwp-windows-msvc"}
FILES = ("Cargo.toml", "horizon-mod.toml", "src/lib.rs")


def plain_file(path):
    meta = path.lstat()
    if not stat.S_ISREG(meta.st_mode) or (
        getattr(meta, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
    ):
        raise ValueError(f"Expected an ordinary file: {path}")
    return path.read_bytes()


def read_source(folder, upstream=UPSTREAM):
    for path in (folder, folder / "src"):
        meta = path.lstat()
        if not stat.S_ISDIR(meta.st_mode) or (
            getattr(meta, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
        ):
            raise ValueError(f"Expected an ordinary source directory: {path}")
    files = {name: plain_file(folder / name) for name in FILES}
    cargo = tomllib.loads(files["Cargo.toml"].decode())
    manifest = tomllib.loads(files["horizon-mod.toml"].decode())
    package = cargo.get("package", {})
    if (set(cargo) != {"package", "dependencies"}
            or set(package) - {"name", "version", "edition", "publish", "description", "license", "authors"}
            or cargo["dependencies"] != {"horizon-mod-api": {"path": SDK_API}}
            or package.get("edition") not in {"2021", "2024"}):
        raise ValueError(f"{folder}: require a standard API-only Cargo.toml with no build scripts, custom targets or other dependencies")
    if {item.name for item in (folder / "src").iterdir()} != {"lib.rs"}:
        raise ValueError(f"{folder}: this prototype accepts one src/lib.rs per project")
    if any((folder / item).exists() for item in ("build.rs", ".cargo", "assets", "data")):
        raise ValueError(f"{folder}: custom build files, data and assets are outside this hook-only build profile")
    name, version = package.get("name"), package.get("version")
    if (not isinstance(name, str) or not re.fullmatch(r"[a-z][a-z0-9_-]{0,63}", name)
            or not isinstance(version, str) or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][a-zA-Z0-9.+-]+)?", version)):
        raise ValueError(f"{folder}: invalid Cargo package name or version")
    mod_id, mod_version = manifest.get("id"), manifest.get("version")
    if (not isinstance(mod_id, str) or not re.fullmatch(r"[a-z0-9_]{1,64}", mod_id)
            or mod_id in {"minecraft", "c", "horizon"} or not isinstance(mod_version, str)
            or not mod_version.strip() or any(ord(character) < 32 for character in mod_version)):
        raise ValueError(f"{folder}: invalid Horizon mod ID or version")
    if (manifest.get("native") != {"game": "26.3", "horizon": upstream, "mixins": MIXINS}
            or manifest.get("side") not in {"client", "both"}
            or manifest.get("api") != "0.2" or manifest.get("capabilities") != ["registries"]
            or any(manifest.get(key) for key in ("inject", "internals", "wasm", "jar"))
            or any(key not in {"id", "name", "version", "description", "authors", "license", "license_url",
                               "credits", "homepage", "issues", "api", "side", "capabilities", "native",
                               "dependencies", "incompatible", "discouraged", "load_before", "properties"}
                   for key in manifest)):
        raise ValueError(f"{folder}: require an exact Nightly client hook manifest with the standard registries declaration and no mixins or injections")
    return package, manifest, files


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"The expected pinned source changed near {old.splitlines()[0]!r}")
    return source.replace(old, new, 1)


def rust_string(value):
    return json.dumps(value, ensure_ascii=False)


def install_embedded_host_start(platform):
    platform = replace_once(
        platform,
        "pub fn start(startup: horizon_client::entry::Startup) -> horizon_client::Result<()> {",
        """pub fn start(startup: horizon_client::entry::Startup) -> horizon_client::Result<()> {
    start_with_native(startup, &[], &[])
}

pub fn start_with_native(
    startup: horizon_client::entry::Startup,
    built: &'static [mod_api::__private::native::BuiltMod],
    factories: &'static [crate::HookFactory],
) -> horizon_client::Result<()> {
    let (native, order) = crate::embedded_host::prepare(&startup, built, factories)?;""",
    )
    platform = replace_once(
        platform,
        """        horizon_mod_package::runtime::Runtime::default(),
        |_, _| {
            mod_api::__private::install(Vec::new().into());
            (Vec::new(), ())
        },""",
        """        native,
        |_, _| (crate::embedded_host::install(built, factories, order), ()),""",
    )
    return platform


def november20_build_ids(architecture):
    return tuple(f"mchorizonuwp-{client}-patch-{patch}-{architecture}-{NOV20_UPSTREAM}"
                 for client, patch in (("0.1.14", "0.1.13"), ("0.1.15", "0.1.13"), ("0.1.16", "0.1.14")))


def stage(workspace, architecture, source_folders, game_build_id=None):
    bridge = workspace / "uwp/game-bridge"
    host = workspace / "crates/horizon-mod-host/src"
    output = bridge / "embedded-native"
    snapshots = output / "horizon-native-mods"
    if output.exists():
        raise ValueError("Native sources already staged. Start from a fresh authenticated Builder snapshot.")
    expected = [f"mchorizonuwp-{client}-patch-{patch}-{architecture}-{UPSTREAM}"
                for client, patch in (("0.1.6", "0.1.5"), ("0.1.7", "0.1.6"),
                                      ("0.1.8", "0.1.7"), ("0.1.9", "0.1.8"),
                                      ("0.1.10", "0.1.9"), ("0.1.11", "0.1.10"),
                                      ("0.1.12", "0.1.11"), ("0.1.13", "0.1.12"))]
    expected.extend(november20_build_ids(architecture))
    game_build_id = game_build_id or expected[0]
    if game_build_id not in expected:
        raise ValueError("Build identity does not match the pinned UWP Nightly and architecture")
    upstream = game_build_id.rsplit("-", 1)[1]
    checked = [read_source(folder, upstream) for folder in source_folders]
    ids = [manifest["id"] for _, manifest, _ in checked]
    names = [package["name"] for package, _, _ in checked]
    lock_path = bridge / "Cargo.lock"
    lock = lock_path.read_text(encoding="utf-8")
    known = {package["name"] for package in tomllib.loads(lock)["package"]}
    if len(ids) != len(set(ids)) or len(names) != len(set(names)) or known.intersection(names):
        raise ValueError("Duplicate native mod ID, duplicate Cargo package, or a collision with a game dependency")
    blocks = re.split(r"(?=^\[\[package\]\]$)", lock, flags=re.MULTILINE)
    bridge_block = next(block for block in blocks[1:] if block.startswith('[[package]]\nname = "horizon-uwp-game-bridge"\n'))
    prefix, dependencies = bridge_block.split("dependencies = [\n")
    native_deps = [' "horizon-mod-api",\n', *(f' "{name}",\n' for name in names)]
    dep_lines, tail = dependencies.split("]\n", 1)
    blocks[blocks.index(bridge_block)] = prefix + "dependencies = [\n" + "".join(
        sorted(dep_lines.splitlines(keepends=True) + native_deps)
    ) + "]\n" + tail
    host_path = host / "uwp.rs"
    platform = host_path.read_text(encoding="utf-8")
    integrated_host = "pub fn start_with_native(" in platform
    if not integrated_host:
        platform = install_embedded_host_start(platform)
    bridge_lib = bridge / "src/lib.rs"
    source = replace_once(
        bridge_lib.read_text(encoding="utf-8"),
        "    horizon_mod_host::start(startup)",
        "    embedded_native::start(startup)",
    )
    bridge_manifest_path = bridge / "Cargo.toml"
    cargo = bridge_manifest_path.read_text(encoding="utf-8")
    bridge_deps = ['mod-api = { package = "horizon-mod-api", path = "../../crates/horizon-mod-api" }']
    factories, inventory, records = [], [], []
    snapshots.mkdir(parents=True)
    for index, (package, manifest, files) in enumerate(checked):
        mod_id, name, version = manifest["id"], package["name"], package["version"]
        crate = output / "crates" / mod_id
        shipped = snapshots / mod_id
        for root in (crate, shipped):
            (root / "src").mkdir(parents=True)
        for relative, data in files.items():
            (shipped / relative).write_bytes(data)
        (crate / "src/lib.rs").write_bytes(files["src/lib.rs"])
        (crate / "Cargo.toml").write_text(
            f'[package]\nname = "{name}"\nversion = "{version}"\n'
            f'edition = "{package["edition"]}"\nbuild = false\npublish = false\n\n'
            '[dependencies]\nhorizon-mod-api = { path = "../../../../../crates/horizon-mod-api" }\n',
            encoding="utf-8",
        )
        blocks.append(
            f'[[package]]\nname = "{name}"\nversion = "{version}"\n'
            'dependencies = [\n "horizon-mod-api",\n]\n\n'
        )
        bridge_deps.append(f'embedded_mod_{index} = {{ package = "{name}", path = "embedded-native/crates/{mod_id}" }}')
        factories.append(f"    embedded_mod_{index}::hooks,")
        inventory.append(
            f"    BuiltMod {{ id: {rust_string(mod_id)}, version: {rust_string(manifest['version'])}, "
            f"side: mod_api::Side::{manifest['side'].capitalize()} }},"
        )
        records.append({"id": mod_id, "version": manifest["version"], "crate": name, "crate_version": version,
                        "source_sha256": {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}})
    generated = """use mod_api::__private::native::BuiltMod;
use horizon_mod_host::HookFactory;

const MODS: &[BuiltMod] = &[
""" + "\n".join(inventory) + """
];
const FACTORIES: &[HookFactory] = &[
""" + "\n".join(factories) + """
];

pub fn start(startup: horizon_client::entry::Startup) -> horizon_client::Result<()> {
    horizon_mod_host::start_with_native(startup, MODS, FACTORIES)
}
"""
    (bridge / "src/embedded_native.rs").write_text(generated, encoding="utf-8")
    bridge_lib.write_text(source + '\n#[cfg(target_vendor = "uwp")]\nmod embedded_native;\n', encoding="utf-8")
    bridge_manifest_path.write_text(replace_once(cargo, "[dependencies]\n",
        "[dependencies]\n" + "\n".join(bridge_deps) + "\n"), encoding="utf-8")
    lock_path.write_text(blocks[0] + "".join(sorted(
        (block.rstrip() + "\n\n" for block in blocks[1:]), key=lambda block: block.splitlines()[1],
    )).rstrip() + "\n", encoding="utf-8")
    if not integrated_host:
        host_path.write_text(platform, encoding="utf-8")
        with (host / "lib.rs").open("a", encoding="utf-8") as metadata:
            metadata.write('\n#[cfg(target_vendor = "uwp")]\nmod embedded_host;\n'
                           '#[cfg(target_vendor = "uwp")]\npub use embedded_host::HookFactory;\n'
                           '#[cfg(target_vendor = "uwp")]\npub use uwp::start_with_native;\n')
        (host / "embedded_host.rs").write_bytes(Path(__file__).with_name("embedded_host.rs").read_bytes())
    record = {"architecture": architecture, "rust_target": TARGETS[architecture], "rust_toolchain": "1.99.1",
              "horizon": upstream, "mixins": MIXINS, "game_build_id": game_build_id,
              "cargo_lock_sha256": hashlib.sha256(lock_path.read_bytes()).hexdigest(), "mods": records}
    (snapshots / "build-record.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(f"Staged {len(checked)} trusted native source project(s).")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--architecture", choices=TARGETS, required=True)
    parser.add_argument("--game-build-id")
    parser.add_argument("--trust-native-source", action="store_true", required=True)
    parser.add_argument("--source", type=Path, action="append", required=True)
    args = parser.parse_args()
    try:
        stage(args.workspace, args.architecture, args.source, args.game_build_id)
    except (OSError, ValueError, KeyError, StopIteration) as error:
        parser.exit(1, f"Embedded native source staging failed: {error}\n")


if __name__ == "__main__":
    main()
