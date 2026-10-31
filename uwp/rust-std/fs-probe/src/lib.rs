use std::{ffi::OsString, os::windows::ffi::OsStringExt, path::PathBuf, io::Write};
#[unsafe(no_mangle)]
pub unsafe extern "C" fn uwp_fs_probe(path: *const u16, len: usize) -> u32 {
    let root = PathBuf::from(OsString::from_wide(unsafe { std::slice::from_raw_parts(path, len) }));
    let dir = root.join("uwp-rust-fs-probe");
    if std::fs::create_dir_all(&dir).is_err() { return 1 }
    let file = dir.join("sample.txt");
    if std::fs::write(&file, b"hello").is_err() { return 2 }
    if std::fs::read(&file).ok().as_deref() != Some(b"hello") { return 3 }
    let Ok(mut f) = std::fs::OpenOptions::new().append(true).open(&file) else { return 4 };
    if f.write_all(b"!").is_err() { return 5 }
    drop(f);
    if std::fs::read(&file).ok().as_deref() != Some(b"hello!") { return 6 }
    if std::fs::OpenOptions::new().write(true).create_new(true).open(&file).is_ok() { return 7 }
    if std::fs::write(&file, b"x").is_err() { return 8 }
    if std::fs::read(&file).ok().as_deref() != Some(b"x") { return 9 }
    if std::fs::remove_file(&file).is_err() { return 10 }
    if std::fs::remove_dir(&dir).is_err() { return 11 }
    0
}
