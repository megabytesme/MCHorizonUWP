# Native touch source mod

This optional source mod owns touch layout, multi-finger state and the inventory
Back control. The UWP host forwards pointer events and renders the controls
returned by the mod. It is selected by the desktop builder at build time using
`native-touch-mod` on `uwp/game-bridge`; it is compiled into the game library
and does not load a module from the phone or require a file picker.

The mod is a separate crate so its controls can be developed independently of
the UWP entry point and packaging. The feature-disabled build retains built-in
controls. Run `cargo test --manifest-path mods/horizon-touch-native/Cargo.toml`
to exercise the controls. The user confirmed gameplay and inventory Back on the
Lumia 950 XL using an ARM32 feature build.
