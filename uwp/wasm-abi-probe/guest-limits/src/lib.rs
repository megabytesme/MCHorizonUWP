use mod_api::{ClientHost, GuiRenderPostEvent, Hooks};
use std::cell::Cell;
use std::hint::black_box;

thread_local! {
    static FRAME: Cell<u32> = const { Cell::new(0) };
}

#[derive(Default)]
struct Probe;

#[mod_api::horizon_mod]
impl Hooks for Probe {
    fn gui_render_post(&self, _: &dyn ClientHost, _: &mut GuiRenderPostEvent<'_>) {
        let frame = FRAME.get();
        FRAME.set(frame.saturating_add(1));
        match frame {
            0 => {
                // Request 128 MiB beyond a 32 MiB guest ceiling. Wasm returns
                // usize::MAX when the growth is refused before allocation.
                assert_eq!(core::arch::wasm32::memory_grow::<0>(2048), usize::MAX);
            }
            30 => {
                // Keep this fixture bounded if the configured deadline is
                // ever disabled. The interpreter normally interrupts it.
                let mut value = 1_u32;
                for step in 0..100_000_000 {
                    value = black_box(value).wrapping_mul(1664525).wrapping_add(step);
                }
                black_box(value);
            }
            _ => {}
        }
    }
}
