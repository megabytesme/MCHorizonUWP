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

// Marker-gated CPU wall timings. Present includes its synchronization wait;
// these are not GPU timestamp queries and do not measure simulation time.
#[cfg(feature = "native-d3d11-readback")]
#[derive(Default)]
struct NativeTimings {
    frames: u32,
    parts: [std::time::Duration; 7],
    uploads: u64,
    upload_time: std::time::Duration,
}

#[cfg(feature = "native-d3d11-readback")]
impl NativeTimings {
    fn record(&mut self, sample: NativeFrameSample, counts: (usize, usize, usize)) {
        for (total, elapsed) in self.parts.iter_mut().zip(sample.parts) {
            *total += elapsed;
        }
        self.frames += 1;
        if self.frames >= 120 {
            let ms = self
                .parts
                .map(|d| d.as_secs_f64() * 1000.0 / f64::from(self.frames));
            log::info!(
                "Horizon: native frame CPU averages ms over {} frames: prep={:.2} opaque={:.2} entities={:.2} translucent={:.2} hand={:.2} hud={:.2} present={:.2}; {} section uploads in {:.1} ms total",
                self.frames,
                ms[0],
                ms[1],
                ms[2],
                ms[3],
                ms[4],
                ms[5],
                ms[6],
                self.uploads,
                self.upload_time.as_secs_f64() * 1000.0
            );
            log::info!(
                "Horizon: native scene visible sections={} opaque batches={} translucent batches={}",
                counts.0,
                counts.1,
                counts.2
            );
            *self = Self::default();
        }
    }
}

#[cfg(feature = "native-d3d11-readback")]
struct NativeFrameSample {
    last: std::time::Instant,
    parts: [std::time::Duration; 7],
}
#[cfg(feature = "native-d3d11-readback")]
impl NativeFrameSample {
    fn new() -> Self {
        Self {
            last: std::time::Instant::now(),
            parts: [std::time::Duration::ZERO; 7],
        }
    }
    fn mark(&mut self, part: usize) {
        let now = std::time::Instant::now();
        self.parts[part] += now.duration_since(self.last);
        self.last = now;
    }
    fn restart(&mut self) {
        self.last = std::time::Instant::now();
    }
}

pub struct NativeRenderer {
    window: WindowRenderer,
    atlas: Atlas,
    hud: HudRenderer,
    panorama: horizon_native_panorama::Panorama,
    entities: horizon_native_entities::d3d11::EntityRenderer,
    viewport: [u32; 2],
    scene: Option<horizon_native_scene::TerrainScene>,
    layout: ColumnLayout,
    #[cfg(feature = "native-d3d11-readback")]
    probed: bool,
    #[cfg(feature = "native-d3d11-readback")]
    probed_world: bool,
    #[cfg(feature = "native-d3d11-readback")]
    world_probe_frames: u64,
    #[cfg(feature = "native-d3d11-readback")]
    timings: Option<NativeTimings>,
}

impl NativeRenderer {
    pub fn new(
        atlas: &HudAtlas,
        faces: &horizon_client_render::gfx::panorama::Faces,
        viewport: [u32; 2],
    ) -> crate::Result<Self> {
        let window = WindowRenderer::from_host_window()?;
        #[cfg(feature = "native-d3d11-readback")]
        let diagnostics = horizon_client_render::logging::game_home()
            .join("native-world-smoke.enable")
            .is_file();
        let atlas = Atlas::from_horizon(atlas, 2048)?;
        let hud = HudRenderer::new(window.renderer(), &atlas)?;
        let panorama =
            horizon_native_panorama::Panorama::new(window.renderer(), faces.size, &faces.texels())?;
        let entities = horizon_native_entities::d3d11::EntityRenderer::new(&atlas);
        Ok(Self {
            window,
            atlas,
            hud,
            panorama,
            entities,
            viewport,
            scene: None,
            layout: ColumnLayout::VANILLA,
            #[cfg(feature = "native-d3d11-readback")]
            probed: !diagnostics,
            #[cfg(feature = "native-d3d11-readback")]
            probed_world: !diagnostics,
            #[cfg(feature = "native-d3d11-readback")]
            world_probe_frames: 0,
            #[cfg(feature = "native-d3d11-readback")]
            timings: diagnostics.then(NativeTimings::default),
        })
    }

    /// Upload block sprite pages before accepting world section meshes.
    pub fn set_terrain(
        &mut self,
        assets: &horizon_client_render::assets::RenderAssets,
    ) -> crate::Result<()> {
        let mip_scene = (|| -> crate::Result<_> {
            let atlas = native_terrain::from_render_assets(assets)?;
            Ok(horizon_native_scene::TerrainScene::new(
                self.window.renderer(),
                atlas,
            )?)
        })();
        let scene = match mip_scene {
            Ok(scene) => scene,
            Err(error) => {
                log::warn!(
                    "Native terrain mip atlas unavailable ({error}); retrying with the compact atlas"
                );
                let atlas = horizon_native_terrain::Atlas::from_layers(
                    assets.sprite_size,
                    assets.texture_layers as usize,
                    &assets.pixels,
                )?;
                horizon_native_scene::TerrainScene::new(self.window.renderer(), atlas)?
            }
        };
        self.window
            .renderer_mut()
            .set_depth_convention(horizon_uwp_d3d11::DepthConvention::Reverse);
        self.scene = Some(scene);
        Ok(())
    }

    pub fn viewport_size(&self) -> [u32; 2] {
        self.viewport
    }

    pub fn adapter_info(&self) -> Option<(String, u32)> {
        self.window.renderer().adapter_info().ok()
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
        #[cfg(feature = "native-d3d11-readback")]
        let mut sample = (!frame.title && self.timings.is_some()).then(NativeFrameSample::new);
        let converted = self.atlas.convert_horizon(frame.hud)?;
        let color = if frame.title {
            [0.0, 0.0, 0.0]
        } else {
            frame.sky.sky_color
        };
        self.window
            .renderer()
            .begin_frame([color[0], color[1], color[2], 1.0])?;
        if frame.title {
            self.panorama.draw(
                self.window.renderer(),
                self.viewport,
                frame.yaw,
                frame.pitch,
            )?;
        }
        let world_view = if frame.title {
            None
        } else {
            let aspect = self.viewport[0] as f32 / self.viewport[1].max(1) as f32;
            let projection = glam::Mat4::perspective_rh(
                frame.fov_degrees.to_radians(),
                aspect,
                frame.depth_far,
                0.05,
            );
            Some(
                (projection
                    * frame.bob
                    * frame.spin
                    * horizon_client_render::camera::view_matrix(frame.yaw, frame.pitch))
                .to_cols_array_2d(),
            )
        };
        #[cfg(feature = "native-d3d11-readback")]
        if let Some(sample) = &mut sample {
            sample.mark(0);
        }
        if let (Some(scene), Some(view)) = (&self.scene, world_view) {
            scene.draw_opaque(self.window.renderer(), view, frame.camera.to_array());
        }
        #[cfg(feature = "native-d3d11-readback")]
        if let Some(sample) = &mut sample {
            sample.mark(1);
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
        #[cfg(feature = "native-d3d11-readback")]
        if let Some(sample) = &mut sample {
            sample.restart();
        }
        if !frame.title {
            self.entities.draw_entities(
                self.window.renderer(),
                &self.atlas,
                &converted.patches,
                frame,
                self.viewport,
            )?;
            #[cfg(feature = "native-d3d11-readback")]
            if let Some(sample) = &mut sample {
                sample.mark(2);
            }
            if let (Some(scene), Some(view)) = (&self.scene, world_view) {
                scene.draw_translucent(self.window.renderer(), view, frame.camera.to_array());
            }
            #[cfg(feature = "native-d3d11-readback")]
            if let Some(sample) = &mut sample {
                sample.mark(3);
            }
            if !frame.hand.is_empty() {
                self.window.renderer().clear_depth()?;
                self.entities.draw_hand(
                    self.window.renderer(),
                    &self.atlas,
                    &converted.patches,
                    frame,
                    self.viewport,
                )?;
            }
        }
        #[cfg(feature = "native-d3d11-readback")]
        if let Some(sample) = &mut sample {
            sample.mark(4);
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
        if let Some(sample) = &mut sample {
            sample.mark(5);
        }
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
        #[cfg(feature = "native-d3d11-readback")]
        if let Some(sample) = &mut sample {
            sample.restart();
        }
        self.window.renderer().present()?;
        #[cfg(feature = "native-d3d11-readback")]
        if let (Some(mut sample), Some(timings)) = (sample, &mut self.timings) {
            sample.mark(6);
            let counts = if timings.frames == 119 {
                match (&self.scene, world_view) {
                    (Some(scene), Some(view)) => {
                        scene.visible_counts(view, frame.camera.to_array())
                    }
                    _ => (0, 0, 0),
                }
            } else {
                (0, 0, 0)
            };
            timings.record(sample, counts);
        }
        Ok(true)
    }
}

impl TerrainBackend for NativeRenderer {
    fn upload_section(&mut self, key: SectionKey, mesh: &SectionMesh) -> crate::Result<()> {
        #[cfg(feature = "native-d3d11-readback")]
        let started = self.timings.as_ref().map(|_| std::time::Instant::now());
        let mesh = native_terrain::from_section(mesh);
        let origin = [
            key.0 as f64 * 16.0,
            self.layout.min_y() as f64 + key.1 as f64 * 16.0,
            key.2 as f64 * 16.0,
        ];
        let result = self
            .scene
            .as_mut()
            .ok_or("native terrain is not initialized")?
            .upload(
                self.window.renderer(),
                key,
                origin,
                &mesh.solid,
                &mesh.translucent,
            );
        #[cfg(feature = "native-d3d11-readback")]
        if let (Some(started), Some(timings)) = (started, &mut self.timings) {
            timings.uploads += 1;
            timings.upload_time += started.elapsed();
        }
        result
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
