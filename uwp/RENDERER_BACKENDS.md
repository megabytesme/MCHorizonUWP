# Renderer integration

Horizon's primary renderer is `gfx::Renderer` on top of `wgpu`. On UWP, the
shell can select among:

- `0`: Native Direct3D 11 (`HORIZON_UWP_NATIVE_D3D11`, legacy standalone feature-level
  `9_1` renderer outside `wgpu`; omitted in client `0.1.5+` / builder `1.0.3+`
  builds in favor of renderer `3`).
- `1`: ANGLE (`HORIZON_UWP_ANGLE`, `wgpu` GLES over D3D11).
- `2`: Direct3D 12 (`HORIZON_UWP_DX12`, `wgpu` DX12).
- `3`: Direct3D 11 (`HORIZON_UWP_WGPU_D3D11`, Cargo feature `uwp-dx11-renderer`
  enabling the experimental `wgpu/dx11` feature-level `9_1` backend from the
  `uwp` branch of `megabytesme/wgpu-dx11` with `WGPU_BACKEND=dx11` and
  `HORIZON_GRAPHICS_API=dx11`).

When `HORIZON_UWP_WGPU_D3D11` is packaged, the XAML shell defaults to renderer `3`
whenever `HORIZON_UWP_NATIVE_D3D11` is omitted, probes renderer `3` against the
shared `SwapChainPanel` on startup without falling back to DX12, ANGLE, or WARP,
and `horizon_uwp_run_prepared` configures `gfx::Renderer` to request `wgpu` DX11
at `FL9_1`.

`presentation::PresentationBackend` defines the first shared boundary: frame
submission, viewport size, resize, surface release/recreation and present
mode. Loading, no-resources, save and gameplay frame calls pass through this
interface. The existing renderer implements the interface by delegating to
its existing wgpu methods, so its rendering behavior and ANGLE selection are
unchanged. Game code now reads viewport dimensions without accessing wgpu's
`SurfaceConfiguration`.

`gfx::RendererAssets` groups the CPU texture, atlas and panorama inputs used
by the loading and full-game constructors. The wgpu prepared textures, window,
API and previous-device state remain separate from those shared inputs.
`presentation::TerrainBackend` defines section upload, removal, reset and
presence queries using CPU `SectionMesh` data and supports fallible uploads.
The existing renderer delegates these operations to wgpu. The game keeps a
failed mesh in its pending queue for retry, and discards it if a newer mesh
supersedes it or its column unloads.

These interfaces are deliberately not a complete native-renderer contract yet.
Before a native renderer can be selected, the game also needs backend-neutral
interfaces for at least:

- initialization and resource reload through an actual native implementation;
- LOD meshes, section block-entity and visibility metadata;
- screenshot readback, diagnostics and timing currently exposed through
  wgpu-specific fields;
- resource reloads, shader-pack capabilities and settings, with clear behavior
  where a backend lacks a feature.

Once both implementations support those operations, select the backend before
creating the loading renderer and retain that choice through the loading-to-game
handoff. Expose only implemented renderers in the settings UI, persist the
choice for the next launch, and report a clear initialization failure if a
previously selected renderer is unavailable. ANGLE should remain an independent
choice; selecting native D3D11 must not silently create a wgpu/ANGLE renderer.
