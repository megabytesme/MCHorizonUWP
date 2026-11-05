# Direct3D 11 feature-level 9_1 terrain data

This crate turns Horizon-style terrain quads and layer-major RGBA sprite data
into 2D atlas pages and 16-bit indexed triangle batches suitable for the
native Direct3D 11 renderer. It has no graphics dependencies.

`Atlas::from_layers` packs sprites into power-of-two RGBA pages no larger than
2048 pixels per side and provides one wrapped-texel gutter around each sprite.
`Atlas::convert_quads` emits batches in draw order, one atlas page per batch,
and splits repeated sprite coordinates at integer boundaries. Vertices have
the same fields as `horizon-d3d11-backend::Vertex`. Pass the section's
camera-relative origin (or world origin paired with a world-space matrix).

The converter approximates packed sky/block light as a scalar; it does not yet
sample Horizon's lightmap. It preserves texture alpha and identifies Horizon's
force-opaque flag, which the caller must map to the appropriate draw pass.
Mipmaps, texture animation, fog, and translucent sorting must be supplied by
the caller. It rejects missing layers, malformed data, and exceptionally large
UV repetitions rather than producing incorrect geometry.

Run `cargo test --manifest-path uwp/native-terrain/Cargo.toml` in an MSVC
developer command prompt. The tests cover atlas gutters, actual vertex shape,
wrapping, index splitting, and malformed inputs.
