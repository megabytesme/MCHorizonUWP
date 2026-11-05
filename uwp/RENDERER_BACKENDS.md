# Renderer integration

The current game uses the wgpu `gfx::Renderer`. On UWP it selects wgpu's GLES
backend with ANGLE's D3D11 device. The native D3D11 work is a separate renderer,
not another wgpu backend. It cannot be selected as a game renderer until it
implements the same game-facing operations.

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
