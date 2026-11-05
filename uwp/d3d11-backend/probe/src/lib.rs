use horizon_native_scene::TerrainScene;
use horizon_native_terrain::{Atlas, SourceVertex};
use horizon_uwp_d3d11::{DepthConvention, DrawPass, Mesh, Renderer, Texture, Vertex};
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
        probe
            .renderer
            .set_depth_convention(DepthConvention::Reverse);
        let reverse_pixels = probe
            .renderer
            .begin_frame([0.04, 0.18, 0.28, 1.0])
            .and_then(|_| {
                probe.renderer.draw(&probe.occluded, &probe.atlas, identity);
                probe.renderer.draw(&probe.mesh, &probe.atlas, identity);
                probe.renderer.probe_pixels()
            });
        probe
            .renderer
            .set_depth_convention(DepthConvention::Forward);
        let mip_result = test_mip_selection(&probe.renderer, identity);
        let hud_result = test_hud(&probe.renderer);
        let blend_result =
            test_overlay_blends(&probe.renderer, &probe.occluded, &probe.atlas, identity);
        let depth_reset_result = test_depth_reset(
            &probe.renderer,
            &probe.mesh,
            &probe.occluded,
            &probe.atlas,
            identity,
        );
        let dynamic_result = test_dynamic_mesh(&probe.renderer, identity);
        let entity_result = test_entity_pass(
            &probe.renderer,
            &probe.occluded,
            &probe.overlay,
            &probe.atlas,
            identity,
        );
        let message = match (
            original.unwrap(),
            blended.unwrap(),
            overlay_pixels,
            terrain_pixels,
            patched_pixels,
            reverse_pixels,
        ) {
            (Ok(pixels), Ok(blended), Ok(overlay), Ok(terrain), Ok(patched), Ok(reverse)) => {
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
                let expected_reverse = [[255u8, 230, 70, 255]; 4];
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
                    && matches(&reverse, expected_reverse)
                    && rejects_invalid_patch
                    && mip_result.is_ok()
                    && hud_result.is_ok()
                    && blend_result.is_ok()
                    && depth_reset_result.is_ok()
                    && entity_result.is_ok()
                    && dynamic_result.is_ok();
                format!(
                    "{}: pixels={pixels:?}; blended={blended:?}; overlay={overlay:?}; terrain={terrain:?}; patched={patched:?}; reverse={reverse:?}; invalid_rejected={rejects_invalid_patch}; mip={mip_result:?}; hud={hud_result:?}; blends={blend_result:?}; depth_reset={depth_reset_result:?}; entity={entity_result:?}; dynamic={dynamic_result:?}\n",
                    if pass { "PASS" } else { "FAIL" }
                )
            }
            (before, blended, overlay, terrain, patched, reverse) => format!(
                "FAIL: readback errors before={before:?} blended={blended:?} overlay={overlay:?} terrain={terrain:?} patched={patched:?} reverse={reverse:?}\n"
            ),
        };
        unsafe {
            horizon_d3d11_probe_report(message.as_ptr(), message.len());
        }
        probe.checked = true;
    }
    probe.renderer.present().is_ok()
}

fn test_dynamic_mesh(
    renderer: &Renderer,
    matrix: [[f32; 4]; 4],
) -> Result<(), Box<dyn std::error::Error>> {
    if renderer.create_dynamic_mesh(0).is_ok() || renderer.create_dynamic_mesh(65_536).is_ok() {
        return Err(std::io::Error::other("invalid dynamic capacity accepted").into());
    }
    let texture = renderer.create_atlas(1, 1, &[255, 255, 255, 255])?;
    let quad = |left: f32, right: f32, color: [f32; 4]| {
        let vertex = |x, y| Vertex {
            position: [x, y, 0.5],
            uv: [0.5, 0.5],
            color,
        };
        [
            vertex(left, -0.9),
            vertex(left, 0.9),
            vertex(right, -0.9),
            vertex(right, -0.9),
            vertex(left, 0.9),
            vertex(right, 0.9),
        ]
    };
    let red = quad(-0.9, 0.0, [1., 0., 0., 1.]);
    let green = quad(0.0, 0.9, [0., 1., 0., 1.]);
    let mut mesh = renderer.create_dynamic_mesh(6)?;
    renderer.begin_frame([0., 0., 0., 1.])?;
    renderer.update_dynamic_mesh(&mut mesh, &red)?;
    renderer.draw_pass(mesh.mesh(), &texture, matrix, DrawPass::Opaque);
    renderer.update_dynamic_mesh(&mut mesh, &green)?;
    renderer.draw_pass(mesh.mesh(), &texture, matrix, DrawPass::Opaque);
    let pixels = renderer.probe_pixels()?;
    let expected = [
        [255u8, 0, 0, 255],
        [0, 255, 0, 255],
        [255, 0, 0, 255],
        [0, 255, 0, 255],
    ];
    if pixels
        .iter()
        .zip(expected)
        .any(|(p, e)| p.iter().zip(e).any(|(&a, b)| a.abs_diff(b) > 5))
    {
        return Err(std::io::Error::other(format!(
            "dynamic discard changed queued draw: {pixels:?}"
        ))
        .into());
    }
    if renderer.update_dynamic_mesh(&mut mesh, &[]).is_ok()
        || renderer
            .update_dynamic_mesh(&mut mesh, &[green[0]; 7])
            .is_ok()
    {
        return Err(std::io::Error::other("invalid dynamic update accepted").into());
    }
    let mut combined = renderer.create_dynamic_mesh(12)?;
    let vertices: Vec<_> = red.into_iter().chain(green).collect();
    renderer.begin_frame([0., 0., 0., 1.])?;
    renderer.update_dynamic_mesh(&mut combined, &vertices)?;
    renderer.draw_pass_range(combined.mesh(), &texture, matrix, DrawPass::Opaque, 0, 6)?;
    renderer.draw_pass_range(combined.mesh(), &texture, matrix, DrawPass::Opaque, 6, 6)?;
    let pixels = renderer.probe_pixels()?;
    if pixels.iter().zip(expected).any(|(p, e)| p.iter().zip(e).any(|(&a, b)| a.abs_diff(b) > 5)) {
        return Err(std::io::Error::other(format!("dynamic draw ranges changed ordering: {pixels:?}")).into());
    }
    if renderer.draw_pass_range(combined.mesh(), &texture, matrix, DrawPass::Opaque, 7, 6).is_ok()
        || renderer.draw_pass_range(combined.mesh(), &texture, matrix, DrawPass::Opaque, 0, 0).is_ok()
    {
        return Err(std::io::Error::other("invalid draw range accepted").into());
    }
    renderer.begin_frame([0., 0., 0., 1.])?;
    renderer.draw_pass(mesh.mesh(), &texture, matrix, DrawPass::Opaque);
    let pixels = renderer.probe_pixels()?;
    if pixels[1][1] < 250 || pixels[3][1] < 250 {
        return Err(std::io::Error::other(format!(
            "rejected update changed prior buffer: {pixels:?}"
        ))
        .into());
    }
    Ok(())
}

fn test_mip_selection(
    renderer: &Renderer,
    matrix: [[f32; 4]; 4],
) -> Result<(), Box<dyn std::error::Error>> {
    let solid = |w: usize, color: [u8; 4]| -> Vec<u8> { (0..w * w).flat_map(|_| color).collect() };
    let base = solid(16, [255, 0, 0, 255]);
    let mid = solid(8, [0, 255, 0, 255]);
    let min = solid(4, [0, 0, 255, 255]);
    let texture = renderer.create_atlas_mips(16, 16, &[&base, &mid, &min])?;
    if renderer
        .update_atlas(&texture, 0, 0, 1, 1, &[255, 255, 255, 255])
        .is_ok()
    {
        return Err(std::io::Error::other(
            "mipmapped atlas update should be rejected without refreshing its other levels",
        )
        .into());
    }
    let quad = [
        Vertex {
            position: [-1.0, -1.0, 0.5],
            uv: [0.0, 4096.0],
            color: [1.0; 4],
        },
        Vertex {
            position: [-1.0, 1.0, 0.5],
            uv: [0.0, 0.0],
            color: [1.0; 4],
        },
        Vertex {
            position: [1.0, -1.0, 0.5],
            uv: [4096.0, 4096.0],
            color: [1.0; 4],
        },
        Vertex {
            position: [1.0, 1.0, 0.5],
            uv: [4096.0, 0.0],
            color: [1.0; 4],
        },
    ];
    let mesh = renderer.create_mesh(&quad, &[0, 1, 2, 2, 1, 3])?;
    renderer.begin_frame([0.0, 0.0, 0.0, 1.0])?;
    renderer.draw(&mesh, &texture, matrix);
    let pixels = renderer.probe_pixels()?;
    if pixels.iter().any(|p| p[0] > 5 || p[1] > 5 || p[2] < 250) {
        return Err(
            std::io::Error::other(format!("expected blue minified mip, got {pixels:?}")).into(),
        );
    }
    Ok(())
}

fn test_entity_pass(
    renderer: &Renderer,
    front: &Mesh,
    back: &Mesh,
    atlas: &Texture,
    matrix: [[f32; 4]; 4],
) -> Result<(), Box<dyn std::error::Error>> {
    let translucent_green = renderer.create_atlas(1, 1, &[0, 255, 0, 64])?;
    renderer.begin_frame([0.2, 0.4, 0.6, 1.0])?;
    renderer.draw_pass(front, &translucent_green, matrix, DrawPass::Entity);
    renderer.draw_pass(back, atlas, matrix, DrawPass::Entity);
    let pixels = renderer.probe_pixels()?;
    let expected = [38u8, 140, 115, 255];
    if pixels
        .iter()
        .any(|p| p.iter().zip(expected).any(|(&a, b)| a.abs_diff(b) > 5))
    {
        return Err(
            std::io::Error::other(format!("entity blend/depth mismatch: {pixels:?}")).into(),
        );
    }
    Ok(())
}

fn test_depth_reset(
    renderer: &Renderer,
    near: &Mesh,
    far: &Mesh,
    atlas: &Texture,
    matrix: [[f32; 4]; 4],
) -> Result<(), Box<dyn std::error::Error>> {
    renderer.begin_frame([0.0, 0.0, 0.0, 1.0])?;
    renderer.draw(near, atlas, matrix);
    renderer.clear_depth()?;
    renderer.draw(far, atlas, matrix);
    let pixels = renderer.probe_pixels()?;
    let expected = [255u8, 230, 70, 255];
    if pixels
        .iter()
        .any(|p| p.iter().zip(expected).any(|(&a, b)| a.abs_diff(b) > 5))
    {
        return Err(
            std::io::Error::other(format!("depth reset did not expose hand: {pixels:?}")).into(),
        );
    }
    Ok(())
}

fn test_overlay_blends(
    renderer: &Renderer,
    mesh: &Mesh,
    atlas: &Texture,
    matrix: [[f32; 4]; 4],
) -> Result<(), Box<dyn std::error::Error>> {
    let cases = [
        (DrawPass::OverlayInvert, [204u8, 148, 139, 255]),
        (DrawPass::OverlayAdditive, [255u8, 255, 223, 255]),
        (DrawPass::OverlayVignette, [0u8, 10, 111, 255]),
    ];
    for (pass, expected) in cases {
        renderer.begin_frame([0.2, 0.4, 0.6, 1.0])?;
        renderer.draw_pass(mesh, atlas, matrix, pass);
        let pixels = renderer.probe_pixels()?;
        if pixels
            .iter()
            .any(|p| p.iter().zip(expected).any(|(&a, b)| a.abs_diff(b) > 5))
        {
            return Err(std::io::Error::other(format!(
                "{pass:?}: expected={expected:?} got={pixels:?}"
            ))
            .into());
        }
    }
    let transparent = renderer.create_atlas(1, 1, &[255, 255, 255, 0])?;
    renderer.begin_frame([0.2, 0.4, 0.6, 1.0])?;
    renderer.draw_pass(mesh, &transparent, matrix, DrawPass::OverlayInvert);
    let pixels = renderer.probe_pixels()?;
    let expected = [51u8, 102, 153, 255];
    if pixels
        .iter()
        .any(|p| p.iter().zip(expected).any(|(&a, b)| a.abs_diff(b) > 5))
    {
        return Err(std::io::Error::other(format!(
            "transparent invert modified background: {pixels:?}"
        ))
        .into());
    }
    renderer.begin_frame([0.2, 0.4, 0.6, 1.0])?;
    renderer.draw_pass(mesh, &transparent, matrix, DrawPass::Opaque);
    let opaque = renderer.probe_pixels()?;
    if opaque.iter().any(|p| *p != [255, 255, 255, 255]) {
        return Err(
            std::io::Error::other(format!("forced opaque discarded texels: {opaque:?}")).into(),
        );
    }
    renderer.begin_frame([0.2, 0.4, 0.6, 1.0])?;
    renderer.draw_pass(mesh, &transparent, matrix, DrawPass::Cutout);
    let cutout = renderer.probe_pixels()?;
    if cutout
        .iter()
        .any(|p| p.iter().zip(expected).any(|(&a, b)| a.abs_diff(b) > 5))
    {
        return Err(
            std::io::Error::other(format!("cutout drew transparent texels: {cutout:?}")).into(),
        );
    }
    Ok(())
}

fn test_hud(renderer: &Renderer) -> Result<(), Box<dyn std::error::Error>> {
    use horizon_native_hud::{Atlas, Blend, Run, Vertex, d3d11::HudRenderer};
    let mut atlas = Atlas::new(
        [2, 2],
        &[
            255, 70, 70, 255, 70, 255, 70, 255, 70, 70, 255, 255, 255, 230, 70, 255,
        ],
        1,
    )?;
    let make = |x, y, u, v| Vertex {
        pos: [x, y],
        uv: [u, v],
        color: [1.0; 4],
        depth: 0.5,
        glint_uv: [0.0; 2],
    };
    let tl = make(0.125, 0.125, 0.0, 0.0);
    let tr = make(0.875, 0.125, 1.0, 0.0);
    let bl = make(0.125, 0.875, 0.0, 1.0);
    let br = make(0.875, 0.875, 1.0, 1.0);
    let vertices = [tl, tr, br, tl, br, bl];
    let draws = atlas.convert(
        &vertices,
        &[Run {
            vertices: 0..6,
            blend: Blend::Alpha,
        }],
    )?;
    let mut hud = HudRenderer::new(renderer, &atlas)?;
    renderer.begin_frame([0.0, 0.0, 0.0, 1.0])?;
    hud.draw(renderer, &atlas, &[], &draws, [1, 1], 1.0)?;
    let original = renderer.probe_pixels()?;
    let patches = atlas.patch([0, 0], [1, 1], &[180, 30, 220, 255])?;
    let mut unsupported = draws.clone();
    for draw in &mut unsupported {
        draw.blend = Blend::EndPortal;
    }
    if hud
        .draw(renderer, &atlas, &patches, &unsupported, [1, 1], 1.0)
        .is_ok()
    {
        return Err(std::io::Error::other("unsupported HUD blend was accepted").into());
    }
    renderer.begin_frame([0.0, 0.0, 0.0, 1.0])?;
    hud.draw(renderer, &atlas, &[], &draws, [1, 1], 1.0)?;
    let updated = renderer.probe_pixels()?;
    let expected = [
        [255u8, 70, 70, 255],
        [70, 255, 70, 255],
        [70, 70, 255, 255],
        [255, 230, 70, 255],
    ];
    let expected_updated = [
        [180u8, 30, 220, 255],
        [70, 255, 70, 255],
        [70, 70, 255, 255],
        [255, 230, 70, 255],
    ];
    let matches = |actual: [[u8; 4]; 4], expected: [[u8; 4]; 4]| {
        actual
            .iter()
            .zip(expected)
            .all(|(a, b)| a.iter().zip(b).all(|(&x, y)| x.abs_diff(y) <= 5))
    };
    if !matches(original, expected) || !matches(updated, expected_updated) {
        return Err(std::io::Error::other(format!(
            "HUD pixels: original={original:?} updated={updated:?}"
        ))
        .into());
    }
    Ok(())
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
