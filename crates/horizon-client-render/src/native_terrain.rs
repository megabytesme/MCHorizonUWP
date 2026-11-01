//! Safe bridge from Horizon's own meshed sections and sprites to the CPU
//! conversion used by the feature-level 9_1 D3D11 renderer.

use crate::{
    assets::RenderAssets,
    gfx::RendererAssets,
    mesher::{SectionMesh, TerrainVertex},
    sprites::Sprites,
};
use horizon_native_terrain::{Atlas, Error, SourceVertex};

/// Build atlas pages from the finalized, layer-major sprite images. A sprite
/// collection finalized without pixels is rejected by the atlas validator.
pub fn from_sprites(sprites: &Sprites) -> Result<Atlas, Error> {
    build_atlas(
        sprites.size,
        sprites.count(),
        &sprites.pixels,
        &sprites.mips,
    )
}

/// Build the native block atlas from the resources passed to renderer startup.
pub fn from_renderer_assets(assets: &RendererAssets<'_>) -> Result<Atlas, Error> {
    build_atlas(
        assets.sprite_size,
        assets.layers as usize,
        assets.sprite_pixels,
        assets.sprite_mips,
    )
}

/// Build the native block atlas from loaded game assets before renderer startup.
pub fn from_render_assets(assets: &RenderAssets) -> Result<Atlas, Error> {
    build_atlas(
        assets.sprite_size,
        assets.texture_layers as usize,
        &assets.pixels,
        &assets.sprite_mips,
    )
}

/// Generate the same per-sprite mip colors and cutout coverage as wgpu before
/// placing the images in aligned atlas slots. Tiny or unsupported sprite sizes
/// use the existing single-level atlas.
fn build_atlas(
    size: u32,
    layers: usize,
    pixels: &[u8],
    settings: &[crate::mipmap::Settings],
) -> Result<Atlas, Error> {
    // The padded mip atlas needs substantially more texture memory. Keep the
    // compact atlas on 32-bit UWP until it has been measured on the device.
    if cfg!(all(target_vendor = "uwp", target_arch = "arm")) {
        return Atlas::from_layers(size, layers, pixels);
    }
    let expected = (size as usize)
        .checked_mul(size as usize)
        .and_then(|n| n.checked_mul(4))
        .and_then(|n| n.checked_mul(layers));
    if expected != Some(pixels.len()) || size == 0 || layers == 0 {
        return Atlas::from_layers(size, layers, pixels);
    }
    if !size.is_power_of_two() || size < 4 || size > 1024 {
        return Atlas::from_layers(size, layers, pixels);
    }
    let extra = size.ilog2().saturating_sub(1);
    let layer_bytes = (size * size * 4) as usize;
    let mut levels = vec![Vec::new(); extra as usize + 1];
    for layer in 0..layers {
        let base = crate::mipmap::Image {
            width: size,
            height: size,
            pixels: pixels[layer * layer_bytes..(layer + 1) * layer_bytes]
                .as_chunks::<4>()
                .0
                .to_vec(),
        };
        let setting = settings.get(layer).copied().unwrap_or_default();
        let has_transparent = setting.has_transparent || base.pixels.iter().any(|p| p[3] == 0);
        for (dest, image) in levels.iter_mut().zip(crate::mipmap::generate(
            base,
            extra,
            crate::mipmap::Settings {
                has_transparent,
                ..setting
            },
        )) {
            dest.extend(image.pixels.into_iter().flatten());
        }
    }
    let atlas = Atlas::from_mip_layers(size, layers, &levels)?;
    let gpu_bytes: usize = atlas
        .pages
        .iter()
        .map(|page| page.rgba.len() + page.mips.iter().map(Vec::len).sum::<usize>())
        .sum();
    log::info!(
        "Native terrain atlas: {layers} sprites of {size}px, {} pages, {gpu_bytes} texture bytes including mip levels",
        atlas.pages.len()
    );
    Ok(atlas)
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
