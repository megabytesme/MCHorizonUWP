//! Isolated D3D11 panorama GPU readback diagnostic.
use std::ffi::c_void;
use horizon_native_panorama::Panorama;
use horizon_uwp_d3d11::Renderer;

#[repr(C)]
#[derive(Default)]
pub struct Report {
    pub status: i32,
    pub width: u32,
    pub height: u32,
    pub non_black: u64,
    pub first: [[u8; 4]; 4],
    pub rotated: [[u8; 4]; 4],
}

unsafe fn run(window: *mut c_void) -> Result<Report, Box<dyn std::error::Error>> {
    let renderer = unsafe { Renderer::new(window) }?;
    let colors = [[255,0,0,255], [0,255,0,255], [0,0,255,255],
                  [255,255,0,255], [255,0,255,255], [0,255,255,255]];
    let pixels: Vec<u8> = colors.into_iter().flat_map(|c| c.repeat(4)).collect();
    let panorama = Panorama::new(&renderer, 2, &pixels)?;
    renderer.begin_frame([0., 0., 0., 1.])?;
    // Use the actual back buffer size, not a guessed logical window size.
    let (width, height, _) = renderer.probe_non_black_pixels()?;
    panorama.draw(&renderer, [width, height], 0., 0.)?;
    let first = renderer.probe_pixels()?;
    let (_, _, non_black) = renderer.probe_non_black_pixels()?;
    renderer.begin_frame([0., 0., 0., 1.])?;
    panorama.draw(&renderer, [width, height], 90., 0.)?;
    let rotated = renderer.probe_pixels()?;
    let (_, _, rotated_non_black) = renderer.probe_non_black_pixels()?;
    renderer.present()?;
    let area = u64::from(width) * u64::from(height);
    let covered = non_black >= area * 9 / 10 && rotated_non_black >= area * 9 / 10;
    Ok(Report { status: if covered && first != rotated { 0 } else { 2 }, width, height, non_black, first, rotated })
}

/// `window` must be a live CoreWindow owned by this thread; `report` must be
/// valid and writable. Returns zero only after successful GPU readback.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn panorama_probe_run(window: *mut c_void, report: *mut Report) -> i32 {
    if window.is_null() || report.is_null() { return -1; }
    match unsafe { run(window) } {
        Ok(result) => { let status = result.status; unsafe { *report = result; } status },
        Err(_) => { unsafe { (*report).status = 1; } 1 },
    }
}
