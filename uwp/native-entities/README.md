# Native D3D11 entity and hand meshes

Converts Horizon's camera-relative `Frame::entities` and `Frame::hand` quads
to feature-level 9_1 triangle meshes. Triangles are clipped against the pages
of the shared HUD atlas in UV space, with 3-D positions and colors interpolated
at page boundaries. GPU pages are uploaded lazily; the same patches used by the
HUD update only affected texture regions on pages that already exist.

Create `d3d11::EntityRenderer` with the native HUD atlas. After drawing world
terrain, call `draw_entities`; clear the D3D depth buffer, then call `draw_hand`
before the HUD. Both methods accept the renderer, atlas, current HUD patches,
frame, and physical viewport. The hand uses Horizon's dedicated hand FOV,
bob and camera rotation. The renderer's reverse-Z convention must be enabled.

This initial adapter approximates the lightmap and directional entity shading.
Special overlay effects, enchantment glint, back-face-specific shading,
weather, entity outlines, and translucent ordering are not yet implemented.
Dynamic meshes reuse bounded GPU buffers. Atlas patches update existing pages
in place; performance still needs testing on a phone.

The `probe` crate is an isolated x64 UWP GPU test. Build its static library for
`x86_64-uwp-windows-msvc` against the prepared Rust standard library, then set
`HORIZON_ENTITY_PROBE_LIB` to the resulting library and run `probe/build-host.cmd`.
The host must be registered in a separate UWP package. It writes
`LocalState/entity-probe.txt` after rendering and reading back a two-page
Horizon entity quad, compositing translucent water over the depth-tested
entity, then changing one atlas page. The strict linker rejects any
live dummy window functions or forbidden desktop import sentinels.
