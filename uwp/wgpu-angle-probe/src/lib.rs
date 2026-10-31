use std::{ffi::c_void, ptr::NonNull};
use raw_window_handle::{RawWindowHandle, WinRtWindowHandle};

struct Probe {
    surface: wgpu::Surface<'static>,
    device: wgpu::Device,
    queue: wgpu::Queue,
}

fn create(window: *mut c_void) -> Result<Probe, String> {
    let ptr = NonNull::new(window).ok_or("null CoreWindow")?;
    let mut descriptor = wgpu::InstanceDescriptor::new_without_display_handle();
    descriptor.backends = wgpu::Backends::GL;
    let instance = wgpu::Instance::new(descriptor);
    let surface = unsafe { instance.create_surface_unsafe(wgpu::SurfaceTargetUnsafe::RawHandle {
        raw_display_handle: None,
        raw_window_handle: RawWindowHandle::WinRt(WinRtWindowHandle::new(ptr)),
    }) }.map_err(|e| e.to_string())?;
    let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
        compatible_surface: Some(&surface), ..Default::default()
    })).map_err(|e| e.to_string())?;
    let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor::default()))
        .map_err(|e| e.to_string())?;
    let config = surface.get_default_config(&adapter, 640, 480).ok_or("no surface configuration")?;
    surface.configure(&device, &config);
    Ok(Probe { surface, device, queue })
}

#[no_mangle]
pub extern "C" fn wgpu_probe_create(window: *mut c_void) -> *mut c_void {
    match create(window) {
        Ok(probe) => Box::into_raw(Box::new(probe)).cast(),
        Err(error) => { eprintln!("wgpu UWP probe: {error}"); std::ptr::null_mut() }
    }
}

#[no_mangle]
pub unsafe extern "C" fn wgpu_probe_render(probe: *mut c_void) -> i32 {
    let probe = unsafe { &*(probe as *const Probe) };
    let frame = match probe.surface.get_current_texture() {
        wgpu::CurrentSurfaceTexture::Success(frame) | wgpu::CurrentSurfaceTexture::Suboptimal(frame) => frame,
        _ => return -1,
    };
    let view = frame.texture.create_view(&Default::default());
    let mut encoder = probe.device.create_command_encoder(&Default::default());
    {
        let _pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                view: &view, resolve_target: None, depth_slice: None,
                ops: wgpu::Operations { load: wgpu::LoadOp::Clear(wgpu::Color { r: 0.1, g: 0.35, b: 0.1, a: 1.0 }), store: wgpu::StoreOp::Store },
            })], ..Default::default()
        });
    }
    probe.queue.submit([encoder.finish()]);
    probe.queue.present(frame);
    0
}

#[no_mangle]
pub unsafe extern "C" fn wgpu_probe_destroy(probe: *mut c_void) {
    if !probe.is_null() { drop(unsafe { Box::from_raw(probe as *mut Probe) }); }
}
