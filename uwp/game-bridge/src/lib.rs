//! C ABI for embedding the actual Horizon game loop in a UWP host.

#[cfg(target_vendor = "uwp")]
unsafe extern "C" {
    fn horizon_uwp_report_error(text: *const u8, len: usize);
}

#[cfg(target_vendor = "uwp")]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_run(
    resources: *const u16,
    resources_len: usize,
    game_dir: *const u16,
    game_dir_len: usize,
) -> i32 {
    use std::os::windows::ffi::OsStringExt;
    use std::path::PathBuf;

    if resources.is_null() || game_dir.is_null() {
        return -1;
    }
    // The host owns both buffers for the duration of this call.
    let resources = unsafe { std::slice::from_raw_parts(resources, resources_len) };
    let game_dir = unsafe { std::slice::from_raw_parts(game_dir, game_dir_len) };
    let resources = PathBuf::from(std::ffi::OsString::from_wide(resources));
    let game_dir = PathBuf::from(std::ffi::OsString::from_wide(game_dir));
    horizon_client::logging::set_game_home(game_dir.clone());
    horizon_client::logging::init(&game_dir, "Render thread");
    let result = horizon_client::run_uwp(&resources, &game_dir);
    horizon_client::logging::flush();
    match result {
        Ok(()) => 0,
        Err(error) => {
            let message = format!("Horizon UWP startup failed: {error}");
            // The host writes this to app-local storage, since UWP has no console.
            unsafe { horizon_uwp_report_error(message.as_ptr(), message.len()) };
            1
        }
    }
}
