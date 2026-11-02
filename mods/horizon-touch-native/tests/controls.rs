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
#[test]
#[ignore = "requires separate v2 guest at HORIZON_TOUCH_TEST_MODULE"]
fn matches_the_existing_v2_guest() {
    let bytes = std::fs::read(std::env::var("HORIZON_TOUCH_TEST_MODULE").unwrap()).unwrap();
    let mut old = horizon_touch_mod_runtime::TouchModule::load(&bytes).unwrap();
    let mut new = TouchModule::new();
    for menu in [false, true, false] {
        old.set_context(menu).unwrap();
        new.set_context(menu).unwrap();
        let a = old.draw(1000., 600.).unwrap();
        let b = new.draw(1000., 600.).unwrap();
        assert_eq!(
            a.iter()
                .map(|c| (c.id, c.x, c.y, c.radius))
                .collect::<Vec<_>>(),
            b.iter()
                .map(|c| (c.id, c.x, c.y, c.radius))
                .collect::<Vec<_>>()
        );
        let points = if menu {
            vec![(9, 0, 48., 48.), (9, 2, 48., 48.)]
        } else {
            vec![
                (1, 0, 108., 492.),
                (1, 1, 145., 455.),
                (2, 0, 600., 300.),
                (2, 1, 610., 305.),
                (1, 3, 145., 455.),
                (2, 2, 610., 305.),
            ]
        };
        for (id, phase, x, y) in points {
            let a = old.touch(id, phase, x, y, 1000., 600.).unwrap();
            let b = new.touch(id, phase, x, y, 1000., 600.).unwrap();
            assert_eq!(a.0, b.0);
            assert_eq!(
                a.1.iter().map(|a| (a.id, a.value)).collect::<Vec<_>>(),
                b.1.iter().map(|a| (a.id, a.value)).collect::<Vec<_>>()
            );
        }
    }
}
