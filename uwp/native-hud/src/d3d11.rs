//! The initial FL9_1 alpha HUD pass. Unsupported states return an error.
use crate::{Atlas, Blend, Draw, Error, PagePatch, SampleMode};
use horizon_uwp_d3d11::{DrawPass, Renderer, Texture, Vertex};
use std::{collections::BTreeSet, error::Error as StdError};

type Result<T> = std::result::Result<T, Box<dyn StdError>>;

/// GPU atlas pages belonging to the same `Atlas` passed at construction.
pub struct HudRenderer {
    textures: Vec<Option<Texture>>,
    layout: Vec<([u32; 2], [u32; 2])>,
    dirty: BTreeSet<usize>,
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
            dirty: BTreeSet::new(),
        })
    }

    /// Render a converted batch in order after the frame has begun. Immutable
    /// texture pages touched by patches are recreated before drawing. This
    /// first pass explicitly supports standard atlas alpha blending only.
    pub fn draw(
        &mut self,
        renderer: &Renderer,
        atlas: &Atlas,
        patches: &[PagePatch],
        draws: &[Draw],
        viewport: [u32; 2],
        gui_scale: f32,
    ) -> Result<()> {
        if self.layout.len() != atlas.pages.len()
            || self
                .layout
                .iter()
                .zip(&atlas.pages)
                .any(|(layout, page)| *layout != (page.origin, page.size))
        {
            return Err(Box::new(Error("HUD atlas does not match GPU pages")));
        }
        if patches.iter().any(|patch| patch.page >= atlas.pages.len()) {
            return Err(Box::new(Error("HUD patch refers to an invalid page")));
        }
        // Keep page changes even if validation or upload fails, because a
        // subsequent frame may no longer carry the original glyph patches.
        self.dirty.extend(patches.iter().map(|patch| patch.page));
        if viewport.contains(&0) || !gui_scale.is_finite() || gui_scale <= 0.0 {
            return Err(Box::new(Error("invalid HUD viewport or GUI scale")));
        }
        if draws
            .iter()
            .any(|draw| draw.blend != Blend::Alpha || draw.sample != SampleMode::Atlas)
        {
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
        let used: BTreeSet<_> = draws.iter().map(|draw| draw.page).collect();
        let mut replacement = Vec::new();
        for &id in &used {
            if self.textures[id].is_none() || self.dirty.contains(&id) {
                let page = &atlas.pages[id];
                replacement.push((
                    id,
                    renderer.create_atlas(page.size[0], page.size[1], &page.rgba)?,
                ));
            }
        }
        let mut meshes = Vec::new();
        for draw in draws {
            // 65,535 is divisible by three and the maximum 16-bit index remains
            // valid even when the original batch contains more vertices.
            for part in draw.vertices.chunks(65_535) {
                if part.is_empty() {
                    continue;
                }
                let vertices: Vec<_> = part
                    .iter()
                    .map(|v| Vertex {
                        position: [v.pos[0], v.pos[1], v.depth],
                        uv: v.uv,
                        color: v.color,
                    })
                    .collect();
                let indices: Vec<u16> = (0..part.len()).map(|i| i as u16).collect();
                meshes.push((draw.page, renderer.create_mesh(&vertices, &indices)?));
            }
        }
        for (id, texture) in replacement {
            self.textures[id] = Some(texture);
            self.dirty.remove(&id);
        }
        let sx = 2.0 * gui_scale / viewport[0] as f32;
        let sy = -2.0 * gui_scale / viewport[1] as f32;
        let matrix = [
            [sx, 0.0, 0.0, 0.0],
            [0.0, sy, 0.0, 0.0],
            [0.0, 0.0, 1.0, 0.0],
            [-1.0, 1.0, 0.0, 1.0],
        ];
        for (id, mesh) in meshes {
            renderer.draw_pass(
                &mesh,
                self.textures[id].as_ref().expect("used page was uploaded"),
                matrix,
                DrawPass::Overlay,
            );
        }
        Ok(())
    }
}
