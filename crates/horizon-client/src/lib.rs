//! Gameplay and startup, above the reusable client library.
#![recursion_limit = "256"]
#![forbid(unsafe_code)]
extern crate self as horizon_client;
pub use horizon_client_lib::*;
pub mod entry;
pub mod game;
pub mod mod_api;

/// Run the game in a UWP host with explicit resource and writable data paths.
#[cfg(target_vendor = "uwp")]
pub fn run_uwp(resources: &std::path::Path, game_dir: &std::path::Path) -> Result<()> {
    run_uwp_with_session(resources, game_dir, None)
}

/// Run with a session supplied in memory by the UWP host. The session is not
/// passed through an environment variable, command line, stdin, or file.
#[cfg(target_vendor = "uwp")]
pub fn run_uwp_with_session(
    resources: &std::path::Path,
    game_dir: &std::path::Path,
    session: Option<auth::AuthSession>,
) -> Result<()> {
    uwp_mods::install()?;
    let mut args = vec![
        "--resources".to_owned(),
        resources.to_string_lossy().into_owned(),
        "--game-dir".to_owned(),
        game_dir.to_string_lossy().into_owned(),
    ];
    if let Ok(server) = std::env::var("HORIZON_UWP_SERVER_ADDRESS")
        && !server.is_empty()
        && server.len() <= 255
    {
        args.extend(["--server".to_owned(), server]);
    }
    if let Ok(world) = std::env::var("HORIZON_UWP_QUICK_WORLD")
        && !world.is_empty()
        && world.len() <= 1024
    {
        args.extend(["--quickPlaySingleplayer".to_owned(), world]);
    }
    if std::env::var("HORIZON_UWP_BENCHMARK").as_deref() == Ok("standard") {
        args.extend(["--benchmark".to_owned(), "standard".to_owned()]);
    }
    // The diagnostic marker is honored only by the opt-in readback build.
    // Its game directory is separate, so ordinary worlds remain untouched.
    #[cfg(feature = "native-d3d11-readback")]
    let args = if game_dir.join("native-world-smoke.enable").is_file() {
        let mut args = args;
        let isolated = game_dir.join("native-world-smoke");
        args[3] = isolated.to_string_lossy().into_owned();
        args.extend([
            "--world".to_owned(),
            "NativeSmoke".to_owned(),
            "--creative".to_owned(),
        ]);
        log::info!(
            "Horizon: native world smoke enabled in {}",
            isolated.display()
        );
        args
    } else {
        args
    };
    let smoke_diagnostics = cfg!(feature = "native-d3d11-readback")
        && game_dir.join("native-world-smoke.enable").is_file();
    let result = game::run_with_args(args, smoke_diagnostics, session);
    // UWP hosts reuse the process after the game's window closes. Release the
    // in-memory account only after the game and its worker threads have ended,
    // so the next launch can select an account independently.
    auth::clear_current();
    result
}
#[cfg(all(target_vendor = "uwp", feature = "native-d3d11"))]
pub mod native_renderer;
#[cfg(target_vendor = "uwp")]
mod uwp_mods;

#[cfg(all(target_vendor = "uwp", feature = "uwp-controller"))]
pub mod uwp_controller;

#[cfg(all(target_vendor = "uwp", feature = "uwp-controller"))]
mod usb_ids;
