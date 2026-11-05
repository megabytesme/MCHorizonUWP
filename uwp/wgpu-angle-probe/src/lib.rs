use raw_window_handle::{
    RawDisplayHandle, RawWindowHandle, WinRtWindowHandle, WindowsDisplayHandle,
};
use std::{ffi::c_void, ptr::NonNull, sync::Mutex};

static LAST_ERROR: Mutex<String> = Mutex::new(String::new());
static ADAPTER_INFO: Mutex<String> = Mutex::new(String::new());

struct Probe {
    surface: wgpu::Surface<'static>,
    device: wgpu::Device,
    queue: wgpu::Queue,
    format: wgpu::TextureFormat,
}

fn create(window: *mut c_void) -> Result<Probe, String> {
    let ptr = NonNull::new(window).ok_or("null CoreWindow")?;
    let mut descriptor = wgpu::InstanceDescriptor::new_without_display_handle();
    descriptor.backends = wgpu::Backends::GL;
    let instance = wgpu::Instance::new(descriptor);
    let surface = unsafe {
        instance.create_surface_unsafe(wgpu::SurfaceTargetUnsafe::RawHandle {
            raw_display_handle: Some(RawDisplayHandle::Windows(WindowsDisplayHandle::new())),
            raw_window_handle: RawWindowHandle::WinRt(WinRtWindowHandle::new(ptr)),
        })
    }
    .map_err(|e| e.to_string())?;
    let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
        compatible_surface: Some(&surface),
        ..Default::default()
    }))
    .map_err(|e| e.to_string())?;
    let info = adapter.get_info();
    let limits = adapter.limits();
    *ADAPTER_INFO.lock().unwrap() = format!(
        "adapter={:?}; max_texture_array_layers={}; max_sampled_textures_per_shader_stage={}; max_storage_textures_per_shader_stage={}; max_storage_buffers_per_shader_stage={}; max_compute_workgroups_per_dimension={}",
        info, limits.max_texture_array_layers, limits.max_sampled_textures_per_shader_stage,
        limits.max_storage_textures_per_shader_stage, limits.max_storage_buffers_per_shader_stage,
        limits.max_compute_workgroups_per_dimension
    );
    // GLES adapters may report no compute support, so default WebGPU limits can
    // exceed the adapter's limits. Horizon also requests its adapter limits.
    let desc = wgpu::DeviceDescriptor {
        required_limits: adapter.limits(),
        ..Default::default()
    };
    let (device, queue) =
        pollster::block_on(adapter.request_device(&desc)).map_err(|e| e.to_string())?;
    let config = surface
        .get_default_config(&adapter, 640, 480)
        .ok_or("no surface configuration")?;
    surface.configure(&device, &config);
    Ok(Probe {
        surface,
        device,
        queue,
        format: config.format,
    })
}

/// Compile Horizon's actual screen-effects shader on the selected adapter.
#[no_mangle]
pub unsafe extern "C" fn wgpu_probe_horizon_shader(probe: *mut c_void) -> i32 {
    let Some(probe) = (unsafe { (probe as *const Probe).as_ref() }) else {
        return -1;
    };
    let scope = probe.device.push_error_scope(wgpu::ErrorFilter::Validation);
    let module = probe
        .device
        .create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("Horizon screen effects probe"),
            source: wgpu::ShaderSource::Wgsl(
                include_str!("../../../crates/horizon-client-render/src/screen.wgsl").into(),
            ),
        });
    // Mirror Renderer::new's explicit entity bind group layout.
    let stages = wgpu::ShaderStages::VERTEX_FRAGMENT;
    let texture = wgpu::BindingType::Texture {
        sample_type: wgpu::TextureSampleType::Float { filterable: true },
        view_dimension: wgpu::TextureViewDimension::D2,
        multisampled: false,
    };
    let sampler = wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering);
    let uniform = wgpu::BindingType::Buffer {
        ty: wgpu::BufferBindingType::Uniform,
        has_dynamic_offset: false,
        min_binding_size: None,
    };
    let entries: Vec<_> = [
        uniform, texture, sampler, texture, sampler, texture, sampler, texture,
    ]
    .into_iter()
    .enumerate()
    .map(|(binding, ty)| wgpu::BindGroupLayoutEntry {
        binding: binding as u32,
        visibility: stages,
        ty,
        count: None,
    })
    .collect();
    let bind_group_layout =
        probe
            .device
            .create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
                label: Some("Horizon entity bindings probe"),
                entries: &entries,
            });
    let layout = probe
        .device
        .create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("Horizon entity layout probe"),
            bind_group_layouts: &[Some(&bind_group_layout)],
            ..Default::default()
        });
    let attrs =
        wgpu::vertex_attr_array![0 => Float32x2, 1 => Float32x2, 2 => Float32x4, 3 => Float32x4];
    let buffers = [Some(wgpu::VertexBufferLayout {
        array_stride: 48,
        step_mode: wgpu::VertexStepMode::Vertex,
        attributes: &attrs,
    })];
    let _pipeline = probe
        .device
        .create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("Horizon screen effects probe"),
            layout: Some(&layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("vs_screen"),
                compilation_options: Default::default(),
                buffers: &buffers,
            },
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some("fs_screen"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format: probe.format,
                    blend: None,
                    write_mask: wgpu::ColorWrites::ALL,
                })],
            }),
            primitive: Default::default(),
            depth_stencil: None,
            multisample: Default::default(),
            multiview_mask: None,
            cache: None,
        });
    if let Some(error) = pollster::block_on(scope.pop()) {
        *LAST_ERROR.lock().unwrap() = error.to_string();
        return -1;
    }
    0
}

/// Compile the actual Horizon terrain pipeline against the active adapter.
#[no_mangle]
pub unsafe extern "C" fn wgpu_probe_terrain_shader(probe: *mut c_void) -> i32 {
    let Some(probe) = (unsafe { (probe as *const Probe).as_ref() }) else {
        return -1;
    };
    let scope = probe.device.push_error_scope(wgpu::ErrorFilter::Validation);
    let module = probe
        .device
        .create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("Horizon terrain shader probe"),
            source: wgpu::ShaderSource::Wgsl(
                include_str!("../../../crates/horizon-client-render/src/terrain.wgsl").into(),
            ),
        });
    let texture = |view_dimension| wgpu::BindingType::Texture {
        sample_type: wgpu::TextureSampleType::Float { filterable: true },
        view_dimension,
        multisampled: false,
    };
    let array = texture(wgpu::TextureViewDimension::D2Array);
    let sampler = wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering);
    let uniform = wgpu::BindingType::Buffer {
        ty: wgpu::BufferBindingType::Uniform,
        has_dynamic_offset: false,
        min_binding_size: None,
    };
    let entries: Vec<_> = [
        uniform,
        array,
        sampler,
        texture(wgpu::TextureViewDimension::D2),
        sampler,
        array,
        array,
        array,
        array,
        array,
        array,
        array,
    ]
    .into_iter()
    .enumerate()
    .map(|(binding, ty)| wgpu::BindGroupLayoutEntry {
        binding: binding as u32,
        visibility: wgpu::ShaderStages::VERTEX_FRAGMENT,
        ty,
        count: None,
    })
    .collect();
    let bindings = probe
        .device
        .create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("Horizon terrain bindings probe"),
            entries: &entries,
        });
    let layout = probe
        .device
        .create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("Horizon terrain layout probe"),
            bind_group_layouts: &[Some(&bindings)],
            ..Default::default()
        });
    let terrain_attributes =
        wgpu::vertex_attr_array![0=>Float32x3,1=>Uint16x2,2=>Uint16,4=>Uint8x2,3=>Unorm8x4];
    let instance_attributes = wgpu::vertex_attr_array![5=>Float32x4];
    let buffers = [
        Some(wgpu::VertexBufferLayout {
            array_stride: 24,
            step_mode: wgpu::VertexStepMode::Vertex,
            attributes: &terrain_attributes,
        }),
        Some(wgpu::VertexBufferLayout {
            array_stride: 16,
            step_mode: wgpu::VertexStepMode::Instance,
            attributes: &instance_attributes,
        }),
    ];
    let constants = [("SPRITE_ARRAYS", 1.0), ("SPRITE_SPLIT", 2048.0)];
    let _pipeline = probe
        .device
        .create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("Horizon terrain solid probe"),
            layout: Some(&layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("vs_terrain"),
                compilation_options: Default::default(),
                buffers: &buffers,
            },
            primitive: wgpu::PrimitiveState {
                topology: wgpu::PrimitiveTopology::TriangleList,
                cull_mode: Some(wgpu::Face::Back),
                ..Default::default()
            },
            depth_stencil: Some(wgpu::DepthStencilState {
                format: wgpu::TextureFormat::Depth32Float,
                depth_write_enabled: Some(true),
                depth_compare: Some(wgpu::CompareFunction::GreaterEqual),
                stencil: Default::default(),
                bias: Default::default(),
            }),
            multisample: Default::default(),
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some("fs_solid"),
                compilation_options: wgpu::PipelineCompilationOptions {
                    constants: &constants,
                    ..Default::default()
                },
                targets: &[Some(wgpu::ColorTargetState {
                    format: probe.format.remove_srgb_suffix(),
                    blend: None,
                    write_mask: wgpu::ColorWrites::ALL,
                })],
            }),
            multiview_mask: None,
            cache: None,
        });
    if let Some(error) = pollster::block_on(scope.pop()) {
        *LAST_ERROR.lock().unwrap() = error.to_string();
        return -1;
    }
    0
}

#[no_mangle]
pub extern "C" fn wgpu_probe_create(window: *mut c_void) -> *mut c_void {
    match create(window) {
        Ok(probe) => Box::into_raw(Box::new(probe)).cast(),
        Err(error) => {
            *LAST_ERROR.lock().unwrap() = error;
            std::ptr::null_mut()
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn wgpu_probe_render(probe: *mut c_void) -> i32 {
    let probe = unsafe { &*(probe as *const Probe) };
    let frame = match probe.surface.get_current_texture() {
        wgpu::CurrentSurfaceTexture::Success(frame)
        | wgpu::CurrentSurfaceTexture::Suboptimal(frame) => frame,
        _ => return -1,
    };
    let view = frame.texture.create_view(&Default::default());
    let mut encoder = probe.device.create_command_encoder(&Default::default());
    {
        let _pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                view: &view,
                resolve_target: None,
                depth_slice: None,
                ops: wgpu::Operations {
                    load: wgpu::LoadOp::Clear(wgpu::Color {
                        r: 0.1,
                        g: 0.35,
                        b: 0.1,
                        a: 1.0,
                    }),
                    store: wgpu::StoreOp::Store,
                },
            })],
            ..Default::default()
        });
    }
    probe.queue.submit([encoder.finish()]);
    probe.queue.present(frame);
    0
}

#[no_mangle]
pub unsafe extern "C" fn wgpu_probe_destroy(probe: *mut c_void) {
    if !probe.is_null() {
        drop(unsafe { Box::from_raw(probe as *mut Probe) });
    }
}

/// Copies the last creation error as a NUL-terminated UTF-8 string.
/// Pass a valid buffer of `len` bytes, or zero for `len` to obtain its size.
#[no_mangle]
pub unsafe extern "C" fn wgpu_probe_error(buf: *mut u8, len: usize) -> usize {
    let error = LAST_ERROR.lock().unwrap();
    let bytes = error.as_bytes();
    if len == 0 {
        return bytes.len();
    }
    let copied = bytes.len().min(len - 1);
    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, copied);
        *buf.add(copied) = 0;
    }
    copied
}

/// Copies adapter info and limits for the most recently created probe.
#[no_mangle]
pub unsafe extern "C" fn wgpu_probe_info(buf: *mut u8, len: usize) -> usize {
    let info = ADAPTER_INFO.lock().unwrap();
    let bytes = info.as_bytes();
    if len == 0 {
        return bytes.len();
    }
    let copied = bytes.len().min(len - 1);
    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, copied);
        *buf.add(copied) = 0;
    }
    copied
}
