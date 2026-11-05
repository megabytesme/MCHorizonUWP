# Feature-level 9_1 HUD conversion

This crate converts Horizon's actual CPU `HudAtlas` and `HudBatch` data into
ordered 2D-atlas page draw calls, without depending on a graphics API. Enable
its `horizon` feature when integrating from an outer game/presentation crate.
The dependency-free core is available without that feature, and may also be
called from a thin adapter inside the rendering crate.

`Atlas::from_horizon(&hud_atlas, 2048)` partitions the RGBA atlas into
feature-level-9_1-sized pages. `convert_horizon(&batch)` clips each original
triangle against the page boundaries, preserves triangle order and
interpolates position, color, depth, and glint coordinates. It returns page-local
UVs and the original blend/scissor/sample metadata. Apply each returned patch
to its texture before drawing the returned batches in order. CPU copies of
patched pages are retained for dynamic glyph updates.

The renderer must provide the GUI-pixel-to-clip-space transform using the
actual physical viewport and returned GUI scale. It must preserve each blend,
scissor and sample mode: `GlintMask` needs a separate glint texture and shader;
`Depth` and `EndPortal` need their own rendering states or must be
reported as unsupported. The native presenter supports alpha, invert,
additive, and vignette atlas blending. Page boundaries currently
assume nearest sampling with clamp-to-edge; filtered sampling requires border
texels to avoid seams. The CPU page copy increases atlas memory use until the
source atlas storage is shared or released.

The crate is a rendering component, not a runnable game by itself. The
standalone tests exercise page crossings, draw ordering, interpolation,
scissored and glint metadata, and glyph patches; the optional Horizon feature
also includes a test using a real `HudBatch`.

Enable `d3d11` for `d3d11::HudRenderer`, which creates GPU pages on first use and
renders HUD batches with the GUI transform. Strict `draw` returns an error
for unsupported glint, scissored depth, or EndPortal; `draw_supported` skips
those runs, reports them to the caller, and logs each category once. Changed
regions are coalesced and uploaded when a page is next used. Ordered HUD draws
share one reusable dynamic vertex/index buffer, divided into bounded batches
and ranges to avoid creating a GPU buffer for every individual draw. ARM32 compilation has passed, and x64 UWP GPU readback verified
paged alpha drawing and retained glyph updates.
