# MCHorizonUWP Builder 1.0.4

Builder 1.0.4 can assemble the UWP port of Horizon Nightly 2026-11-18 for x64, x86, ARM64 and ARM32 when a signed catalog authorizes patch 0.1.5 and client 0.1.6. It verifies the pinned wgpu snapshot and Direct3D 11 upload-buffer patch before preparing the game source. ARM32 uses the offline HTTPS profile and its own locked dependencies.

Public native SDK mods are unavailable for this Nightly because the SDK build and mixin pipeline have not been validated for UWP. The existing touch and vibration options are available independently.

The two existing signed catalog combinations remain available through the retained Builder 1.0.3 recipe. The Builder checks that their source, patch and client still match the published hashes and that the signed catalog still allows the selected architecture.
