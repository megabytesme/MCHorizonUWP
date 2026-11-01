//! Safe bridge from Horizon's own meshed sections and sprites to the CPU
//! conversion used by the feature-level 9_1 D3D11 renderer.

use crate::{
    mesher::{SectionMesh, TerrainVertex},
    sprites::Sprites,
};
use horizon_native_terrain::{Atlas, Error, SourceVertex};

/// Build atlas pages from the finalized, layer-major sprite images. A sprite
/// collection finalized without pixels is rejected by the atlas validator.
pub fn from_sprites(sprites: &Sprites) -> Result<Atlas, Error> {
    Atlas::from_layers(sprites.size, sprites.count(), &sprites.pixels)
}

/// A section's real meshed quads, kept in Horizon's solid/translucent order.
/// Upload the returned arrays with the section's origin; both native scene
/// conversion and GPU upload can fail without mutating the original mesh.
#[derive(Debug, Default)]
pub struct SectionData {
    pub solid: Vec<SourceVertex>,
    pub translucent: Vec<SourceVertex>,
}

pub fn from_section(section: &SectionMesh) -> SectionData {
    let convert = |vertices: &[TerrainVertex]| {
        vertices
            .iter()
            .map(|v| SourceVertex {
                position: v.pos,
                uv: v.uv(),
                layer: v.layer,
                color: v.color,
                light: v.light,
            })
            .collect()
    };
    SectionData {
        solid: convert(&section.solid),
        translucent: convert(&section.translucent),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::mesher::OPAQUE_LAYER;

    #[test]
    fn preserves_horizon_terrain_fields_and_translucency() {
        let mut section = SectionMesh::default();
        let first = TerrainVertex::new(
            [1., 2., 3.],
            [0.25, 0.75],
            OPAQUE_LAYER | 5,
            0xab56_3412,
            0x98ef,
        );
        let second = TerrainVertex::new([4., 5., 6.], [1., 0.], 7, 0x4433_2211, 0x00f0);
        section.solid.push(first);
        section.translucent.push(second);
        let converted = from_section(&section);
        assert_eq!(converted.solid.len(), 1);
        assert_eq!(converted.translucent.len(), 1);
        let v = converted.solid[0];
        assert_eq!(v.position, first.pos);
        assert_eq!(v.uv, first.uv());
        assert_eq!(v.layer, first.layer);
        assert_eq!(v.color, first.color);
        assert_eq!(v.light, first.light);
        assert_eq!(converted.translucent[0].position, second.pos);
        assert_eq!(converted.translucent[0].layer, second.layer);
    }

    #[test]
    fn rejects_sprite_collections_without_pixel_data() {
        assert!(from_sprites(&Sprites::default()).is_err());
    }

    #[test]
    fn packs_real_finished_sprite_collection() {
        let mut sprites = Sprites::default();
        sprites
            .insert(
                std::path::Path::new("nonexistent-horizon-native-test"),
                "minecraft:test",
            )
            .unwrap();
        sprites.finish();
        let atlas = from_sprites(&sprites).unwrap();
        assert_eq!(atlas.pages.len(), 1);
        assert!(
            atlas.pages[0]
                .rgba
                .chunks_exact(4)
                .any(|pixel| pixel == [248, 0, 248, 255])
        );
    }
}
