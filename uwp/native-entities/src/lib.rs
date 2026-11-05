//! Camera-relative Horizon entity and first-person meshes on D3D11 FL9_1.
//!
//! The atlas is split at page boundaries in UV space, retaining interpolated
//! 3-D positions. Special entity overlays and the dynamic lightmap are not yet
//! reproduced; ordinary atlas sampling and an approximate light are supported.

use horizon_client_render::{gfx::Frame, mesher::Vertex as SourceVertex};
use horizon_native_hud::{Atlas, PagePatch};

// Keep retained dynamic GPU buffers bounded on 32-bit devices. Geometry
// outside this budget is still rendered using a short-lived immutable mesh.
#[cfg(any(windows, test))]
const MAX_DYNAMIC_VERTICES: usize = 262_144;
#[cfg(any(windows, test))]
const MAX_DYNAMIC_SLOTS: usize = 128;

#[cfg(any(windows, test))]
fn dynamic_capacity(used: usize, old: usize, needed: usize) -> Option<usize> {
    if needed == 0 || needed > u16::MAX as usize {
        return None;
    }
    if old >= needed {
        return Some(old);
    }
    let capacity = needed.next_power_of_two().min(u16::MAX as usize);
    (used.checked_sub(old)?.checked_add(capacity)? <= MAX_DYNAMIC_VERTICES).then_some(capacity)
}

#[derive(Clone, Copy, Debug)]
struct Vertex {
    position: [f32; 3],
    uv: [f32; 2],
    color: [f32; 4],
}

#[derive(Debug)]
struct Batch {
    page: usize,
    vertices: Vec<Vertex>,
}

fn mix(a: Vertex, b: Vertex, t: f32) -> Vertex {
    let m = |x: f32, y: f32| x + (y - x) * t;
    Vertex {
        position: std::array::from_fn(|i| m(a.position[i], b.position[i])),
        uv: std::array::from_fn(|i| m(a.uv[i], b.uv[i])),
        color: std::array::from_fn(|i| m(a.color[i], b.color[i])),
    }
}

fn clip(poly: &[Vertex], axis: usize, edge: f32, greater: bool) -> Vec<Vertex> {
    let Some(mut a) = poly.last().copied() else {
        return Vec::new();
    };
    let inside = |v: Vertex| {
        if greater {
            v.uv[axis] >= edge
        } else {
            v.uv[axis] <= edge
        }
    };
    let mut result = Vec::new();
    for &b in poly {
        if inside(a) != inside(b) {
            let t = (edge - a.uv[axis]) / (b.uv[axis] - a.uv[axis]);
            result.push(mix(a, b, t));
        }
        if inside(b) {
            result.push(b);
        }
        a = b;
    }
    result
}

fn batches(atlas: &Atlas, source: &[SourceVertex], nether: bool) -> Vec<Batch> {
    let width = atlas
        .pages
        .iter()
        .map(|p| p.origin[0] + p.size[0])
        .max()
        .unwrap_or(1) as f32;
    let height = atlas
        .pages
        .iter()
        .map(|p| p.origin[1] + p.size[1])
        .max()
        .unwrap_or(1) as f32;
    let mut result: Vec<Batch> = Vec::new();
    for quad in source.chunks_exact(4) {
        let mut converted = [Vertex {
            position: [0.; 3],
            uv: [0.; 2],
            color: [0.; 4],
        }; 4];
        for (v, out) in quad.iter().zip(&mut converted) {
            let overlay = v.layer & 0xffff;
            let unlit = matches!(overlay, 2 | 4 | 6 | 9 | 10 | 11 | 12);
            let sky = (v.light & 255) as f32 / 240.;
            let block = ((v.light >> 8) & 255) as f32 / 240.;
            let mut light = if unlit {
                1.
            } else {
                sky.max(block).clamp(0.2, 1.)
            };
            if v.normal[3].unsigned_abs() >= 64 {
                let n = v.normal.map(|n| n as f32 / 127.);
                let a = (0.2 * n[0] + n[1] - 0.7 * n[2]) / 1.2369317;
                let b = (-0.2 * n[0] + if nether { -n[1] } else { n[1] } + 0.7 * n[2]) / 1.2369317;
                light *= ((a.max(0.) + b.max(0.)) * 0.6 + 0.4).min(1.);
            }
            let mut color = [0, 8, 16, 24].map(|s| ((v.color >> s) & 255) as f32 / 255.);
            for c in &mut color[..3] {
                *c *= light;
            }
            *out = Vertex {
                position: v.pos,
                uv: [v.uv[0] * width, v.uv[1] * height],
                color,
            };
        }
        if converted.iter().any(|v| {
            v.position
                .iter()
                .chain(v.uv.iter())
                .chain(v.color.iter())
                .any(|x| !x.is_finite())
        }) {
            continue;
        }
        // Most entity quads use a sprite wholly inside one page. Their
        // triangles need no polygon clipping; emit them directly. On a page
        // boundary retain the general path, since a degenerate UV triangle
        // can legitimately map to both neighbouring pages.
        let single_page = atlas.pages.iter().enumerate().find(|(_, page)| {
            converted.iter().all(|v| {
                (0..2).all(|axis| {
                    let lo = page.origin[axis] as f32;
                    let hi = (page.origin[axis] + page.size[axis]) as f32;
                    if atlas.pages.len() == 1 {
                        v.uv[axis] >= lo && v.uv[axis] <= hi
                    } else {
                        v.uv[axis] > lo && v.uv[axis] < hi
                    }
                })
            })
        });
        if let Some((page_id, page)) = single_page {
            for v in &mut converted {
                for axis in 0..2 {
                    v.uv[axis] = (v.uv[axis] - page.origin[axis] as f32) / page.size[axis] as f32;
                }
            }
            if result.last().is_none_or(|b| b.page != page_id) {
                result.push(Batch {
                    page: page_id,
                    vertices: Vec::new(),
                });
            }
            let vertices = &mut result.last_mut().unwrap().vertices;
            for i in [0, 1, 2, 0, 2, 3] {
                vertices.push(converted[i]);
            }
            continue;
        }
        for indices in [[0, 1, 2], [0, 2, 3]] {
            let tri = indices.map(|i| converted[i]);
            let min = [0, 1].map(|i| tri.iter().map(|v| v.uv[i]).fold(f32::INFINITY, f32::min));
            let max = [0, 1].map(|i| {
                tri.iter()
                    .map(|v| v.uv[i])
                    .fold(f32::NEG_INFINITY, f32::max)
            });
            for (page_id, page) in atlas.pages.iter().enumerate() {
                let lo = page.origin.map(|v| v as f32);
                let hi = [
                    (page.origin[0] + page.size[0]) as f32,
                    (page.origin[1] + page.size[1]) as f32,
                ];
                if (0..2).any(|i| max[i] < lo[i] || min[i] > hi[i]) {
                    continue;
                }
                let mut p = tri.to_vec();
                for axis in 0..2 {
                    p = clip(&p, axis, lo[axis], true);
                    p = clip(&p, axis, hi[axis], false);
                }
                if p.len() < 3 {
                    continue;
                }
                for v in &mut p {
                    for axis in 0..2 {
                        v.uv[axis] = (v.uv[axis] - lo[axis]) / page.size[axis] as f32;
                    }
                }
                if result.last().is_none_or(|b| b.page != page_id) {
                    result.push(Batch {
                        page: page_id,
                        vertices: Vec::new(),
                    });
                }
                let b = result.last_mut().unwrap();
                for i in 1..p.len() - 1 {
                    b.vertices.extend_from_slice(&[p[0], p[i], p[i + 1]]);
                }
            }
        }
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;

    fn vertex(pos: [f32; 3], uv: [f32; 2]) -> SourceVertex {
        SourceVertex {
            pos,
            uv,
            layer: 0,
            color: 0xff_ff_ff_ff,
            light: 0xf0,
            normal: [0; 4],
            glint_uv: [0.; 2],
        }
    }

    #[test]
    fn clips_3d_quads_across_atlas_pages_without_losing_depth() {
        let atlas = Atlas::new([4, 2], &[255; 32], 2).unwrap();
        let quad = [
            vertex([0., 0., 1.], [0.25, 0.]),
            vertex([0., 0., 3.], [0.75, 0.]),
            vertex([0., 1., 3.], [0.75, 1.]),
            vertex([0., 1., 1.], [0.25, 1.]),
        ];
        let draws = batches(&atlas, &quad, false);
        assert!(draws.iter().any(|b| b.page == 0));
        assert!(draws.iter().any(|b| b.page == 1));
        assert!(
            draws
                .iter()
                .flat_map(|b| &b.vertices)
                .any(|v| (v.position[2] - 2.).abs() < 1e-6)
        );
        assert!(
            draws
                .iter()
                .flat_map(|b| &b.vertices)
                .all(|v| v.uv.iter().all(|u| *u >= 0. && *u <= 1.))
        );
    }

    #[test]
    fn dynamic_mesh_budget_reuses_and_bounds_gpu_capacity() {
        assert_eq!(dynamic_capacity(0, 0, 3), Some(4));
        assert_eq!(dynamic_capacity(16, 16, 3), Some(16));
        assert_eq!(dynamic_capacity(16, 16, 17), Some(32));
        assert_eq!(dynamic_capacity(MAX_DYNAMIC_VERTICES, 0, 3), None);
        assert_eq!(dynamic_capacity(MAX_DYNAMIC_VERTICES, 16, 17), None);
        assert_eq!(dynamic_capacity(0, 0, 65_535), Some(65_535));
        assert_eq!(dynamic_capacity(0, 0, 65_536), None);
        assert_eq!(dynamic_capacity(0, 0, 0), None);
    }

    #[test]
    fn interior_quad_emits_original_triangles_in_page_coordinates() {
        let atlas = Atlas::new([4, 2], &[255; 32], 2).unwrap();
        let quad = [
            vertex([0., 0., 1.], [0.1, 0.1]),
            vertex([1., 0., 1.], [0.4, 0.1]),
            vertex([1., 1., 2.], [0.4, 0.9]),
            vertex([0., 1., 2.], [0.1, 0.9]),
        ];
        let out = batches(&atlas, &quad, false);
        assert_eq!(out.len(), 1);
        assert_eq!(out[0].page, 0);
        assert_eq!(out[0].vertices.len(), 6);
        for (v, i) in out[0].vertices.iter().zip([0, 1, 2, 0, 2, 3]) {
            assert_eq!(v.position, quad[i].pos);
            assert!((v.uv[0] - quad[i].uv[0] * 2.).abs() < 1e-6);
            assert!((v.uv[1] - quad[i].uv[1]).abs() < 1e-6);
        }
    }

    #[test]
    fn page_boundary_with_collapsed_uv_keeps_both_page_draws() {
        let atlas = Atlas::new([4, 2], &[255; 32], 2).unwrap();
        let quad = [
            vertex([0., 0., 0.], [0.5, 0.2]),
            vertex([1., 0., 0.], [0.5, 0.2]),
            vertex([1., 1., 0.], [0.5, 0.8]),
            vertex([0., 1., 0.], [0.5, 0.8]),
        ];
        let out = batches(&atlas, &quad, false);
        assert!(out.iter().any(|b| b.page == 0));
        assert!(out.iter().any(|b| b.page == 1));
    }

    #[test]
    fn preserves_source_quads_and_alpha() {
        let atlas = Atlas::new([1, 1], &[255; 4], 1).unwrap();
        let mut quad = [
            vertex([0., 0., 0.], [0., 0.]),
            vertex([1., 0., 0.], [1., 0.]),
            vertex([1., 1., 0.], [1., 1.]),
            vertex([0., 1., 0.], [0., 1.]),
        ];
        for v in &mut quad {
            v.color = 0x80ffffff;
        }
        let out = batches(&atlas, &quad, false);
        assert_eq!(out.len(), 1);
        assert_eq!(out[0].vertices.len(), 6);
        assert_eq!(out[0].vertices[0].color[3], 128. / 255.);
    }
}

#[cfg(windows)]
pub mod d3d11 {
    use super::*;
    use horizon_uwp_d3d11::{DrawPass, DynamicMesh, Renderer, Texture, Vertex as GpuVertex};
    use std::error::Error;
    type Result<T> = std::result::Result<T, Box<dyn Error>>;

    pub struct EntityRenderer {
        textures: Vec<Option<Texture>>,
        layout: Vec<([u32; 2], [u32; 2])>,
        dynamic: Vec<DynamicMesh>,
        dynamic_vertices: usize,
    }
    impl EntityRenderer {
        pub fn new(atlas: &Atlas) -> Self {
            Self {
                textures: (0..atlas.pages.len()).map(|_| None).collect(),
                layout: atlas.pages.iter().map(|p| (p.origin, p.size)).collect(),
                dynamic: Vec::new(),
                dynamic_vertices: 0,
            }
        }
        pub fn draw_vertices(
            &mut self,
            renderer: &Renderer,
            atlas: &Atlas,
            patches: &[PagePatch],
            vertices: &[SourceVertex],
            matrix: glam::Mat4,
            nether: bool,
        ) -> Result<()> {
            if self.layout.len() != atlas.pages.len()
                || self
                    .layout
                    .iter()
                    .zip(&atlas.pages)
                    .any(|(a, b)| *a != (b.origin, b.size))
            {
                return Err("entity atlas layout changed".into());
            }
            for p in patches {
                if p.page >= self.textures.len() {
                    return Err("invalid entity atlas patch".into());
                }
                // Glyph updates usually touch only a few pixels of a large
                // atlas page. Recreating the whole GPU texture for each
                // patch can rapidly exhaust graphics memory on mobile GPUs.
                // Pages not uploaded yet will use the current atlas pixels
                // when they are first needed.
                if let Some(texture) = &self.textures[p.page] {
                    renderer
                        .update_atlas(
                            texture,
                            p.origin[0],
                            p.origin[1],
                            p.size[0],
                            p.size[1],
                            &p.rgba,
                        )
                        .map_err(|e| {
                            format!(
                                "entity atlas page {} update ({}x{}): {e}",
                                p.page, p.size[0], p.size[1]
                            )
                        })?;
                }
            }
            let mut slot = 0;
            for batch in batches(atlas, vertices, nether) {
                let page = &atlas.pages[batch.page];
                if self.textures[batch.page].is_none() {
                    self.textures[batch.page] = Some(
                        renderer
                            .create_atlas(page.size[0], page.size[1], &page.rgba)
                            .map_err(|e| {
                                format!(
                                    "entity atlas page {} ({}x{}): {e}",
                                    batch.page, page.size[0], page.size[1]
                                )
                            })?,
                    );
                }
                for part in batch.vertices.chunks(65535) {
                    let v: Vec<_> = part
                        .iter()
                        .map(|v| GpuVertex {
                            position: v.position,
                            uv: v.uv,
                            color: v.color,
                        })
                        .collect();
                    let old = self.dynamic.get(slot).map_or(0, DynamicMesh::capacity);
                    let capacity = if slot < MAX_DYNAMIC_SLOTS && slot <= self.dynamic.len() {
                        dynamic_capacity(self.dynamic_vertices, old, part.len())
                    } else {
                        None
                    };
                    if let Some(capacity) = capacity {
                        if old < capacity {
                            let mesh = renderer.create_dynamic_mesh(capacity).map_err(|e| {
                                format!("entity dynamic buffer ({capacity} vertices): {e}")
                            })?;
                            if slot == self.dynamic.len() {
                                self.dynamic.push(mesh);
                            } else {
                                self.dynamic[slot] = mesh;
                            }
                            self.dynamic_vertices = self.dynamic_vertices - old + capacity;
                        }
                        let mesh = &mut self.dynamic[slot];
                        renderer.update_dynamic_mesh(mesh, &v).map_err(|e| {
                            format!("entity dynamic buffer update ({} vertices): {e}", v.len())
                        })?;
                        renderer.draw_pass(
                            mesh.mesh(),
                            self.textures[batch.page].as_ref().unwrap(),
                            matrix.to_cols_array_2d(),
                            DrawPass::Entity,
                        );
                    } else {
                        let indices: Vec<_> = (0..part.len()).map(|i| i as u16).collect();
                        let mesh = renderer.create_mesh(&v, &indices).map_err(|e| {
                            format!("entity immutable buffer ({} vertices): {e}", v.len())
                        })?;
                        renderer.draw_pass(
                            &mesh,
                            self.textures[batch.page].as_ref().unwrap(),
                            matrix.to_cols_array_2d(),
                            DrawPass::Entity,
                        );
                    }
                    slot += 1;
                }
            }
            Ok(())
        }
        pub fn draw_entities(
            &mut self,
            renderer: &Renderer,
            atlas: &Atlas,
            patches: &[PagePatch],
            frame: &Frame<'_>,
            viewport: [u32; 2],
        ) -> Result<()> {
            let aspect = viewport[0] as f32 / viewport[1].max(1) as f32;
            let p = glam::Mat4::perspective_rh(
                frame.fov_degrees.to_radians(),
                aspect,
                frame.depth_far,
                0.05,
            );
            let m = p
                * frame.bob
                * frame.spin
                * horizon_client_render::camera::view_matrix(frame.yaw, frame.pitch);
            self.draw_vertices(
                renderer,
                atlas,
                patches,
                frame.entities,
                m,
                frame.nether_lighting,
            )
        }
        /// Call after clearing the world depth buffer, before drawing the HUD.
        pub fn draw_hand(
            &mut self,
            renderer: &Renderer,
            atlas: &Atlas,
            patches: &[PagePatch],
            frame: &Frame<'_>,
            viewport: [u32; 2],
        ) -> Result<()> {
            let p = glam::Mat4::from_cols_array(&horizon_client_render::camera::hand_projection(
                frame.hand_fov_degrees,
                viewport[0] as f32,
                viewport[1].max(1) as f32,
                frame.depth_far,
            ));
            let m =
                p * frame.bob * horizon_client_render::camera::view_matrix(frame.yaw, frame.pitch);
            self.draw_vertices(
                renderer,
                atlas,
                patches,
                frame.hand,
                m,
                frame.nether_lighting,
            )
        }
    }
}
