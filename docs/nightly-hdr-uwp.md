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
| Local development identifiers | Builder `1.0.10`, patch `0.1.16`, client `0.1.18` |

Builder 1.0.10 accepts the dedicated HDR recipe for x64, x86, ARM64 and ARM32. Its local client bundle includes all four linked host kits, the pinned wgpu archive and its verified HDR overlay. Builder tests, the WinUI release build and assembly of the local candidate passed. The assembled wgpu tree matches `432a009d8` and the seven HDR engine files match the prior signed x64 build source. [Builder recipe and hashes](../deliverables/hdr/builder/candidate.json) · [Source patch](../patches/nightly-2026-11-20/horizon-uwp-0.1.16.patch) · [Local client bundle](../deliverables/hdr/builder/candidate/uwp-client-0.1.18.zip)

Local patch `0.1.17` pairs with the same `0.1.18` client bundle and Builder 1.0.10. It refuses classic shader packs on the D3D11 FL9_1 compatibility renderer before requesting pack materials or copying the terrain atlas. The signed x64 development build kept the stock world running after refusing the previously crashing `audit_post_only` fixture. On the same binary, D3D12 ran the fixture's composite pass and separately presented a stock HDR world with paired screenshots. [Guard patch](../patches/nightly-2026-11-20/horizon-uwp-0.1.17.patch) · [Shader validation](horizon-uwp-shader-compatibility.md)

Use the exact upstream and wgpu archives, the pinned Wasm inputs and the four-architecture host kit to regenerate the candidate. The resulting development catalog is unsigned. Run from the product worktree on Ben-Desktop. [Pinned input recipe](nightly-runtime-completion-2026-11-21.md)

```powershell
python ./tools/prepare_candidate.py --revision 3b09323fdbe8f7ae24f760393f86d5f4b2aad042 `
  --patch-version 0.1.16 --client-version 0.1.18 --builder-version 1.0.10 `
  --upstream-commit 0a875da91dbb0fdcb7ba8c42c11341aa229af51a `
  --upstream-repo $HorizonRepo --upstream-archive $HorizonArchive --wgpu-archive $WgpuBaseArchive `
  --host-artifacts $HostKit --wasmtime-archive $WasmtimeArchive --wasm-standard-library $WasmStdlib `
  --output $CandidateDirectory
```

To build the staged game bridge directly, use the matching host kit and verified Rust standard-library sources with the desktop release lock held.

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
- [Settings and shader audit reconciliation](hdr-shader-audit-reconciliation.md) records what the captures establish for render scale, shader stages, mods and screenshots.

The official Horizon shader passes were exercised against reference PQ, scRGB, UI composition and screenshot vectors on a software D3D12 driver. Use the signed hardware results for claims about game presentation, and the shader results for numeric color and transfer checks.
