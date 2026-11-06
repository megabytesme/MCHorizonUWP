//! C ABI for embedding the actual Horizon game loop in a UWP host.

#[cfg(any(test, all(target_vendor = "uwp", feature = "uwp-account")))]
mod account_client_id;

#[cfg(target_vendor = "uwp")]
#[global_allocator]
static ALLOCATOR: horizon_threads::Allocator = horizon_threads::ALLOCATOR;

#[cfg(all(target_vendor = "uwp", feature = "uwp-account"))]
mod pregame;
#[cfg(any(test, all(target_vendor = "uwp", feature = "xaml-shell")))]
mod server_status;
#[cfg(any(test, all(target_vendor = "uwp", feature = "xaml-shell")))]
mod update_check;
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
mod xaml_pregame;

#[cfg(all(
    target_vendor = "uwp",
    feature = "xaml-shell",
    feature = "uwp-controller"
))]
thread_local! {
    static CONTROLLER_TEST: std::cell::RefCell<Option<horizon_client::uwp_controller::ControllerTest>> = const { std::cell::RefCell::new(None) };
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_controller_test_read(buffer: *mut u8, capacity: usize) -> usize {
    #[cfg(feature = "uwp-controller")]
    let message = CONTROLLER_TEST.with(|slot| {
        let mut slot = slot.borrow_mut();
        if slot.is_none() {
            match horizon_client::uwp_controller::ControllerTest::new() {
                Ok(test) => *slot = Some(test),
                Err(error) => return format!("Controller input is unavailable: {error}"),
            }
        }
        slot.as_mut().unwrap().read()
    });
    #[cfg(not(feature = "uwp-controller"))]
    let message = String::from("Controller support is not included in this build.");
    copy_build_label(buffer, capacity, &message)
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_controller_test_stop() {
    #[cfg(feature = "uwp-controller")]
    CONTROLLER_TEST.with(|slot| *slot.borrow_mut() = None);
}

/// Read or update the game's existing face-button preference. `value` is -1
/// to read, or 0/1 to write. The path is the same game directory used at launch.
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_controller_face_swap(
    game_dir: *const u16,
    game_dir_len: usize,
    value: i32,
) -> i32 {
    use std::os::windows::ffi::OsStringExt;

    if game_dir.is_null() || !(-1..=1).contains(&value) {
        return -1;
    }
    let wide = unsafe { std::slice::from_raw_parts(game_dir, game_dir_len) };
    let dir = std::path::PathBuf::from(std::ffi::OsString::from_wide(wide));
    let file = dir.join(horizon_client::gamepad::FILE);
    let mut settings = match std::fs::read_to_string(&file) {
        Ok(contents) => horizon_client::gamepad::PadSettings::parse(&contents),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Default::default(),
        Err(_) => return -1,
    };
    if value >= 0 {
        settings.swap_face_buttons = value != 0;
        if std::fs::create_dir_all(&dir).is_err()
            || std::fs::write(file, settings.serialize()).is_err()
        {
            return -1;
        }
    }
    i32::from(settings.swap_face_buttons)
}

/// Read the app-local telemetry consent state for the WinUI settings page.
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_telemetry_state(
    game_dir: *const u16,
    game_dir_len: usize,
    mask: *mut u32,
    confirmed: *mut i32,
) -> i32 {
    use std::os::windows::ffi::OsStringExt;

    if game_dir.is_null() || mask.is_null() || confirmed.is_null() || game_dir_len > 32767 {
        return -1;
    }
    let wide = unsafe { std::slice::from_raw_parts(game_dir, game_dir_len) };
    let dir = std::path::PathBuf::from(std::ffi::OsString::from_wide(wide));
    let (saved_mask, is_confirmed) = horizon_client::telemetry::uwp_consent_mask(&dir);
    unsafe {
        *mask = saved_mask;
        *confirmed = i32::from(is_confirmed);
    }
    0
}

/// Persist explicit per-kind and per-destination consent. Unknown bits are rejected.
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_telemetry_save(
    game_dir: *const u16,
    game_dir_len: usize,
    mask: u32,
) -> i32 {
    use std::os::windows::ffi::OsStringExt;

    if game_dir.is_null() || game_dir_len > 32767 {
        return -1;
    }
    let wide = unsafe { std::slice::from_raw_parts(game_dir, game_dir_len) };
    let dir = std::path::PathBuf::from(std::ffi::OsString::from_wide(wide));
    match horizon_client::telemetry::save_uwp_consent_mask(&dir, mask) {
        Ok(()) => 0,
        Err(_) => -1,
    }
}

/// Clear local queues and request deletion from every endpoint that has an install ID.
/// 0 means complete, 1 means local deletion completed but remote deletion needs retry,
/// and -1 means local deletion could not complete.
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_telemetry_delete(
    game_dir: *const u16,
    game_dir_len: usize,
) -> i32 {
    use std::os::windows::ffi::OsStringExt;

    if game_dir.is_null() || game_dir_len > 32767 {
        return -1;
    }
    let wide = unsafe { std::slice::from_raw_parts(game_dir, game_dir_len) };
    let dir = std::path::PathBuf::from(std::ffi::OsString::from_wide(wide));
    match horizon_client::telemetry::delete_uwp_data(&dir) {
        Ok(()) => 0,
        Err(message) if message.starts_with("Local telemetry was cleared") => 1,
        Err(_) => -1,
    }
}

/// Queue a user-authored bug report through the existing consent-aware telemetry outboxes.
/// The caller passes the log only after showing its preview and obtaining a separate choice.
/// Success is a bitfield: bits 0..1 were queued (official, MCHorizonUWP), bits 8..9 declined
/// (for example, a daily cap), and bits 16..17 failed to write. Negative values are input,
/// consent, validation, or queue failures.
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_uwp_bug_report_submit(
    game_dir: *const u16,
    game_dir_len: usize,
    report: *const u16,
    report_len: usize,
    previewed_log: *const u16,
    previewed_log_len: usize,
    attach_log: i32,
) -> i32 {
    use std::os::windows::ffi::OsStringExt;

    if game_dir.is_null()
        || report.is_null()
        || game_dir_len > 32767
        || report_len > 16384
        || previewed_log_len > 131072
        || !matches!(attach_log, 0 | 1)
        || (previewed_log_len != 0 && previewed_log.is_null())
    {
        return -1;
    }
    if attach_log == 1 && (previewed_log.is_null() || previewed_log_len == 0) {
        return -4;
    }

    let dir = std::path::PathBuf::from(std::ffi::OsString::from_wide(unsafe {
        std::slice::from_raw_parts(game_dir, game_dir_len)
    }));
    let Ok(report) = String::from_utf16(unsafe { std::slice::from_raw_parts(report, report_len) })
    else {
        return -1;
    };
    let log = if previewed_log_len == 0 {
        None
    } else {
        match String::from_utf16(unsafe {
            std::slice::from_raw_parts(previewed_log, previewed_log_len)
        }) {
            Ok(log) => Some(log),
            Err(_) => return -1,
        }
    };

    match horizon_client::telemetry::queue_uwp_bug_report(
        &dir,
        &report,
        log.as_deref(),
        attach_log == 1,
    ) {
        Ok(result) => (result.queued | (result.declined << 8) | (result.failed << 16)) as i32,
        Err(message)
            if message.contains("Confirm telemetry") || message.contains("Enable Bug reports") =>
        {
            -2
        }
        Err(message) if message.contains("Review the latest log") => -4,
        Err(message) if message.starts_with("Write") || message.contains("at most") => -3,
        Err(_) => -1,
    }
}

#[cfg(all(
    target_vendor = "uwp",
    feature = "xaml-shell",
    feature = "uwp-controller"
))]
thread_local! {
    static CONTROLLER_NAVIGATION: std::cell::RefCell<Option<horizon_client::uwp_controller::ControllerTest>> = const { std::cell::RefCell::new(None) };
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_controller_navigation_read() -> u32 {
    #[cfg(feature = "uwp-controller")]
    {
        CONTROLLER_NAVIGATION.with(|slot| {
            let mut slot = slot.borrow_mut();
            if slot.is_none() {
                *slot = horizon_client::uwp_controller::ControllerTest::new().ok();
            }
            slot.as_mut().map_or(0, |test| test.navigation())
        })
    }
    #[cfg(not(feature = "uwp-controller"))]
    {
        0
    }
}

#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_controller_navigation_stop() {
    #[cfg(feature = "uwp-controller")]
    CONTROLLER_NAVIGATION.with(|slot| *slot.borrow_mut() = None);
}

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
    fn horizon_uwp_memory_usage(used: *mut u64, limit: *mut u64) -> i32;
    fn horizon_uwp_fatal_begin(text: *const u8, len: usize);
    fn horizon_uwp_fatal_done() -> i32;
    fn horizon_uwp_is_ui_thread() -> i32;
    fn horizon_uwp_pump_events();
    fn horizon_uwp_is_local_test() -> i32;
    #[cfg(feature = "uwp-account")]
    fn horizon_uwp_discard_pregame_input();
    #[cfg(feature = "xaml-shell")]
    fn horizon_uwp_page_selected_server(buffer: *mut u8, capacity: usize) -> usize;
    #[cfg(feature = "xaml-shell")]
    fn horizon_uwp_page_selected_world(buffer: *mut u8, capacity: usize) -> usize;
    #[cfg(feature = "xaml-shell")]
    fn horizon_uwp_page_selected_benchmark() -> i32;
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
    let adapter_info = adapter.get_info();
    if adapter_info.backend != expected_backend {
        return 0;
    }
    // On ARM32 Mobile, WARP passes device/presentation checks but exhausts
    // the process address space during game startup. Recheck on each launch
    // so an actual D3D12 hardware driver can enable the option later.
    #[cfg(target_arch = "arm")]
    if renderer == 2 && adapter_info.device_type == wgpu::DeviceType::Cpu {
        trace_renderer_probe("DX12 probe: ARM32 software adapter is not supported");
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
    if renderer == 2 && !probe_dx12_pipeline(&device, configuration.format) {
        trace_renderer_probe("DX12 probe: shader pipeline creation failed");
        return 0;
    }
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

// A device and swap chain alone do not prove that the D3D12 driver can compile
// and create a graphics pipeline. Treat any error as an unavailable renderer,
// while leaving the other backends usable.
#[cfg(all(
    target_vendor = "uwp",
    feature = "xaml-shell",
    feature = "uwp-dx12-renderer"
))]
fn probe_dx12_pipeline(device: &wgpu::Device, format: wgpu::TextureFormat) -> bool {
    let oom = device.push_error_scope(wgpu::ErrorFilter::OutOfMemory);
    let internal = device.push_error_scope(wgpu::ErrorFilter::Internal);
    let validation = device.push_error_scope(wgpu::ErrorFilter::Validation);
    let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
        label: Some("Horizon D3D12 compatibility probe"),
        source: wgpu::ShaderSource::Wgsl(
            "@vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {\n\
             let x = f32(i32(i) - 1); return vec4f(x, x, 0.0, 1.0); }\n\
             @fragment fn fs() -> @location(0) vec4f { return vec4f(0.0, 0.0, 0.0, 1.0); }"
                .into(),
        ),
    });
    let _pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
        label: Some("Horizon D3D12 compatibility probe"),
        layout: None,
        vertex: wgpu::VertexState {
            module: &shader,
            entry_point: Some("vs"),
            compilation_options: Default::default(),
            buffers: &[],
        },
        primitive: Default::default(),
        depth_stencil: None,
        multisample: Default::default(),
        fragment: Some(wgpu::FragmentState {
            module: &shader,
            entry_point: Some("fs"),
            compilation_options: Default::default(),
            targets: &[Some(wgpu::ColorTargetState {
                format,
                blend: None,
                write_mask: wgpu::ColorWrites::ALL,
            })],
        }),
        multiview_mask: None,
        cache: None,
    });
    let validation_error = pollster::block_on(validation.pop());
    let internal_error = pollster::block_on(internal.pop());
    let oom_error = pollster::block_on(oom.pop());
    validation_error.is_none() && internal_error.is_none() && oom_error.is_none()
}

#[cfg(all(
    target_vendor = "uwp",
    feature = "xaml-shell",
    not(feature = "uwp-dx12-renderer")
))]
fn probe_dx12_pipeline(_: &wgpu::Device, _: wgpu::TextureFormat) -> bool {
    false
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
pub unsafe extern "C" fn horizon_uwp_server_icon_read(
    game_dir: *const u16,
    game_dir_len: usize,
    address: *const u8,
    address_len: usize,
    output: *mut u8,
    capacity: usize,
) -> isize {
    use std::os::windows::ffi::OsStringExt;
    if game_dir.is_null()
        || address.is_null()
        || output.is_null()
        || game_dir_len > 32767
        || address_len > 1024
    {
        return -1;
    }
    let game = std::path::PathBuf::from(std::ffi::OsString::from_wide(unsafe {
        std::slice::from_raw_parts(game_dir, game_dir_len)
    }));
    let Ok(address) =
        std::str::from_utf8(unsafe { std::slice::from_raw_parts(address, address_len) })
    else {
        return -1;
    };
    let Some(icon) = horizon_protocol::servers::load(&game.join("servers.dat"))
        .into_iter()
        .find(|entry| entry.ip == address)
        .and_then(|entry| entry.icon)
        .and_then(|value| server_status::decode_saved_icon(&value))
    else {
        return 0;
    };
    if icon.len() > capacity {
        return -1;
    }
    unsafe { std::ptr::copy_nonoverlapping(icon.as_ptr(), output, icon.len()) };
    icon.len() as isize
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
        // Test packages contain an explicit isolation marker and have no real
        // player data. Capture the full panic there to diagnose renderer
        // validation failures; regular packages must not record payloads,
        // which can contain credentials or server responses.
        if unsafe { horizon_uwp_is_local_test() != 0 } {
            let diagnostic = info.to_string();
            unsafe { horizon_uwp_report_error(diagnostic.as_ptr(), diagnostic.len()) };
        }
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
    match xaml_pregame::start(&game_dir) {
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
        let mut world = [0u8; 1024];
        let length = horizon_uwp_page_selected_world(world.as_mut_ptr(), world.len());
        let name = world
            .get(..length)
            .and_then(|bytes| std::str::from_utf8(bytes).ok())
            .unwrap_or("");
        std::env::set_var("HORIZON_UWP_QUICK_WORLD", name);
        let benchmark = horizon_uwp_page_selected_benchmark() != 0;
        std::env::set_var(
            "HORIZON_UWP_BENCHMARK",
            if benchmark { "standard" } else { "" },
        );
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
            let detail = error.to_string();
            let mut message = if detail.to_ascii_uppercase().contains("0X8007000E") {
                format!(
                    "Windows reported insufficient memory (0x8007000E) while Horizon was running. Reducing the render distance may help. Details: {detail}"
                )
            } else {
                format!("Horizon UWP encountered an error: {detail}")
            };
            let (mut used, mut limit) = (0u64, 0u64);
            if unsafe { horizon_uwp_memory_usage(&mut used, &mut limit) } != 0 {
                message.push_str(&format!(
                    "\nApp memory: {} MiB of {} MiB limit.",
                    used / 1_048_576,
                    limit / 1_048_576
                ));
            }
            report_failure(&message);
            1
        }
    }
}
