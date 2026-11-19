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

## Stock desktop client proof

The exact desktop build of Horizon `0b14f8acfd97fa2c750e42795249dcc981178ca4` loaded the package through its stock Wasm host on Windows x64. The executable's SHA-256 is `089a49e06d4103aeb8ad8006e39a85b36997293e86ff205f10174e71959522dd`. The game used the standard desktop Wasmtime configuration and an offscreen DirectX 12 renderer.

Two fresh game profiles used the same package and shared Wasm std with `mod.uwp_wasm_hook_probe` set to `true` and `false` respectively. Both runs used `--offscreen --control-socket` with the pinned resources and a copy of the executable. The enabled title frame contained a 32×32 magenta square at x=912–943, y=56–87 in the 960×540 screenshot. The disabled frame contained zero magenta pixels in the same upper-right search area. The game reports put both screenshots on the title screen, both processes exited cleanly, and the recorded foreground PID stayed the same.

The enabled run reported 115 ms to read packages, 1,329 ms to compile modules, 11 ms to link and 1 ms to instantiate. Title capture took 75.106 seconds enabled and 9.357 seconds disabled, including resource startup. The screenshots, control reports, logs and pixel analysis are in the local evidence folder under `desktop-stock-abi`.

## Engine integration requirements

The existing UWP build refuses Wasm packages. An experimental engine package would use the stock `horizon_mod_wasm::load_installed` and `initialize` paths, preserve the existing registration order and seal, and use the validated UWP Wasmtime Pulley configuration with explicit traps. The stock loader reads the shared standard-library member beside `HorizonUWP.exe` and the enabled `.hmod` under the isolated game's `horizon-mods` directory. Compile the guest on the Builder PC.

The exact x64 UWP host executable's PE stack reserve is 8 MiB. `uwp/host/App.cpp` calls the bridge from a WinRT thread-pool worker. That worker has no explicit guest-stack reservation in the host code. The stock Horizon runtime checks for 136 MiB on x64 before instantiation and guest entry. An engine proof needs a verified, appropriately sized game worker with the host's required thread context. The existing guest guard remains in effect. The Windows SDK 19041 WindowsApp import library contains `GetCurrentThreadStackLimits`.

A standalone Windows x64 executable linked with the UWP host's 8 MiB reservation reproduced the stock guard refusal on its main thread. A thread created with the existing `horizon_guest_stack::builder()` reported 142,606,336 bytes and passed the same guard. This exercised the exact stack policy on desktop Windows. The stack reservation of the live WinRT worker remains unmeasured.

The signed Wasmtime Pulley proof covers a standalone core module in x64 AppContainer. It establishes runtime compilation, execution, a bounded memory configuration and a fuel trap in that test. The desktop title test exercises the generated `horizon.host` ABI through the stock mod loader. A UWP game proof still needs shared module linking, generated imports, inventory validation, epoch-based deadlines and a title capture with the package enabled and disabled inside AppContainer.

The stock Windows loader disables serialized module caching and compiles modules in process. Its compiler-child deadline is Unix-only. The standalone fuel trap is evidence for guest execution limits. A compatible experimental engine still needs its epoch-based call limits checked separately from compilation time and persistent cache behavior.
