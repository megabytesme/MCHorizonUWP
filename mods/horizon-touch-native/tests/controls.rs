use horizon_touch_native::TouchModule;
#[test]
fn inventory_back_and_gameplay_actions() {
    let mut t = TouchModule::new();
    assert_eq!(t.draw(1000., 600.).unwrap().len(), 10);
    t.set_context(true).unwrap();
    let controls = t.draw(1000., 600.).unwrap();
    assert_eq!(controls.len(), 1);
    assert_eq!(controls[0].id, 14);
    assert!(!t.touch(7, 0, 500., 300., 1000., 600.).unwrap().0);
    let (claimed, actions) = t.touch(9, 0, 48., 48., 1000., 600.).unwrap();
    assert!(claimed);
    assert_eq!((actions[0].id, actions[0].value), (14, 1.));
    t.set_context(false).unwrap();
    assert!(t.touch(9, 2, 48., 48., 1000., 600.).unwrap().0);
    assert!(t.touch(1, 0, 108., 492., 1000., 600.).unwrap().0);
    let (_, actions) = t.touch(1, 1, 145., 455., 1000., 600.).unwrap();
    assert!(actions.iter().any(|a| a.id == 1 && a.value < 0.));
    assert!(actions.iter().any(|a| a.id == 2 && a.value > 0.));
    let (_, actions) = t.touch(1, 3, 145., 455., 1000., 600.).unwrap();
    assert!(actions.iter().all(|a| a.value == 0.));
    assert!(t.touch(1, 0, f32::NAN, 1., 1000., 600.).is_err());
}
