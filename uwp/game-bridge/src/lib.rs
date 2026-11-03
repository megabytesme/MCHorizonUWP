//! C ABI for embedding the actual Horizon game loop in a UWP host.

#[cfg(target_vendor = "uwp")]
#[global_allocator]
static ALLOCATOR: horizon_threads::Allocator = horizon_threads::ALLOCATOR;

#[cfg(all(target_vendor = "uwp", feature = "uwp-account"))]
mod pregame;
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
mod xaml_pregame;

// The XAML shell and DirectX game view have different UI threads. A successful
// account session crosses that boundary only through this in-process slot;
// it is consumed once and is never serialized to the command line or disk.
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
static PREPARED_SESSION: std::sync::Mutex<Option<xaml_pregame::Selection>> =
    std::sync::Mutex::new(None);

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_release_label(buffer: *mut u8, capacity: usize) -> usize {
    let label = horizon_core::release::nightly_label()
        .unwrap_or_else(|| format!("Horizon {}", horizon_core::release::VERSION));
    copy_build_label(buffer, capacity, &label)
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
fn copy_build_label(buffer: *mut u8, capacity: usize, label: &str) -> usize {
    let bytes = label.as_bytes();
    if buffer.is_null() || bytes.len() > capacity {
        return 0;
    }
    // SAFETY: the C++ caller provides a writable buffer of `capacity` bytes.
    unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), buffer, bytes.len()) };
    bytes.len()
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_upstream_ref(buffer: *mut u8, capacity: usize) -> usize {
    copy_build_label(
        buffer,
        capacity,
        option_env!("HORIZON_UPSTREAM_COMMIT").unwrap_or("not recorded"),
    )
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_upstream_release(buffer: *mut u8, capacity: usize) -> usize {
    copy_build_label(
        buffer,
        capacity,
        option_env!("HORIZON_VERSION").unwrap_or("not recorded"),
    )
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_port_ref(buffer: *mut u8, capacity: usize) -> usize {
    copy_build_label(
        buffer,
        capacity,
        option_env!("HORIZON_UWP_PORT_REF").unwrap_or("not recorded"),
    )
}

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

#[cfg(all(
    target_vendor = "uwp",
    feature = "xaml-shell",
    feature = "native-touch-mod"
))]
unsafe extern "C" {
    fn horizon_uwp_touch_settings(
        gyro_enabled: *mut i32,
        gyro_sensitivity: *mut f32,
        touch_sensitivity: *mut f32,
        control_scale: *mut f32,
    ) -> i32;
}

#[cfg(all(
    target_vendor = "uwp",
    feature = "xaml-shell",
    feature = "native-touch-mod"
))]
fn save_touch_settings(game_dir: &std::path::Path) -> Result<(), std::io::Error> {
    let (mut enabled, mut gyro_sensitivity, mut touch_sensitivity, mut control_scale) =
        (0, 1.0, 1.0, 1.0);
    if unsafe {
        horizon_uwp_touch_settings(
            &mut enabled,
            &mut gyro_sensitivity,
            &mut touch_sensitivity,
            &mut control_scale,
        )
    } == 0
    {
        return Err(std::io::Error::other("touch settings were unavailable"));
    }
    std::fs::create_dir_all(game_dir)?;
    std::fs::write(
        game_dir.join("touch-settings.txt"),
        format!(
            "gyro_enabled={}\ngyro_sensitivity={gyro_sensitivity:.2}\ntouch_sensitivity={touch_sensitivity:.2}\ncontrol_scale={control_scale:.2}\n",
            u8::from(enabled != 0)
        ),
    )
}

#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
unsafe extern "C" {
    fn horizon_uwp_gyro_enable(enabled: i32) -> i32;
    fn horizon_uwp_gyro_take(x: *mut f32, y: *mut f32, rotation: *mut i32) -> i32;
}

#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
fn enable_touch_gyro(enabled: bool) -> bool {
    unsafe { horizon_uwp_gyro_enable(i32::from(enabled)) != 0 }
}

#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
fn take_touch_gyro() -> Option<(f32, f32, i32)> {
    let (mut x, mut y, mut rotation) = (0.0, 0.0, 0);
    (unsafe { horizon_uwp_gyro_take(&mut x, &mut y, &mut rotation) != 0 }
        && x.is_finite()
        && y.is_finite())
    .then_some((x, y, rotation))
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_probe_renderer(
    core_window: *mut std::ffi::c_void,
    renderer: u32,
    width: u32,
    height: u32,
) -> i32 {
    install_panic_reporter();
    let Some(core_window) = std::ptr::NonNull::new(core_window) else {
        return 0;
    };
    let (backend, expected_backend) = match renderer {
        1 => (wgpu::Backends::GL, wgpu::Backend::Gl),
        2 => (wgpu::Backends::DX12, wgpu::Backend::Dx12),
        _ => return 0,
    };
    let mut descriptor = wgpu::InstanceDescriptor::new_without_display_handle();
    descriptor.backends = backend;
    let instance = wgpu::Instance::new(descriptor);
    let target = wgpu::SurfaceTargetUnsafe::RawHandle {
        raw_display_handle: Some(raw_window_handle::RawDisplayHandle::Windows(
            raw_window_handle::WindowsDisplayHandle::new(),
        )),
        raw_window_handle: raw_window_handle::RawWindowHandle::WinRt(
            raw_window_handle::WinRtWindowHandle::new(core_window),
        ),
    };
    let Ok(surface) = (unsafe { instance.create_surface_unsafe(target) }) else {
        return 0;
    };
    let Ok(adapter) = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
        compatible_surface: Some(&surface),
        ..Default::default()
    })) else {
        return 0;
    };
    if adapter.get_info().backend != expected_backend {
        return 0;
    }
    let limits = adapter.limits();
    let Ok((device, queue)) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
        required_limits: limits,
        ..Default::default()
    })) else {
        return 0;
    };
    device.on_uncaptured_error(std::sync::Arc::new(|_| {}));
    let Some(configuration) = surface.get_default_config(&adapter, width.max(1), height.max(1))
    else {
        return 0;
    };
    surface.configure(&device, &configuration);
    let frame = match surface.get_current_texture() {
        wgpu::CurrentSurfaceTexture::Success(frame)
        | wgpu::CurrentSurfaceTexture::Suboptimal(frame) => frame,
        _ => return 0,
    };
    queue.present(frame);
    1
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
fn configure_isolated_test(game_dir: &std::path::Path) {
    if unsafe { horizon_uwp_is_local_test() != 0 } {
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
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_prepare(game_dir: *const u16, game_dir_len: usize) -> i32 {
    use std::os::windows::ffi::OsStringExt;
    if game_dir.is_null() {
        return -1;
    }
    install_panic_reporter();
    let game_dir = unsafe { std::slice::from_raw_parts(game_dir, game_dir_len) };
    let game_dir = std::path::PathBuf::from(std::ffi::OsString::from_wide(game_dir));
    configure_isolated_test(&game_dir);
    match xaml_pregame::start() {
        Some(session) => {
            #[cfg(feature = "native-touch-mod")]
            if save_touch_settings(&game_dir).is_err() {
                report_failure("Horizon could not save the touch-control settings.");
                return -1;
            }
            let mut slot = PREPARED_SESSION
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            if slot.is_some() {
                return -1;
            }
            *slot = Some(session);
            1
        }
        None => 0,
    }
}

#[cfg(target_vendor = "uwp")]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_run(
    resources: *const u16,
    resources_len: usize,
    game_dir: *const u16,
    game_dir_len: usize,
) -> i32 {
    unsafe { run(resources, resources_len, game_dir, game_dir_len, false) }
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_run_prepared(
    resources: *const u16,
    resources_len: usize,
    game_dir: *const u16,
    game_dir_len: usize,
) -> i32 {
    #[cfg(feature = "uwp-dx12-renderer")]
    unsafe {
        std::env::set_var("WGPU_BACKEND", "dx12")
    };
    #[cfg(all(not(feature = "uwp-dx12-renderer"), not(feature = "native-d3d11")))]
    unsafe {
        std::env::set_var("WGPU_BACKEND", "gl")
    };
    unsafe { run(resources, resources_len, game_dir, game_dir_len, true) }
}

#[cfg(target_vendor = "uwp")]
unsafe fn run(
    resources: *const u16,
    resources_len: usize,
    game_dir: *const u16,
    game_dir_len: usize,
    prepared: bool,
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
    #[cfg(feature = "native-touch-mod")]
    horizon_client::game::install_touch_gyro_adapter(enable_touch_gyro, take_touch_gyro);
    // The shell does this before starting its account worker. The legacy host
    // reaches it here before starting any game or account worker threads.
    if !prepared {
        configure_isolated_test(&game_dir);
    }
    horizon_client::logging::set_game_home(game_dir.clone());
    horizon_client::logging::init(&game_dir, "Render thread");
    #[cfg(feature = "xaml-shell")]
    let selection = if prepared {
        PREPARED_SESSION
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .take()
    } else {
        pregame::start().map(|session| xaml_pregame::Selection {
            session,
            offline_name: None,
        })
    };
    #[cfg(feature = "xaml-shell")]
    let result = match selection {
        Some(selection) => {
            // Do not replay taps or keystrokes from the native dialogs into the game.
            unsafe { horizon_uwp_discard_pregame_input() };
            // Horizon already supports an offline player name through this
            // public (non-secret) setting. Account tokens never enter it.
            if let Some(name) = &selection.offline_name {
                unsafe { std::env::set_var("HORIZON_PLAYER_NAME", name) };
            }
            horizon_client::run_uwp_with_session(&resources, &game_dir, selection.session)
        }
        None => return 0,
    };
    #[cfg(all(feature = "uwp-account", not(feature = "xaml-shell")))]
    let result = match pregame::start() {
        Some(session) => {
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
