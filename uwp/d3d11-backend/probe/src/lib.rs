use horizon_uwp_d3d11::{Mesh, Renderer, Texture, Vertex};
use std::ffi::c_void;

unsafe extern "C" {
    fn horizon_d3d11_probe_report(text: *const u8, len: usize);
}

struct Probe {
    renderer: Renderer,
    mesh: Mesh,
    occluded: Mesh,
    atlas: Texture,
    checked: bool,
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_d3d11_probe_create(window: *mut c_void) -> *mut c_void {
    let make = || -> windows_result::Result<Probe> {
        let renderer = unsafe { Renderer::new(window)? };
        let white = [1.0; 4];
        let vertices = [
            Vertex {
                position: [-0.75, -0.75, 0.5],
                uv: [0.0, 1.0],
                color: white,
            },
            Vertex {
                position: [-0.75, 0.75, 0.5],
                uv: [0.0, 0.0],
                color: white,
            },
            Vertex {
                position: [0.75, -0.75, 0.5],
                uv: [1.0, 1.0],
                color: white,
            },
            Vertex {
                position: [0.75, 0.75, 0.5],
                uv: [1.0, 0.0],
                color: white,
            },
        ];
        let mesh = renderer.create_mesh(&vertices, &[0, 1, 2, 2, 1, 3])?;
        // This solid yellow quad is drawn second, behind the first one. If
        // depth testing fails, it overwrites the four expected colours.
        let behind = vertices.map(|mut vertex| {
            vertex.position[2] = 0.8;
            vertex.uv = [0.75, 0.75];
            vertex
        });
        let occluded = renderer.create_mesh(&behind, &[0, 1, 2, 2, 1, 3])?;
        let atlas = renderer.create_atlas(
            2,
            2,
            &[
                255, 70, 70, 255, 70, 255, 70, 255, 70, 70, 255, 255, 255, 230, 70, 255,
            ],
        )?;
        Ok(Probe {
            renderer,
            mesh,
            occluded,
            atlas,
            checked: false,
        })
    };
    match make() {
        Ok(probe) => Box::into_raw(Box::new(probe)).cast(),
        Err(_) => std::ptr::null_mut(),
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_d3d11_probe_render(probe: *mut c_void) -> bool {
    let Some(probe) = (unsafe { probe.cast::<Probe>().as_mut() }) else {
        return false;
    };
    if probe.renderer.begin_frame([0.04, 0.18, 0.28, 1.0]).is_err() {
        return false;
    }
    let identity = [
        [1.0, 0.0, 0.0, 0.0],
        [0.0, 1.0, 0.0, 0.0],
        [0.0, 0.0, 1.0, 0.0],
        [0.0, 0.0, 0.0, 1.0],
    ];
    probe.renderer.draw(&probe.mesh, &probe.atlas, identity);
    probe.renderer.draw(&probe.occluded, &probe.atlas, identity);
    if !probe.checked {
        let message = match probe.renderer.probe_pixels() {
            Ok(pixels) => {
                let expected = [
                    [255u8, 70, 70, 255],
                    [70, 255, 70, 255],
                    [70, 70, 255, 255],
                    [255, 230, 70, 255],
                ];
                let pass = pixels.iter().zip(expected).all(|(actual, expected)| {
                    actual
                        .iter()
                        .zip(expected)
                        .all(|(&a, b)| a.abs_diff(b) <= 5)
                });
                format!(
                    "{}: pixels={pixels:?}\n",
                    if pass { "PASS" } else { "FAIL" }
                )
            }
            Err(error) => format!("FAIL: readback error {error}\n"),
        };
        unsafe {
            horizon_d3d11_probe_report(message.as_ptr(), message.len());
        }
        probe.checked = true;
    }
    probe.renderer.present().is_ok()
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_d3d11_probe_resize(probe: *mut c_void) -> bool {
    let Some(probe) = (unsafe { probe.cast::<Probe>().as_mut() }) else {
        return false;
    };
    probe.renderer.resize().is_ok()
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_d3d11_probe_destroy(probe: *mut c_void) {
    if !probe.is_null() {
        drop(unsafe { Box::from_raw(probe.cast::<Probe>()) });
    }
}
