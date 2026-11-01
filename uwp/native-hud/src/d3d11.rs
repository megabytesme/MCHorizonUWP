//! The initial FL9_1 alpha HUD pass. Unsupported states return an error.
use crate::{Atlas, Blend, Draw, Error, PagePatch, SampleMode};
use horizon_uwp_d3d11::{DrawPass, Renderer, Texture, Vertex};
use std::{collections::BTreeSet, error::Error as StdError};

type Result<T> = std::result::Result<T, Box<dyn StdError>>;

/// GPU atlas pages belonging to the same `Atlas` passed at construction.
pub struct HudRenderer {
    textures: Vec<Texture>,
    layout: Vec<([u32; 2], [u32; 2])>,
}

impl HudRenderer {
    pub fn new(renderer: &Renderer, atlas: &Atlas) -> Result<Self> {
        let textures = atlas
            .pages
            .iter()
            .map(|page| renderer.create_atlas(page.size[0], page.size[1], &page.rgba))
            .collect::<std::result::Result<Vec<_>, _>>()?;
        let layout = atlas
            .pages
            .iter()
            .map(|page| (page.origin, page.size))
            .collect();
        Ok(Self { textures, layout })
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
        if viewport.contains(&0) || !gui_scale.is_finite() || gui_scale <= 0.0 {
            return Err(Box::new(Error("invalid HUD viewport or GUI scale")));
        }
        if self.layout.len() != atlas.pages.len()
            || self
                .layout
                .iter()
                .zip(&atlas.pages)
                .any(|(layout, page)| *layout != (page.origin, page.size))
        {
            return Err(Box::new(Error("HUD atlas does not match GPU pages")));
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
            || patches.iter().any(|patch| patch.page >= atlas.pages.len())
        {
            return Err(Box::new(Error(
                "HUD draw or patch refers to an invalid page",
            )));
        }
        let changed: BTreeSet<_> = patches.iter().map(|patch| patch.page).collect();
        // Finish all uploads before replacing resident textures so failure is
        // recoverable by retrying this same batch.
        let mut replacement = Vec::new();
        for id in changed {
            let page = &atlas.pages[id];
            replacement.push((
                id,
                renderer.create_atlas(page.size[0], page.size[1], &page.rgba)?,
            ));
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
            self.textures[id] = texture;
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
            renderer.draw_pass(&mesh, &self.textures[id], matrix, DrawPass::Overlay);
        }
        Ok(())
    }
}
