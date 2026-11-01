//! Feature-level 9_1 D3D11 presentation of Horizon CPU-side HUD frames.
//!
//! Presents Horizon HUD and streamed terrain using CPU-side game data.

use horizon_client_render::{
    chunk::ColumnLayout,
    gfx::{Frame, SectionKey},
    hud::HudAtlas,
    mesher::SectionMesh,
    native_terrain,
    presentation::TerrainBackend,
};
use horizon_native_hud::{Atlas, d3d11::HudRenderer};
use horizon_native_window::NativeRenderer as WindowRenderer;

pub struct NativeRenderer {
    window: WindowRenderer,
    atlas: Atlas,
    hud: HudRenderer,
    viewport: [u32; 2],
    scene: Option<horizon_native_scene::TerrainScene>,
    layout: ColumnLayout,
    #[cfg(feature = "native-d3d11-readback")]
    probed: bool,
    #[cfg(feature = "native-d3d11-readback")]
    probed_world: bool,
    #[cfg(feature = "native-d3d11-readback")]
    world_probe_frames: u64,
}

impl NativeRenderer {
    pub fn new(atlas: &HudAtlas, viewport: [u32; 2]) -> crate::Result<Self> {
        let window = WindowRenderer::from_host_window()?;
        let atlas = Atlas::from_horizon(atlas, 2048)?;
        let hud = HudRenderer::new(window.renderer(), &atlas)?;
        Ok(Self {
            window,
            atlas,
            hud,
            viewport,
            scene: None,
            layout: ColumnLayout::VANILLA,
            #[cfg(feature = "native-d3d11-readback")]
            probed: false,
            #[cfg(feature = "native-d3d11-readback")]
            probed_world: false,
            #[cfg(feature = "native-d3d11-readback")]
            world_probe_frames: 0,
        })
    }

    /// Upload block sprite pages before accepting world section meshes.
    pub fn set_terrain(
        &mut self,
        pixels: &[u8],
        sprite_size: u32,
        layers: u32,
    ) -> crate::Result<()> {
        let atlas =
            horizon_native_terrain::Atlas::from_layers(sprite_size, layers as usize, pixels)?;
        let scene = horizon_native_scene::TerrainScene::new(self.window.renderer(), atlas)?;
        self.window
            .renderer_mut()
            .set_depth_convention(horizon_uwp_d3d11::DepthConvention::Reverse);
        self.scene = Some(scene);
        Ok(())
    }

    pub fn viewport_size(&self) -> [u32; 2] {
        self.viewport
    }

    pub fn resize(&mut self, viewport: [u32; 2]) -> crate::Result<()> {
        if viewport.contains(&0) || viewport == self.viewport {
            return Ok(());
        }
        self.window.renderer_mut().resize()?;
        self.viewport = viewport;
        Ok(())
    }

    pub fn present(&mut self, frame: &Frame<'_>) -> crate::Result<bool> {
        let converted = self.atlas.convert_horizon(frame.hud)?;
        let color = if frame.title {
            [0.0, 0.0, 0.0]
        } else {
            frame.sky.sky_color
        };
        self.window
            .renderer()
            .begin_frame([color[0], color[1], color[2], 1.0])?;
        if !frame.title
            && let Some(scene) = &self.scene
        {
            let aspect = self.viewport[0] as f32 / self.viewport[1].max(1) as f32;
            let projection = glam::Mat4::perspective_rh(
                frame.fov_degrees.to_radians(),
                aspect,
                frame.depth_far,
                0.05,
            );
            let transform = projection
                * frame.bob
                * frame.spin
                * horizon_client_render::camera::view_matrix(frame.yaw, frame.pitch);
            scene.draw(
                self.window.renderer(),
                transform.to_cols_array_2d(),
                frame.camera.to_array(),
            );
        }
        #[cfg(feature = "native-d3d11-readback")]
        if !frame.title && !self.probed_world {
            if let Some(scene) = &self.scene {
                let drawable = scene.drawable_section_count();
                if drawable > 0 {
                    self.world_probe_frames = self.world_probe_frames.wrapping_add(1);
                    if self.world_probe_frames % 60 == 1 {
                        let clear = color.map(|c| (c.clamp(0.0, 1.0) * 255.0).round() as u8);
                        match self.window.renderer().probe_pixels_different_from(clear, 1) {
                            Ok((width, height, count)) => {
                                log::info!(
                                    "Horizon: native world readback {width}x{height}, {count} non-sky pixels before HUD, {drawable} drawable sections ({} resident)",
                                    scene.section_count()
                                );
                                self.probed_world = count > 0;
                            }
                            Err(error) => {
                                self.probed_world = true;
                                log::warn!("Horizon: native world readback failed: {error}");
                            }
                        }
                    }
                }
            }
        }
        self.hud.draw_supported(
            self.window.renderer(),
            &self.atlas,
            &converted.patches,
            &converted.draws,
            self.viewport,
            converted.scale,
        )?;
        #[cfg(feature = "native-d3d11-readback")]
        if frame.title && !self.probed {
            self.probed = true;
            match self.window.renderer().probe_non_black_pixels() {
                Ok((width, height, count)) => log::info!(
                    "Horizon: native title readback {width}x{height}, {count} non-black pixels, {} HUD draws",
                    converted.draws.len()
                ),
                Err(error) => log::warn!("Horizon: native title readback failed: {error}"),
            }
        }
        self.window.renderer().present()?;
        Ok(true)
    }
}

impl TerrainBackend for NativeRenderer {
    fn upload_section(&mut self, key: SectionKey, mesh: &SectionMesh) -> crate::Result<()> {
        let mesh = native_terrain::from_section(mesh);
        let origin = [
            key.0 as f64 * 16.0,
            self.layout.min_y() as f64 + key.1 as f64 * 16.0,
            key.2 as f64 * 16.0,
        ];
        self.scene
            .as_mut()
            .ok_or("native terrain is not initialized")?
            .upload(
                self.window.renderer(),
                key,
                origin,
                &mesh.solid,
                &mesh.translucent,
            )
    }
    fn clear_sections(&mut self, layout: ColumnLayout) {
        if let Some(scene) = &mut self.scene {
            scene.clear();
        }
        self.layout = layout;
    }
    fn remove_columns(&mut self, columns: &mut dyn Iterator<Item = (i32, i32)>) {
        if let Some(scene) = &mut self.scene {
            scene.remove_columns(columns);
        }
    }
    fn has_section(&self, key: SectionKey) -> bool {
        self.scene
            .as_ref()
            .is_some_and(|scene| scene.has_section(key))
    }
    fn section_count(&self) -> usize {
        self.scene.as_ref().map_or(0, |scene| scene.section_count())
    }
}
