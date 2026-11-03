//! C ABI for embedding the actual Horizon game loop in a UWP host.

#[cfg(target_vendor = "uwp")]
#[global_allocator]
static ALLOCATOR: horizon_threads::Allocator = horizon_threads::ALLOCATOR;

#[cfg(all(target_vendor = "uwp", feature = "uwp-account"))]
mod pregame;

#[cfg(target_vendor = "uwp")]
unsafe extern "C" {
    fn horizon_uwp_report_error(text: *const u8, len: usize);
    fn horizon_uwp_fatal_begin(text: *const u8, len: usize);
    fn horizon_uwp_fatal_done() -> i32;
    fn horizon_uwp_is_ui_thread() -> i32;
    fn horizon_uwp_pump_events();
    fn horizon_uwp_is_local_test() -> i32;
    #[cfg(feature = "uwp-account")]
    fn horizon_uwp_discard_pregame_input();
}

#[cfg(target_vendor = "uwp")]
fn report_failure(message: &str) {
    use std::time::{Duration, Instant};
    // Diagnostics deliberately exclude panic payloads, which may contain tokens
    // or a server response. Each report gets a new app-local file.
    unsafe {
        horizon_uwp_report_error(message.as_ptr(), message.len());
        horizon_uwp_fatal_begin(message.as_ptr(), message.len());
    }
    let deadline = Instant::now() + Duration::from_secs(120);
    while unsafe { horizon_uwp_fatal_done() == 0 } && Instant::now() < deadline {
        if unsafe { horizon_uwp_is_ui_thread() != 0 } {
            unsafe { horizon_uwp_pump_events() };
        }
        std::thread::sleep(Duration::from_millis(20));
    }
}

#[cfg(target_vendor = "uwp")]
fn install_panic_reporter() {
    std::panic::set_hook(Box::new(|info| {
        let location = info.location().map_or_else(
            || "unknown location".to_owned(),
            |location| format!("{}:{}", location.file(), location.line()),
        );
        report_failure(&format!(
            "Horizon encountered an unexpected error at {location} and must close. A diagnostic was saved in app storage."
        ));
    }));
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

    install_panic_reporter();
    if resources.is_null() || game_dir.is_null() {
        report_failure("Horizon could not find its resource or data path.");
        return -1;
    }
    // The host owns both buffers for the duration of this call.
    let resources = unsafe { std::slice::from_raw_parts(resources, resources_len) };
    let game_dir = unsafe { std::slice::from_raw_parts(game_dir, game_dir_len) };
    let resources = PathBuf::from(std::ffi::OsString::from_wide(resources));
    let game_dir = PathBuf::from(std::ffi::OsString::from_wide(game_dir));
    if unsafe { horizon_uwp_is_local_test() != 0 } {
        // Only the separately registered local test has this package marker.
        // Set these before any game or account worker threads are created.
        let home = game_dir.join("isolated-test-home");
        for name in [
            "HORIZON_HOME",
            "HORIZON_LAUNCHER_HOME",
            "XDG_DATA_HOME",
            "XDG_CONFIG_HOME",
        ] {
            unsafe { std::env::set_var(name, &home) };
        }
        unsafe {
            std::env::set_var("HORIZON_LAUNCHER_NO_SELF_UPDATE", "1");
            std::env::set_var("HORIZON_NO_AUDIO", "1");
        }
    }
    horizon_client::logging::set_game_home(game_dir.clone());
    horizon_client::logging::init(&game_dir, "Render thread");
    #[cfg(feature = "uwp-account")]
    let result = match pregame::start() {
        Some(session) => {
            // Do not replay taps or keystrokes from the native dialogs into the game.
            unsafe { horizon_uwp_discard_pregame_input() };
            horizon_client::run_uwp_with_session(&resources, &game_dir, session)
        }
        None => return 0,
    };
    #[cfg(not(feature = "uwp-account"))]
    let result = horizon_client::run_uwp(&resources, &game_dir);
    horizon_client::logging::flush();
    match result {
        Ok(()) => 0,
        Err(error) => {
            let message = format!("Horizon UWP startup failed: {error}");
            report_failure(&message);
            1
        }
    }
}
