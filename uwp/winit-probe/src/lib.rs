//! Runs a winit-driven UWP window and clears a wgpu surface through ANGLE.
use std::sync::Arc;
use winit::application::ApplicationHandler;
use winit::event::WindowEvent;
use winit::event_loop::{ActiveEventLoop, EventLoop};
use winit::window::{Window, WindowId};

unsafe extern "C" { fn horizon_uwp_frame_presented(); }

struct Probe {
    window: Option<Arc<Window>>,
    surface: Option<wgpu::Surface<'static>>,
    device: Option<wgpu::Device>,
    queue: Option<wgpu::Queue>,
}
impl ApplicationHandler for Probe {
    fn resumed(&mut self, event_loop: &ActiveEventLoop) {
        let window = Arc::new(event_loop.create_window(Window::default_attributes()).expect("window"));
        let mut descriptor = wgpu::InstanceDescriptor::new_without_display_handle();
        descriptor.backends = wgpu::Backends::GL;
        let instance = wgpu::Instance::new(descriptor);
        let surface = instance.create_surface(window.clone()).expect("surface");
        let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
            compatible_surface: Some(&surface), ..Default::default()
        })).expect("adapter");
        let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
            required_limits: adapter.limits(), ..Default::default()
        })).expect("device");
        let size = window.inner_size();
        let config = surface.get_default_config(&adapter, size.width.max(1), size.height.max(1)).expect("config");
        surface.configure(&device, &config);
        self.window = Some(window);
        self.surface = Some(surface);
        self.device = Some(device);
        self.queue = Some(queue);
    }
    fn window_event(&mut self, event_loop: &ActiveEventLoop, _: WindowId, event: WindowEvent) {
        match event {
            WindowEvent::CloseRequested => event_loop.exit(),
            WindowEvent::Resized(size) if size.width > 0 && size.height > 0 => {
                if let (Some(surface), Some(device)) = (&self.surface, &self.device) {
                    let mut config = surface.get_configuration().unwrap();
                    config.width = size.width;
                    config.height = size.height;
                    surface.configure(device, &config);
                }
            }
            WindowEvent::RedrawRequested => {
                let (Some(surface), Some(device), Some(queue)) = (&self.surface, &self.device, &self.queue) else { return; };
                let frame = match surface.get_current_texture() {
                    wgpu::CurrentSurfaceTexture::Success(frame) | wgpu::CurrentSurfaceTexture::Suboptimal(frame) => frame,
                    _ => return,
                };
                let view = frame.texture.create_view(&Default::default());
                let mut encoder = device.create_command_encoder(&Default::default());
                {
                    let _pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                        color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                            view: &view, resolve_target: None, depth_slice: None,
                            ops: wgpu::Operations { load: wgpu::LoadOp::Clear(wgpu::Color { r: 0.2, g: 0.05, b: 0.4, a: 1.0 }), store: wgpu::StoreOp::Store },
                        })], ..Default::default()
                    });
                }
                queue.submit([encoder.finish()]);
                queue.present(frame);
                unsafe { horizon_uwp_frame_presented() };
            }
            _ => {}
        }
    }
    fn about_to_wait(&mut self, _: &ActiveEventLoop) {
        if let Some(window) = &self.window { window.request_redraw(); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_run(_: *const u16, _: usize, _: *const u16, _: usize) -> i32 {
    let mut app = Probe { window: None, surface: None, device: None, queue: None };
    match EventLoop::new().and_then(|event_loop| event_loop.run_app(&mut app)) {
        Ok(()) => 0,
        Err(_) => 1,
    }
}
