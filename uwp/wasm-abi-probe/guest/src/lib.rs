use mod_api::{ClientHost, GuiRenderPostEvent, Hooks};

#[derive(Default)]
struct Probe;

#[mod_api::horizon_mod]
impl Hooks for Probe {
    fn gui_render_post(&self, _: &dyn ClientHost, event: &mut GuiRenderPostEvent<'_>) {
        let [width, _] = event.batch.size();
        event
            .batch
            .fill([width - 24.0, 28.0, 16.0, 16.0], 0xffff00ff);
    }
}
