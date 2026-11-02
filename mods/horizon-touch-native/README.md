# Native touch source mod

This optional source mod owns touch layout, multi-finger state and the inventory
Back control. The UWP host forwards pointer events and renders the controls
returned by the mod. It is selected by the desktop builder at build time using
`native-touch-mod` on `uwp/game-bridge`; it is compiled into the game library
and does not load a module from the phone or require a file picker.

The mod is a separate crate so its controls can be developed independently of
the UWP entry point and packaging. The feature-disabled build retains the
existing controls path. Its native logic has unit tests and a comparison test
against the earlier v2 touch module; run the latter by setting
`HORIZON_TOUCH_TEST_MODULE` to that separately built guest and running
`cargo test --manifest-path mods/horizon-touch-native/Cargo.toml -- --include-ignored`.
A phone test is required to verify the compiled controls and Back button.
