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

## Build and runtime matrix

| Architecture | Build evidence | Runtime evidence |
| --- | --- | --- |
| x64 | Locked release-profile UWP bridge type check passed with Wasmtime and D3D11/D3D12 enabled. | D3D11 offscreen probe passed on Ben-Desktop. The stock x64 client entered a DX12 world and rendered the rebuilt Wasm hook. Signed UWP gameplay is a separate gate. |
| x86 | Exact source and separate native mod crates staged. The architecture host kit passed strict linking, machine type and AppContainer checks. | D3D11 Win32 offscreen probe passed. Signed UWP gameplay is pending. |
| ARM64 | Exact source and separate native mod crates staged. Strict-linked ARM64 host kit. | Signed offscreen D3D11 FL9_1 probe passed on physical Surface Pro X hardware. WinRT reported gyrometer, orientation sensor and accelerometer availability. November 20 gameplay and gyro aiming remain pending. |
| ARM32 | Exact source, legacy Wasmtime overrides and three embedded native source mods staged. The strict-linked ARMNT host kit retains the previous Lumia imports. | The isolated signed DRS probe passed eight offscreen cases on the Lumia’s physical Adreno 430 FL9_1 after Device Portal reconnected. Phone gameplay, the fixed-scale benchmark and gyro aiming remain pending. |

The architecture builds use separate source and Cargo target paths under `%TEMP%\mchorizon-nov20-integration`. The x64 bridge check used `tools/build_nightly_runtime.ps1 -Architecture x64 -Check -AdditionalFeatures uwp-wasm`. Full x64, x86 and ARM64 mod test builds use `-NativeMods -WithBridge -AdditionalFeatures uwp-wasm,uwp-native-mods` after staging with `stage-packaged.py`. ARM32 uses `stage-source.py` and `-AdditionalFeatures uwp-wasm`.

Pass `-PatchVersion 0.1.13 -ClientVersion 0.1.14 -UpstreamCommit 0a875da91dbb0fdcb7ba8c42c11341aa229af51a` through bridge compilation and packaging. Use the corresponding exact build identity for mod staging. The APPX packager requires the strict link map. The Lumia packaging command also checks the legacy Wasmtime and Windows SDK imports.

[DRS implementation and probe evidence](nightly-drs-2026-11-20.md) · [Gyro source and activation](../uwp/mods/horizon-gyro-aim/README.md) · [Feature parity matrix](horizon-uwp-feature-parity-2026-11-20.md)
