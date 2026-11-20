# HorizonUWP Nightly mod runtime development

The worktree started at `53e3815be`. All builds in this report ran on
Ben-Desktop against Horizon `0b14f8acfd97fa2c750e42795249dcc981178ca4` and the
patched wgpu `4c24e58de1ee12b9dd3a81cc4490662353011c30`. The upstream Git
checkout resolved `nightly-source/2026-11-18-0b14f8a` to that exact commit.
The original product worktree and the wgpu worktree were preserved.

## Code paths

- Wasm packages use the pinned Nightly package loader, generated host ABI,
  shared Wasm standard library, and engine registration. The UWP profile
  compiles into Pulley bytecode with Wasmtime 50.0.2. It uses explicit traps,
  no static linear-memory reservation, and the stock epoch interruption
  budget. An owned 8 MiB game worker supplies the native host stack, and
  Pulley uses a separate guest interpreter stack. The interpreted package
  has no `codeGeneration` declaration. [Runtime source and ABI notes](../uwp/wasm-abi-probe/README.md)
- Native code uses a signed executable optional modpack in the main package's
  related set. The loader validates the package graph, publisher, architecture,
  manifest, exact UWP build identity and DLL hash. After sharing the game
  allocator, the DLL returns the stock typed hook factory. The Builder
  accepts explicitly trusted API-only sources. This profile covers typed
  hooks and does not provide the shared Rust crate state required by some
  native API calls. The executable optional route requires Windows build
  16299 or newer. ARM32 keeps Builder-time embedding as the default.
  [Native architecture, trust and packaging](native-mods-uwp-2026-11-19.md)
- The current desktop Wasm guard also requires build 16299. The optional
  ARM32 legacy SDK patches use a `VirtualQuery` stack-reservation check and
  the 15063 SDK's memory contracts. Removing unused desktop JIT dependencies
  resolved 39 missing Windows ARM binding errors. The actual
  `horizon-mod-wasm` crate passed an ARM32 type check. The guard and import
  mapping strict-linked in focused ARM32 DLLs. The signed full-engine ARM32
  proof below uses that same profile. [Legacy ARM32 details](../uwp/wasm-abi-probe/README.md)

Useful build commands are below. Set `$assembly` to the verified source
snapshot and `$runtimeTarget` and `$runtimeOutput` to separate scratch
directories on Ben-Desktop. The exact commands and compiler outputs for
the device and x86 builds are in their evidence directories.

```powershell
.\tools\build_nightly_runtime.ps1 -Source $assembly -Architecture x64 `
    -AdditionalFeatures uwp-wasm,uwp-native-mods `
    -TargetDirectory $runtimeTarget -Output $runtimeOutput
python -B uwp/native-mod-probe/stage-packaged.py --workspace $nativeAssembly `
    --architecture x64 --game-build-id $gameBuildId --trust-native-source `
    --source uwp/native-mod-probe/hook-source
.\tools\build_nightly_runtime.ps1 -Source $nativeAssembly -Architecture x64 `
    -NativeMods -TargetDirectory $nativeTarget -Output $nativeOutput
```

## Signed x64 hook proof

The x64 bridge built in 19 minutes 12 seconds, passed the strict AppContainer
link, and contained `VirtualAllocFromApp` and `VirtualProtectFromApp` imports.
Its loaded imports omit the forbidden desktop `VirtualAlloc` and
`VirtualProtect`. The standard Wasm fixture had the same SHA-256 as the
previous stock desktop proof. The signed base APPX 1.0.1.0 loaded it in
HorizonUWP and drew 6,400 magenta pixels on the title screen.

Installing both signed bundles in one operation updated the isolated main
identity to 1.0.1.1. The related set kept the base EXE and bridge SHA-256
values. The native hook came from the executable optional package. Both
hooks drew through Horizon's typed `gui_render_post` API. The package was
restarted for each setting and closed cleanly in all three valid captures.

| Setting at launch | Exact cyan pixels | Exact magenta pixels |
| --- | ---: | ---: |
| Enabled | 6,400 | 6,400 |
| Disabled | 0 | 0 |
| Re-enabled | 6,400 | 6,400 |

The first disabled capture showed the loading screen. The table uses a
replacement capture from the rendered title on the same signed package.
[Render captures, logs and marker counts](../deliverables/nightly-2026-11-20/x64/)

| x64 artifact | SHA-256 |
| --- | --- |
| Base APPX 1.0.1.0 | `36d5b72893da6f12744a092cae14ef69b11c3f4989a6b7290c09e594cc65090f` |
| Main related-set bundle 1.0.1.1 | `4b29a6fe433b58d4afa777cce55772e802fd262b5aa5cc5025a7f44ebda7be48` |
| Executable optional bundle 1.0.1.1 | `6a596627b31698cbeda9b947b8e8fc372260068c69f7ea3e030cc8bb72fc8fcf` |
| Native hook DLL | `44147ac0b0ade0625a2abee045add38e3ae980f1b719578d1407e9f2d508912d` |
| Game bridge DLL | `65f49f8b20428ef30f88ea2c9818f9dc427eca463752e06eaf94210db955a057` |
| Game EXE | `e6c705d8aa60aa0f3bac21f7acfba834483266ec5dbca1d0e48182dc1101c52b` |

The signed x64 game refused an unsupported API version, a module SHA-512
mismatch, a missing required dependency, and a 256 MiB declaration against
a 128 MiB configured cap. A genuine second mod trapped through its regular
GUI hook. Wasmtime stopped its dependency group, and the game logged a
rendered frame afterward. The window had lost focus before the screenshot.
The earlier standalone AppContainer probe covers bounded growth, an
out-of-bounds trap and fuel. Those three checks still need separate
in-game coverage.

The signed 1.0.1.2 optional pack carried two native DLLs. Its inventory
listed the dependent first, while the dependent manifest required exactly
`uwp_native_hook_probe` 0.1.0. With both enabled, the green dependent hook
painted over cyan and the title showed 6,400 green and zero cyan pixels.
Disabling only the dependent produced 6,400 cyan and zero green pixels.
Re-enabling the dependent restored the green marker. Each run exited
cleanly, and both main binaries kept their original SHA-256 values.
[Dependency-order screenshots and hashes](../deliverables/nightly-2026-11-20/x64/dependency-order/)

## Signed x86 hook proof

The first x86 release compiled both crates and passed the original strict
link. The signed 1.0.1.1 package then faulted with `0xc0000005` before
installing any hooks. CDB captured a null receiver in the `gilrs` event
channel. The linker placed `_tls_used` at the start of `.tls`, ahead of the
raw template by 24 bytes. Rust used section-relative offsets and read an
uninitialized controller slot. The same executable in the earlier x86
package used a correctly positioned template and passed startup.

The host shim now places `_tls_used` in `.rdata$T`. The strict linker
checks that the TLS template begins at the address used by section-relative
relocations and rejects the failing layout. Relinking the original Rust
archives with the corrected shim kept controller support enabled. The
main and optional 1.0.1.2 x86 bundles passed SignTool and installed as a
related set. The Wasm fixture used the normal 32-bit Pulley interpretation.

| Setting at launch | Exact cyan pixels | Exact magenta pixels |
| --- | ---: | ---: |
| Enabled | 6,400 | 6,400 |
| Disabled | 0 | 0 |
| Re-enabled | 6,400 | 6,400 |

All three runs reached the rendered title on hardware D3D11 and exited
cleanly. The enabled run accepted the packaged native mod and the same
standard Wasm fixture as x64. [Signed x86 bundles](../deliverables/nightly-2026-11-20/x86/related-set-tls-fixed/) · [Captures and logs](../deliverables/nightly-2026-11-20/x86/hooks-tls-fixed/) · [Debugger and TLS regression](../deliverables/nightly-2026-11-20/x86/hooks/)

| x86 artifact | SHA-256 |
| --- | --- |
| Main bundle 1.0.1.2 | `add26e40c5c35f2aeb261670fac1b7e6f010064fdebb495e3e2c3e7102291e9c5` |
| Optional bundle 1.0.1.2 | `611b6d17cc41fe2055034c67af5ca2c0f80ce4f6196c0f94adb59b6ee75d5b5d` |
| Game bridge DLL | `95b92738701af3bd21904941eab997dd1c8df6249d214737d94565e59c5ea267` |
| Native hook DLL | `70a7e85bbe64cb9c3948a2e8796a2fd12037eb389703046f6c58fb9c56b35480` |

## ARM64 release

The game bridge passed locked ARM64 type checking in 4 minutes 31 seconds.
The optimized build exhausted C: at the final archive after compiling its
dependencies. It reused those objects and emitted the final archive on D:
in 9.13 seconds. The combined Wasm and native loader bridge and the
native-only fixture DLL both passed the strict ARM64 link. The main APPX
and related-set bundles were signed and passed SignTool verification.

| ARM64 artifact | SHA-256 |
| --- | --- |
| Game bridge DLL | `7c96bea0e264a5e0bcf875eff9cd7387e646a13460db1b10bcaacc060dec5cdd` |
| Native hook DLL | `7f498c5280667bba0a181e822253de8c87e572eb4755a57804c66cabb1f54148` |
| Base APPX 1.0.1.0 | `3abffb16693e57257065c020c143e031a9dad0d494191802887b51c619734906` |
| Main bundle 1.0.1.1 | `4248cb29ba2aa7039224bb395f4f7bbc30e55844aef4f8a053519fd41449afe7` |
| Optional bundle 1.0.1.1 | `14d3f3317a54f0739c0a019f53ce978b9d7349b6d495d16a1586a8e80d664ce0` |

The main and optional bundles, base package and test fixture reached the
Surface's `C:\Users\ben\AppData\Local\Temp\MCHorizonUWP-NightlyMods-20261120`
from `D:\Temp\mchorizon-mod-runtime-mission\arm64-related-set`,
`D:\Temp\mchorizon-mod-runtime-mission\arm64-base`, and the Nightly
deliverables. Installation from the SSH session returned `0x80070005`.
An interactive task installed the main package. The initial installation
with `-RelatedPackages` left the optional package unregistered. Using
`-ExternalPackages` in the signed-in session registered the matching
ARM64 optional package.

The Surface's first 1.0.1.1 hardware D3D11 run loaded the stock Wasm mod
and rendered the panorama with no menu or marker. After the optional
package was installed, the hardware DX12 run on the same Adreno 680
logged both code mods and rendered the welcome UI. A full-image count
found 9,216 exact cyan pixels and 9,216 exact magenta pixels. The process closed cleanly. The
capture establishes both actual hooks on the Surface. Toggle state,
world entry, audio and controller use remain unverified for this package.
[D3D11 panorama-only capture](../deliverables/nightly-2026-11-20/arm64/surface-first-hooks/enabled.png) · [DX12 hooks and logs](../deliverables/nightly-2026-11-20/arm64/surface-dx12-hooks/) · [Build and signatures](../deliverables/nightly-2026-11-20/arm64/)

## Physical devices

| Device | Deployment and execution | Remaining verification |
| --- | --- | --- |
| Surface Pro X | The isolated ARM64 package rendered both code hooks and the title UI on hardware DX12. The prior D3D11 package presented a 2048×1536 FIFO panorama and measured a 1,480,892,416-byte peak working set. | D3D11 title UI remains invisible. Real world entry, multiplayer, gameplay input, audio and suspend/resume remain unverified. [Surface device report](nightly-surface-2026-11-20.md) |
| Lumia 950 XL | Windows Mobile 16241.1001 installed and ran isolated ARM32 1.0.0.4. After importing and restarting, the same signed game drew both hooks on the title and in an existing saved world opened from the launcher. | The title UI and in-world HUD remain visibly incomplete. The on-screen keyboard and content mods remain unverified. [Saved world screenshot](../deliverables/nightly-2026-11-20/lumia/20261120_0006.png) |

The Lumia returned a legacy HTTP 204 response with a JSON status body while
installation was running. A read of the same supported endpoint over TLS with
the stored certificate pin confirmed progress and then HTTP 200, code 0.
The installed-app inventory confirmed the exact ARM package.
[Device Portal deployment and activation APIs](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/device-portal-api-core)

The deployed ARM executable statically imported `CreateFileFromAppW` from
a contract first listed at Windows build 17134. The call opens a small
release metadata file from the app's own installation. The host now opens
it using `CreateFile2`, which is available on the Lumia's OS. The prior
bridge's TLS layout and all named imports between that bridge and the EXE
passed review. [Microsoft's UWP Win32 API inventory](https://learn.microsoft.com/en-us/uwp/win32-and-com/win32-apis)

Activation of 1.0.0.2 returned `0x8027025B` before any LocalState log,
including on manual launch. The rebuilt host passed ARM import inspection.
The signed 1.0.0.3 update kept the original bridge SHA-256
`37d3e9f62dcd342539d1fe139928620b8fe61a9208b657df3df647a33a80eb82`.
The updated package SHA-256 is
`413d35870bd8e7d1d207f1e6d1905b893ad324bfb57c77c12c2bafbdced1dc40`.
Device Portal recorded the new process and the successful wgpu D3D11
presentation probe. ANGLE failed its surface probe, and DX12 reported an
unsupported ARM32 software adapter. The observed 589,426,688-byte working
set after Start game is a point sample.

The user's screenshots were retrieved from the exposed Pictures Screenshots
folder with matching file lengths. The fatal reporter saved a wgpu
validation diagnostic at `wgpu_core.rs:2689` after the world creation
attempt. A draw in `Horizon frame` bound 112 bytes at group 0, binding 0,
while the shader required 128 bytes. [Lumia error report](../deliverables/nightly-2026-11-20/lumia/horizon-error-20261120-011521-104-7692-0.txt)

The FL9_1 stars shader declared an extra unused `hdr_shares` vector.
Patch 0.1.8 removes it and sets the declared binding size to the Rust
uniform's 112 bytes, so future size mismatches surface at pipeline
creation. Horizon drew the missing-glyph outlines in the Lumia captures.
The pinned neutral framework contains the default font definition and
Latin bitmap assets. Patch 0.1.8 also opens the known default font
definition directly when UWP enumeration missed it and the definition
is present. The revised package still showed a panorama and broken or
invisible title UI on the Lumia.
[Follow-up patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.8.patch)

## Signed ARM32 hook proof

The complete ARM32 build used
`F:\Temp\mchorizon-lumia-wasm-legacy-20261120\source`, patch 0.1.8,
client 0.1.9, `-Architecture arm -AdditionalFeatures uwp-wasm`, and the
trusted static native fixture staged into the bridge. The optimized retry
completed in 12 minutes 39 seconds. The archive was written on D:.
After the D: linker exhausted temporary output space, the original archive
passed the strict ARM AppContainer link on F:. The final PE used the
15063 memory and stack contracts and the manifest declared a 15063
minimum. The APPX included the pinned shared standard library and no
`codeGeneration` capability.

| ARM32 artifact | SHA-256 |
| --- | --- |
| Signed APPX 1.0.0.4 | `e3742c98f2012de7184972977ba7bdb4fc1b6f3c1a2a4e355998beef7f1c6995` |
| Game bridge DLL | `390c83cd7274b697deb55702e2d541acb635d41b7f52a24986d97e06f232c92d` |
| Standard Wasm `.hmod` read back from the phone | `dfa34da1970cea7df2b7e9031fd17c56f9f47d591912f542a08275d82c5d1194` |

Device Portal confirmed the new isolated package and the still-installed
original client. The user imported the official-format `.hmod` through
the launcher's file picker. Returning to launcher and starting again in
the same process produced `mod registration seal already installed`.
Horizon uses a process-wide registration seal. The full app was stopped
and launched again in a new PID before retrying the imported mod.

| Run | Cyan pixels | Magenta pixels |
| --- | ---: | ---: |
| First launch with embedded native fixture | 438 | 0 |
| Fresh app process with native and Wasm fixtures | 432 | 464 |

The two shapes are distinct in the rotated on-device screenshots. The
first image's cyan marker and the fresh run's cyan and magenta markers
confirm actual `gui_render_post` execution inside the signed ARM32 client.
The user reopened the app and selected the existing saved world using
the launcher's Worlds and saves screen. The subsequent capture shows
terrain, both mod markers, and fragmented HUD text. This package uses
an embedded ARM32 native fixture. [Native-only screenshot](../deliverables/nightly-2026-11-20/lumia/20261120_0003.png) · [Both hooks at title](../deliverables/nightly-2026-11-20/lumia/20261120_0005.png) · [Both hooks in the saved world](../deliverables/nightly-2026-11-20/lumia/20261120_0006.png)

The ARM UI follow-up records the two application changes, the signed
diagnostic builds, and the remaining device tests.
[ARM HUD investigation](nightly-arm-hud-2026-11-20.md)

## Content staging

The bounded copier reduced median copy time from 17.628 to 7.013 seconds
on x64 and from 25.750 to 8.323 seconds on x86. Both implementations copied
23,213 files and 242,683,397 bytes. A 32-bit desktop test of the production
ARM32 JSON serialization strategy reduced peak working set by 16.6% for
the largest table, with identical output bytes. Five focused tests passed
on each desktop architecture. These are controlled component measurements.
The signed x64 Wasm-only launch copied 23,219 files and 248,353,373 bytes
in 28.878 seconds. UI-ready took 32.395 seconds cold and 0.973 seconds
when that exact staging generation was reused. The earlier baseline used
a different content fixture. The controlled entity-fixture comparison
therefore remains the basis for the copy improvement figures above.
[Methods, raw evidence and previous signed startup baseline](content-staging-2026-11-19.md)

The signed x86 1.0.1.2 related set also loaded the standard entity fixture
with both code mods active. It staged 23,213 files and 242,683,397 bytes
in 42.999 seconds, then rewrote six registry tables in 68 ms. UI-ready
took 44.736 seconds cold and 1.388 seconds when the generation was reused.
The generated registry kept protocol ID 161 for `uwp_smoke:probe`, exposed
it in the command registry and kept the fixture's 0.65 shadow radius.
These end-to-end runs overlapped the ARM64 build. The earlier controlled
probe remains the performance comparison.
[Signed x86 cold and warm logs](../deliverables/nightly-2026-11-20/x86/)

## Graphics

The pinned x64 ANGLE build produced `libEGL.dll`, `libGLESv2.dll` and
`z.dll`. The related three imports resolve inside the signed package. The
package uses the same strict-linked game bridge as the prior UWP DX12/DX11
comparison and the ANGLE-enabled host. The isolated package installed with
status Ok. Hardware ES3 gameplay and matched foreground measurements are
pending. The earlier background comparisons remain diagnostic.
[Graphics evidence and benchmark setup](nightly-graphics-2026-11-19.md)

## Build and workspace

The combined x64 and ARM64 source assembly is at
`E:\Temp\mchorizon-mod-runtime-mission\source`. The x86 game and native
fixture use the isolated `source-native-x86` overlay. The x64 and x86
release target is `D:\Temp\mchorizon-mod-runtime-mission\target-x64`. The
ARM64 build and the first x86 check use
`E:\Temp\mchorizon-mod-runtime-mission\target-cross`. The x86 release
dependency compilation completed on D:. Its final archive hit disk error
112 there and produced a 433,989,486-byte archive in C: scratch using
`cargo rustc -- -o`. The archive now resides in the D: mission's
`x86-build-archive` directory. Move receipts record the original paths and
matching SHA-256 values, including prior inactive archives retained on D:.

The first complete ARM32 Wasm and embedded-native build failed while
writing dependency output on E: with disk error 112. Locked offline
metadata had passed. Rebuilding the privately staged Windows bindings
from the local registry restored the override files and retained the
original incomplete junction. Cleaning the six completed file-copy
benchmark destinations and three duplicated x64 packaging trees raised
E: from about 25 MiB free to about 2 GiB. A final expired ANGLE packaging
directory was also cleared after verifying its files against the signed
APPX. The signed packages and raw
timing reports were retained. Compressing inactive ARM64 and x86 files
reclaimed space on D: and preserved their contents. J: was left untouched.
[Cleanup inventory](../deliverables/nightly-2026-11-20/disposable-copy-benchmark-cleanup-audit.json)

The Builder Core and test executable built successfully. The full setup
checker passed. The WinUI Builder compiled with two missing ruleset warnings
from the local Visual Studio task configuration.

Builder 1.0.6, patch 0.1.7 and client 0.1.8 assembled from
`4b01902dbf09509a1d66054f01245dcc39b11bda` with refreshed TLS objects for
all four architectures. The fresh Builder assembly verified the pinned
upstream and wgpu archives. Locked offline Cargo resolution succeeded for
x64, x86 and ARM64 against the staged Wasmtime source. The runtime
candidate remains local with an unsigned catalog.

A second candidate from `b5b64d53bb3cf45018b1a86e537fbf5ae15a4731`
includes the rebuilt hosts for all four architectures with the legacy
metadata import fixed. `D:\Temp\mchorizon-mod-runtime-mission\builder-candidate-final`
contains the patch, client archive, and unsigned catalog. Candidate hash
records are stored alongside it. The updated host images passed import
inspection for every architecture.

Patch 0.1.8 and client 0.1.9 give the FL9_1 and font changes a distinct
build identity. The local unsigned candidate at
`D:\Temp\mchorizon-mod-runtime-mission\builder-candidate-0.1.8` was
generated from `fc14183894b0c9240ab33bdd8725d2df0587d7a0`. The previously
tested 0.1.7 and 0.1.8 pairing remains supported. Builder Core and its
test executable rebuilt with zero warnings and setup checker tests passed.
The new full patch forward check passed against an index of the immutable
upstream commit, as did the ZIP integrity and three pinned bundle digests.
The fresh full 0.1.8 Builder extraction hit disk error 112 on E:.

## Agent work

- The Wasm agent adapted the pinned standard host and runtime, staged the
  Wasmtime source, prepared package validation fixtures and audited ARM32.
- The native agent implemented the ABI loader, source validation, strict
  mod DLL link and related-set packaging workflow.
- The content agent implemented and measured staging and verified the ANGLE
  package imports and benchmark prerequisites.
- The primary agent integrated the changes, coordinated builds, wired the
  Builder and led signed-game and device verification.
