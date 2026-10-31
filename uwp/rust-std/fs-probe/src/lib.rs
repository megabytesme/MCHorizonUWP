use std::{ffi::OsString, os::windows::ffi::OsStringExt, path::PathBuf, io::Write};
#[unsafe(no_mangle)]
pub unsafe extern "C" fn uwp_fs_probe(path: *const u16, len: usize) -> u32 {
    unsafe { std::env::set_var("HORIZON_UWP_TEST_VAR", "value") };
    if std::env::var("horizon_uwp_test_var").ok().as_deref() != Some("value") { return 20 }
    if !std::env::vars_os().any(|(key, value)| key == "HORIZON_UWP_TEST_VAR" && value == "value") { return 21 }
    unsafe { std::env::remove_var("HORIZON_UWP_TEST_VAR") };
    if std::env::var_os("HORIZON_UWP_TEST_VAR").is_some() { return 22 }
    if !matches!(std::process::Command::new("invalid.exe").spawn(), Err(e) if e.kind() == std::io::ErrorKind::Unsupported) { return 23 }
    use std::os::windows::io::AsRawHandle;
    if !std::io::stdout().as_raw_handle().is_null() { return 24 }
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
    let duplicate = dir.join("duplicate.txt");
    if std::fs::copy(&file, &duplicate).ok() != Some(1) { return 12 }
    if std::fs::read(&duplicate).ok().as_deref() != Some(b"x") { return 13 }
    let entries: Vec<_> = match std::fs::read_dir(&dir) { Ok(iter) => iter.collect::<Result<_, _>>().unwrap_or_default(), Err(_) => return 25 };
    if !entries.iter().any(|e| e.file_name() == "sample.txt") || !entries.iter().any(|e| e.file_name() == "duplicate.txt") { return 26 }
    let renamed = dir.join("renamed.txt");
    if std::fs::rename(&duplicate, &renamed).is_err() { return 27 }
    if std::fs::remove_file(&renamed).is_err() { return 14 }
    let Ok(f) = std::fs::OpenOptions::new().read(true).write(true).open(&file) else { return 15 };
    if f.lock().is_err() { return 16 }
    if f.unlock().is_err() { return 17 }
    drop(f);
    if std::fs::remove_file(&file).is_err() { return 10 }
    if std::fs::remove_dir(&dir).is_err() { return 11 }
    0
}

#[unsafe(no_mangle)]
pub extern "C" fn uwp_sync_probe() -> u32 {
    use std::sync::Arc;
    use std::time::Duration;
    let value = Arc::new(parking_lot::Mutex::new(0usize));
    let guard = value.lock();
    let handles: Vec<_> = (0..4).map(|_| {
        let value = value.clone();
        std::thread::spawn(move || { for _ in 0..1000 { *value.lock() += 1; } })
    }).collect();
    std::thread::sleep(Duration::from_millis(50));
    drop(guard);
    for handle in handles { if handle.join().is_err() { return 1; } }
    if *value.lock() != 4000 { return 2; }
    0
}
