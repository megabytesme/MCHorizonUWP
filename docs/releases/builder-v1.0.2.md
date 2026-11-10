Builder 1.0.2 checks native-mod releases against the selected Horizon source before presenting them in the picker. A source-level compatibility check skips versions that construct Horizon structs without required fields; the builder leaves third-party mod releases unchanged. Native-mod targets that use a Horizon crate re-export are resolved to the crate that defines the target.

The builder also applies a narrowly versioned UWP source backport for Horizon commit `e797c725ad99ef311db216de7f41a2e6593353db`. This keeps the desktop-only processor-emulation probe out of the packaged UWP link while retaining it for desktop builds.

Builder 1.0.2 supports Horizon patch 0.1.1 with client 0.1.3 and patch 0.1.2 with client 0.1.4 on x86, x64, ARM32 and ARM64, as listed in the signed catalog.

The x64 smoke build completed with nine compatible pinned mods: Classic Zoom 2.0.0, FPS Counter 2.0.0, JEI for Horizon 0.1.0, Keystrokes 2.0.0, KillC 5.1.0, Minimap 2.0.0, Recipes 2.0.0, Serverify 0.1.0 and Zoom 2.0.0. Eating Animation 0.1.1 is excluded because its source omits fields required by the selected Horizon revision. No mod source or release was changed.
