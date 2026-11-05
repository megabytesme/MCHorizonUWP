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
    gpu_bytes: usize,
}
struct Section {
    origin: [f64; 3],
    bounds: Option<([f32; 3], [f32; 3])>,
    solid: Vec<Batch>,
    translucent: Vec<Batch>,
}

/// Resident sections and atlas pages independent of wgpu.
pub struct TerrainScene {
    atlas: Atlas,
    textures: Vec<Texture>,
    atlas_bytes: usize,
    sections: HashMap<SectionKey, Section>,
}

impl TerrainScene {
    pub fn new(renderer: &Renderer, mut atlas: Atlas) -> Result<Self> {
        let mut textures = Vec::with_capacity(atlas.pages.len());
        let mut atlas_bytes = 0usize;
        for page in &mut atlas.pages {
            let levels: Vec<&[u8]> = std::iter::once(page.rgba.as_slice())
                .chain(page.mips.iter().map(Vec::as_slice))
                .collect();
            let texture = renderer.create_atlas_mips(page.width, page.height, &levels)?;
            atlas_bytes += levels.iter().map(|level| level.len()).sum::<usize>();
            // Mesh conversion only needs dimensions and placements. Release each
            // CPU page promptly, reducing the startup peak on smaller devices.
            page.rgba = Vec::new();
            page.mips = Vec::new();
            textures.push(texture);
        }
        Ok(Self {
            atlas,
            textures,
            atlas_bytes,
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
        let bounds = section_bounds(solid.iter().chain(translucent));
        let solid = upload_batches(renderer, self.atlas.convert_solid_quads(solid, [0.0; 3])?)?;
        let translucent =
            upload_batches(renderer, self.atlas.convert_quads(translucent, [0.0; 3])?)?;
        self.sections.insert(
            key,
            Section {
                origin,
                bounds,
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
    /// Requested resource sizes, excluding driver and allocation overhead.
    pub fn allocation_stats(&self) -> (usize, usize, usize, usize) {
        let mut batches = 0;
        let mut bytes = 0;
        for section in self.sections.values() {
            for batch in section.solid.iter().chain(&section.translucent) {
                batches += 1;
                bytes += batch.gpu_bytes;
            }
        }
        (self.sections.len(), batches, bytes, self.atlas_bytes)
    }
    /// Sections with at least one uploaded draw batch, excluding empty meshes.
    pub fn drawable_section_count(&self) -> usize {
        self.sections
            .values()
            .filter(|section| !section.solid.is_empty() || !section.translucent.is_empty())
            .count()
    }

    /// Draw terrain in two passes, allowing entities to be drawn between them.
    /// `view_projection` is camera-relative, with no camera translation baked in.
    pub fn draw(&self, renderer: &Renderer, view_projection: [[f32; 4]; 4], camera: [f64; 3]) {
        self.draw_opaque(renderer, view_projection, camera);
        self.draw_translucent(renderer, view_projection, camera);
    }

    /// Opaque and alpha-cutout terrain writes world depth.
    pub fn draw_opaque(
        &self,
        renderer: &Renderer,
        view_projection: [[f32; 4]; 4],
        camera: [f64; 3],
    ) {
        for (section, matrix) in self.visible_sections(view_projection, camera) {
            self.draw_batches(renderer, &section.solid, matrix, DrawPass::Cutout);
        }
    }

    /// Blended terrain reads depth and is sorted back-to-front by section.
    /// Per-quad translucent sorting is pending.
    pub fn draw_translucent(
        &self,
        renderer: &Renderer,
        view_projection: [[f32; 4]; 4],
        camera: [f64; 3],
    ) {
        let mut sections = self.visible_sections(view_projection, camera);
        let distance = |section: &Section| -> f64 {
            (0..3)
                .map(|i| (section.origin[i] + 8.0 - camera[i]).powi(2))
                .sum()
        };
        sections.sort_by(|(a, _), (b, _)| distance(b).total_cmp(&distance(a)));
        for (section, matrix) in sections {
            self.draw_batches(
                renderer,
                &section.translucent,
                matrix,
                DrawPass::Translucent,
            );
        }
    }

    /// Visible section and draw-batch counts for opt-in diagnostics.
    pub fn visible_counts(
        &self,
        view_projection: [[f32; 4]; 4],
        camera: [f64; 3],
    ) -> (usize, usize, usize) {
        let sections = self.visible_sections(view_projection, camera);
        let opaque = sections
            .iter()
            .map(|(section, _)| section.solid.len())
            .sum();
        let translucent = sections
            .iter()
            .map(|(section, _)| section.translucent.len())
            .sum();
        (sections.len(), opaque, translucent)
    }

    fn visible_sections(
        &self,
        view_projection: [[f32; 4]; 4],
        camera: [f64; 3],
    ) -> Vec<(&Section, [[f32; 4]; 4])> {
        self.sections
            .values()
            .filter_map(|section| {
                let matrix = section_matrix(view_projection, section.origin, camera);
                section
                    .bounds
                    .is_some_and(|bounds| visible(bounds, matrix))
                    .then_some((section, matrix))
            })
            .collect()
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
                gpu_bytes: vertices.len() * std::mem::size_of::<Vertex>()
                    + batch.indices.len() * std::mem::size_of::<u16>(),
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

fn section_bounds<'a>(
    vertices: impl Iterator<Item = &'a SourceVertex>,
) -> Option<([f32; 3], [f32; 3])> {
    let mut bounds: Option<([f32; 3], [f32; 3])> = None;
    for vertex in vertices {
        if !vertex.position.iter().all(|v| v.is_finite()) {
            return None;
        }
        match &mut bounds {
            None => bounds = Some((vertex.position, vertex.position)),
            Some((min, max)) => {
                for i in 0..3 {
                    min[i] = min[i].min(vertex.position[i]);
                    max[i] = max[i].max(vertex.position[i]);
                }
            }
        }
    }
    bounds
}

// Discard only when every bounding-box corner is outside the same clip plane.
// A non-finite transform conservatively remains visible.
fn visible((min, max): ([f32; 3], [f32; 3]), matrix: [[f32; 4]; 4]) -> bool {
    let mut outside = [true; 6];
    for mask in 0..8 {
        let p = [
            if mask & 1 == 0 { min[0] } else { max[0] },
            if mask & 2 == 0 { min[1] } else { max[1] },
            if mask & 4 == 0 { min[2] } else { max[2] },
        ];
        let clip: [f32; 4] = std::array::from_fn(|r| {
            matrix[0][r] * p[0] + matrix[1][r] * p[1] + matrix[2][r] * p[2] + matrix[3][r]
        });
        if !clip.iter().all(|v| v.is_finite()) {
            return true;
        }
        let [x, y, z, w] = clip;
        for (flag, is_outside) in
            outside
                .iter_mut()
                .zip([x < -w, x > w, y < -w, y > w, z < 0.0, z > w])
        {
            *flag &= is_outside;
        }
    }
    !outside.into_iter().any(|all| all)
}

#[cfg(test)]
mod tests {
    use super::{section_matrix, visible};
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

    #[test]
    fn clip_culling_keeps_intersections_and_rejects_outside_boxes() {
        let identity = [
            [1., 0., 0., 0.],
            [0., 1., 0., 0.],
            [0., 0., 1., 0.],
            [0., 0., 0., 1.],
        ];
        assert!(visible(([-0.5, -0.5, 0.2], [0.5, 0.5, 0.8]), identity));
        assert!(visible(([0.5, 0., 0.5], [1.5, 0.5, 0.8]), identity));
        assert!(!visible(([1.1, 0., 0.5], [2., 0.5, 0.8]), identity));
        assert!(!visible(([0., 0., -1.], [0.5, 0.5, -0.1]), identity));
        let mut invalid = identity;
        invalid[0][0] = f32::NAN;
        assert!(visible(([-0.5; 3], [0.5; 3]), invalid));
    }
}
