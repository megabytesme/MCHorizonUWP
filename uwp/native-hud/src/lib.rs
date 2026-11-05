//! CPU conversion of Horizon HUD geometry for feature-level 9_1 textures.
//! No graphics API is required. Consumers must preserve returned draw order.

use std::ops::Range;

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Vertex {
    pub pos: [f32; 2],
    /// Normalized coordinates in the original atlas. U >= 2 marks glint.
    pub uv: [f32; 2],
    /// Linear interpolation of the original RGBA8 vertex color.
    pub color: [f32; 4],
    pub depth: f32,
    pub glint_uv: [f32; 2],
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SampleMode {
    Atlas,
    GlintMask,
}

/// State associated with a source run; unsupported modes remain explicit so a
/// backend can report them rather than incorrectly rendering them as alpha.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Blend {
    Alpha,
    Invert,
    Additive,
    Depth([u32; 4]),
    Vignette,
    EndPortal,
    /// Upstream CIT glint needs its own textures and blend pipeline.
    CitGlint(u8),
}

#[derive(Clone, Debug, PartialEq)]
pub struct Run {
    pub vertices: Range<usize>,
    pub blend: Blend,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Draw {
    pub page: usize,
    pub blend: Blend,
    pub sample: SampleMode,
    /// Triangle-list vertices with page-local UVs; other attributes unchanged.
    pub vertices: Vec<Vertex>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Page {
    pub origin: [u32; 2],
    pub size: [u32; 2],
    pub rgba: Vec<u8>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PagePatch {
    pub page: usize,
    pub origin: [u32; 2],
    pub size: [u32; 2],
    pub rgba: Vec<u8>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Error(pub &'static str);
impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(self.0)
    }
}
impl std::error::Error for Error {}

#[derive(Clone, Debug)]
pub struct Atlas {
    size: [u32; 2],
    page_size: u32,
    columns: u32,
    pub pages: Vec<Page>,
}

fn byte_len(size: [u32; 2]) -> Result<usize, Error> {
    (size[0] as usize)
        .checked_mul(size[1] as usize)
        .and_then(|n| n.checked_mul(4))
        .ok_or(Error("atlas size overflows"))
}

impl Atlas {
    /// Split a packed RGBA atlas into pages no larger than `page_size`.
    pub fn new(size: [u32; 2], rgba: &[u8], page_size: u32) -> Result<Self, Error> {
        if size.contains(&0) || page_size == 0 || rgba.len() != byte_len(size)? {
            return Err(Error("invalid atlas dimensions or pixel data"));
        }
        let columns = size[0].div_ceil(page_size);
        let rows = size[1].div_ceil(page_size);
        let mut pages = Vec::new();
        for y in 0..rows {
            for x in 0..columns {
                let origin = [x * page_size, y * page_size];
                let page = [
                    page_size.min(size[0] - origin[0]),
                    page_size.min(size[1] - origin[1]),
                ];
                let mut pixels = vec![0; byte_len(page)?];
                for row in 0..page[1] as usize {
                    let src =
                        ((origin[1] as usize + row) * size[0] as usize + origin[0] as usize) * 4;
                    let dst = row * page[0] as usize * 4;
                    pixels[dst..dst + page[0] as usize * 4]
                        .copy_from_slice(&rgba[src..src + page[0] as usize * 4]);
                }
                pages.push(Page {
                    origin,
                    size: page,
                    rgba: pixels,
                });
            }
        }
        Ok(Self {
            size,
            page_size,
            columns,
            pages,
        })
    }

    /// Apply a glyph/image patch and return page-local updates for the GPU.
    pub fn patch(
        &mut self,
        origin: [u32; 2],
        size: [u32; 2],
        rgba: &[u8],
    ) -> Result<Vec<PagePatch>, Error> {
        if size.contains(&0)
            || rgba.len() != byte_len(size)?
            || (0..2).any(|i| {
                origin[i]
                    .checked_add(size[i])
                    .is_none_or(|end| end > self.size[i])
            })
        {
            return Err(Error("patch outside atlas or invalid pixel data"));
        }
        let mut result = Vec::new();
        for (page_id, page) in self.pages.iter_mut().enumerate() {
            let lo = [origin[0].max(page.origin[0]), origin[1].max(page.origin[1])];
            let hi = [
                (origin[0] + size[0]).min(page.origin[0] + page.size[0]),
                (origin[1] + size[1]).min(page.origin[1] + page.size[1]),
            ];
            if lo[0] >= hi[0] || lo[1] >= hi[1] {
                continue;
            }
            let local = [lo[0] - page.origin[0], lo[1] - page.origin[1]];
            let extent = [hi[0] - lo[0], hi[1] - lo[1]];
            let mut pixels = vec![0; byte_len(extent)?];
            for row in 0..extent[1] as usize {
                let src = ((lo[1] - origin[1]) as usize + row) * size[0] as usize * 4
                    + (lo[0] - origin[0]) as usize * 4;
                let dst = row * extent[0] as usize * 4;
                let page_dst =
                    (local[1] as usize + row) * page.size[0] as usize * 4 + local[0] as usize * 4;
                let bytes = extent[0] as usize * 4;
                pixels[dst..dst + bytes].copy_from_slice(&rgba[src..src + bytes]);
                page.rgba[page_dst..page_dst + bytes].copy_from_slice(&rgba[src..src + bytes]);
            }
            result.push(PagePatch {
                page: page_id,
                origin: local,
                size: extent,
                rgba: pixels,
            });
        }
        Ok(result)
    }

    /// Split ordered triangle runs at atlas page boundaries. Input UVs must be
    /// within the atlas, apart from the `u + 2` glint-mask convention.
    pub fn convert(&self, vertices: &[Vertex], runs: &[Run]) -> Result<Vec<Draw>, Error> {
        let mut draws: Vec<Draw> = Vec::new();
        let mut expected = 0;
        for run in runs {
            if run.vertices.start != expected
                || run.vertices.end > vertices.len()
                || run.vertices.start > run.vertices.end
                || run.vertices.len() % 3 != 0
            {
                return Err(Error("HUD runs must cover triangles in order"));
            }
            expected = run.vertices.end;
            for triangle in vertices[run.vertices.clone()].chunks_exact(3) {
                let mode = if triangle[0].uv[0] >= 2.0 {
                    SampleMode::GlintMask
                } else {
                    SampleMode::Atlas
                };
                let mut tri = [triangle[0], triangle[1], triangle[2]];
                for v in &mut tri {
                    if v.pos
                        .into_iter()
                        .chain(v.uv)
                        .chain(v.color)
                        .chain([v.depth])
                        .chain(v.glint_uv)
                        .any(|x| !x.is_finite())
                    {
                        return Err(Error("non-finite HUD vertex"));
                    }
                    if (v.uv[0] >= 2.0) != (mode == SampleMode::GlintMask) {
                        return Err(Error("mixed glint and atlas triangle"));
                    }
                    if mode == SampleMode::GlintMask {
                        v.uv[0] -= 2.0;
                    }
                    if v.uv[0] < 0.0 || v.uv[1] < 0.0 || v.uv[0] > 1.0 || v.uv[1] > 1.0 {
                        return Err(Error("HUD UV outside atlas"));
                    }
                    v.uv[0] *= self.size[0] as f32;
                    v.uv[1] *= self.size[1] as f32;
                }
                let page_at = |value: f32, axis: usize| -> u32 {
                    ((value / self.page_size as f32) as u32)
                        .min((self.size[axis] - 1) / self.page_size)
                };
                let min = [
                    tri.iter().map(|v| v.uv[0]).fold(f32::INFINITY, f32::min),
                    tri.iter().map(|v| v.uv[1]).fold(f32::INFINITY, f32::min),
                ];
                let max = [
                    tri.iter()
                        .map(|v| v.uv[0])
                        .fold(f32::NEG_INFINITY, f32::max),
                    tri.iter()
                        .map(|v| v.uv[1])
                        .fold(f32::NEG_INFINITY, f32::max),
                ];
                for py in page_at(min[1], 1)..=page_at(max[1], 1) {
                    for px in page_at(min[0], 0)..=page_at(max[0], 0) {
                        let page_id = (py * self.columns + px) as usize;
                        let page = &self.pages[page_id];
                        let mut polygon = tri.to_vec();
                        for axis in 0..2 {
                            polygon = clip(&polygon, axis, page.origin[axis] as f32, true);
                            polygon = clip(
                                &polygon,
                                axis,
                                (page.origin[axis] + page.size[axis]) as f32,
                                false,
                            );
                        }
                        if polygon.len() < 3 {
                            continue;
                        }
                        for v in &mut polygon {
                            for i in 0..2 {
                                v.uv[i] = (v.uv[i] - page.origin[i] as f32) / page.size[i] as f32;
                            }
                        }
                        for i in 1..polygon.len() - 1 {
                            let t = [polygon[0], polygon[i], polygon[i + 1]];
                            let area = (t[1].pos[0] - t[0].pos[0]) * (t[2].pos[1] - t[0].pos[1])
                                - (t[1].pos[1] - t[0].pos[1]) * (t[2].pos[0] - t[0].pos[0]);
                            if area.abs() <= 1e-8 {
                                continue;
                            }
                            if draws.last().is_none_or(|d| {
                                d.page != page_id || d.blend != run.blend || d.sample != mode
                            }) {
                                draws.push(Draw {
                                    page: page_id,
                                    blend: run.blend,
                                    sample: mode,
                                    vertices: Vec::new(),
                                });
                            }
                            draws.last_mut().unwrap().vertices.extend(t);
                        }
                    }
                }
            }
        }
        if expected != vertices.len() {
            return Err(Error("HUD runs do not cover all vertices"));
        }
        Ok(draws)
    }
}

fn clip(vertices: &[Vertex], axis: usize, bound: f32, above: bool) -> Vec<Vertex> {
    if vertices.is_empty() {
        return Vec::new();
    }
    let inside = |v: &Vertex| {
        if above {
            v.uv[axis] >= bound
        } else {
            v.uv[axis] <= bound
        }
    };
    let mut out = Vec::new();
    for i in 0..vertices.len() {
        let a = vertices[i];
        let b = vertices[(i + 1) % vertices.len()];
        let ai = inside(&a);
        let bi = inside(&b);
        if ai {
            out.push(a);
        }
        if ai != bi {
            let t = (bound - a.uv[axis]) / (b.uv[axis] - a.uv[axis]);
            let mix = |x: f32, y: f32| x + (y - x) * t;
            let mut v = Vertex {
                pos: [0.0; 2],
                uv: [0.0; 2],
                color: [0.0; 4],
                depth: mix(a.depth, b.depth),
                glint_uv: [0.0; 2],
            };
            for j in 0..2 {
                v.pos[j] = mix(a.pos[j], b.pos[j]);
                v.uv[j] = mix(a.uv[j], b.uv[j]);
                v.glint_uv[j] = mix(a.glint_uv[j], b.glint_uv[j]);
            }
            for j in 0..4 {
                v.color[j] = mix(a.color[j], b.color[j]);
            }
            v.uv[axis] = bound;
            out.push(v);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    fn vertex(pos: [f32; 2], uv: [f32; 2], color: [f32; 4]) -> Vertex {
        Vertex {
            pos,
            uv,
            color,
            depth: pos[0],
            glint_uv: pos,
        }
    }
    fn area(draws: &[Draw]) -> f32 {
        draws
            .iter()
            .flat_map(|d| d.vertices.chunks_exact(3))
            .map(|v| {
                ((v[1].pos[0] - v[0].pos[0]) * (v[2].pos[1] - v[0].pos[1])
                    - (v[1].pos[1] - v[0].pos[1]) * (v[2].pos[0] - v[0].pos[0]))
                    .abs()
                    / 2.0
            })
            .sum()
    }
    #[test]
    fn crossing_triangles_keep_area_page_order_and_interpolated_attributes() {
        let atlas = Atlas::new([8, 4], &vec![0; 8 * 4 * 4], 4).unwrap();
        let t = [
            vertex([0.0, 0.0], [0.25, 0.0], [0.0; 4]),
            vertex([4.0, 0.0], [0.75, 0.0], [1.0; 4]),
            vertex([0.0, 4.0], [0.25, 1.0], [0.0; 4]),
        ];
        let vertices = [t, t].concat();
        let runs = [
            Run {
                vertices: 0..3,
                blend: Blend::Alpha,
            },
            Run {
                vertices: 3..6,
                blend: Blend::Additive,
            },
        ];
        let draws = atlas.convert(&vertices, &runs).unwrap();
        assert_eq!(
            draws.iter().map(|d| (d.page, d.blend)).collect::<Vec<_>>(),
            vec![
                (0, Blend::Alpha),
                (1, Blend::Alpha),
                (0, Blend::Additive),
                (1, Blend::Additive)
            ]
        );
        assert!((area(&draws) - 16.0).abs() < 1e-5);
        assert!(
            draws
                .iter()
                .flat_map(|d| &d.vertices)
                .any(|v| (v.pos[0] - 2.0).abs() < 1e-5
                    && (v.color[0] - 0.5).abs() < 1e-5
                    && (v.depth - 2.0).abs() < 1e-5)
        );
        assert!(
            draws
                .iter()
                .flat_map(|d| &d.vertices)
                .all(|v| v.uv.into_iter().all(|x| (0.0..=1.0).contains(&x)))
        );
    }
    #[test]
    fn patches_split_and_update_the_correct_page_pixels() {
        let mut atlas = Atlas::new([5, 3], &vec![0; 5 * 3 * 4], 2).unwrap();
        let patch = vec![7; 3 * 2 * 4];
        let updates = atlas.patch([1, 1], [3, 2], &patch).unwrap();
        assert_eq!(
            updates.iter().map(|p| p.page).collect::<Vec<_>>(),
            vec![0, 1, 3, 4]
        );
        for y in 0..3 {
            for x in 0..5 {
                let page = &atlas.pages[((y / 2) * 3 + x / 2) as usize];
                let pixel = ((y % 2 * page.size[0] + x % 2) * 4) as usize;
                assert_eq!(
                    page.rgba[pixel],
                    if x >= 1 && x < 4 && y >= 1 { 7 } else { 0 }
                );
            }
        }
    }
    #[test]
    fn rejects_invalid_geometry_and_preserves_glint_marker() {
        let atlas = Atlas::new([4, 4], &vec![0; 64], 2).unwrap();
        let mut t = [
            vertex([0.0, 0.0], [2.25, 0.0], [1.0; 4]),
            vertex([2.0, 0.0], [2.75, 0.0], [1.0; 4]),
            vertex([0.0, 2.0], [2.25, 1.0], [1.0; 4]),
        ];
        let runs = [Run {
            vertices: 0..3,
            blend: Blend::Depth([1, 2, 3, 4]),
        }];
        let draws = atlas.convert(&t, &runs).unwrap();
        assert!(
            draws
                .iter()
                .all(|d| d.sample == SampleMode::GlintMask && d.blend == runs[0].blend)
        );
        t[2].uv[0] -= 2.0;
        assert!(atlas.convert(&t, &runs).is_err());
        assert!(
            atlas
                .convert(
                    &t,
                    &[Run {
                        vertices: 1..3,
                        blend: Blend::Alpha
                    }]
                )
                .is_err()
        );
    }
    #[test]
    fn paged_triangles_match_unpaged_point_sampling() {
        let mut pixels = vec![0; 8 * 8 * 4];
        for y in 0..8 {
            for x in 0..8 {
                pixels[(y * 8 + x) * 4] = (y * 8 + x) as u8;
                pixels[(y * 8 + x) * 4 + 3] = 255;
            }
        }
        let atlas = Atlas::new([8, 8], &pixels, 4).unwrap();
        let vertices = [
            vertex([0.0, 0.0], [0.0, 0.0], [1.0; 4]),
            vertex([8.0, 0.0], [1.0, 0.0], [1.0; 4]),
            vertex([0.0, 8.0], [0.0, 1.0], [1.0; 4]),
        ];
        let draws = atlas
            .convert(
                &vertices,
                &[Run {
                    vertices: 0..3,
                    blend: Blend::Alpha,
                }],
            )
            .unwrap();
        for y in 0..7 {
            for x in 0..7 - y {
                // Offset from integer edges, including page boundaries, so the
                // test does not depend on triangle rasterizer edge ownership.
                let p = [x as f32 + 0.31, y as f32 + 0.17];
                let expected = pixels[(y * 8 + x) * 4];
                let mut found = None;
                for draw in &draws {
                    for triangle in draw.vertices.chunks_exact(3) {
                        let a = triangle[0].pos;
                        let b = triangle[1].pos;
                        let c = triangle[2].pos;
                        let det = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1]);
                        let w0 =
                            ((b[1] - c[1]) * (p[0] - c[0]) + (c[0] - b[0]) * (p[1] - c[1])) / det;
                        let w1 =
                            ((c[1] - a[1]) * (p[0] - c[0]) + (a[0] - c[0]) * (p[1] - c[1])) / det;
                        let w2 = 1.0 - w0 - w1;
                        if w0 >= -1e-6 && w1 >= -1e-6 && w2 >= -1e-6 {
                            let uv = [0, 1].map(|i| {
                                triangle[0].uv[i] * w0
                                    + triangle[1].uv[i] * w1
                                    + triangle[2].uv[i] * w2
                            });
                            let page = &atlas.pages[draw.page];
                            let u = ((uv[0] * page.size[0] as f32) as u32).min(page.size[0] - 1);
                            let v = ((uv[1] * page.size[1] as f32) as u32).min(page.size[1] - 1);
                            found = Some(page.rgba[((v * page.size[0] + u) * 4) as usize]);
                            break;
                        }
                    }
                }
                assert_eq!(found, Some(expected), "point {p:?}");
            }
        }
    }
}

#[cfg(feature = "horizon")]
pub mod horizon;

#[cfg(all(feature = "d3d11", windows))]
pub mod d3d11;
