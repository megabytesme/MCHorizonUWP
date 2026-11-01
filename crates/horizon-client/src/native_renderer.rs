//! Feature-level 9_1 D3D11 presentation of Horizon CPU-side HUD frames.
//!
//! This first stage presents the real loading/title HUD. Gameplay still uses
//! the wgpu renderer until world mesh and game state integration is complete.

use horizon_client_render::{gfx::Frame, hud::HudAtlas};
use horizon_native_hud::{Atlas, d3d11::HudRenderer};
use horizon_native_window::NativeRenderer as WindowRenderer;

pub struct NativeRenderer {
    window: WindowRenderer,
    atlas: Atlas,
    hud: HudRenderer,
    viewport: [u32; 2],
    #[cfg(feature = "native-d3d11-readback")]
    probed: bool,
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
            #[cfg(feature = "native-d3d11-readback")]
            probed: false,
        })
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
        self.window.renderer().begin_frame([0.0, 0.0, 0.0, 1.0])?;
        self.hud.draw(
            self.window.renderer(),
            &self.atlas,
            &converted.patches,
            &converted.draws,
            self.viewport,
            converted.scale,
        )?;
        #[cfg(feature = "native-d3d11-readback")]
        if !self.probed {
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
