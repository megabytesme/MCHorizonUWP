//! CPU-side terrain conversion for Direct3D feature level 9_1.
//!
//! Horizon's mesher produces quads using texture-array layers. FL9_1 has no
//! texture arrays, so this crate packs the sprite layers into ordinary 2D
//! atlases and turns the quads into 16-bit indexed triangle batches.

use std::fmt;

pub const MAX_TEXTURE_SIDE: u32 = 2048;
pub const OPAQUE_LAYER: u16 = 0x8000;
const MAX_REPEAT_CELLS: i32 = 64;
const MAX_VERTICES: usize = u16::MAX as usize;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Error(pub String);
impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}
impl std::error::Error for Error {}

/// Matches `horizon_d3d11_backend::Vertex` field for field.
#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Vertex {
    pub position: [f32; 3],
    pub uv: [f32; 2],
    pub color: [f32; 4],
}

/// One `TerrainVertex` after reading its public `uv()` method.
#[derive(Clone, Copy, Debug)]
pub struct SourceVertex {
    pub position: [f32; 3],
    pub uv: [f32; 2],
    pub layer: u16,
    pub color: u32,
    pub light: u16,
}

#[derive(Clone, Debug)]
pub struct AtlasPage {
    pub width: u32,
    pub height: u32,
    pub rgba: Vec<u8>,
    /// Additional, independently packed mip levels, from largest to smallest.
    pub mips: Vec<Vec<u8>>,
}

#[derive(Clone, Copy, Debug)]
struct Placement {
    page: usize,
    x: u32,
    y: u32,
}

#[derive(Clone, Debug)]
pub struct Atlas {
    pub pages: Vec<AtlasPage>,
    sprite_side: u32,
    placements: Vec<Placement>,
}

impl Atlas {
    /// Packs `layer_count` square RGBA sprites in layer-major order.
    /// One wrapped texel of gutter surrounds each sprite so linear filtering
    /// at a repeating edge cannot sample an unrelated sprite.
    pub fn from_layers(sprite_side: u32, layer_count: usize, rgba: &[u8]) -> Result<Self, Error> {
        if sprite_side == 0 || sprite_side > MAX_TEXTURE_SIDE - 2 || layer_count == 0 {
            return Err(Error("invalid sprite dimensions or layer count".into()));
        }
        let layer_bytes = (sprite_side as usize)
            .checked_mul(sprite_side as usize)
            .and_then(|n| n.checked_mul(4))
            .ok_or_else(|| Error("sprite size overflow".into()))?;
        if layer_bytes.checked_mul(layer_count) != Some(rgba.len()) {
            return Err(Error("sprite pixel buffer has the wrong length".into()));
        }
        if layer_count > (OPAQUE_LAYER as usize) {
            return Err(Error(
                "sprite layer count exceeds Horizon's 15-bit layer field".into(),
            ));
        }
        let tile = sprite_side + 2;
        let max_cols = MAX_TEXTURE_SIDE / tile;
        let capacity = max_cols as usize * max_cols as usize;
        let mut pages = Vec::new();
        let mut placements = Vec::with_capacity(layer_count);
        for first in (0..layer_count).step_by(capacity) {
            let count = (layer_count - first).min(capacity);
            let mut cols = 1usize;
            while cols * cols < count && cols < max_cols as usize {
                cols += 1;
            }
            let rows = count.div_ceil(cols);
            let width = (cols as u32 * tile).next_power_of_two();
            let height = (rows as u32 * tile).next_power_of_two();
            let mut page = AtlasPage {
                width,
                height,
                rgba: vec![0; width as usize * height as usize * 4],
                mips: Vec::new(),
            };
            for i in 0..count {
                let x = (i % cols) as u32 * tile;
                let y = (i / cols) as u32 * tile;
                placements.push(Placement {
                    page: pages.len(),
                    x: x + 1,
                    y: y + 1,
                });
                let source = &rgba[(first + i) * layer_bytes..(first + i + 1) * layer_bytes];
                for ty in 0..tile {
                    let sy = (ty + sprite_side - 1) % sprite_side;
                    for tx in 0..tile {
                        let sx = (tx + sprite_side - 1) % sprite_side;
                        let src = ((sy * sprite_side + sx) * 4) as usize;
                        let dst = (((y + ty) * width + x + tx) * 4) as usize;
                        page.rgba[dst..dst + 4].copy_from_slice(&source[src..src + 4]);
                    }
                }
            }
            pages.push(page);
        }
        Ok(Self {
            pages,
            sprite_side,
            placements,
        })
    }

    /// Pack precomputed layer-major mip images into aligned, wrapped slots.
    /// Every supplied level must have room for a one-texel gutter.
    pub fn from_mip_layers(
        sprite_side: u32,
        layer_count: usize,
        levels: &[Vec<u8>],
    ) -> Result<Self, Error> {
        if !sprite_side.is_power_of_two()
            || sprite_side < 4
            || sprite_side > MAX_TEXTURE_SIDE / 2
            || layer_count == 0
            || layer_count > OPAQUE_LAYER as usize
            || levels.is_empty()
        {
            return Err(Error(
                "invalid mipmapped sprite dimensions or layer count".into(),
            ));
        }
        let max_extra = sprite_side.ilog2().saturating_sub(1) as usize;
        if levels.len() > max_extra + 1 {
            return Err(Error("mip levels would consume the sprite gutter".into()));
        }
        for (level, pixels) in levels.iter().enumerate() {
            let side = sprite_side >> level;
            if (side as usize)
                .checked_mul(side as usize)
                .and_then(|n| n.checked_mul(4))
                .and_then(|n| n.checked_mul(layer_count))
                != Some(pixels.len())
            {
                return Err(Error("mip pixel buffer has the wrong length".into()));
            }
        }
        let tile = sprite_side * 2;
        let inset = sprite_side / 2;
        let max_cols = MAX_TEXTURE_SIDE / tile;
        let capacity = max_cols as usize * max_cols as usize;
        let mut pages = Vec::new();
        let mut placements = Vec::with_capacity(layer_count);
        for first in (0..layer_count).step_by(capacity) {
            let count = (layer_count - first).min(capacity);
            let mut cols = 1usize;
            while cols * cols < count && cols < max_cols as usize {
                cols += 1;
            }
            let rows = count.div_ceil(cols);
            let width = (cols as u32 * tile).next_power_of_two();
            let height = (rows as u32 * tile).next_power_of_two();
            let mut packed = Vec::with_capacity(levels.len());
            for (level, pixels) in levels.iter().enumerate() {
                let side = sprite_side >> level;
                let level_tile = tile >> level;
                let level_inset = inset >> level;
                let level_width = width >> level;
                let level_height = height >> level;
                let layer_bytes = (side * side * 4) as usize;
                let mut page = vec![0; level_width as usize * level_height as usize * 4];
                for i in 0..count {
                    let x = (i % cols) as u32 * level_tile;
                    let y = (i / cols) as u32 * level_tile;
                    let source = &pixels[(first + i) * layer_bytes..(first + i + 1) * layer_bytes];
                    for ty in 0..level_tile {
                        let sy = (ty + side - (level_inset % side)) % side;
                        for tx in 0..level_tile {
                            let sx = (tx + side - (level_inset % side)) % side;
                            let src = ((sy * side + sx) * 4) as usize;
                            let dst = (((y + ty) * level_width + x + tx) * 4) as usize;
                            page[dst..dst + 4].copy_from_slice(&source[src..src + 4]);
                        }
                    }
                }
                packed.push(page);
            }
            for i in 0..count {
                placements.push(Placement {
                    page: pages.len(),
                    x: (i % cols) as u32 * tile + inset,
                    y: (i / cols) as u32 * tile + inset,
                });
            }
            let rgba = packed.remove(0);
            pages.push(AtlasPage {
                width,
                height,
                rgba,
                mips: packed,
            });
        }
        Ok(Self {
            pages,
            sprite_side,
            placements,
        })
    }

    /// Convert quads in their original draw order. `origin` is the section's
    /// world-space or camera-relative origin matching the caller's matrix.
    /// Each returned batch uses one atlas page and fits in u16 indices.
    pub fn convert_quads(
        &self,
        vertices: &[SourceVertex],
        origin: [f32; 3],
    ) -> Result<Vec<MeshBatch>, Error> {
        if vertices.len() % 4 != 0 {
            return Err(Error("terrain input must contain whole quads".into()));
        }
        if !origin.iter().all(|x| x.is_finite()) {
            return Err(Error("section origin is non-finite".into()));
        }
        let mut batches = Vec::new();
        for quad in vertices.chunks_exact(4) {
            let layer = quad[0].layer;
            if quad.iter().any(|v| {
                v.layer != layer
                    || !v.uv.iter().all(|x| x.is_finite() && x.abs() <= 65536.0)
                    || !v.position.iter().all(|x| x.is_finite())
            }) {
                return Err(Error(
                    "quad has mixed layers or non-finite vertex data".into(),
                ));
            }
            let placement = *self
                .placements
                .get((layer & !OPAQUE_LAYER) as usize)
                .ok_or_else(|| {
                    Error(format!("sprite layer {} is missing", layer & !OPAQUE_LAYER))
                })?;
            let opaque = layer & OPAQUE_LAYER != 0;
            let converted: Vec<_> = quad
                .iter()
                .map(|v| ClipVertex::from_source(v, origin))
                .collect();
            for triangle in [[0, 1, 2], [0, 2, 3]] {
                self.emit_triangle(&mut batches, &converted, triangle, placement, opaque)?;
            }
        }
        Ok(batches)
    }

    /// Convert depth-writing solid/cutout quads and group by atlas page and
    /// opaque pass. This reduces state changes while preserving triangle order
    /// within each group. Do not use this for alpha-blended terrain: its
    /// source draw order must be preserved by `convert_quads`.
    pub fn convert_solid_quads(
        &self,
        vertices: &[SourceVertex],
        origin: [f32; 3],
    ) -> Result<Vec<MeshBatch>, Error> {
        Ok(merge_solid_batches(self.convert_quads(vertices, origin)?))
    }

    fn emit_triangle(
        &self,
        batches: &mut Vec<MeshBatch>,
        quad: &[ClipVertex],
        tri: [usize; 3],
        p: Placement,
        opaque: bool,
    ) -> Result<(), Error> {
        let input = [quad[tri[0]], quad[tri[1]], quad[tri[2]]];
        let bounds = |axis: usize| {
            let min = input
                .iter()
                .map(|v| v.uv[axis])
                .fold(f32::INFINITY, f32::min);
            let max = input
                .iter()
                .map(|v| v.uv[axis])
                .fold(f32::NEG_INFINITY, f32::max);
            let start = min.floor() as i32;
            let end = ((max.ceil() as i32) - 1).max(start);
            (start, end)
        };
        let ((u0, u1), (v0, v1)) = (bounds(0), bounds(1));
        if u1.saturating_sub(u0) > MAX_REPEAT_CELLS || v1.saturating_sub(v0) > MAX_REPEAT_CELLS {
            return Err(Error("quad repeats its sprite too many times".into()));
        }
        for y in v0..=v1 {
            for x in u0..=u1 {
                let mut poly = input.to_vec();
                for (axis, edge, keep_greater) in [
                    (0, x as f32, true),
                    (0, x as f32 + 1.0, false),
                    (1, y as f32, true),
                    (1, y as f32 + 1.0, false),
                ] {
                    poly = clip(&poly, axis, edge, keep_greater);
                    if poly.len() < 3 {
                        break;
                    }
                }
                if poly.len() < 3 {
                    continue;
                }
                if batches.last().is_none_or(|b| {
                    b.atlas_page != p.page
                        || b.force_opaque != opaque
                        || b.vertices.len() + poly.len() > MAX_VERTICES
                }) {
                    batches.push(MeshBatch {
                        atlas_page: p.page,
                        force_opaque: opaque,
                        vertices: Vec::new(),
                        indices: Vec::new(),
                    });
                }
                let page = &self.pages[p.page];
                let batch = batches.last_mut().unwrap();
                let base = batch.vertices.len() as u16;
                for v in &poly {
                    batch.vertices.push(Vertex {
                        position: v.position,
                        uv: [
                            (p.x as f32 + (v.uv[0] - x as f32) * self.sprite_side as f32)
                                / page.width as f32,
                            (p.y as f32 + (v.uv[1] - y as f32) * self.sprite_side as f32)
                                / page.height as f32,
                        ],
                        color: v.color,
                    });
                }
                for i in 1..poly.len() - 1 {
                    batch
                        .indices
                        .extend_from_slice(&[base, base + i as u16, base + i as u16 + 1]);
                }
            }
        }
        Ok(())
    }
}

/// `force_opaque` corresponds to Horizon's `OPAQUE_LAYER` bit. Draw the
/// batch with alpha-cutout disabled when set; draw translucent batches in
/// the order returned by `convert_quads`.
#[derive(Clone, Debug)]
pub struct MeshBatch {
    pub atlas_page: usize,
    pub force_opaque: bool,
    pub vertices: Vec<Vertex>,
    pub indices: Vec<u16>,
}

/// Group depth-writing batches by page/pass while keeping all indices within
/// the u16 vertex limit. A new batch starts when that group is full.
fn merge_solid_batches(batches: Vec<MeshBatch>) -> Vec<MeshBatch> {
    let mut out: Vec<MeshBatch> = Vec::new();
    let mut current = std::collections::HashMap::<(usize, bool), usize>::new();
    for mut batch in batches {
        let key = (batch.atlas_page, batch.force_opaque);
        let destination = current
            .get(&key)
            .copied()
            .filter(|&index| out[index].vertices.len() + batch.vertices.len() <= MAX_VERTICES);
        if let Some(index) = destination {
            let target = &mut out[index];
            let base = target.vertices.len() as u16;
            target
                .indices
                .extend(batch.indices.into_iter().map(|i| i + base));
            target.vertices.append(&mut batch.vertices);
        } else {
            current.insert(key, out.len());
            out.push(batch);
        }
    }
    out
}

#[derive(Clone, Copy)]
struct ClipVertex {
    position: [f32; 3],
    uv: [f32; 2],
    color: [f32; 4],
}
impl ClipVertex {
    fn from_source(v: &SourceVertex, origin: [f32; 3]) -> Self {
        // Temporary approximation of Horizon's lightmap until the native
        // renderer has a compatible lightmap sampler. Color alpha encodes
        // emission metadata, not opacity; atlas texture alpha remains intact.
        let sky = (v.light & 0xff) as f32 / 240.0;
        let block = (v.light >> 8) as f32 / 240.0;
        let light = sky.max(block).clamp(0.2, 1.0);
        Self {
            position: std::array::from_fn(|i| v.position[i] + origin[i]),
            uv: v.uv,
            color: [
                ((v.color & 0xff) as f32 / 255.0) * light,
                (((v.color >> 8) & 0xff) as f32 / 255.0) * light,
                (((v.color >> 16) & 0xff) as f32 / 255.0) * light,
                1.0,
            ],
        }
    }
    fn mix(a: Self, b: Self, t: f32) -> Self {
        Self {
            position: std::array::from_fn(|i| a.position[i] + (b.position[i] - a.position[i]) * t),
            uv: std::array::from_fn(|i| a.uv[i] + (b.uv[i] - a.uv[i]) * t),
            color: std::array::from_fn(|i| a.color[i] + (b.color[i] - a.color[i]) * t),
        }
    }
}

fn clip(poly: &[ClipVertex], axis: usize, edge: f32, greater: bool) -> Vec<ClipVertex> {
    let mut out = Vec::new();
    let Some(mut previous) = poly.last().copied() else {
        return out;
    };
    let inside = |v: ClipVertex| {
        if greater {
            v.uv[axis] >= edge
        } else {
            v.uv[axis] <= edge
        }
    };
    for &current in poly {
        let (a, b) = (inside(previous), inside(current));
        if a != b {
            let t = (edge - previous.uv[axis]) / (current.uv[axis] - previous.uv[axis]);
            out.push(ClipVertex::mix(previous, current, t));
        }
        if b {
            out.push(current);
        }
        previous = current;
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn quad(layer: u16, repeat: f32) -> [SourceVertex; 4] {
        [[0., 0.], [repeat, 0.], [repeat, 1.], [0., 1.]].map(|uv| SourceVertex {
            position: [uv[0], uv[1], 0.],
            uv,
            layer,
            color: 0xff33_2211,
            light: 0x00f0,
        })
    }

    #[test]
    fn mip_slots_keep_wrapped_gutters_isolated_at_every_level() {
        let levels = vec![
            [vec![11u8; 4 * 4 * 4], vec![99u8; 4 * 4 * 4]].concat(),
            [vec![11u8; 2 * 2 * 4], vec![99u8; 2 * 2 * 4]].concat(),
        ];
        let atlas = Atlas::from_mip_layers(4, 2, &levels).unwrap();
        let page = &atlas.pages[0];
        for (level, pixels) in std::iter::once(&page.rgba)
            .chain(page.mips.iter())
            .enumerate()
        {
            let tile = 8 >> level;
            let width = page.width >> level;
            for y in 0..tile {
                for x in 0..tile * 2 {
                    let px = ((y * width + x) * 4) as usize;
                    assert_eq!(pixels[px], if x < tile { 11 } else { 99 });
                }
            }
        }
        assert!(Atlas::from_mip_layers(4, 2, &levels[..1]).is_ok());
        assert!(
            Atlas::from_mip_layers(
                4,
                2,
                &levels[..2]
                    .iter()
                    .chain(std::iter::once(&vec![]))
                    .cloned()
                    .collect::<Vec<_>>()
            )
            .is_err()
        );
    }

    #[test]
    fn atlas_wraps_gutters_and_is_power_of_two() {
        let pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        let atlas = Atlas::from_layers(2, 1, &pixels).unwrap();
        let page = &atlas.pages[0];
        assert_eq!((page.width, page.height), (4, 4));
        let texel = |x: usize, y: usize| &page.rgba[(y * 4 + x) * 4..(y * 4 + x + 1) * 4];
        assert_eq!(texel(0, 0), &pixels[12..16]);
        assert_eq!(texel(1, 1), &pixels[0..4]);
        assert_eq!(texel(3, 1), &pixels[0..4]);
        assert_eq!(texel(1, 3), &pixels[0..4]);
    }

    #[test]
    fn converts_quads_and_preserves_opaque_and_color() {
        let atlas = Atlas::from_layers(1, 1, &[10, 20, 30, 40]).unwrap();
        let batches = atlas
            .convert_quads(&quad(OPAQUE_LAYER, 1.), [4., 5., 6.])
            .unwrap();
        assert_eq!(batches.len(), 1);
        assert!(batches[0].force_opaque);
        assert_eq!(batches[0].indices.len(), 6);
        assert_eq!(batches[0].vertices[0].position, [4., 5., 6.]);
        assert_eq!(
            batches[0].vertices[0].color,
            [17. / 255., 34. / 255., 51. / 255., 1.]
        );
        assert!(
            batches[0]
                .vertices
                .iter()
                .all(|v| v.uv.iter().all(|&x| (0.0..=1.0).contains(&x)))
        );
    }

    #[test]
    fn repeated_uvs_split_at_atlas_boundaries() {
        let atlas = Atlas::from_layers(1, 1, &[1, 2, 3, 255]).unwrap();
        let batches = atlas.convert_quads(&quad(0, 2.), [0.; 3]).unwrap();
        let all = &batches[0];
        assert!(all.indices.len() > 6);
        // Both repeats map to the same inner tile; no vertex crosses its gutter.
        assert!(
            all.vertices
                .iter()
                .all(|v| v.uv[0] >= 0.25 && v.uv[0] <= 0.5)
        );
        let area: f32 = all
            .indices
            .chunks_exact(3)
            .map(|i| {
                let a = all.vertices[i[0] as usize].position;
                let b = all.vertices[i[1] as usize].position;
                let c = all.vertices[i[2] as usize].position;
                ((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])).abs() * 0.5
            })
            .sum();
        assert!((area - 2.0).abs() < 1e-5, "area {area}");
    }

    #[test]
    fn splits_large_mesh_without_u16_overflow() {
        let atlas = Atlas::from_layers(1, 1, &[1, 2, 3, 255]).unwrap();
        let vertices = quad(0, 1.).repeat(12_000);
        let batches = atlas.convert_quads(&vertices, [0.; 3]).unwrap();
        assert!(batches.len() > 1);
        assert!(batches.iter().all(|b| b.vertices.len() <= MAX_VERTICES
            && b.indices.iter().all(|&i| (i as usize) < b.vertices.len())));
    }

    #[test]
    fn solid_batches_merge_alternating_passes_without_losing_triangles() {
        let atlas = Atlas::from_layers(1, 1, &[1, 2, 3, 255]).unwrap();
        let vertices: Vec<_> = (0..100)
            .flat_map(|i| {
                let mut q = quad(if i % 2 == 0 { OPAQUE_LAYER } else { 0 }, 1.);
                for v in &mut q {
                    v.position[0] += i as f32;
                }
                q
            })
            .collect();
        let ordered = atlas.convert_quads(&vertices, [0.; 3]).unwrap();
        assert_eq!(ordered.len(), 100);
        assert!(
            ordered
                .windows(2)
                .all(|pair| pair[0].force_opaque != pair[1].force_opaque)
        );
        let merged = atlas.convert_solid_quads(&vertices, [0.; 3]).unwrap();
        assert_eq!(merged.len(), 2);
        let triangles = |batches: &[MeshBatch], opaque| -> Vec<Vertex> {
            batches
                .iter()
                .filter(|b| b.force_opaque == opaque)
                .flat_map(|b| b.indices.iter().map(|&i| b.vertices[i as usize]))
                .collect()
        };
        for opaque in [false, true] {
            assert_eq!(triangles(&merged, opaque), triangles(&ordered, opaque));
        }
    }

    #[test]
    fn solid_batch_merging_splits_before_u16_limit() {
        let v = Vertex {
            position: [0.; 3],
            uv: [0.; 2],
            color: [1.; 4],
        };
        let batch = |page, count| MeshBatch {
            atlas_page: page,
            force_opaque: true,
            vertices: vec![v; count],
            indices: vec![0, 1, 2],
        };
        let merged = merge_solid_batches(vec![batch(0, 65_532), batch(1, 3), batch(0, 6)]);
        let page_zero: Vec<_> = merged.iter().filter(|b| b.atlas_page == 0).collect();
        assert_eq!(page_zero.len(), 2);
        assert_eq!(
            page_zero.iter().map(|b| b.vertices.len()).sum::<usize>(),
            65_538
        );
        assert_eq!(page_zero.iter().map(|b| b.indices.len()).sum::<usize>(), 6);
        assert!(merged.iter().all(|b| b.vertices.len() <= MAX_VERTICES
            && b.indices.iter().all(|&i| (i as usize) < b.vertices.len())));
    }

    #[test]
    fn rejects_bad_data() {
        assert!(Atlas::from_layers(0, 1, &[]).is_err());
        assert!(Atlas::from_layers(2, 1, &[0; 4]).is_err());
        let atlas = Atlas::from_layers(1, 1, &[0; 4]).unwrap();
        assert!(atlas.convert_quads(&quad(1, 1.), [0.; 3]).is_err());
        assert!(atlas.convert_quads(&quad(0, 100.), [0.; 3]).is_err());
    }
}
