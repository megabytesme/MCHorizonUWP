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

On UWP builds the mod can also read an optional gyroscope setting. A UWP host
adapter reads the platform gyrometer only while gameplay is active, then passes
integrated angular movement into this crate for orientation conversion. Gyro
aiming is disabled by default; the pre-game settings page enables it per app
and saves its sensitivity. Devices without a gyrometer keep the regular touch
look controls. Other platforms can provide their own sensor adapter without
adding platform APIs to this crate.

The UWP settings page also exposes touch-look sensitivity and on-screen control
size. The native module applies sensitivity to touch aiming and scales both
the drawn controls and their hit areas together, keeping the movement stick
and buttons aligned.

The axis conversion follows the touch gyro behavior in Starling, whose
upstream project is MIT licensed. The shared crate contains only the platform
independent setting and conversion code; each host owns sensor permissions,
sampling, display rotation, and lifecycle.
