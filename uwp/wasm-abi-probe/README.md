# Horizon Wasm ABI probe

The probe uses the Nightly's regular `mod_api` client hook. It draws a magenta square in the upper-right corner after the normal GUI batch. It has no native mixins, custom imports or separate API. The manifest sets a 32 MiB group memory request and lets Horizon's builder write its Wasm metadata.

## Build inputs

Use a disposable assembly of Horizon commit `0b14f8acfd97fa2c750e42795249dcc981178ca4`, the `cargo-horizon-mod` executable built from that commit, and Rust `1.99.1 58cf8f9c35b355199727627d07ae34a4e2846d67` with `rust-src`. Run in an x64 Visual Studio developer shell on the Builder PC. The script uses the assembly's local API source. Cargo fetches dependencies missing from its cache, including the pinned Wasm allocator on some setups.

```powershell
.\uwp\wasm-abi-probe\build-guest.ps1 -HorizonSource $snapshot -CargoHorizonMod $cargoHorizonMod
```

The script creates a fresh `.local/uwp-wasm-abi-probe-*` folder inside the assembly. It runs the stock tool's `wasm-std` and `build --wasm` commands. Expected outputs in that folder's `target/horizon-mod` are `horizon-wasm-std.wasm` and `uwp_wasm_hook_probe.hmod`. `-PrepareOnly` copies the guest project for inspection without running a build.

The Windows compiler executable has SHA-256 `bb02dd977745a4f56f54b7559243fa835712b33bd19e472c1e8d7be216c4ad9d`. Its Cargo dependency record names 2,122 source inputs that matched the pinned upstream archive after normalizing line endings.

The stock tool built both artifacts against the 0.1.6 candidate source assembly on the Builder PC. Cargo reported 32.47 seconds for shared Wasm std and 3 minutes 16 seconds for the GUI hook package. The package manifest's SHA-512 matched its embedded Wasm module. The module exports `horizon_layout`, `horizon_in` and `horizon_hook` and includes `dylink.0` and `horizon.build`.

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `horizon-wasm-std.wasm` | 652,933 | `123d39c44516ea2fed47f9a8099b5a96e35a675cf802a7e9aa4d149cb781296b` |
| `uwp_wasm_hook_probe.hmod` | 2,637,059 | `dfa34da1970cea7df2b7e9031fd17c56f9f47d591912f542a08275d82c5d1194` |

The artifacts, build log, and structural verification are in the local `deliverables/nightly-2026-11-19/wasm-abi-probe` folder.

The upstream fixture driver `crates/horizon-mod-wasm/tests/real_member.rs` shows the normal shared-std loading, inventory initialization and generated boundary dispatch. The rendering-specific lower-layer fixture is `crates/horizon-mod-wasm/wire-loopback/tests/wire_loopback/gui_entity.rs`. The probe uses an ordinary typed hook, so its build does not require a point catalog. Mods with `[[inject]]` require the exact game's staged point catalog.

`stock-host-test.rs` is a lower-layer desktop test for the built guest. Copy it to `crates/horizon-mod-wasm/tests/uwp_wasm_gui_probe.rs` in a disposable source assembly and set `HORIZON_WASM_STD` and `HORIZON_WASM_FIXTURE` to the two built files. Run `cargo +1.99.1 test --offline --locked -p horizon-mod-wasm --test uwp_wasm_gui_probe -- --ignored --nocapture` on the Builder PC. The test uses Horizon's generated runtime and checks that ten dispatches produce the expected draw commands on a guest-sized worker. It uses the desktop engine configuration unless the disposable runtime is separately configured for Pulley.

## Trap control

Pass `-Fixture Trap` to `build-guest.ps1` to build the independent standard client mod `uwp_wasm_trap_probe.hmod`. Its ordinary `gui_render_post` hook executes a Wasm `unreachable` instruction. In an isolated test profile, enable it beside the original visual fixture. A successful control records a Wasmtime fault for the trap owner, stops its group and continues drawing the original magenta hook.

The pinned mod tool built and checked the trap package with API 0.2. The recorded package is 2,637,031 bytes, SHA-256 `5db570be385710be06dc536503beb17fca2bccee92f3e889eb84d75067d80b48`. The standard library and original visual fixture retain the hashes above.

## Stock desktop client proof

The exact desktop build of Horizon `0b14f8acfd97fa2c750e42795249dcc981178ca4` loaded the package through its stock Wasm host on Windows x64. The executable's SHA-256 is `089a49e06d4103aeb8ad8006e39a85b36997293e86ff205f10174e71959522dd`. The game used the standard desktop Wasmtime configuration and an offscreen DirectX 12 renderer.

Two fresh game profiles used the same package and shared Wasm std with `mod.uwp_wasm_hook_probe` set to `true` and `false` respectively. Both runs used `--offscreen --control-socket` with the pinned resources and a copy of the executable. The enabled title frame contained a 32×32 magenta square at x=912–943, y=56–87 in the 960×540 screenshot. The disabled frame contained zero magenta pixels in the same upper-right search area. The game reports put both screenshots on the title screen, both processes exited cleanly, and the recorded foreground PID stayed the same.

The enabled run reported 115 ms to read packages, 1,329 ms to compile modules, 11 ms to link and 1 ms to instantiate. Title capture took 75.106 seconds enabled and 9.357 seconds disabled, including resource startup. The screenshots, control reports, logs and pixel analysis are in the local evidence folder under `desktop-stock-abi`.

## UWP engine candidate

Use an isolated source assembly with the exact 0.1.6 UWP patch and the current client files. Stage the pinned Wasmtime registry archive, then enable the UWP bridge feature. For an assembly whose engine patch already contains the integration, pass `-VendorOnly`.

```powershell
& uwp/wasm-abi-probe/stage-runtime.ps1 -Workspace $assembly -WasmtimeArchive $wasmtimeCrate
& tools/build_nightly_runtime.ps1 -Source $assembly -Output $build -TargetDirectory $target -Architecture x64 -AdditionalFeatures uwp-wasm
```

Use the packaged game bridge produced by the strict link. Place the verified `horizon-wasm-std.wasm` beside `HorizonUWP.exe` in the signed package. Import `uwp_wasm_hook_probe.hmod` through the launcher or put it in the isolated profile's normal `game/horizon-mods` directory. Set `mod.uwp_wasm_hook_probe:true` in that game's `horizon-mods.txt`. After the GUI hook renders, set it to `false` and restart the same package for the control.

The feature uses Horizon's existing `load_installed` and `initialize` paths, shared Wasm Rust library, dependency resolution and registration seal. The patched Wasmtime 50.0.2 engine defaults to the host pointer-width Pulley target. It compiles Wasm to interpreter bytecode in process and keeps explicit bounds checks and Horizon's epoch deadlines. It uses anonymous non-executable memory and does not declare the AppContainer `codeGeneration` capability.

The UWP runtime reserves a 2 MiB interpreter stack and bounds declared linear memory to 128 MiB per group on 64-bit targets or 64 MiB on 32-bit targets. The game worker uses an 8 MiB native host stack for synchronous host calls. Existing 32-bit host worker limits still apply. Desktop Cranelift runs guest code on the native host thread with a 136 MiB reservation. The Windows loader keeps serialized caching disabled and compiles modules in process without the Unix compiler-child deadline.

Locked Cargo metadata resolved the patched runtime for x64, x86 and ARM64. The ARM32 offline lock accepts the same source override when staged after the ARM32 dependency overrides. Compilation, strict link, package installation and hook execution are separate verification steps.

### Lumia experimental import profile

The stock UWP Wasm feature uses `GetCurrentThreadStackLimits`, which first appears in the UWP 16299 API-set list. The installed ARM Store SDK 15063 has no import object for it. Its FromApp memory imports also use older API-set names. [Windows UWP API versions](https://learn.microsoft.com/en-us/uwp/win32-and-com/win32-apis)

The optional legacy patches keep the Windows 10 Mobile package minimum. They select exact import contracts from the SDK 15063 ARM `WindowsApp.lib` and check a stack reservation through `VirtualQuery`. The guard walks regions with the same allocation base and requires at least 8 MiB. [VirtualQuery](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualquery)

| ARM import | SDK 15063 | SDK 19041 |
| --- | --- | --- |
| `VirtualAllocFromApp`, `VirtualProtectFromApp` | `api-ms-win-core-memory-l1-1-5.dll` | `api-ms-win-core-memory-l1-1-3.dll` |
| `VirtualFree`, `VirtualQuery` | `api-ms-win-core-memory-l1-1-2.dll` | `api-ms-win-core-memory-l1-1-0.dll` |
| `GetSystemInfo` | `api-ms-win-core-sysinfo-l1-2-1.dll` | `api-ms-win-core-sysinfo-l1-1-0.dll` |
| `GetCurrentThreadStackLimits` | Absent | `api-ms-win-core-processthreads-l1-1-1.dll` |

Apply `horizon-uwp-wasm-legacy-arm32.patch` from the assembled engine root and `wasmtime-pulley-legacy-arm32.patch` from `third_party/wasmtime-50.0.2`, after Wasmtime staging. Use a separate ARM32 snapshot with the offline overrides and the staged lockfile. Check the resulting PE import table against the 15063 contracts before packaging and verify on the Lumia.

The patched stack guard compiled on `thumbv7a-uwp-windows-msvc`. A small ARM32 AppContainer DLL linked the actual guard with the existing product sentinel and imported the SDK 15063 `VirtualQuery` contract. A separate ARM32 AppContainer import witness selected the legacy Wasmtime APIs with the modern Store import library on the link line.

The first targeted engine check reported 39 `windows-sys` errors involving ARM32 Debug and Kernel types, including `CONTEXT` and `SLIST_HEADER`. The legacy Wasmtime patch limits the ARM32 Windows bindings to memory and system information and excludes the unused JIT instruction-cache dependency. The real `horizon-mod-wasm` crate then passed locked offline `cargo check` in 4m 38s on Ben-Desktop. The independent source snapshot is `F:/Temp/mchorizon-lumia-wasm-legacy-20261120/source` and the Cargo target is `E:/Temp/mchorizon-mod-runtime-mission/target-arm32-wasm-focused`. The check and original failure logs are beside the source snapshot. The check snapshot activates the bridge's Wasm feature by default to select the dependency with `cargo check -p horizon-mod-wasm`.

The complete `thumbv7a-uwp-windows-msvc` bridge passed the strict ARM32 link with the legacy patches and the embedded native fixture. The signed isolated package 1.0.0.4 ran on the Lumia's Windows Mobile 16241.1001. A clean app process drew the real cyan native marker and the original magenta Wasm marker. The launcher return retains Horizon's registration seal, so changing the mod set requires closing and reopening the full app. The ARM32 title UI still needs work. The default Builder Windows Mobile profile keeps the static native fallback, and the legacy interpreter profile is an explicit test build. [Device proof, signatures and limits](../../docs/nightly-mod-runtime-2026-11-20.md)
