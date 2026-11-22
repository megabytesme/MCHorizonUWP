#[path = "support/no_hooks.rs"]
mod no_hooks;

use horizon_mod_package::Package;
use horizon_mod_wasm::{WasmPackage, load};
use mod_api::{ClientHost, DrawContext, Host, PackageInfo, PackageKind, Side};
use std::{path::PathBuf, time::Instant};

struct Client;
impl Host for Client {}
mod_api::impl_host_areas!(Client);
impl ClientHost for Client {}
mod_api::impl_client_host_areas!(Client);

#[derive(Default)]
struct Draw(Vec<([f32; 4], u32)>);

impl DrawContext for Draw {
    fn size(&self) -> [f32; 2] {
        [320.0, 180.0]
    }

    fn fill(&mut self, rectangle: [f32; 4], argb: u32) {
        self.0.push((rectangle, argb));
    }
}

#[test]
#[ignore = "needs the fixture .hmod and matching horizon-wasm-std.wasm"]
fn compiled_horizon_wasm_guest_draws_through_the_stock_client_hook() {
    horizon_guest_stack::run_on_guest_stack(|| {
        let path = PathBuf::from(std::env::var_os("HORIZON_WASM_FIXTURE").expect("guest package"));
        let std =
            std::fs::read(std::env::var_os("HORIZON_WASM_STD").expect("shared Wasm std")).unwrap();
        let package = Package::read(&path).unwrap();
        let module = package.wasm(&path).unwrap();
        let manifest = package.manifest;
        assert_eq!(manifest.id, "uwp_wasm_hook_probe");
        assert_eq!(manifest.wasm_memory_mib, Some(32));
        let inventory = [PackageInfo::new(
            manifest.id.clone(),
            manifest.version.clone(),
            PackageKind::Code,
            Side::Client,
            vec![manifest.id.clone()],
            Vec::new(),
        )];
        mod_api::__private::initialize().unwrap();

        let load_started = Instant::now();
        let loaded = load(&std, vec![WasmPackage { manifest, module }]);
        assert!(loaded.refused.is_empty(), "{:?}", loaded.refused);
        assert_eq!(loaded.mods.len(), 1);
        loaded.initialize(&inventory).unwrap();
        let elapsed_load = load_started.elapsed();

        mod_api::testing::with_mods(loaded.mods.into(), || {
            mod_api::__private::mark_game_thread();
            let _phase = mod_api::__private::PhaseOwner::scope(mod_api::__private::ModPhase::World);
            let mut draw = Draw::default();
            let call_started = Instant::now();
            for _ in 0..10 {
                mod_api::dispatch_gui_render_post(&Client, &mut draw, false, true);
            }
            assert_eq!(draw.0, [([296.0, 28.0, 16.0, 16.0], 0xffff00ff); 10]);
            println!(
                "Guest load and initialization {:?}; ten GUI dispatches {:?}",
                elapsed_load,
                call_started.elapsed()
            );
        });
    });
}
