//! Bridges Horizon's CPU terrain conversion to the native D3D11 renderer.
//! The game still needs to feed this scene its actual resources and meshes.

#![cfg(windows)]

use horizon_native_terrain::{Atlas, MeshBatch, SourceVertex};
use horizon_uwp_d3d11::{DrawPass, Mesh, Renderer, Texture, Vertex};
use std::{
    collections::{HashMap, HashSet},
    error::Error,
};

type Result<T> = std::result::Result<T, Box<dyn Error>>;
pub type SectionKey = (i32, i32, i32);

struct Batch {
    page: usize,
    mesh: Mesh,
    force_opaque: bool,
}
struct Section {
    origin: [f64; 3],
    solid: Vec<Batch>,
    translucent: Vec<Batch>,
}

/// Resident sections and atlas pages independent of wgpu.
pub struct TerrainScene {
    atlas: Atlas,
    textures: Vec<Texture>,
    sections: HashMap<SectionKey, Section>,
}

impl TerrainScene {
    pub fn new(renderer: &Renderer, atlas: Atlas) -> Result<Self> {
        let textures = atlas
            .pages
            .iter()
            .map(|page| renderer.create_atlas(page.width, page.height, &page.rgba))
            .collect::<std::result::Result<Vec<_>, _>>()?;
        Ok(Self {
            atlas,
            textures,
            sections: HashMap::new(),
        })
    }

    /// Converts and uploads a section transactionally. Origins remain f64 so
    /// camera-relative translation stays precise at large world coordinates.
    pub fn upload(
        &mut self,
        renderer: &Renderer,
        key: SectionKey,
        origin: [f64; 3],
        solid: &[SourceVertex],
        translucent: &[SourceVertex],
    ) -> Result<()> {
        if !origin.iter().all(|v| v.is_finite()) {
            return Err(std::io::Error::other("non-finite terrain origin").into());
        }
        let solid = upload_batches(renderer, self.atlas.convert_quads(solid, [0.0; 3])?)?;
        let translucent =
            upload_batches(renderer, self.atlas.convert_quads(translucent, [0.0; 3])?)?;
        self.sections.insert(
            key,
            Section {
                origin,
                solid,
                translucent,
            },
        );
        Ok(())
    }

    pub fn remove(&mut self, key: SectionKey) {
        self.sections.remove(&key);
    }
    pub fn has_section(&self, key: SectionKey) -> bool {
        self.sections.contains_key(&key)
    }
    pub fn remove_columns(&mut self, columns: &mut dyn Iterator<Item = (i32, i32)>) {
        let columns: HashSet<_> = columns.collect();
        self.sections
            .retain(|key, _| !columns.contains(&(key.0, key.2)));
    }
    pub fn clear(&mut self) {
        self.sections.clear();
    }
    pub fn section_count(&self) -> usize {
        self.sections.len()
    }
    /// Sections with at least one uploaded draw batch, excluding empty meshes.
    pub fn drawable_section_count(&self) -> usize {
        self.sections
            .values()
            .filter(|section| !section.solid.is_empty() || !section.translucent.is_empty())
            .count()
    }

    /// Draw solid geometry first, then translucent sections back-to-front.
    /// `view_projection` is camera-relative, with no camera translation baked in.
    /// Per-quad translucent sorting is pending.
    pub fn draw(&self, renderer: &Renderer, view_projection: [[f32; 4]; 4], camera: [f64; 3]) {
        for section in self.sections.values() {
            let matrix = section_matrix(view_projection, section.origin, camera);
            self.draw_batches(renderer, &section.solid, matrix, DrawPass::Cutout);
        }
        let mut sections: Vec<_> = self.sections.values().collect();
        let distance = |section: &Section| -> f64 {
            (0..3)
                .map(|i| (section.origin[i] + 8.0 - camera[i]).powi(2))
                .sum()
        };
        sections.sort_by(|a, b| distance(b).total_cmp(&distance(a)));
        for section in sections {
            let matrix = section_matrix(view_projection, section.origin, camera);
            self.draw_batches(
                renderer,
                &section.translucent,
                matrix,
                DrawPass::Translucent,
            );
        }
    }

    fn draw_batches(
        &self,
        renderer: &Renderer,
        batches: &[Batch],
        matrix: [[f32; 4]; 4],
        pass: DrawPass,
    ) {
        for batch in batches {
            let pass = if pass == DrawPass::Cutout && batch.force_opaque {
                DrawPass::Opaque
            } else {
                pass
            };
            renderer.draw_pass(&batch.mesh, &self.textures[batch.page], matrix, pass);
        }
    }
}

fn upload_batches(renderer: &Renderer, batches: Vec<MeshBatch>) -> Result<Vec<Batch>> {
    batches
        .into_iter()
        .map(|batch| {
            let vertices: Vec<_> = batch
                .vertices
                .into_iter()
                .map(|v| Vertex {
                    position: v.position,
                    uv: v.uv,
                    color: v.color,
                })
                .collect();
            Ok(Batch {
                page: batch.atlas_page,
                mesh: renderer.create_mesh(&vertices, &batch.indices)?,
                force_opaque: batch.force_opaque,
            })
        })
        .collect()
}

fn section_matrix(mut matrix: [[f32; 4]; 4], origin: [f64; 3], camera: [f64; 3]) -> [[f32; 4]; 4] {
    let offset: [f32; 3] = std::array::from_fn(|i| (origin[i] - camera[i]) as f32);
    for row in 0..4 {
        matrix[3][row] +=
            matrix[0][row] * offset[0] + matrix[1][row] * offset[1] + matrix[2][row] * offset[2];
    }
    matrix
}

#[cfg(test)]
mod tests {
    use super::section_matrix;
    #[test]
    fn section_translation_retains_precision_at_large_coordinates() {
        let identity = [
            [1., 0., 0., 0.],
            [0., 1., 0., 0.],
            [0., 0., 1., 0.],
            [0., 0., 0., 1.],
        ];
        let matrix = section_matrix(
            identity,
            [30_000_016., -64., 30_000_000.],
            [30_000_015.75, -64.5, 30_000_000.25],
        );
        assert_eq!(matrix[3], [0.25, 0.5, -0.25, 1.]);
    }
}
