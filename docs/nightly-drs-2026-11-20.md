# Dynamic resolution on the D3D11 compatibility renderer

The November 20 HorizonUWP integration uses Horizon's Resolution Scale, Dynamic Resolution and Target FPS settings for the D3D11 compatibility renderer. It draws the world into a separate color and depth target when scaling is active, then uses a single bilinear raster pass before drawing the full-size hand, screen effects, HUD and menus. The title and 100% gameplay keep the direct draw path. Signed native offscreen probes verified the new pass at physical FL9_1 on the Lumia 950 XL and Surface Pro X. Lumia gameplay measurements are pending.

Horizon source is `0a875da91dbb0fdcb7ba8c42c11341aa229af51a` and the wgpu fork remains `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157`. The scaler uses the frame's 8-bit format, the compatibility renderer's depth format, one mip and one sample. Clamp-to-edge sampling supports non-power-of-two target sizes. Its long edge stays within the actual device limit and its two dimensions use the same scale before pixel rounding. The configured output dimensions, camera projection, HUD coordinates and touch mapping follow the window as before. Pipelines and allocations are checked. An unavailable target falls back to drawing at the configured output size and records the error and actual dimensions.

The modern renderer keeps Horizon's November 20 TAA, shader pack and 4× terrain MSAA logic. The D3D11 compatibility path uses the simple scaling pass and does not request those pipelines. The wgpu D3D11 depthless state fix remains necessary when the world depth pass is followed by the upscale and HUD passes.

## Adaptive policy

When timestamps are unavailable on the compatibility renderer, Dynamic Resolution uses median active frame work, CPU work and renderer waits. Active work excludes the game's deliberate frame-limiter sleep. The fallback gathers settled, focused gameplay frames after 2.5 seconds of warm-up or after a scale change, using windows of at least 1.25 seconds and eight samples. Loading, focus gaps and stalls invalidate its observations. When the projected increase fits comfortably, it raises resolution five percentage points at a time. It trials a reduction only when active work exceeds the budget and there is both CPU headroom and significant renderer waiting. A lower scale must reduce median active time by at least 4% to stay in place. A failed trial restores the previous scale and waits for a material change in active work before another reduction.

The existing percentage floor and Target FPS setting control the fallback. The timing source appears as frame pressure in the log, alongside CPU work and waits. The GPU timing display continues to say `n/a` without timestamp samples. The old Lumia observation of about 43 ms CPU work and 64 ms between frames leaves no CPU headroom for a 30 FPS target under the fallback's rule. Matched runs are needed to see how much scene resolution affects either measurement.

## Verified on hardware

| Platform | Driver feature level | Result |
| --- | --- | --- |
| Ben-Desktop x64 Win32, RTX 3090 Ti | Actual FL9_1 and FL11_1 with the compatibility shader projection | 8 of 8 cases per level |
| Ben-Desktop x86 Win32, RTX 3090 Ti | Actual FL9_1 and FL11_1 with the compatibility shader projection | 8 of 8 cases per level |
| Surface Pro X ARM64 AppContainer, Adreno 680 | Actual FL9_1 | 8 of 8 cases |
| Lumia 950 XL ARM32 AppContainer, Adreno 430 | Actual FL9_1 | 8 of 8 cases |

The 48 readback cases exercise 100%, 75%, 67% and 50%, a repeated scale, portrait resize, the return to 100%, and 1024 × 576 world rendering into a 2048 × 1152 output. Each checks the physical color and depth target dimensions, the gradient after scaling, and an isolated one-pixel HUD marker drawn at output resolution after the world. The harness includes the production Rust scaler and WGSL. It uses an offscreen texture in place of the swapchain. The controller's ten tests passed, including three new fallback tests for warm-up, CPU-bound behavior, confirmation and rollback of reductions, and interrupted samples. A separate sizing test verified uniform scaling and rounding against a 2048-pixel texture limit. [Readback data](../deliverables/nightly-2026-11-20/drs/evidence/offscreen-readbacks.csv) · [Lumia log](../deliverables/nightly-2026-11-20/drs/evidence/lumia-world-scale.log) · [Surface log](../deliverables/nightly-2026-11-20/drs/evidence/surface-world-scale.log) · [Controller tests](../deliverables/nightly-2026-11-20/drs/evidence/controller-tests.log) · [Sizing test](../deliverables/nightly-2026-11-20/drs/evidence/target-sizing-tests.log)

Ben-Desktop built both UWP probes from `%TEMP%\mchorizon-nov20-drs` with Rust 1.99.1, using the previously recorded exact-fork wgpu production rlibs. The strict link used the AppContainer host kits and Windows SDK 15063 import libraries on ARM32 and 19041 on ARM64. SignTool verified both signed APPXs with the existing device test issuer. The isolated `MCHorizonUWP.WgpuWorldScaleProbe` 1.0.0.0 package leaves game profiles and previous test apps separate. [Probe sources](../deliverables/nightly-2026-11-20/drs/probe/) · [ARM32 imports](../deliverables/nightly-2026-11-20/drs/evidence/imports-arm.txt) · [ARM64 imports](../deliverables/nightly-2026-11-20/drs/evidence/imports-arm64.txt)

| Probe | Signed SHA-256 |
| --- | --- |
| [ARM32 APPX](../deliverables/nightly-2026-11-20/drs/WgpuWorldScaleProbe-arm.signed.appx) | `75B2E527F0C6436719DDFA7E0F767E77E652342347678D755DC986274EFCF2EB` |
| [ARM64 APPX](../deliverables/nightly-2026-11-20/drs/WgpuWorldScaleProbe-arm64.signed.appx) | `723181EA9D43D877EC3C947FC7425279BA2664671759ACEB6E1864D182F6141E` |

The signed ARM64 APPX was transferred from `C:\Users\ben\AppData\Local\Temp\mchorizon-nov20-drs\probe-arm64\WgpuWorldScaleProbe-arm64.signed.appx` on Ben-Desktop to `C:\Users\ben\AppData\Local\Temp\MCHorizonUWP-DynamicResolution-20261121\WgpuWorldScaleProbe-arm64.signed.appx` on Ben-Tablet with pinned LAN SSH. The hash matched before installation. Package status was `Ok` and the interactive test task exited with code 0. The app wrote its result under `LocalState\wgpu-world-scale-probe.txt`. [Install and activation record](../deliverables/nightly-2026-11-20/drs/evidence/surface-probe-status.txt)

The Lumia's pinned HTTPS Device Portal was initially unavailable and recovered before the probe install. Package Manager returned success for the signed ARM32 package. Activation was accepted at 00:44:37 on November 21. The full report was retrieved by 00:45:18 and Device Portal confirmed that the probe process had exited. The driver advertised a 2048-pixel texture limit at actual FL9_1. [Connectivity record](../deliverables/nightly-2026-11-20/drs/evidence/lumia-device-portal.txt) · [Lumia activation](../deliverables/nightly-2026-11-20/drs/evidence/lumia-activation.log)

## Remaining Lumia gameplay checks

Use a copied two-chunk test world and the same position, view, weather, time, input and audio in each run. Hold `dynamicResolution:false` and measure 100%, 75%, 67% and 50% after the world settles. Log actual dimensions, presented frame intervals, median and average FPS, p95 and p99 frame time, CPU time, memory, device-loss events and any available power or temperature observations. Capture the same view and native HUD for each scale. Repeat short and sustained runs. With the same floor and target settings, enable DRS and alternate scenes to measure the time-window decisions and their effect on frame pacing. Exercise touch, controller, menus and resume while the target changes.

The dimensions below are calculated for the previous projected D3D11 output. Actual strict FL9_1 device limits and the window's configured dimensions determine each run's output. These figures are targets for comparison against logs, so they carry no performance estimate.

| Fixed scale | Calculated world target for 2561 × 1439 output | Share of full world pixels |
| --- | --- | --- |
| 100% | 2561 × 1439 | 100% |
| 75% | 1921 × 1079 | 56.2% |
| 67% | 1716 × 964 | 44.9% |
| 50% | 1281 × 720 | 25.0% |

The presentation depth surface still serves the full-size hand and HUD. The 50% offscreen color and depth pair above represents about 7 MiB of tightly packed pixels before driver allocation overhead. Compared with the direct 100% path, it adds render targets and one full-screen pass while reducing the world pixel workload. Only matched device measurements can show the net impact on memory usage and frame time.

The readback probes establish that the compatibility pass runs on both mobile GPUs at physical FL9_1 and can keep their HUDs separate. Windowed gameplay, save-world stability, matched performance, audio continuity, and adaptive resolution changes on the Lumia require the signed game candidate's device run. The integration's cross-architecture test matrix tracks the full game's DX12 and ANGLE coverage.
