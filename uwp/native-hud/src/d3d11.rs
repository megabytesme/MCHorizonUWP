//! The initial FL9_1 alpha HUD pass. Unsupported states return an error.
use crate::{Atlas, Blend, Draw, Error, PagePatch, SampleMode, byte_len};
use horizon_uwp_d3d11::{DrawPass, DynamicMesh, Renderer, Texture, Vertex};
use std::{
    collections::{BTreeMap, BTreeSet},
    error::Error as StdError,
};

type Result<T> = std::result::Result<T, Box<dyn StdError>>;

/// An unsupported run omitted by `draw_supported`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct UnsupportedMode {
    pub blend: Blend,
    pub sample: SampleMode,
}

// Each batch fits 16-bit indices and ends on a triangle boundary.
const MAX_BATCH_VERTICES: usize = 16_383;

#[derive(Debug, PartialEq, Eq)]
struct DrawSpan {
    draw: usize,
    source: usize,
    first: u32,
    count: u32,
}

fn batch_plan(draws: &[&Draw]) -> Vec<Vec<DrawSpan>> {
    let mut batches = Vec::new();
    let mut batch = Vec::new();
    let mut used = 0;
    for (draw, entry) in draws.iter().enumerate() {
        let mut source = 0;
        while source < entry.vertices.len() {
            let count = (entry.vertices.len() - source).min(MAX_BATCH_VERTICES - used);
            batch.push(DrawSpan {
                draw,
                source,
                first: used as u32,
                count: count as u32,
            });
            used += count;
            source += count;
            if used == MAX_BATCH_VERTICES {
                batches.push(std::mem::take(&mut batch));
                used = 0;
            }
        }
    }
    if !batch.is_empty() {
        batches.push(batch);
    }
    batches
}

/// GPU atlas pages belonging to the same `Atlas` passed at construction.
pub struct HudRenderer {
    textures: Vec<Option<Texture>>,
    layout: Vec<([u32; 2], [u32; 2])>,
    dirty: BTreeMap<usize, [u32; 4]>,
    warned: Vec<UnsupportedMode>,
    mesh: Option<DynamicMesh>,
}

impl HudRenderer {
    pub fn new(_renderer: &Renderer, atlas: &Atlas) -> Result<Self> {
        if atlas
            .pages
            .iter()
            .any(|page| page.size.contains(&0) || page.size[0] > 2048 || page.size[1] > 2048)
        {
            return Err(Box::new(Error(
                "HUD atlas page exceeds feature-level 9_1 limits",
            )));
        }
        // Avoid reserving GPU memory for pages no visible HUD draw uses.
        let textures = (0..atlas.pages.len()).map(|_| None).collect();
        let layout = atlas
            .pages
            .iter()
            .map(|page| (page.origin, page.size))
            .collect();
        Ok(Self {
            textures,
            layout,
            dirty: BTreeMap::new(),
            warned: Vec::new(),
            mesh: None,
        })
    }

    /// Render a converted batch in order after the frame has begun. Changed
    /// texture regions are uploaded before drawing. This first pass
    /// supports alpha, invert, additive and vignette atlas blending.
    pub fn draw(
        &mut self,
        renderer: &Renderer,
        atlas: &Atlas,
        patches: &[PagePatch],
        draws: &[Draw],
        viewport: [u32; 2],
        gui_scale: f32,
    ) -> Result<()> {
        self.draw_impl(renderer, atlas, patches, draws, viewport, gui_scale, false)
            .map(|_| ())
    }

    /// Draw supported runs in source order and return the skipped modes.
    /// Each distinct unsupported mode is also logged once per renderer.
    pub fn draw_supported(
        &mut self,
        renderer: &Renderer,
        atlas: &Atlas,
        patches: &[PagePatch],
        draws: &[Draw],
        viewport: [u32; 2],
        gui_scale: f32,
    ) -> Result<Vec<UnsupportedMode>> {
        let unsupported =
            self.draw_impl(renderer, atlas, patches, draws, viewport, gui_scale, true)?;
        for mode in &unsupported {
            if !self.warned.iter().any(|seen| same_mode(*seen, *mode)) {
                log::warn!(
                    "Horizon native D3D11: skipping unsupported HUD blend {:?}, sample {:?}",
                    mode.blend,
                    mode.sample
                );
                self.warned.push(*mode);
            }
        }
        Ok(unsupported)
    }

    fn draw_impl(
        &mut self,
        renderer: &Renderer,
        atlas: &Atlas,
        patches: &[PagePatch],
        draws: &[Draw],
        viewport: [u32; 2],
        gui_scale: f32,
        allow_unsupported: bool,
    ) -> Result<Vec<UnsupportedMode>> {
        if self.layout.len() != atlas.pages.len()
            || self
                .layout
                .iter()
                .zip(&atlas.pages)
                .any(|(layout, page)| *layout != (page.origin, page.size))
        {
            return Err(Box::new(Error("HUD atlas does not match GPU pages")));
        }
        // Retain coalesced changed regions across failed/unsupported frames.
        // The source atlas owns the latest pixels, so no patch bytes need to
        // be kept and the queue is bounded by the number of atlas pages.
        for patch in patches {
            record_dirty(&mut self.dirty, atlas, patch)?;
        }
        if viewport.contains(&0) || !gui_scale.is_finite() || gui_scale <= 0.0 {
            return Err(Box::new(Error("invalid HUD viewport or GUI scale")));
        }
        let (supported, unsupported) = partition_draws(draws);
        if !allow_unsupported && !unsupported.is_empty() {
            return Err(Box::new(Error(
                "HUD batch requires an unsupported blend or glint mode",
            )));
        }
        if draws
            .iter()
            .any(|draw| draw.page >= atlas.pages.len() || draw.vertices.len() % 3 != 0)
        {
            return Err(Box::new(Error(
                "HUD draw or patch refers to an invalid page",
            )));
        }
        // Only upload pages used by this batch. Keep dirty unused pages queued
        // until they next appear, and stage all replacements before committing.
        let used: BTreeSet<_> = supported.iter().map(|draw| draw.page).collect();
        let mut replacement = Vec::new();
        for &id in &used {
            if self.textures[id].is_none() {
                let page = &atlas.pages[id];
                replacement.push((
                    id,
                    renderer
                        .create_atlas(page.size[0], page.size[1], &page.rgba)
                        .map_err(|e| {
                            format!(
                                "HUD atlas page {id} ({}x{}): {e}",
                                page.size[0], page.size[1]
                            )
                        })?,
                ));
            }
        }
        for (id, texture) in replacement {
            self.textures[id] = Some(texture);
            self.dirty.remove(&id);
        }
        for &id in &used {
            if let Some(rect) = self.dirty.get(&id).copied() {
                let page = &atlas.pages[id];
                let size = [rect[2] - rect[0], rect[3] - rect[1]];
                let mut rgba = vec![0; byte_len(size)?];
                let row_bytes = size[0] as usize * 4;
                for row in 0..size[1] as usize {
                    let src =
                        ((rect[1] as usize + row) * page.size[0] as usize + rect[0] as usize) * 4;
                    let dst = row * row_bytes;
                    rgba[dst..dst + row_bytes].copy_from_slice(&page.rgba[src..src + row_bytes]);
                }
                renderer
                    .update_atlas(
                        self.textures[id].as_ref().expect("used page was uploaded"),
                        rect[0],
                        rect[1],
                        size[0],
                        size[1],
                        &rgba,
                    )
                    .map_err(|e| {
                        format!("HUD atlas page {id} update ({}x{}): {e}", size[0], size[1])
                    })?;
                self.dirty.remove(&id);
            }
        }
        let sx = 2.0 * gui_scale / viewport[0] as f32;
        let sy = -2.0 * gui_scale / viewport[1] as f32;
        let matrix = [
            [sx, 0.0, 0.0, 0.0],
            [0.0, sy, 0.0, 0.0],
            [0.0, 0.0, 1.0, 0.0],
            [-1.0, 1.0, 0.0, 1.0],
        ];
        for batch in batch_plan(&supported) {
            let count: usize = batch.iter().map(|span| span.count as usize).sum();
            let mut vertices = Vec::with_capacity(count);
            for span in &batch {
                let draw = supported[span.draw];
                let end = span.source + span.count as usize;
                vertices.extend(draw.vertices[span.source..end].iter().map(|v| Vertex {
                    position: [v.pos[0], v.pos[1], v.depth],
                    uv: v.uv,
                    color: v.color,
                }));
            }
            if self
                .mesh
                .as_ref()
                .is_none_or(|mesh| mesh.capacity() < count)
            {
                // Release the smaller buffers first, keeping peak GPU usage bounded.
                self.mesh = None;
                let capacity = count.next_power_of_two().min(MAX_BATCH_VERTICES);
                self.mesh = Some(
                    renderer
                        .create_dynamic_mesh(capacity)
                        .map_err(|e| format!("HUD batch buffer ({capacity} vertices): {e}"))?,
                );
            }
            let mesh = self.mesh.as_mut().expect("batch mesh was allocated");
            renderer
                .update_dynamic_mesh(mesh, &vertices)
                .map_err(|e| format!("HUD batch update ({count} vertices): {e}"))?;
            for span in &batch {
                let draw = supported[span.draw];
                renderer
                    .draw_pass_range(
                        mesh.mesh(),
                        self.textures[draw.page]
                            .as_ref()
                            .expect("used page was uploaded"),
                        matrix,
                        draw_pass(draw).expect("partitioned as supported"),
                        span.first,
                        span.count,
                    )
                    .map_err(|e| format!("HUD batch draw: {e}"))?;
            }
        }
        Ok(unsupported)
    }
}

fn draw_pass(draw: &Draw) -> Option<DrawPass> {
    if draw.sample != SampleMode::Atlas {
        return None;
    }
    match draw.blend {
        Blend::Alpha => Some(DrawPass::Overlay),
        Blend::Invert => Some(DrawPass::OverlayInvert),
        Blend::Additive => Some(DrawPass::OverlayAdditive),
        Blend::Vignette => Some(DrawPass::OverlayVignette),
        Blend::Depth(_) | Blend::EndPortal | Blend::CitGlint(_) => None,
    }
}

fn same_mode(a: UnsupportedMode, b: UnsupportedMode) -> bool {
    a.sample == b.sample
        && (a.blend == b.blend || matches!((a.blend, b.blend), (Blend::Depth(_), Blend::Depth(_))))
}

fn partition_draws(draws: &[Draw]) -> (Vec<&Draw>, Vec<UnsupportedMode>) {
    let mut supported = Vec::new();
    let mut unsupported = Vec::new();
    for draw in draws {
        if draw_pass(draw).is_some() {
            supported.push(draw);
        } else {
            let mode = UnsupportedMode {
                blend: draw.blend,
                sample: draw.sample,
            };
            if !unsupported.iter().any(|seen| same_mode(*seen, mode)) {
                unsupported.push(mode);
            }
        }
    }
    (supported, unsupported)
}

fn record_dirty(
    dirty: &mut BTreeMap<usize, [u32; 4]>,
    atlas: &Atlas,
    patch: &PagePatch,
) -> Result<()> {
    let Some(page) = atlas.pages.get(patch.page) else {
        return Err(Box::new(Error("HUD patch refers to an invalid page")));
    };
    let right = patch.origin[0].checked_add(patch.size[0]);
    let bottom = patch.origin[1].checked_add(patch.size[1]);
    if patch.size.contains(&0)
        || patch.rgba.len() != byte_len(patch.size)?
        || right.is_none_or(|v| v > page.size[0])
        || bottom.is_none_or(|v| v > page.size[1])
    {
        return Err(Box::new(Error("HUD patch is outside its page")));
    }
    let rect = [
        patch.origin[0],
        patch.origin[1],
        right.unwrap(),
        bottom.unwrap(),
    ];
    dirty
        .entry(patch.page)
        .and_modify(|r| {
            r[0] = r[0].min(rect[0]);
            r[1] = r[1].min(rect[1]);
            r[2] = r[2].max(rect[2]);
            r[3] = r[3].max(rect[3]);
        })
        .or_insert(rect);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn batches_keep_draw_order_and_split_only_on_triangle_boundaries() {
        let vertex = crate::Vertex {
            pos: [0.0; 2],
            uv: [0.0; 2],
            color: [1.0; 4],
            depth: 0.0,
            glint_uv: [0.0; 2],
        };
        let a = Draw {
            page: 0,
            blend: Blend::Alpha,
            sample: SampleMode::Atlas,
            vertices: vec![vertex; MAX_BATCH_VERTICES - 3],
        };
        let b = Draw {
            page: 1,
            blend: Blend::Invert,
            sample: SampleMode::Atlas,
            vertices: vec![vertex; 9],
        };
        let c = Draw {
            page: 0,
            blend: Blend::Additive,
            sample: SampleMode::Atlas,
            vertices: vec![vertex; 3],
        };
        let plan = batch_plan(&[&a, &b, &c]);
        assert_eq!(plan.len(), 2);
        assert_eq!(
            plan[0],
            [
                DrawSpan {
                    draw: 0,
                    source: 0,
                    first: 0,
                    count: (MAX_BATCH_VERTICES - 3) as u32
                },
                DrawSpan {
                    draw: 1,
                    source: 0,
                    first: (MAX_BATCH_VERTICES - 3) as u32,
                    count: 3
                },
            ]
        );
        assert_eq!(
            plan[1],
            [
                DrawSpan {
                    draw: 1,
                    source: 3,
                    first: 0,
                    count: 6
                },
                DrawSpan {
                    draw: 2,
                    source: 0,
                    first: 6,
                    count: 3
                },
            ]
        );
    }
    #[test]
    fn supported_runs_keep_order_and_unsupported_modes_are_distinct() {
        let draw = |page, blend, sample| Draw {
            page,
            blend,
            sample,
            vertices: Vec::new(),
        };
        let draws = [
            draw(0, Blend::Alpha, SampleMode::Atlas),
            draw(1, Blend::Depth([1, 2, 3, 4]), SampleMode::Atlas),
            draw(2, Blend::Invert, SampleMode::Atlas),
            draw(3, Blend::Depth([5, 6, 7, 8]), SampleMode::Atlas),
            draw(4, Blend::Alpha, SampleMode::GlintMask),
        ];
        let (supported, unsupported) = partition_draws(&draws);
        assert_eq!(
            supported.iter().map(|d| d.page).collect::<Vec<_>>(),
            vec![0, 2]
        );
        assert_eq!(unsupported.len(), 2);
        assert!(matches!(unsupported[0].blend, Blend::Depth(_)));
        assert_eq!(unsupported[1].sample, SampleMode::GlintMask);
    }

    #[test]
    fn pending_updates_are_coalesced_and_bounds_checked() {
        let atlas = Atlas::new([4, 4], &vec![0; 4 * 4 * 4], 4).unwrap();
        let mut dirty = BTreeMap::new();
        let mut patch = PagePatch {
            page: 0,
            origin: [1, 0],
            size: [1, 2],
            rgba: vec![0; 8],
        };
        record_dirty(&mut dirty, &atlas, &patch).unwrap();
        patch.origin = [3, 2];
        record_dirty(&mut dirty, &atlas, &patch).unwrap();
        assert_eq!(dirty.get(&0), Some(&[1, 0, 4, 4]));
        patch.origin = [4, 0];
        assert!(record_dirty(&mut dirty, &atlas, &patch).is_err());
        assert_eq!(dirty.get(&0), Some(&[1, 0, 4, 4]));
    }
}
