use horizon_touch_mod_runtime::TouchModule;
#[test]
fn missing_and_invalid_module_are_rejected() {
    assert!(TouchModule::load(&[]).is_err());
    assert!(TouchModule::load(b"not wasm").is_err());
}
#[test]
#[ignore = "requires separately built guest passed by HORIZON_TOUCH_TEST_MODULE"]
fn separately_built_guest_controls_and_cancellation() {
    let path = std::env::var("HORIZON_TOUCH_TEST_MODULE").unwrap();
    let bytes = std::fs::read(path).unwrap();
    let mut m = TouchModule::load(&bytes).unwrap();
    assert_eq!(m.draw(1000., 600.).unwrap().len(), 10);
    assert!(m.touch(1, 0, 108., 492., 1000., 600.).unwrap().0);
    assert!(m.touch(2, 0, 600., 300., 1000., 600.).unwrap().0);
    let (_, a) = m.touch(1, 1, 145., 455., 1000., 600.).unwrap();
    assert!(a.iter().any(|v| v.id == 1 && v.value < 0.));
    assert!(a.iter().any(|v| v.id == 2 && v.value > 0.));
    let (_, a) = m.touch(2, 1, 610., 305., 1000., 600.).unwrap();
    assert!(a.iter().any(|v| v.id == 3 && v.value > 0.));
    let (_, a) = m.touch(1, 3, 145., 455., 1000., 600.).unwrap();
    assert_eq!(a.len(), 2);
    assert!(a.iter().all(|v| v.value == 0.));
    assert!(m.touch(1, 0, f32::NAN, 1., 1000., 600.).is_err());
}

fn guest_with_touch(body: &str) -> Vec<u8> {
    let source = format!(
        r#"(module
  (import "horizon_touch_v1" "action" (func $action (param i32 f32)))
  (func (export "mod_api_version") (result i32) i32.const 1)
  (func (export "touch") (param i32 i32 f32 f32 f32 f32) (result i32) {body})
  (func (export "draw") (param f32 f32))
  (func (export "reset")))"#
    );
    wat::parse_str(source).unwrap()
}
#[test]
fn traps_exhausted_fuel_and_rejects_invalid_actions() {
    let mut m = TouchModule::load(&guest_with_touch("(loop $spin br $spin) i32.const 1")).unwrap();
    assert!(m.touch(1, 0, 1., 1., 100., 100.).is_err());
    let mut m = TouchModule::load(&guest_with_touch(
        "i32.const 999 f32.const 1 call $action i32.const 1",
    ))
    .unwrap();
    assert!(m.touch(1, 0, 1., 1., 100., 100.).is_err());
    let mut m = TouchModule::load(&guest_with_touch(
        "i32.const 1 f32.const nan call $action i32.const 1",
    ))
    .unwrap();
    assert!(m.touch(1, 0, 1., 1., 100., 100.).is_err());
}
