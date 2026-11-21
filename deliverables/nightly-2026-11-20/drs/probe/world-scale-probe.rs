use std::{
    sync::{OnceLock, mpsc},
    time::Duration,
};
use wgpu::{util::DeviceExt, *};

#[path = "renderer/fl91_world_scale.rs"]
mod fl91_world_scale;

struct GpuChecks {
    memory: ErrorScopeGuard,
    validation: ErrorScopeGuard,
}

impl GpuChecks {
    fn push(device: &Device) -> Self {
        let memory = device.push_error_scope(ErrorFilter::OutOfMemory);
        let validation = device.push_error_scope(ErrorFilter::Validation);
        Self { memory, validation }
    }

    fn pop(self) -> Option<Error> {
        let validation = pollster::block_on(self.validation.pop());
        let memory = pollster::block_on(self.memory.pop());
        memory.or(validation)
    }
}

const COLOR: TextureFormat = TextureFormat::Bgra8Unorm;
const DEPTH: TextureFormat = TextureFormat::Depth24Plus;
type Report = extern "C" fn(*const u8, usize);
static REPORT: OnceLock<Report> = OnceLock::new();

fn report(line: &str) {
    let line = format!("{line}\n");
    if let Some(output) = REPORT.get() {
        output(line.as_ptr(), line.len());
    }
}

const FIXTURE: &str = r#"
struct VertexOut {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
}
@vertex fn vs_main(@location(0) p: vec2<f32>) -> VertexOut {
    return VertexOut(vec4(p, 0.5, 1.0), p * vec2(0.5, -0.5) + vec2(0.5));
}
@fragment fn fs_world(in: VertexOut) -> @location(0) vec4<f32> {
    return vec4(in.uv.x, in.uv.y, 0.25, 1.0);
}
@fragment fn fs_hud(in: VertexOut) -> @location(0) vec4<f32> {
    return vec4(1.0, 0.0, 1.0, 1.0);
}
"#;

fn pipeline(device: &Device, shader: &ShaderModule, draws_world: bool) -> RenderPipeline {
    let layout = device.create_pipeline_layout(&PipelineLayoutDescriptor::default());
    device.create_render_pipeline(&RenderPipelineDescriptor {
        label: Some("World scale probe"),
        layout: Some(&layout),
        vertex: VertexState {
            module: shader,
            entry_point: Some("vs_main"),
            compilation_options: Default::default(),
            buffers: &[Some(VertexBufferLayout {
                array_stride: 8,
                step_mode: VertexStepMode::Vertex,
                attributes: &vertex_attr_array![0 => Float32x2],
            })],
        },
        primitive: Default::default(),
        depth_stencil: draws_world.then_some(DepthStencilState {
            format: DEPTH,
            depth_write_enabled: Some(true),
            depth_compare: Some(CompareFunction::GreaterEqual),
            stencil: Default::default(),
            bias: Default::default(),
        }),
        multisample: Default::default(),
        fragment: Some(FragmentState {
            module: shader,
            entry_point: Some(if draws_world { "fs_world" } else { "fs_hud" }),
            compilation_options: Default::default(),
            targets: &[Some(ColorTargetState {
                format: COLOR,
                blend: None,
                write_mask: ColorWrites::ALL,
            })],
        }),
        multiview_mask: None,
        cache: None,
    })
}

fn extent([width, height]: [u32; 2]) -> Extent3d {
    Extent3d {
        width,
        height,
        depth_or_array_layers: 1,
    }
}

fn fixture_texture(
    device: &Device,
    size: [u32; 2],
    format: TextureFormat,
    usage: TextureUsages,
) -> Texture {
    device.create_texture(&TextureDescriptor {
        label: Some("World scale fixture"),
        size: extent(size),
        mip_level_count: 1,
        sample_count: 1,
        dimension: TextureDimension::D2,
        format,
        usage,
        view_formats: &[],
    })
}

struct ReportLogger;

impl log::Log for ReportLogger {
    fn enabled(&self, _: &log::Metadata<'_>) -> bool {
        true
    }
    fn log(&self, record: &log::Record<'_>) {
        if record.target().contains("fl91_world_scale") {
            report(&record.args().to_string());
        }
    }
    fn flush(&self) {}
}

#[unsafe(no_mangle)]
pub extern "C" fn wgpu_world_scale_probe(output: Report) -> i32 {
    REPORT.set(output).unwrap();
    std::panic::set_hook(Box::new(|panic| report(&format!("PANIC {panic}"))));
    // Windows synchronizes its process environment. Set the backend flags before opening wgpu.
    #[cfg(target_vendor = "uwp")]
    unsafe {
        std::env::set_var("WGPU_DX11_FORCE_FL9_1", "1");
        std::env::set_var("WGPU_DX11_REQUIRE_HARDWARE", "1");
    }
    match run() {
        Ok(()) => {
            report("overall=PASS");
            0
        }
        Err(error) => {
            report(&format!("overall=FAIL {error}"));
            1
        }
    }
}

#[cfg(not(target_vendor = "uwp"))]
fn main() {
    extern "C" fn stdout(data: *const u8, len: usize) {
        use std::io::Write;
        let data = unsafe { std::slice::from_raw_parts(data, len) };
        let mut stdout = std::io::stdout().lock();
        let _ = stdout.write_all(data);
        let _ = stdout.flush();
    }
    std::process::exit(wgpu_world_scale_probe(stdout));
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    log::set_logger(&ReportLogger).unwrap();
    log::set_max_level(log::LevelFilter::Info);
    let instance = Instance::new(InstanceDescriptor {
        backends: Backends::DX11,
        ..InstanceDescriptor::new_without_display_handle()
    });
    let adapter = pollster::block_on(instance.request_adapter(&RequestAdapterOptions {
        power_preference: PowerPreference::HighPerformance,
        ..Default::default()
    }))?;
    let info = adapter.get_info();
    assert_eq!(info.backend, Backend::Dx11);
    assert_ne!(info.device_type, DeviceType::Cpu);
    if std::env::var_os("WGPU_DX11_FORCE_FL9_1").is_some() {
        assert_eq!(info.driver_info.split(';').next(), Some("FL9_1"));
    }
    report(&format!(
        "adapter={info:?}; max_texture_dimension_2d={}",
        adapter.limits().max_texture_dimension_2d
    ));
    let (device, queue) = pollster::block_on(adapter.request_device(&DeviceDescriptor {
        label: Some("FL9_1 world scaling probe"),
        required_limits: adapter.limits(),
        ..Default::default()
    }))?;
    device.on_uncaptured_error(std::sync::Arc::new(|error| {
        panic!("wgpu uncaptured error {error}")
    }));
    let shader = device.create_shader_module(ShaderModuleDescriptor {
        label: Some("World scale fixture"),
        source: ShaderSource::Wgsl(FIXTURE.into()),
    });
    let world = pipeline(&device, &shader, true);
    let hud = pipeline(&device, &shader, false);
    let bytes: Vec<_> = [-1.0f32, -1.0, 3.0, -1.0, -1.0, 3.0]
        .into_iter()
        .flat_map(|v| v.to_ne_bytes())
        .collect();
    let fullscreen = device.create_buffer_init(&util::BufferInitDescriptor {
        label: Some("Fullscreen triangle"),
        contents: &bytes,
        usage: BufferUsages::VERTEX,
    });
    let mut scaler =
        fl91_world_scale::WorldScale::new(&adapter, &device, COLOR, DEPTH, &fullscreen)
            .expect("a world-scale pipeline on this hardware");
    for (output, selected, expected) in [
        ([161, 95], 1.0, [161, 95]),
        ([161, 95], 0.75, [121, 71]),
        ([161, 95], 0.67, [108, 64]),
        ([161, 95], 0.5, [81, 48]),
        ([161, 95], 0.5, [81, 48]),
        ([95, 161], 0.5, [48, 81]),
        ([95, 161], 1.0, [95, 161]),
        ([2048, 1152], 0.5, [1024, 576]),
    ] {
        let [width, height] = output;
        let presentation = fixture_texture(
            &device,
            output,
            COLOR,
            TextureUsages::RENDER_ATTACHMENT | TextureUsages::COPY_SRC,
        );
        let presentation_view = presentation.create_view(&Default::default());
        let scaled = scaler.prepare(&device, output, selected);
        let direct_depth = scaled.is_none().then(|| {
            fixture_texture(&device, output, DEPTH, TextureUsages::RENDER_ATTACHMENT)
                .create_view(&Default::default())
        });
        let (scene, depth) = scaled.as_ref().map_or_else(
            || (&presentation_view, direct_depth.as_ref().unwrap()),
            |(color, depth)| (color, depth),
        );
        let size = |t: &TextureView| [t.texture().width(), t.texture().height()];
        assert_eq!(size(scene), expected);
        assert_eq!(size(depth), expected);
        let mut encoder = device.create_command_encoder(&Default::default());
        {
            let mut pass = encoder.begin_render_pass(&RenderPassDescriptor {
                color_attachments: &[Some(color_attachment(scene, LoadOp::Clear(Color::BLACK)))],
                depth_stencil_attachment: Some(RenderPassDepthStencilAttachment {
                    view: depth,
                    depth_ops: Some(Operations {
                        load: LoadOp::Clear(0.0),
                        store: StoreOp::Store,
                    }),
                    stencil_ops: None,
                }),
                ..Default::default()
            });
            pass.set_pipeline(&world);
            pass.set_vertex_buffer(0, fullscreen.slice(..));
            pass.draw(0..3, 0..1);
        }
        if scaled.is_some() {
            scaler.finish(&mut encoder, &presentation_view);
        }
        {
            let mut pass = encoder.begin_render_pass(&RenderPassDescriptor {
                color_attachments: &[Some(color_attachment(&presentation_view, LoadOp::Load))],
                ..Default::default()
            });
            pass.set_pipeline(&hud);
            pass.set_vertex_buffer(0, fullscreen.slice(..));
            pass.set_scissor_rect(2, 3, 1, 1);
            pass.draw(0..3, 0..1);
        }
        let row = (width * 4).next_multiple_of(COPY_BYTES_PER_ROW_ALIGNMENT);
        let readback = device.create_buffer(&BufferDescriptor {
            label: Some("World scale readback"),
            size: (row * height) as u64,
            usage: BufferUsages::COPY_DST | BufferUsages::MAP_READ,
            mapped_at_creation: false,
        });
        encoder.copy_texture_to_buffer(
            presentation.as_image_copy(),
            TexelCopyBufferInfo {
                buffer: &readback,
                layout: TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(row),
                    rows_per_image: Some(height),
                },
            },
            extent(output),
        );
        let submission = queue.submit([encoder.finish()]);
        let (tx, rx) = mpsc::channel();
        let slice = readback.slice(..);
        slice.map_async(MapMode::Read, move |result| {
            let _ = tx.send(result);
        });
        let timeout = Duration::from_secs(15);
        device.poll(PollType::Wait {
            submission_index: Some(submission),
            timeout: Some(timeout),
        })?;
        rx.recv_timeout(timeout)??;
        let pixels = slice.get_mapped_range()?;
        let pixel = |x: u32, y: u32| {
            let offset = (row * y + 4 * x) as usize;
            [
                pixels[offset + 2],
                pixels[offset + 1],
                pixels[offset],
                pixels[offset + 3],
            ]
        };
        let marker = [255, 0, 255, 255];
        assert_eq!(pixel(2, 3), marker);
        for (x, y) in [
            (1, 3),
            (3, 3),
            (2, 2),
            (2, 4),
            (width / 4, height / 3),
            (width * 3 / 4, height * 3 / 4),
        ] {
            let expected_r = ((x as f32 + 0.5) * 255.0 / width as f32).round() as u8;
            let expected_g = ((y as f32 + 0.5) * 255.0 / height as f32).round() as u8;
            let actual = pixel(x, y);
            assert!(
                actual[0].abs_diff(expected_r) <= 2,
                "x {x}, y {y}, {actual:?}"
            );
            assert!(
                actual[1].abs_diff(expected_g) <= 2,
                "x {x}, y {y}, {actual:?}"
            );
            assert_eq!([actual[2], actual[3]], [64, 255]);
        }
        report(&format!(
            "PASS: world color and depth {}x{}, output {}x{}, 1-pixel HUD and world gradient intact",
            expected[0], expected[1], output[0], output[1]
        ));
    }
    Ok(())
}

fn color_attachment(view: &TextureView, load: LoadOp<Color>) -> RenderPassColorAttachment<'_> {
    RenderPassColorAttachment {
        view,
        resolve_target: None,
        depth_slice: None,
        ops: Operations {
            load,
            store: StoreOp::Store,
        },
    }
}
