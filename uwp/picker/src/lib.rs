//! Safe facade for the UWP content file picker.
#![cfg(target_vendor = "uwp")]
use std::{os::windows::ffi::OsStrExt, path::Path};

unsafe extern "C" {
    fn horizon_uwp_pick_files(path: *const u16, length: usize, kind: u32) -> i32;
}

pub fn pick(path: &Path, kind: u32) -> bool {
    let wide: Vec<u16> = path.as_os_str().encode_wide().collect();
    !wide.is_empty() && unsafe { horizon_uwp_pick_files(wide.as_ptr(), wide.len(), kind) == 0 }
}
