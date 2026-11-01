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
}
struct Section {
    origin: [f32; 3],
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

    /// Converts and uploads a section transactionally. Origin is in the same
    /// world-space coordinates expected by the supplied view-projection matrix.
    pub fn upload(
        &mut self,
        renderer: &Renderer,
        key: SectionKey,
        origin: [f32; 3],
        solid: &[SourceVertex],
        translucent: &[SourceVertex],
    ) -> Result<()> {
        let solid = upload_batches(renderer, self.atlas.convert_quads(solid, origin)?)?;
        let translucent = upload_batches(renderer, self.atlas.convert_quads(translucent, origin)?)?;
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

    /// Draw solid geometry first, then translucent sections back-to-front.
    /// Per-quad translucent sorting and camera-relative precision are pending.
    pub fn draw(&self, renderer: &Renderer, view_projection: [[f32; 4]; 4], camera: [f32; 3]) {
        for section in self.sections.values() {
            self.draw_batches(renderer, &section.solid, view_projection, DrawPass::Cutout);
        }
        let mut sections: Vec<_> = self.sections.values().collect();
        let distance = |section: &Section| -> f32 {
            (0..3)
                .map(|i| (section.origin[i] + 8.0 - camera[i]).powi(2))
                .sum()
        };
        sections.sort_by(|a, b| distance(b).total_cmp(&distance(a)));
        for section in sections {
            self.draw_batches(
                renderer,
                &section.translucent,
                view_projection,
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
            })
        })
        .collect()
}
