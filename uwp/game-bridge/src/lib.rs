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
    #[cfg(feature = "xaml-shell")]
    fn horizon_uwp_page_selected_server(buffer: *mut u8, capacity: usize) -> usize;
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
unsafe extern "C" {
    fn horizon_uwp_trace_shell_event(message: *const std::ffi::c_char);
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
fn trace_renderer_probe(message: &str) {
    let Ok(message) = std::ffi::CString::new(message) else {
        return;
    };
    unsafe { horizon_uwp_trace_shell_event(message.as_ptr()) };
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_probe_renderer(
    surface_handle: *mut std::ffi::c_void,
    renderer: u32,
    width: u32,
    height: u32,
) -> i32 {
    install_panic_reporter();
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: instance creation starting"
    } else {
        "ANGLE probe: instance creation starting"
    });
    let Some(surface_handle) = std::ptr::NonNull::new(surface_handle) else {
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
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: instance created"
    } else {
        "ANGLE probe: instance created"
    });
    let target = match renderer {
        1 => wgpu::SurfaceTargetUnsafe::RawHandle {
            raw_display_handle: Some(raw_window_handle::RawDisplayHandle::Windows(
                raw_window_handle::WindowsDisplayHandle::new(),
            )),
            raw_window_handle: raw_window_handle::RawWindowHandle::WinRt(
                raw_window_handle::WinRtWindowHandle::new(surface_handle),
            ),
        },
        2 => {
            #[cfg(feature = "uwp-dx12-renderer")]
            {
                wgpu::SurfaceTargetUnsafe::SwapChainPanel(surface_handle.as_ptr())
            }
            #[cfg(not(feature = "uwp-dx12-renderer"))]
            {
                return 0;
            }
        }
        _ => return 0,
    };
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: surface creation starting"
    } else {
        "ANGLE probe: surface creation starting"
    });
    let Ok(surface) = (unsafe { instance.create_surface_unsafe(target) }) else {
        return 0;
    };
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: surface created"
    } else {
        "ANGLE probe: surface created"
    });
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: adapter request starting"
    } else {
        "ANGLE probe: adapter request starting"
    });
    let Ok(adapter) = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
        compatible_surface: Some(&surface),
        ..Default::default()
    })) else {
        return 0;
    };
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: adapter returned"
    } else {
        "ANGLE probe: adapter returned"
    });
    if adapter.get_info().backend != expected_backend {
        return 0;
    }
    let limits = adapter.limits();
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: device request starting"
    } else {
        "ANGLE probe: device request starting"
    });
    let Ok((device, queue)) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
        required_limits: limits,
        ..Default::default()
    })) else {
        return 0;
    };
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: device returned"
    } else {
        "ANGLE probe: device returned"
    });
    device.on_uncaptured_error(std::sync::Arc::new(|_| {}));
    let Some(configuration) = surface.get_default_config(&adapter, width.max(1), height.max(1))
    else {
        return 0;
    };
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: surface configuration starting"
    } else {
        "ANGLE probe: surface configuration starting"
    });
    surface.configure(&device, &configuration);
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: surface configured"
    } else {
        "ANGLE probe: surface configured"
    });
    let frame = match surface.get_current_texture() {
        wgpu::CurrentSurfaceTexture::Success(frame)
        | wgpu::CurrentSurfaceTexture::Suboptimal(frame) => frame,
        _ => return 0,
    };
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: frame acquired"
    } else {
        "ANGLE probe: frame acquired"
    });
    queue.present(frame);
    trace_renderer_probe(if renderer == 2 {
        "DX12 probe: frame presented"
    } else {
        "ANGLE probe: frame presented"
    });
    1
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_server_list_read(
    game_dir: *const u16,
    game_dir_len: usize,
    output: *mut u8,
    capacity: usize,
) -> isize {
    use std::os::windows::ffi::OsStringExt;

    if game_dir.is_null() || output.is_null() {
        return -1;
    }
    let wide = unsafe { std::slice::from_raw_parts(game_dir, game_dir_len) };
    let file = std::path::PathBuf::from(std::ffi::OsString::from_wide(wide)).join("servers.dat");
    let entries = horizon_protocol::servers::load(&file);
    let mut encoded = Vec::new();
    for entry in entries.into_iter().take(64) {
        let name = entry.name.as_bytes();
        let address = entry.ip.as_bytes();
        if name.len() > u16::MAX as usize || address.len() > u16::MAX as usize {
            continue;
        }
        let needed = 4 + name.len() + address.len();
        if encoded.len() + needed > capacity {
            return -1;
        }
        encoded.extend_from_slice(&(name.len() as u16).to_le_bytes());
        encoded.extend_from_slice(&(address.len() as u16).to_le_bytes());
        encoded.extend_from_slice(name);
        encoded.extend_from_slice(address);
    }
    unsafe { std::ptr::copy_nonoverlapping(encoded.as_ptr(), output, encoded.len()) };
    encoded.len() as isize
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_server_list_add(
    game_dir: *const u16,
    game_dir_len: usize,
    name: *const u8,
    name_len: usize,
    address: *const u8,
    address_len: usize,
) -> i32 {
    use std::os::windows::ffi::OsStringExt;

    if game_dir.is_null() || name.is_null() || address.is_null() {
        return -1;
    }
    let game_dir = std::path::PathBuf::from(std::ffi::OsString::from_wide(unsafe {
        std::slice::from_raw_parts(game_dir, game_dir_len)
    }));
    let name = match std::str::from_utf8(unsafe { std::slice::from_raw_parts(name, name_len) }) {
        Ok(name) if !name.trim().is_empty() && name.len() <= 256 => name,
        _ => return -1,
    };
    let address =
        match std::str::from_utf8(unsafe { std::slice::from_raw_parts(address, address_len) }) {
            Ok(address) if horizon_protocol::servers::parse_address(address).is_some() => address,
            _ => return -1,
        };
    let file = game_dir.join("servers.dat");
    let entries = horizon_protocol::servers::load(&file);
    if entries
        .iter()
        .any(|entry| entry.ip.eq_ignore_ascii_case(address))
    {
        return 1;
    }
    match horizon_protocol::servers::prepend(
        &file,
        &horizon_protocol::servers::ServerEntry::new(name.trim(), address.trim()),
    ) {
        Ok(()) => 0,
        Err(_) => -1,
    }
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_server_list_remove(
    game_dir: *const u16,
    game_dir_len: usize,
    address: *const u8,
    address_len: usize,
) -> i32 {
    use std::os::windows::ffi::OsStringExt;

    if game_dir.is_null() || address.is_null() {
        return -1;
    }
    let game_dir = std::path::PathBuf::from(std::ffi::OsString::from_wide(unsafe {
        std::slice::from_raw_parts(game_dir, game_dir_len)
    }));
    let address =
        match std::str::from_utf8(unsafe { std::slice::from_raw_parts(address, address_len) }) {
            Ok(address) => address,
            Err(_) => return -1,
        };
    match horizon_protocol::servers::remove(&game_dir.join("servers.dat"), address) {
        Ok(true) => 0,
        Ok(false) => 1,
        Err(_) => -1,
    }
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
    renderer: u32,
) -> i32 {
    let (renderer_name, backend) = match renderer {
        1 => ("angle", "gl"),
        2 => ("dx12", "dx12"),
        _ => ("d3d11", "gl"),
    };
    unsafe {
        std::env::set_var("HORIZON_UWP_RENDERER", renderer_name);
        std::env::set_var("WGPU_BACKEND", backend);
        let mut server = [0u8; 256];
        let length = horizon_uwp_page_selected_server(server.as_mut_ptr(), server.len());
        let address = server
            .get(..length)
            .filter(|_| length <= 255)
            .and_then(|bytes| std::str::from_utf8(bytes).ok())
            .filter(|address| !address.is_empty());
        std::env::set_var("HORIZON_UWP_SERVER_ADDRESS", address.unwrap_or(""));
    }
    unsafe { run(resources, resources_len, game_dir, game_dir_len, true) }
}

#[cfg(all(
    target_vendor = "uwp",
    feature = "xaml-shell",
    feature = "native-d3d11"
))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_prepare_native_renderer() -> i32 {
    horizon_client::native_renderer::prepare_window_renderer_on_ui_thread()
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
