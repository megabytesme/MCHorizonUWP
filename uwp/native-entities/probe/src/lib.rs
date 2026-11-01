//! UWP readback of actual Horizon entity vertex conversion and atlas paging.
use horizon_client_render::mesher::Vertex;
use horizon_native_entities::d3d11::EntityRenderer;
use horizon_native_hud::Atlas;
use horizon_uwp_d3d11::{DepthConvention, Renderer};
use std::ffi::c_void;

#[repr(C)]
#[derive(Default)]
pub struct Report {
    status: i32,
    width: u32,
    height: u32,
    non_black: u64,
    first: [[u8; 4]; 4],
    patched: [[u8; 4]; 4],
}

unsafe fn run(window: *mut c_void) -> Result<Report, Box<dyn std::error::Error>> {
    let mut renderer = unsafe { Renderer::new(window) }?;
    renderer.set_depth_convention(DepthConvention::Reverse);
    // Two atlas pages: the left page is red and the right page green.
    let mut atlas = Atlas::new(
        [4, 2],
        &[
            255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 0,
            0, 255, 0, 255, 0, 255, 0, 255, 0, 255,
        ],
        2,
    )?;
    let mut entities = EntityRenderer::new(&atlas);
    let vertex = |pos, uv| Vertex {
        pos,
        uv,
        layer: 0,
        color: 0xffffffff,
        light: 0xf0,
        normal: [0; 4],
        glint_uv: [0.; 2],
    };
    let quad = [
        vertex([-1., -1., 0.5], [0., 1.]),
        vertex([1., -1., 0.5], [1., 1.]),
        vertex([1., 1., 0.5], [1., 0.]),
        vertex([-1., 1., 0.5], [0., 0.]),
    ];
    renderer.begin_frame([0., 0., 0., 1.])?;
    entities.draw_vertices(&renderer, &atlas, &[], &quad, glam::Mat4::IDENTITY, false)?;
    let first = renderer.probe_pixels()?;
    let (width, height, non_black) = renderer.probe_non_black_pixels()?;
    let patches = atlas.patch([0, 0], [2, 2], &[0, 0, 255, 255].repeat(4))?;
    renderer.begin_frame([0., 0., 0., 1.])?;
    entities.draw_vertices(
        &renderer,
        &atlas,
        &patches,
        &quad,
        glam::Mat4::IDENTITY,
        false,
    )?;
    let patched = renderer.probe_pixels()?;
    let ok = first[0][0] > 200
        && first[0][1] < 10
        && first[1][1] > 200
        && first[1][0] < 10
        && patched[0][2] > 200
        && patched[1][1] > 200
        && non_black >= u64::from(width) * u64::from(height) * 9 / 10;
    renderer.present()?;
    Ok(Report {
        status: if ok { 0 } else { 2 },
        width,
        height,
        non_black,
        first,
        patched,
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn entity_probe_run(window: *mut c_void, report: *mut Report) -> i32 {
    if window.is_null() || report.is_null() {
        return -1;
    }
    match unsafe { run(window) } {
        Ok(result) => {
            let status = result.status;
            unsafe {
                *report = result;
            }
            status
        }
        Err(_) => {
            unsafe {
                (*report).status = 1;
            }
            1
        }
    }
}
