use horizon_native_scene::TerrainScene;
use horizon_native_terrain::{Atlas, SourceVertex};
use horizon_uwp_d3d11::{DrawPass, Mesh, Renderer, Texture, Vertex};
use std::ffi::c_void;

unsafe extern "C" {
    fn horizon_d3d11_probe_report(text: *const u8, len: usize);
}

struct Probe {
    renderer: Renderer,
    mesh: Mesh,
    occluded: Mesh,
    translucent: Mesh,
    overlay: Mesh,
    terrain: TerrainScene,
    atlas: Texture,
    checked: bool,
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn horizon_d3d11_probe_create(window: *mut c_void) -> *mut c_void {
    let make = || -> Result<Probe, Box<dyn std::error::Error>> {
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
        let blended = behind.map(|mut vertex| {
            vertex.position[2] = 0.2;
            vertex.color[3] = 0.5;
            vertex
        });
        let translucent = renderer.create_mesh(&blended, &[0, 1, 2, 2, 1, 3])?;
        let overlay_vertices = behind.map(|mut vertex| {
            vertex.position[2] = 0.9;
            vertex.uv = [0.25, 0.25];
            vertex.color[3] = 0.5;
            vertex
        });
        let overlay = renderer.create_mesh(&overlay_vertices, &[0, 1, 2, 2, 1, 3])?;
        let atlas = renderer.create_atlas(
            2,
            2,
            &[
                255, 70, 70, 255, 70, 255, 70, 255, 70, 70, 255, 255, 255, 230, 70, 255,
            ],
        )?;
        let terrain_atlas = Atlas::from_layers(
            1,
            4,
            &[
                255, 70, 70, 255, 70, 255, 70, 255, 70, 70, 255, 255, 255, 230, 70, 255,
            ],
        )?;
        let mut terrain = TerrainScene::new(&renderer, terrain_atlas)?;
        let mut quads = Vec::new();
        for (layer, x0, x1, y0, y1) in [
            (0, -0.75, 0.0, 0.0, 0.75),
            (1, 0.0, 0.75, 0.0, 0.75),
            (2, -0.75, 0.0, -0.75, 0.0),
            (3, 0.0, 0.75, -0.75, 0.0),
        ] {
            for (x, y, u, v) in [
                (x0, y0, 0.0, 1.0),
                (x1, y0, 1.0, 1.0),
                (x1, y1, 1.0, 0.0),
                (x0, y1, 0.0, 0.0),
            ] {
                quads.push(SourceVertex {
                    position: [x, y, 0.5],
                    uv: [u, v],
                    layer,
                    color: 0x00ffffff,
                    light: 240,
                });
            }
        }
        terrain.upload(&renderer, (0, 0, 0), [0.0; 3], &quads, &[])?;
        if !terrain.has_section((0, 0, 0)) {
            return Err(std::io::Error::other("terrain upload was not retained").into());
        }
        terrain.remove_columns(&mut [(0, 0)].into_iter());
        if terrain.has_section((0, 0, 0)) {
            return Err(std::io::Error::other("terrain column was not removed").into());
        }
        terrain.upload(&renderer, (0, 0, 0), [0.0; 3], &quads, &[])?;
        Ok(Probe {
            renderer,
            mesh,
            occluded,
            translucent,
            overlay,
            terrain,
            atlas,
            checked: false,
        })
    };
    match make() {
        Ok(probe) => Box::into_raw(Box::new(probe)).cast(),
        Err(error) => {
            let message = format!("FAIL: Direct3D initialization: {error}\n");
            unsafe {
                horizon_d3d11_probe_report(message.as_ptr(), message.len());
            }
            std::ptr::null_mut()
        }
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
    let original = if !probe.checked {
        Some(probe.renderer.probe_pixels())
    } else {
        None
    };
    probe.renderer.draw_pass(
        &probe.translucent,
        &probe.atlas,
        identity,
        DrawPass::Translucent,
    );
    let blended = if !probe.checked {
        Some(probe.renderer.probe_pixels())
    } else {
        None
    };
    probe
        .renderer
        .draw_pass(&probe.overlay, &probe.atlas, identity, DrawPass::Overlay);
    if !probe.checked {
        let overlay_pixels = probe.renderer.probe_pixels();
        let terrain_pixels = probe
            .renderer
            .begin_frame([0.04, 0.18, 0.28, 1.0])
            .and_then(|_| {
                probe.terrain.draw(&probe.renderer, identity, [0.0; 3]);
                probe.renderer.probe_pixels()
            });
        let rejects_invalid_patch = probe
            .renderer
            .update_atlas(&probe.atlas, 2, 0, 1, 1, &[1, 2, 3, 4])
            .is_err();
        let patched_pixels = probe
            .renderer
            .update_atlas(&probe.atlas, 0, 0, 1, 1, &[180, 30, 220, 255])
            .and_then(|_| probe.renderer.begin_frame([0.04, 0.18, 0.28, 1.0]))
            .and_then(|_| {
                probe.renderer.draw(&probe.mesh, &probe.atlas, identity);
                probe.renderer.probe_pixels()
            });
        let message = match (
            original.unwrap(),
            blended.unwrap(),
            overlay_pixels,
            terrain_pixels,
            patched_pixels,
        ) {
            (Ok(pixels), Ok(blended), Ok(overlay), Ok(terrain), Ok(patched)) => {
                let expected = [
                    [255u8, 70, 70, 255],
                    [70, 255, 70, 255],
                    [70, 70, 255, 255],
                    [255, 230, 70, 255],
                ];
                let expected_blended = [
                    [255u8, 150, 70, 255],
                    [163, 243, 70, 255],
                    [163, 150, 163, 255],
                    [255, 230, 70, 255],
                ];
                let expected_overlay = [
                    [255u8, 110, 70, 255],
                    [209, 156, 70, 255],
                    [209, 110, 116, 255],
                    [255, 150, 70, 255],
                ];
                let expected_patched = [
                    [180u8, 30, 220, 255],
                    [70, 255, 70, 255],
                    [70, 70, 255, 255],
                    [255, 230, 70, 255],
                ];
                let matches = |pixels: &[[u8; 4]; 4], expected: [[u8; 4]; 4]| {
                    pixels.iter().zip(expected).all(|(actual, expected)| {
                        actual
                            .iter()
                            .zip(expected)
                            .all(|(&a, b)| a.abs_diff(b) <= 5)
                    })
                };
                let pass = matches(&pixels, expected)
                    && matches(&blended, expected_blended)
                    && matches(&overlay, expected_overlay)
                    && matches(&terrain, expected)
                    && matches(&patched, expected_patched)
                    && rejects_invalid_patch;
                format!(
                    "{}: pixels={pixels:?}; blended={blended:?}; overlay={overlay:?}; terrain={terrain:?}; patched={patched:?}; invalid_rejected={rejects_invalid_patch}\n",
                    if pass { "PASS" } else { "FAIL" }
                )
            }
            (before, blended, overlay, terrain, patched) => format!(
                "FAIL: readback errors before={before:?} blended={blended:?} overlay={overlay:?} terrain={terrain:?} patched={patched:?}\n"
            ),
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
