# MCHorizonUWP Nightly development report

The baseline was product commit 25e508f8a69cb4524a183a9ce91ed6e399f85c56, Horizon Nightly 0b14f8acfd97fa2c750e42795249dcc981178ca4, and wgpu fork 4c24e58de1ee12b9dd3a81cc4490662353011c30 with the upload-shadow patch. Builds ran on Ben-Desktop. The original 0.1.5 patch and signed packages were retained. Candidate 0.1.6 adds content staging and an opt-in trusted source prototype. [Original Nightly migration](nightly-2026-11-18.md)

## Physical-device verification

| Stage | Surface Pro X ARM64 | Lumia 950 XL ARM32 |
| --- | --- | --- |
| Built | Yes, PE ARM64 | Yes, PE ARMNT |
| Strict-linked | Yes, original build record | Yes, original build record |
| Signed | Verified with SignTool and SHA-256 | Verified with SignTool and SHA-256 |
| Installed | Yes, isolated Nightly Cross identity and exact resource framework | Pending Portal connectivity |
| Launched | Yes, native package in the interactive console | Pending installation |
| Title rendered | Title panorama, first-frame log and screenshot | Pending launch |
| World rendered | Pending interactive session | Pending launch |
| Input verified | Shell UI Automation. Gameplay input pending | Pending launch |
| Multiplayer verified | Pending game session | Pending launch |
| Mods verified | Pending game session | Pending launch |

| Artifact | Version and minimum OS | SHA-256 |
| --- | --- | --- |
| MCHorizonUWP-NightlyCross-arm64-1.0.0.2.appx | 1.0.0.2, 10.0.15063.0 | 5e435fedc01ec1ee870f0c0de96b03b110d3bf0a9016f47cdd2f2e23fb113de9 |
| MCHorizonUWP-NightlyCross-arm-1.0.0.2.appx | 1.0.0.2, 10.0.15063.0 | 80c12dbbb3a2d3a7549310c1e8038cade5652738062102a35a2ad9c54f051839 |
| Horizon.Asset.26-3.98ccbead789d397639d5badd0bb805.appx | 1.0.0.0, 10.0.15063.0, neutral | c5b02839c755d6b801ab47af5ceddfdd953cd6dba0905dfbeea1b7a3724e6835 |

Both game manifests require architecture-matched Microsoft.VCLibs.140.00 ≥ 14.0.0.0 and Microsoft.UI.Xaml.2.7 ≥ 7.2208.15002.0. The Surface already had native ARM64 versions 14.0.33519.0 and 7.2409.9001.0. The Lumia's installed prerequisite versions remain unknown. Both ARM32 dependency packages passed Microsoft signature verification.

| Lumia dependency | Version and minimum OS | SHA-256 |
| --- | --- | --- |
| Microsoft.VCLibs.arm.14.00.appx | 14.0.33519.0, 10.0.10042.0 | 2c422523ff693689a84c109585cfa444143ac3b6b7a5cadf4858afc6a3cb750f |
| Microsoft.UI.Xaml.2.7.appx | 7.2208.15002.0, 10.0.15063.0 | a3db0ba5940eaab87326f6d072ab1bc83297eb784d1d03ff712f586636fba994 |

### Surface Pro X

The Surface reported Windows 11 Pro Insider Preview build 26320, Microsoft SQ1 ARM64 at up to 3 GHz, Adreno 680 driver 27.20.2160.0, a 2880 × 1920 panel at 60 Hz, 7,901,184 KiB reported physical memory, and about 10.2 GB free before deployment. Device Portal answered with HTTP 401. The authorized SSH fallback verified both transferred SHA-256 hashes and imported the verified development publisher certificate into LocalMachine TrustedPeople. Direct session-0 deployment failed at Windows process lifetime manager initialization with 0x80070005. The same Add-AppxPackage commands succeeded through the interactive test account. The framework took 75.383 s and the APPX took 5.122 s. Both registered with status Ok. The existing prototype has its own package identity and LocalState.

The app used the physical Qualcomm DXGI adapter and wgpu D3D11's FL9_1 compatibility path. The reported hardware feature level was 11_1. The game surface was 2048 × 1536 in FIFO mode. The process used 618,053,632 bytes of working set at the title panorama capture and peaked at 1,390,780,416 bytes. The launcher also completed a separate DX12 adapter, device, surface, frame acquisition and presentation probe. DX12 gameplay, FPS, world loading, gameplay pointer and keyboard, physical touch, orientation, controller, suspend/resume and clean exit remain unverified. The isolated package has audio disabled. The console locked before further input, and subsequent SSH connections timed out before authentication. [Surface title panorama](../deliverables/nightly-2026-11-19/devices/surface-dx11-title-panorama.png)

### Lumia 950 XL

The pinned-certificate Device Portal helper timed out at the configured address. A later TCP connection on port 443 briefly succeeded, but authenticated API reads still timed out and subsequent TCP and HTTPS checks failed. No Lumia USB device was present on Ben-Desktop. The actual OS build, package state and GPU remain unverified. Installation, activation and ARM32 gameplay testing require the phone awake on the home LAN with its paired Device Portal serving HTTPS.

## Native mods, staging and graphics

- A standalone x64 AppContainer loaded a DLL after the signed main bundle declared its executable optional bundle as a related set. Installing the optional bundle alongside the standalone main APPX put it in the package graph, but the loader returned Win32 126. The related-set update retained the same main EXE. Windows documents a build 16299 minimum for executable optional bundles. The ARM32 in-main DLL probe uses PhoneAppModelHost.dll and carries a 15063 minimum. [Native mod report](native-mods-uwp-2026-11-19.md)
- The signed x64 native source proof invoked the stock Rust gui_render_post hook. The test saw 6,400 exact cyan pixels when enabled and zero at the same position when disabled in the same APPX. ARM64 and ARM32 compiled the identical source against the pinned UWP API. A Horizon-aware optional DLL loader and ARM runtime proof remain pending. [Native mod report](native-mods-uwp-2026-11-19.md)
- The content candidate keeps vanilla audio in the resource framework and copies 242.7 MB of base data for the entity fixture. Signed x64 reached UI-ready in 123.495 s cold, 0.910 s on an unchanged start, 0.822 s disabled, 0.878 s re-enabled, and 139.717 s after updating the archive. The updated generation preserved the entity's protocol ID. [Content staging report](content-staging-2026-11-19.md)
- The graphics report contains the exact native Nightly build, hardware UWP DX12 title and world evidence, the DX11 comparison and ANGLE qualification status. [Graphics comparison](nightly-graphics-2026-11-19.md)

## WebAssembly

| Experiment | x64 Windows build 26300 | ARM64 | ARM32 |
| --- | --- | --- | --- |
| codeGeneration, RW → RX, flush, call, release | Signed AppContainer returned 42 and released the page | Strict-linked and signed, device runtime pending | Strict-linked and signed for 16299+, mobile runtime pending |
| Same executable without capability | RX transition failed with Win32 5, allocation released | Signed control, device runtime pending | Signed control for 16299+ |
| Wasmtime 50.0.2 Pulley | Signed AppContainer compiled and ran the probe module with memory, trap and fuel checks | Pending | Pending |
| Horizon Wasm client hook | Standard guest built and dispatched in the exact desktop client. UWP game integration pending | Pending | Pending |

The native memory probe uses VirtualAllocFromApp for read-write anonymous memory, VirtualProtectFromApp for read-execute permissions, FlushInstructionCache and VirtualFree. No page requests write and execute at the same time. The ARM Store compiler using SDK 15063 rejected the FlushInstructionCache declaration. Microsoft records the UWP API-set entry beginning with build 16299. The standalone JIT probe declares that minimum. [Capability declaration](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations) · [Memory protection](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualprotectfromapp) · [UWP API versions](https://learn.microsoft.com/en-us/uwp/win32-and-com/win32-apis)

| Import | Experiment and result |
| --- | --- |
| VirtualAllocFromApp | UWP RW reserve and commit in the native probe. Anonymous reservation and commits also link in the Pulley probe. |
| VirtualProtectFromApp | RX succeeds at runtime with codeGeneration and returns access denied without it. Pulley links this for non-executable protection changes. |
| FlushInstructionCache | x64 success. ARM64 and ARM32 link in the modern SDK. Missing from the ARM Store 15063 headers. |
| VirtualFree | x64 release succeeds with and without codeGeneration. |
| VirtualAlloc and VirtualProtect | Stock Wasmtime reached the product's forbidden-import sentinels. Both are absent from the patched DLL import table. |
| Executable file mappings and vectored exception handling | The Pulley experiment avoids both. Native Wasmtime JIT support still requires UWP validation for file mapping, instruction publication, unwind registration and traps. |

The Pulley patch uses UWP reserve, commit and protection calls in Wasmtime's Windows memory backend. It copies file-backed inputs into anonymous memory and keeps native code publication unavailable. The probe avoids executable file mappings and vectored exception and unwind registration. The strict link used the product sentinel, SDK Store libraries and Rust TLS object. The app used in-process Cranelift compilation to Pulley bytecode without declaring codeGeneration. It called a Wasm export ten times, checked the return value 42, refused a memory grow beyond the 64 KiB store limit, observed a memory-out-of-bounds trap, and stopped a loop through fuel exhaustion. The default game still refuses Wasm packages.

| Probe artifact | SHA-256 |
| --- | --- |
| MCHorizonUWP.CodeGenerationProbe.Enabled.x64.appx | 076515fee7be9c0b2446d5fef8c4949d42e916efacf52b88251b5277f4d508c2 |
| MCHorizonUWP.CodeGenerationProbe.Control.x64.appx | 0299a4bf7ff2587b1968a71fa4afad7ba672b5155fcb427811a6ee702b3598bd |
| MCHorizonUWP.CodeGenerationProbe.Enabled.arm64.appx | 5797333fcb98ffefe08547679b82956d083b03d9d341b913879f7fe4724f0794 |
| MCHorizonUWP.CodeGenerationProbe.Control.arm64.appx | 55013b1b1282c68d11375416f751f324aad9a25a71a9156eb3bf0156f1dabad4 |
| MCHorizonUWP.CodeGenerationProbe.Enabled.arm.appx | 391c5b4bb8ee397bd1375d3a15b567308334e7a4cc6bfd82235d277c451bf3ef |
| MCHorizonUWP.CodeGenerationProbe.Control.arm.appx | f4371ed842c2d446a794930e32ef10531b103d344d74857145648fd095fa2a23 |
| MCHorizonUWP.CodeGenerationProbe.Pulley.x64.appx | 57c79fdcc232af3e8a1ed60c2688ec9c8a8fe5421ab2025f7ee746ab9a228104 |
| horizon_wasmtime_uwp_probe.dll | aab8879aa1e6125f70b50bd4b7a4f6245119794fe08938cd09ea7753363d55a2 |

The exact stock desktop client loaded the standard Wasm shared library and .hmod through the generated Horizon ABI. Its offscreen title showed 1,024 exact magenta pixels with the client hook enabled and zero at the same location with the same package disabled. Both runs exited cleanly. In-game UWP Pulley dispatch remains open. The stock stack guard rejected an 8 MiB thread and accepted a 136 MiB owned worker in a desktop test. The shipped UWP game enters via a WinRT worker with no explicit guest-stack reservation. Its live reservation is unmeasured. [Horizon ABI probe](../uwp/wasm-abi-probe/README.md) · [Executable-memory and Wasmtime build](../uwp/code-generation-probe/README.md)

## Release integrity

The local candidate uses patch 0.1.6, client 0.1.7 and Builder 1.0.5. The candidate archives came from product commit ce8dfc629a913ec0fac45567fd382e5e085dce03. The locally verified Builder Core and tests built with zero warnings and errors. The setup-checker suite and five native-source staging tests passed. Clean Builder assembly verified the exact upstream archive, applied patch 0.1.6, and staged the wgpu source and client bundle. The optional native-source path was tested with the same staging, static linking and APPX packaging operations outside the authenticated Wizard.

| Candidate input | SHA-256 |
| --- | --- |
| horizon-uwp-0.1.6.patch | c94f4455d2581f32b53136e3d958e8212633b26bc6ed3a9ffb6e33fd74fc155b |
| uwp-client-0.1.7.zip | d51bf966335a4b7ddcf1ca689e02523fe6db97841c2cf5ed9f6d019cd0bb85fc |
| catalog.UNSIGNED.json | f0c1de595ae705a66c2761d5d329ac1bc39849dcc00b668d774396cb8431c11e |

The catalog compatibility rule passed for x64, x86, ARM64 and ARM32. Build and device evidence values in the unsigned catalog remain unverified. The exact source assembly, signed x64 tests, ARM module compilation and device-specific results are recorded independently. [Unsigned candidate files](../deliverables/nightly-2026-11-19/builder-candidate) · [Source assembly procedure](nightly-2026-11-18.md)

The tracked Wasmtime scripts were also verified from a clean E: scratch path. The final rebuild passed the strict link and all import checks, and its APPX passed SignTool. Its APPX SHA-256 is 769d6e41f25c384cc9759754b26ba3096170302237c4cdf5dcff5878c361e650. The AppContainer runtime result above belongs to the earlier 57c79fdc package.
