# UWP native mod experiments

The native source prototype targets Horizon Nightly `0b14f8acfd97fa2c750e42795249dcc981178ca4` and Rust 1.99.1. It has two proofs with different scopes. The small DLL tests Windows package loading. The Rust fixture tests Horizon's public typed hook and its stock registration pipeline.

## Executable optional packages

The isolated `HorizonNativeModProbe` calls `GetCurrentPackageInfo`, `LoadPackagedLibrary` and `GetProcAddress`. Its DLL has one export returning the constant `2706020`. The app records its package graph and loader result in `LocalState/native-mod-probe.txt` before exiting. It uses no additional AppContainer capabilities.

The x64 AppContainer tests ran on Ben-Desktop with Windows build 26300.

| Installed state | Optional package in process graph | Result |
| --- | --- | --- |
| Main APPX 1.0.0.0 by itself | No | `LoadPackagedLibrary` returned Win32 126 |
| Main APPX 1.0.0.0 plus separately installed optional bundle | Yes | Windows accepted the installation and enumerated the optional package. Loading still returned Win32 126. |
| Main bundle 1.0.1.0 containing the related-set reference, plus the same signed optional bundle | Yes | Loaded the DLL, resolved the export, returned 2706020 and unloaded it successfully. |

Both main packages contain the same EXE with SHA-256 `ba56b2f4a47b9820a4e91bc00e48ffefddbf737a80bf49b48c19c3e0066f4d77`. The signed main bundle version and relationship metadata changed. Windows' package graph can enumerate an installed optional package before the DLL becomes loadable. An optional bundle install by itself cannot add executable code to this already-installed standalone main package. [Microsoft executable optional package requirements](https://learn.microsoft.com/en-us/windows/msix/package/optional-packages-with-executable-code) · [Related-set version rules](https://learn.microsoft.com/en-us/windows/msix/package/optional-packages)

| Architecture | Artifact | SHA-256 |
| --- | --- | --- |
| x64 | Standalone main APPX | `c6f8c79fb4d1df1f8527ffb7334f4e9d215c83207a1733e085129e755c15c64a` |
| x64 | Optional bundle | `b3aeab9e5b840af392e2008b455f929fda5cfc77ff003d058e78d9066605ac14` |
| x64 | Main related-set bundle | `547a6c80a7c1159ff64d58faf194330e8b7fcae0191ae659d0c909bf3e2826a1` |
| ARM64 | Standalone main APPX | `78a6b84e9aab10c766659b713610d59f3cee1369dc351f0a57e67ead82291b98` |
| ARM64 | Optional bundle | `c928400cae8e20c04170fd586dab8cc213f702e37325e3f883bc6de6aeb13ebf` |
| ARM64 | Main related-set bundle | `5754e3ce04821f65db0a107610681e00de7898a77d3a701124bd0ba59ea711f6` |
| ARM32 | Main APPX with DLL and PhoneAppModelHost loader | `07f1a14a98ce809c832c9884379570e1030adc24e48c9c008209258e11fb8b5b` |

All seven signatures passed SignTool verification. MSVC 14.44.35207 and Windows SDK 10.0.19041.0 built all three architectures with `/APPCONTAINER` and the UWP `WindowsApp.lib`. The ARM32 executable imports `LoadPackagedLibrary` from the Windows Phone-specific `PhoneAppModelHost.dll`. Its main APPX declares build 15063 and contains the DLL. Executable optional packages require build 16299 or newer, beyond standard Windows 10 Mobile build 15254. [Microsoft packaged library loader](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-loadpackagedlibrary)

The ARM64 packages were staged to the Surface Pro X over its authenticated LAN SSH connection. SHA-256 on the device matched the table. The interactive console was locked at handoff. A later SSH attempt timed out before authentication on port 22. The Lumia Device Portal timed out, so ARM32 packaged DLL evidence ends at the signed package and import table.

Run these commands from the repository. The PC needs MSVC, the Windows SDK and a test code-signing certificate in the current account.

```powershell
& uwp/native-mod-probe/build.ps1 -Architecture x64 -OutputDirectory $probeOutput `
    -MsvcToolset $msvcToolset -SdkRoot $sdkRoot -SigningThumbprint $testCertificate.Thumbprint
& uwp/native-mod-probe/test-related-set.ps1 -Directory $probeOutput
```

Run the second command in Windows PowerShell 5.1 under the interactive test account with the probe publisher already trusted. It expects the probe identity to be uninstalled.

## Builder-embedded typed hooks

The candidate Builder can compile trusted native source folders selected in **Choose native mods**. It uses the exact Nightly's patched game source to supply the SDK closure, and Cargo builds each mod as a separate crate in the bridge invocation. Source builds use the `MCHorizonUWP-NativeTest` package identity and a separate LocalState. The Builder writes the original manifest, source, Cargo declarations and SHA-256 digests to the immutable `horizon-native-mods` package folder. The normal build skips all native-source staging.

This prototype accepts the standard API-only Cargo dependency and a single `src/lib.rs` file per mod. The profile excludes author-supplied Cargo build scripts, third-party Cargo dependencies, custom targets, content files, mixins and injection declarations. The pinned Horizon manifest schema requires `capabilities = ["registries"]` even on code-only mods. The sample only implements the GUI hook, ships no data files, and adds no AppContainer capability. Rust code from the selected projects executes with the same privileges as the game. The Builder requires the user to trust each project explicitly. The pinned Horizon API's own build scripts and proc macros run as part of the standard game build.

The sample package in [hook-source](../uwp/native-mod-probe/hook-source) implements `gui_render_post` and draws a 16×16 cyan square at the upper right. The bridge supplies the stock `BuiltMod` record and public `#[horizon_mod]` hook factory. The host checks the adjacent package with `Runtime::load`, computes dispatch order with Horizon's dependency resolver, then installs each API entry through `ModEntry` and the existing seal.

For a command-line proof, start with an authenticated, writable 0.1.6 patch and 0.1.7 client assembly. Stage the source before running the normal locked Cargo, strict bridge link and signing steps. For ARM32, prepare the pinned offline overrides and place the ARM32 lockfile before staging. Pass `uwp/game-bridge/embedded-native/horizon-native-mods` from the assembly to the package script as `-EmbeddedNativeMods`.

```powershell
$env:HORIZON_GAME_BUILD_ID = 'mchorizonuwp-0.1.7-patch-0.1.6-x64-0b14f8acfd97fa2c750e42795249dcc981178ca4'
python -I uwp/native-mod-probe/stage-source.py --workspace $assembly --architecture x64 `
    --game-build-id $env:HORIZON_GAME_BUILD_ID --trust-native-source `
    --source uwp/native-mod-probe/hook-source
```

The x64 proof used the content-staging follow-up and the trusted source fixture in the same bridge build. Ben-Desktop completed the locked release build and passed `uwp/host/strict-bridge-link.ps1`. The first isolated package reached the stock manifest validator, which refused the fixture's empty capabilities list. The fixture and Builder validation now use the pinned schema's required `registries` declaration. The 1.0.0.1 package preserved the strict-linked bridge, passed SignTool verification, and installed as `MCHorizonUWP-NativeTest_1.0.0.1_x64__4pd4jeb6snrqw`.

| x64 artifact | SHA-256 |
| --- | --- |
| Signed and run 1.0.0.1 APPX | `fd7b5176194632db0f7a7d2ee9d08ca84e7664421acd76c118183305244f8861` |
| Strict-linked game bridge | `cee1c3e20fbded2f514ce4b01d9c60e06010b3f0dc8faacdfc6621a43b4e14b4` |

Build output and logs are under `%TEMP%/mchorizon-embedded-native-20261119-proof`. The APPX contains the native source manifest and build record. The test build used the same staging and package scripts as Builder with the existing verified x64 host kit. No catalog publishing was involved.

The installed x64 app logged `Embedded native mod: uwp_native_hook_probe 0.1.0` and rendered the welcome screen through wgpu D3D11 on the RTX 3090 Ti. The typed API hook drew the cyan marker. Relaunching the same signed package with `mod.uwp_native_hook_probe:false` in the test game's `horizon-mods.txt` still reached the same welcome screen. Pixel checks across the two 1822×1410 screenshots found 6,400 cyan pixels in the 80×80 indicator rectangle when enabled and zero when disabled. This verified typed hook execution and enable/disable without recompilation. The test restored the original setting. The close helper left the isolated process resident, so the test explicitly stopped that process before the settings change.

The first native-only launch copied 23,219 prepared files and 248,353,373 bytes in 138,031 ms. The enabled screenshot was collected after the title appeared, beyond the original 150-second startup helper timeout. The disabled launch reached the title using the resource framework. Both screenshots, startup logs, and the verified APPX are in `deliverables/nightly-2026-11-18/verification/native-mods-2026-11-19/`.

The same candidate also built through the ordinary x64 path with no native sources selected. The control bridge passed strict link, and the signed `MCHorizonUWP-PlainTest_1.0.0.0_x64__4pd4jeb6snrqw` package installed without a native source inventory. Its APPX SHA-256 is `56fe438a9e23f8d265f700051400f4745d4efdc6b2f347415cbdb8ff6c49382e`. Activation reached the XAML pregame and successfully presented a wgpu D3D11 probe frame. The proof was stopped at the user's request to keep the desktop available. The engine title remains unverified for this separate control.

The same final fixture compiled as a Rust library for both ARM targets using the pinned, patched 1.99.1 standard library. Dumpbin confirmed the target machine types inside the archives. The build directories were `%TEMP%/mchorizon-embedded-native-module-20261119-arm64/source` and `%TEMP%/mchorizon-embedded-native-module-20261119-arm/source`.

| Target | Object machine | Module archive SHA-256 |
| --- | --- | --- |
| ARM64 | AA64 | `a028f065aa9ca471eb9fbcc50b121fb369ea07a50eb9d34fe34f13feb659f4ec` |
| ARM32 | 01C4 | `8474ef57ccf3a4980407e0b235ac5b29f58e6100315940aa1c83e666e012d2e2` |

```powershell
cargo +1.99.1 build --offline --locked --manifest-path uwp/game-bridge/Cargo.toml `
    -p mod-uwp-native-hook-probe --target aarch64-uwp-windows-msvc `
    -Z build-std=std,panic_abort --target-dir ../target
```

For the ARM32 build, use `cargo +1.99.1 --config .local/arm32-windows-sys/patches-offline-arm32.toml build` with the same flags and the `thumbv7a-uwp-windows-msvc` target. Use `CARGO_PROFILE_DEV_DEBUG=0` and `CARGO_INCREMENTAL=0` to reproduce the recorded archive. The ARM checks compiled the mod and API dependencies. They stop short of linking the complete ARM game, packaging its Rust bridge or running it on a device.

Separately packaged native DLLs and Builder-embedded mods have different update paths. A loose DLL in LocalState is outside the package graph. The executable optional-package route requires related-set authoring and a Horizon-aware DLL loader, which the C++ probe does not implement. A DLL shipped within the main APPX could use the same package-loader API with a main package update. This Builder prototype embeds the selected Rust crates in the game bridge and rebuilds and signs that bridge when source changes. The source projects and typed hooks remain portable to another Builder integration using the same Horizon API revision.

The pinned Desktop Windows launcher builds API-only Rust DLLs from shared SDK metadata. Its desktop game uses the system allocator. The UWP bridge uses Horizon's game allocator. A UWP DLL route must agree on the compiler, API closure, type layout, and allocator before handing ownership of `Vec<ModEntry>` or `Box<dyn Hooks>` to the host. A PowerPC Builder can reuse the source selection, pin check and static hook factories with its own target SDK and game linker.

The upstream native library host validates the artifact hash and its `horizon_native_build` identity before calling `horizon_native_mod_v1`. The returned `NativeMod` supplies hook factories, an inventory, API-point listeners and patch install functions. A UWP loader can reuse this contract with package-graph discovery and process-lifetime module handles. Parts and registrations then use the installed API tables. Typed `[[inject]]` listeners need the pinned point catalog and the existing `horizon_mod_point_emit::api` generator plus dependency-ordered registration. Source-level mixins compile modified engine functions and have a larger build and verification scope. The current hook-only Builder profile validates these boundaries by accepting the common typed API and refusing declarations that need the additional generators.
