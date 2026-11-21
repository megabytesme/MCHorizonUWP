# November 20 Nightly build and package evidence

The isolated candidate uses Horizon `0a875da91dbb0fdcb7ba8c42c11341aa229af51a`, wgpu `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157`, patch 0.1.13, client 0.1.14, Builder 1.0.7 and Rust 1.99.1. [Signed release and source provenance](nightly-integration-2026-11-20.md)

## Builder source assembly

Ben-Desktop assembled the patch and client ZIP into a new directory at `%TEMP%\mchorizon-nov20-integration\assembled-clean`. The immutable codeload ZIP has SHA-256 `abd6a91c41407522f9b0ec43d16555f46cd76832625645658bee57e2ab49da3d`. The downloaded ZIP and the archive independently reconstructed from the verified Git tree matched. Builder applied the entire patch and staged the fixed wgpu fork and rebuilt UWP host kit. The clean assembly passed `cargo +1.99.1 metadata --locked --offline --format-version 1 --filter-platform x86_64-pc-windows-msvc`. The standalone bridge lock also resolved for ARM32 with its offline dependency overrides.

Prepare the candidate from the committed product tree. Build the host kit with `tools/build_host_artifact_kit.ps1`. Generate the shared Wasm library with the pinned November 20 `cargo-horizon-mod`. Export the wgpu ZIP from the exact fork commit using the November 18 report's Git archive command. The candidate script checks each input digest. [wgpu archive command](nightly-mobile-2026-11-20.md)

```powershell
python tools/prepare_candidate.py --patch-version 0.1.13 `
  --upstream-repo $UpstreamGit --upstream-archive $UpstreamZip `
  --wgpu-archive $WgpuZip --host-artifacts $HostKit `
  --wasmtime-archive $WasmtimeCrate --wasm-standard-library $WasmStandardLibrary `
  --output $CandidateOutput

dotnet run --project uwp/builder/Tests/Horizon.Builder.Tests.csproj -c Release
dotnet run --project uwp/builder/Tests/Horizon.Builder.Tests.csproj -c Release --no-build -- `
  --assemble-candidate $UpstreamZip "$CandidateOutput/horizon-uwp-0.1.13.patch" `
  "$CandidateOutput/uwp-client-0.1.14.zip" $Assembly 0a875da91dbb0fdcb7ba8c42c11341aa229af51a 0.1.14
```

The unsigned compatibility rule is a local development artifact. The signed production catalog remains in place. Reuse the private resource framework after checking resource schema 7 and preparer output version 5. Development APPXs use isolated identities for saves and settings.

The committed Builder export at `657c2af97f7f4cf394f1082790e2d9ea0492b947` produced the [candidate files](../deliverables/nightly-2026-11-20/builder-candidate/) and passed a second clean assembly. The client ZIP SHA-256 is `0f3a8c747996dc6fa7808a52bff4060d7f2224916b4af15372748d9f183ede80`. The patch SHA-256 is `8c5db5dd57fac40957d75d184033e43323117b0357e23defa982ded57234b707`. All four architecture entries passed the unsigned catalog compatibility check.

The x64 gameplay test exposed a November 18 constant in the engine’s packaged native loader. The caller's stamped engine identity now supplies the expected upstream commit. The corrected loader accepted the exact November 20 native manifests on x64. The ARM32 bridge uses the embedded loader.

The signed x64 D3D11 run recorded 4,096 exact pixels for each mod marker. Its captured F3 text is native resolution. The gyro status partially overlaps the top-left F3 lines when enabled on this machine without a sensor. [World capture](../deliverables/nightly-2026-11-20/runtime-evidence/x64/dx11-world-50/world.png) · [F3 capture](../deliverables/nightly-2026-11-20/runtime-evidence/x64/dx11-world-50/f3.png) · [Runtime measurements](../deliverables/nightly-2026-11-20/runtime-evidence/x64/dx11-world-50/result.json)

Desktop tests used private profiles and a synthetic seed-1234 save. [x64 ANGLE](../deliverables/nightly-2026-11-20/runtime-evidence/x64/angle-world-100/result.json) · [x64 D3D12](../deliverables/nightly-2026-11-20/runtime-evidence/x64/dx12-world-100/result.json) · [x86 D3D11 F3](../deliverables/nightly-2026-11-20/runtime-evidence/x86/dx11-world-75/f3.png) · [Surface install receipts](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/surface-install-record.md) · [All verified package hashes](../deliverables/nightly-2026-11-20/game-packages.json)

## Build and runtime matrix

| Architecture | Build evidence | Runtime evidence |
| --- | --- | --- |
| x64 | Locked release build passed in 24m 31s. The SDK-gate rebuild took 1m 12s. The bridge and all three native DLLs passed strict linking. | Signed hardware D3D11 at 50% opened a saved world with a 770 × 510 world and 1539 × 1020 output, both mod markers, validated content tables and non-silent XAudio2. The no-sensor message appeared when gyro was enabled. Separate hardware D3D12 and ANGLE GLES3 runs rendered saved worlds with both markers and closed cleanly. |
| x86 | Locked release build passed in 20m 40s. The bridge and all three native DLLs strict-linked. Both related bundles signed and verified. | The signed x86 AppContainer rendered a saved world on hardware D3D11 at 1154 × 765 world and 1539 × 1020 output (75%), with native and Wasm markers, readable F3 and non-silent XAudio2. |
| ARM64 | Locked release build passed in 21m. The SDK-gate rebuild took 57.97s. The game bridge and all three DLLs strict-linked. Both related bundles signed and verified. | The related set installed on the Surface. Signed offscreen D3D11 FL9_1 passed on the physical Adreno 680. Windows reported the sensors and a locked console. Foreground game runs require unlock. |
| ARM32 | Locked release build passed in 21m 25s with legacy Wasmtime and three embedded native source mods. The ARMNT bridge strict-linked. The 15063-minimum APPX was signed and verified. | Installed and activated on Lumia. The game shell’s D3D11 startup probe presented a frame. The isolated signed DRS probe passed eight offscreen cases on the physical Adreno 430 FL9_1. The separate sensor probe received real WinRT gyro readings. World entry and in-world gyro remain open. |

The architecture builds use separate source and Cargo target paths under `%TEMP%\mchorizon-nov20-integration`. The x64 bridge check used `tools/build_nightly_runtime.ps1 -Architecture x64 -Check -AdditionalFeatures uwp-wasm`. Full x64, x86 and ARM64 mod test builds use `-NativeMods -WithBridge -AdditionalFeatures uwp-wasm,uwp-native-mods` after staging with `stage-packaged.py`. ARM32 uses `stage-source.py` and `-AdditionalFeatures uwp-wasm`.

Pass `-PatchVersion 0.1.13 -ClientVersion 0.1.14 -UpstreamCommit 0a875da91dbb0fdcb7ba8c42c11341aa229af51a` through bridge compilation and packaging. Use the corresponding exact build identity for mod staging. The APPX packager requires the strict link map. The Lumia packaging command also checks the legacy Wasmtime and Windows SDK imports.

The [signed ARM32 APPX](../deliverables/nightly-2026-11-20/game/lumia-arm/HorizonUWP-arm.appx) SHA-256 is `62d206b68b38a18925ec3c4b841c1c1e6695210406ca60b1b25a16b3bb8d9f56`. Its isolated identity is `MCHorizonUWP.NightlyNov20.arm` 1.0.0.0. The [build record](../deliverables/nightly-2026-11-20/runtime-evidence/arm32/build-build-20261121-002858.json) gives the exact arguments and linked bridge digest. The [Lumia lifecycle log](../deliverables/nightly-2026-11-20/runtime-evidence/arm32/lifecycle.log) records successful D3D11 device and surface checks, the rejected DX12 software adapter, and the ANGLE surface failure. The launcher is waiting at its first-run prompt. Device Portal uploaded the isolated seed ZIP but left it unextracted, so the copied two-chunk save has yet to enter the game.

The separate [ARM32 diagnostic APPX](../deliverables/nightly-2026-11-20/game/lumia-drs-smoke/HorizonUWP-Arm32-DRS-World-Smoke.signed.appx) contains the same linked game bridge. Its test shell seeds a copied world and starts at fixed 50% scale. Its SHA-256 is `ba7cf6a25f4126dad2e01d24c2229485721ba5a69912390f6a5d59522614db5e`. The Lumia stopped answering Device Portal before the diagnostic upload.

[Signed x64 related set](../deliverables/nightly-2026-11-20/game/x64-1.0.2.0/) · [Signed ARM64 related set](../deliverables/nightly-2026-11-20/game/arm64/related/) · [DRS implementation and probe evidence](nightly-drs-2026-11-20.md) · [Gyro source and activation](../uwp/mods/horizon-gyro-aim/README.md) · [Feature parity matrix](horizon-uwp-feature-parity-2026-11-20.md)
