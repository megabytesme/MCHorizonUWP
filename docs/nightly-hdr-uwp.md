# Horizon Nightly HDR on UWP

The development candidate uses the pinned Horizon HDR renderer and Windows Advanced Color to present FP16 scRGB through the UWP D3D12 SwapChainPanel. The physical display can receive PQ while the application sends linear scRGB to the compositor. The game uses an opaque panel, requires the active view to report HDR, and asks DXGI for present support before selecting the swapchain color space. Horizon's existing output transaction restores SDR if HDR configuration fails.

The view-state bridge uses the WinRT runtime contract for GetAdvancedColorInfo and its change event. It reads the active mode on the XAML thread and publishes a synchronized snapshot to the renderer. Windows 10 builds before 17134 use SDR. Current SDR and wide-gamut SDR modes also use SDR. The host diagnostic records the real backbuffer format and alpha mode together with the color-space call result.

## Engine behavior

- Horizon retains the original HDR Off, On and Auto settings, including saved boolean preferences and Auto's display-change handling. The UWP path adds an active-view condition to the renderer's existing capability check.
- Horizon retains its linear lighting, look, output encoding, paper white, UI brightness, peak and black controls, calibration and HDR/SDR screenshot encoders. XAML passes display luminance into Horizon's existing display-reading logic.
- The physical UWP path selects scRGB. An explicit PQ override leaves it in SDR. Offscreen HDR capture and desktop Horizon retain their existing encoding selection.
- The game retains its FL9_1 D3D11 projection on devices that open at a higher native feature level. ANGLE retains SDR until its Windows EGL and HAL route can validate extended-range presentation.

## Inputs and local build

| Input | Revision |
| --- | --- |
| November 20 UWP product baseline | `5c26a8060c1cf2dce20ee5707e84699ed0879e5c` |
| Official Horizon Nightly | `0a875da91dbb0fdcb7ba8c42c11341aa229af51a` |
| Pinned wgpu fork | `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157` |
| HDR wgpu fork branch `codex/hdr-nov20` | `432a009d8d44aa2b5988668bb9620d975a275a00` |
| Local development identifiers | Patch `0.1.16`, client `0.1.18` |

Assemble the pinned November 20 `0.1.15`/`0.1.17` Builder input into a fresh directory following [the November 20 recipe](nightly-runtime-completion-2026-11-21.md). Apply [the HDR engine overlay](../patches/nightly-2026-11-20/hdr-active-view.patch) from the assembled root. Apply [the HDR DX12 overlay](../patches/wgpu-dx11/dx12-uwp-hdr-surface.patch) from the assembled `third_party/wgpu-dx11` root. Both patches support `git apply --check --whitespace=error`. Use the same directory when applying each checked patch.

Build the updated host kit with `tools/build_host_artifact_kit.ps1` for each target architecture and stage the matching prebuilt host and import library. The Rust bridge references both new host exports. Reuse verified Wasmtime and Rust standard-library inputs from the pinned build, then run the following from the product worktree on Ben-Desktop with the desktop release lock held.

```powershell
& ./tools/build_nightly_runtime.ps1 -Source $Assembly -Output $Build `
  -TargetDirectory $CargoTarget -Architecture x64 -Jobs 32 `
  -PatchVersion 0.1.16 -ClientVersion 0.1.18 `
  -UpstreamCommit 0a875da91dbb0fdcb7ba8c42c11341aa229af51a `
  -PreparedRustSource $PatchedRustSource -AdditionalFeatures uwp-wasm
```

Use `tools/package_nightly_runtime.ps1` with the same pin and version IDs, the matching Wasm standard library, the host from the new kit and the development resource framework. Give the package a unique `MCHorizonUWP.HdrDev.*` identity. The catalog and November 20 pinned archives stay outside this local development build.

## Verification

- [D3D12](hdr-dx12-validation.md) covers the Windows modes, native chain and signed game.
- [D3D11](hdr-dx11-validation.md) records native FL11_1 and FL9_1 format results and the product gate.
- [ANGLE](hdr-angle-validation.md) records exact EGL, GL and DXGI measurements on a signed UWP panel probe.
- [Lumia](hdr-lumia-capabilities.md) records the OS contract, prior physical SDR evidence and the Device Portal retry.
- [Capability matrix](../deliverables/hdr/capability-matrix.json), [swapchain evidence](../deliverables/hdr/swapchain-probe-results.json) and [color vectors](../deliverables/hdr/color-validation.json) give machine-readable results.

The official Horizon shader passes were exercised against reference PQ, scRGB, UI composition and screenshot vectors on a software D3D12 driver. Use the signed hardware results for claims about game presentation, and the shader results for numeric color and transfer checks.
