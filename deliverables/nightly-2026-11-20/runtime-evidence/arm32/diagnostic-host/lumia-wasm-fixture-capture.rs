use mod_api::{ClientHost, GuiRenderPostEvent, Hooks, RenderFramePreEvent};
use std::sync::atomic::{AtomicU32, Ordering};

#[derive(Default)]
struct Probe {
    frames_in_world: AtomicU32,
}

#[mod_api::horizon_mod]
impl Hooks for Probe {
    fn render_frame_pre(&self, host: &mut dyn ClientHost, _: &mut RenderFramePreEvent) {
        if host.local_player().is_none() || host.is_paused() {
            return;
        }
        let frame = self.frames_in_world.fetch_add(1, Ordering::Relaxed) + 1;
        if frame == 180 {
            let _ = host.request_screenshot();
        }
        if frame == 900 {
            let _ = host.request_client_quit();
        }
    }

    fn gui_render_post(&self, _: &dyn ClientHost, event: &mut GuiRenderPostEvent<'_>) {
        let [width, _] = event.batch.size();
        event.batch.fill([width - 24.0, 28.0, 16.0, 16.0], 0xffff00ff);
    }
}