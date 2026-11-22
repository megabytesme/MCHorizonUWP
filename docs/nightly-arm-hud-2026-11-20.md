# ARM D3D11 HUD investigation

The signed device follow-up reproduced and fixed the remaining Surface
D3D11 HUD failure. It also confirmed a readable title and saved-world HUD
on Lumia 1.0.0.5. The follow-up records the fork commit, new Builder pin,
regression matrix and subsequent Lumia startup issues.
[Device follow-up](nightly-mobile-2026-11-20.md)

The signed x64 and x86 clients showed the full title UI on hardware D3D11.
On the Surface Pro X, hardware D3D11 drew the title panorama with the UI
missing. Hardware DX12 on the same machine drew the menu and both mod
markers. The Lumia's D3D11 run drew both markers on the title and in an
existing saved world, with parts of the HUD missing or unreadable.
[Surface D3D11](../deliverables/nightly-2026-11-20/arm64/surface-first-hooks/enabled.png) · [Surface DX12](../deliverables/nightly-2026-11-20/arm64/surface-dx12-hooks/enabled.png) · [Lumia world](../deliverables/nightly-2026-11-20/lumia/20261120_0006.png)

## Changes in HorizonUWP

| Change | Cause and files | Verification |
| --- | --- | --- |
| UWP resource discovery | `ResourceSource` discarded any directory whose handle-to-path canonicalization failed. This could hide readable packaged assets from the HUD loader. The UWP walk uses directory enumeration and rejects reparse points at the root, in requested paths, and on listed entries. Code lives in `crates/horizon-assets/src/resource_source.rs` and `crates/horizon-assets/src/resource_source/directory_uwp.rs` in the applied Horizon patch. | Eight focused Windows resource-source tests passed, including real directory junctions and a normal GUI texture. The ARM32 UWP type check passed. The device-side enumeration behavior still needs verification. |
| FL9_1 atlas updates | When an atlas exceeded the FL9_1 limit, a later patch rounded the origin down and sampled relative to its own rectangle. A patch beginning at an odd coordinate could miss sampled texels and overwrite its neighbor. `crates/horizon-client-render/src/gfx/fl91.rs` now uses the initial atlas's sampling grid for full and partial uploads. | The exact helper and focused regression test passed against a whole-image reference for 4,096 ranges. The Surface logs contain no atlas scaling warning. |

Both changes are in MCHorizonUWP branch `codex/nightly-2026-11-18`,
local commit `df59bd0309d0c10be5d46f8d79f2a5ada13c6254`. The Builder 1.0.6
candidate uses Horizon `0b14f8acfd97fa2c750e42795249dcc981178ca4`, patch
0.1.9 and client 0.1.10. The Builder setup tests and full source assembly
passed. The candidate catalog is unsigned. The changes address identifiable
application faults. Whether they repair the full ARM HUD symptoms requires
the signed on-device comparison.
[Patch 0.1.9](../patches/nightly-2026-11-18/horizon-uwp-0.1.9.patch) · [Builder pin](../uwp/builder/Core/NightlyUwp.cs)

The Builder assembled the candidate under
`%TEMP%\mchorizon-nightly-arm-hud-20261120\builder-assembled-0.1.9`.
The adjacent `builder-candidate-0.1.9` contains the archives and unsigned
catalog. The patch SHA-256 is
`d63fce7668cbab0be0abd175267ec693f76de43e50a81831d16e0e0537579e12`.
The client archive SHA-256 is
`00a23a8363e7e414e54bc460ab667026a3d7a58386021d8a3a34a3595d5fc8d8`.

## wgpu fork and hardware checks

The actual fork's `uwp` branch and its local `origin/uwp` reference both
resolved to `4c24e58de1ee12b9dd3a81cc4490662353011c30`. The
`uwp-dx11` fallback resolved to `440895454850d54435c5bca3e99bab36e57f8137`.
The main worktree had existing edits to `wgpu-hal/src/dx11/adapter.rs`
and `wgpu-hal/src/dx11/buffer.rs`. The staged product matched all 2,561
tracked fork files except `buffer.rs`, which matches the tracked
upload-shadow patch and the fork worktree's buffer edit. The
1.0.6 Builder assembly continued to verify the existing archive and
upload-shadow patch. These checks have established no generic HAL defect.
The wgpu branch, worktree, pinned revision and patch remain as observed.
No changes were pushed.

- The native ARM64 direct D3D11 probe ran on the Surface's Adreno 680.
  It passed 36 of 36 pixel checks at its native FL11_1 and 18 of 18 at
  actual FL9_1. Stock and widened HUD shaders both passed, including
  six atlas samples, alpha, glint and discard. This was the baseline
  probe with SHA-256
  `9a6b25011877f6cfeb45659753578b388acb30440b9856f42f0e82cec62af171`.
- The local NVIDIA reference reproduced those counts. The subsequent
  sparse atlas fixture passed 416 raw RGBA texel checks and 36 stock
  shader checks per level at native FL11_1 and forced FL9_1. The public
  wgpu replay passed 60 of 60 checks per level with matching queue
  writes and staging-belt pixels across three frames.
- The native direct probe bypasses the game's wgpu command replay and
  SwapChainPanel. The wgpu replay uses an offscreen target with explicit
  readbacks. ARM32 and ARM64 AppContainer replay packages were built
  against the matching product wgpu libraries, signed and verified.
  They await execution on the two devices.
  [Probe sources and logs](../deliverables/nightly-2026-11-20/arm-hud/)

## Signed diagnostic updates

Both builds were compiled on Ben-Desktop with `tools/build_nightly_runtime.ps1`,
locked and offline. ARM32 used
`F:\Temp\mchorizon-lumia-wasm-legacy-20261120\source`,
`thumbv7a-uwp-windows-msvc` and `-AdditionalFeatures uwp-wasm`.
ARM64 used `E:\Temp\mchorizon-mod-runtime-mission\source`,
`aarch64-uwp-windows-msvc` and `-AdditionalFeatures uwp-wasm,uwp-native-mods`.
The builds took 14 minutes 45 seconds and 15 minutes 12 seconds respectively.
Both passed the strict AppContainer link.

| Device | Signed isolated artifact | SHA-256 |
| --- | --- | --- |
| Lumia | `MCHorizonUWP.NightlyCross.arm` 1.0.0.5 | `61d8fc9397f3a5868953045d80cae9cf5de7a3263c61f2348abe9b8c69f441c7` |
| Surface | `MCHorizonUWP.NightlyModRuntime.arm64` 1.0.1.2 main bundle | `76aa8677c661292dd2b34bd553bcc24a1169f7bb9851081a5b1ee3b457c973e0` |
| Surface | `MCHorizonUWP.NightlyModRuntime.arm64.NativeMods` 1.0.1.2 optional bundle | `ad887d137c4e49432544f241c91dbf0c286b43d687c277d7cfeff8dcf5a324c0` |

The isolated game builds have a local diagnostic overlay on top of patch
0.1.9. A cold launch can select queue-written dynamic atlas patches, queue
uploads for HUD vertices and uniforms, or both. The default retains the
game's staging-belt path. The trace records direct resource reads, stock
sprite presence, atlas dimensions, active tiles and prepared draw ranges.
A stock Wasm diagnostic mod records the title and GUI hook calls and
draws a solid fill, a white sprite sampled across its UVs, a stock button
and text. Its initial x64 DX12 reference drew every element.
[Private diagnostic overlay and build records](../deliverables/nightly-2026-11-20/arm-hud/)

The Surface connection timed out during the later transfer. The Lumia's
pinned Device Portal also timed out. The updated packages and AppContainer
probes were still on Ben-Desktop when these device attempts ended. A cold
launch on each device is needed to compare the same title and world
before and after the update, then isolate atlas and geometry uploads if
any HUD elements are still missing.
