# Native terrain scene

This experimental scene uploads the native-terrain crate's CPU atlas pages
and section batches to the Rust D3D11 backend. It retains sections by key,
draws cutout sections before back-to-front translucent sections, and removes
unloaded columns. The game still has to supply its live assets, meshes and
camera transform to this scene.

The backend's UWP GPU probe exercises atlas packing, section upload and column
removal and checks the rendered colours by reading back the x64 framebuffer.
The same probe compiles and strict-links for ARM32; no phone run is implied.
The scene keeps section origins in `f64` and converts them to camera-relative
`f32` offsets when drawing, and conservatively culls sections against the
camera frustum using their actual geometry bounds. It currently uses coarse
per-section rather than per-quad translucent sorting, has no cave culling,
and has no animated sprite updates. These limits must be addressed for a
complete world renderer.
