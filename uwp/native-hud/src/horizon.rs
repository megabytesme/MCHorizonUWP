//! Direct conversion from Horizon's real HUD atlas and per-frame batch.

use crate::{Atlas, Blend, Draw, Error, PagePatch, Run, Vertex, byte_len};
use horizon_client_render::hud::{Blend as HorizonBlend, HudAtlas, HudBatch};

/// Everything the D3D11 backend needs in source draw order.
pub struct ConvertedHud {
    pub draws: Vec<Draw>,
    pub patches: Vec<PagePatch>,
    /// GUI-space dimensions and the scale to physical pixels.
    pub size: [f32; 2],
    pub scale: f32,
}

impl Atlas {
    /// Page Horizon's actual CPU-side UI atlas.
    pub fn from_horizon(atlas: &HudAtlas, max_page_size: u32) -> Result<Self, Error> {
        Self::new([atlas.width, atlas.height], &atlas.pixels, max_page_size)
    }

    /// Convert real HUD triangles and apply any font glyph patches. The
    /// returned blend/sample modes must be handled explicitly by the backend.
    pub fn convert_horizon(&mut self, batch: &HudBatch) -> Result<ConvertedHud, Error> {
        for patch in &batch.patches {
            let origin = [patch.x, patch.y];
            let size = [patch.width, patch.height];
            if size.contains(&0)
                || patch.rgba.len() != byte_len(size)?
                || (0..2).any(|i| {
                    origin[i]
                        .checked_add(size[i])
                        .is_none_or(|end| end > self.size[i])
                })
            {
                return Err(Error("patch outside atlas or invalid pixel data"));
            }
        }
        let vertices: Vec<_> = batch
            .vertices
            .iter()
            .map(|v| Vertex {
                pos: v.pos,
                uv: v.uv,
                color: [0, 8, 16, 24].map(|shift| ((v.color >> shift) & 255) as f32 / 255.0),
                depth: v.depth,
                glint_uv: v.glint_uv,
            })
            .collect();
        let runs: Vec<_> = batch
            .runs()
            .into_iter()
            .map(|(r, blend)| Run {
                vertices: r.start as usize..r.end as usize,
                blend: match blend {
                    HorizonBlend::Alpha => Blend::Alpha,
                    HorizonBlend::Invert => Blend::Invert,
                    HorizonBlend::Additive => Blend::Additive,
                    HorizonBlend::Depth(rect) => Blend::Depth(rect),
                    HorizonBlend::Vignette => Blend::Vignette,
                    HorizonBlend::EndPortal => Blend::EndPortal,
                    HorizonBlend::CitGlint(index) => Blend::CitGlint(index),
                },
            })
            .collect();
        let draws = self.convert(&vertices, &runs)?;
        let mut patches = Vec::new();
        for patch in &batch.patches {
            patches.extend(self.patch(
                [patch.x, patch.y],
                [patch.width, patch.height],
                &patch.rgba,
            )?);
        }
        Ok(ConvertedHud {
            draws,
            patches,
            size: [batch.width, batch.height],
            scale: batch.scale,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn real_batch_retains_pixel_color_blend_and_page() {
        let mut atlas = Atlas::new([4, 4], &vec![255; 64], 2).unwrap();
        let mut batch = HudBatch::new([640, 480]);
        batch.quad(
            [[0.0, 0.0], [10.0, 0.0], [10.0, 10.0], [0.0, 10.0]],
            [[0.1, 0.1], [0.9, 0.1], [0.9, 0.9], [0.1, 0.9]],
            0x80402010,
        );
        batch.blend_since(0, HorizonBlend::Invert);
        let converted = atlas.convert_horizon(&batch).unwrap();
        assert_eq!(converted.scale, 2.0);
        assert!(
            converted
                .draws
                .iter()
                .all(|draw| draw.blend == Blend::Invert)
        );
        assert!(
            converted
                .draws
                .iter()
                .flat_map(|draw| &draw.vertices)
                .all(|v| (v.color[0] - 16.0 / 255.0).abs() < 1e-6
                    && (v.color[3] - 128.0 / 255.0).abs() < 1e-6)
        );
        assert!(converted.draws.iter().any(|draw| draw.page == 3));
    }
}
