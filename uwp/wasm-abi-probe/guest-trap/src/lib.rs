use mod_api::{ClientHost, GuiRenderPostEvent, Hooks};

#[derive(Default)]
struct Probe;

#[mod_api::horizon_mod]
impl Hooks for Probe {
    fn gui_render_post(&self, _: &dyn ClientHost, _: &mut GuiRenderPostEvent<'_>) {
        core::arch::wasm32::unreachable();
    }
}
