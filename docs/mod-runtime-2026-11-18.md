# UWP mod runtime checks for Horizon Nightly 2026-11-18

The port uses the package, registry and startup code from Horizon commit `0b14f8acfd97fa2c750e42795249dcc981178ca4`.

## Content packages

A standalone Rust test used the exact checkout's `horizon-mod-package` with default features disabled and a separate Cargo target directory. It built a `.hmod` using `Package::write_files` containing a `registries` manifest and an entity-type JSON file. It verified `Package::read`, `Content::read`, no selection before enabling, selection after writing `mod.bird:true` to the scratch `horizon-mods.txt`, and rejection of a corrupt `.hmod`. No installed game or user data was used.

The test ran on the Windows desktop host, not in an AppContainer. Command (from a Visual Studio x64 developer environment, with the probe manifest and target located under the system temporary directory):

```powershell
cargo +1.99.1 test --offline --manifest-path "$env:TEMP\horizon-uwp-content-probe\Cargo.toml" --target-dir "$env:TEMP\horizon-uwp-content-probe\target"
```

The focused host tests passed. They also checked that the UWP selector refuses an enabled Wasm package and a content package that requires it, and keeps content packages with an optional Wasm dependency eligible.

In the signed x64 client, the integrated world loaded a real `uwp_smoke.hmod` package from app-local `game/horizon-mods`. The engine registered `uwp_smoke:probe`, marked it enabled for both client and server, and accepted `/summon uwp_smoke:probe`. The on-screen result used the package’s English translation, “Summoned new Nightly Content Probe.” The malformed `.hmod` placed beside it stayed out of the enabled inventory, and the game continued to the world.

Players can import `.hmod` packages from the UWP launcher’s Mods settings and manage their enabled selection through Horizon’s in-game Mods screen. Content uses the Nightly’s current package and registry formats.

The first content startup built a private snapshot with 28,189 files and about 624 MB of data. On UWP the patched Rust standard library reports hard links as unsupported, so the overlay copies the resources and regenerates merged tables. In two packaged tests this delayed content startup by roughly four minutes. A no-mod startup can read directly from the installed framework.

## WebAssembly

Two separate x64 UWP static libraries used Wasmtime 50.0.2, Rust 1.99.1, the port's patched Rust standard library, and a separate temporary Cargo target. Both were compiled with:

```powershell
cargo +1.99.1 build --offline --manifest-path "$env:TEMP\horizon-wasmtime-uwp-probe\Cargo.toml" --target x86_64-uwp-windows-msvc -Z build-std=std,panic_abort --target-dir "$env:TEMP\horizon-wasmtime-uwp-probe\target"
```

The environment used `RUSTC_BOOTSTRAP=1`, the port's prepared `rust-src`, and the x64 UWP compiler/linker settings from the local build setup. One probe enabled the upstream `anyhow`, `runtime`, `std`, `pulley`, `cranelift`, `parallel-compilation` and `threads` features and called `Engine::new` and `Module::new` for a Pulley target. The second enabled only `anyhow`, `runtime`, `std` and `pulley` and called `Module::deserialize` for a caller-provided trusted precompiled module. Both builds succeeded. They were not run.

Each static library was linked using Rust LLD with `/DLL /NOENTRY /APPCONTAINER /MACHINE:X64 /OPT:REF`, the port's x64 host kit, Windows Store MSVC runtime, Windows SDK UWP libraries, TLS object and dead-import sentinel object; the probe export was explicitly retained. Both link maps included sentinel-backed `__imp_VirtualAlloc`, `__imp_VirtualProtect`, and `forbidden_api`. The port's strict bridge link rejects a map containing the sentinel. Thus neither compilation nor Pulley deserialization has passed the current UWP link policy. No executable-memory or device behavior has been inferred from this link test.

Pulley interprets precompiled bytecode; [Wasmtime's platform documentation](https://docs.wasmtime.dev/stability-platform-support.html) explains that Cranelift generates its bytecode. Deserializing a precompiled Wasmtime module requires trusted, compatible bytes and is not a replacement for accepting arbitrary installed mod packages. The exact upstream Windows mod cache currently compiles in process with `Module::new`; its bounded compiler child is Unix-only. The port's sentinel marks `CreateProcessW` unavailable, and the patched Windows Rust standard library's process implementation calls it; that separate subprocess restriction does not explain the Windows mod-cache behavior.

## Native libraries

Upstream desktop loading uses `LoadLibraryEx` for executable-adjacent mod DLLs. `LoadPackagedLibrary` requires the library to belong to the AppContainer process’s package dependency graph, so the desktop mod folder cannot serve as that app’s library search path. [Microsoft LoadPackagedLibrary documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-loadpackagedlibrary)

The upstream SDK’s Windows x64 artifact targets GNU desktop Windows. The x64 UWP bridge targets `x86_64-uwp-windows-msvc` and uses a distinct build identity. A native-mod implementation would require UWP-targeted SDK artifacts and mixin validation for each architecture, plus a trusted way to package and load the DLLs. Builder rejects public native SDK mods for this Nightly. Touch and vibration are statically linked UWP services. [Exact upstream SDK release](https://github.com/horizon-rust/horizon/releases/tag/sdk-0b14f8acfd97fa2c750e42795249dcc981178ca4)

### Follow-up: interpreter without virtual memory

A disposable copy of Wasmtime 50.0.2 was changed to exclude `target_vendor = "uwp"` from `supported_os` in `build.rs` and select the custom platform backend for UWP. A Pulley-only probe set `signals_based_traps(false)`, `memory_reservation(0)` and `memory_guard_size(0)`. Building with the command above failed before linking: with `std` enabled and no `has_virtual_memory`, `runtime/vm.rs` still imports `sys::mmap`, while `mmap_vec.rs` refers to the unavailable `Mmap` and `MmapVec::new_mmap`. These source edits are an experiment, not a supported configuration; further Wasmtime changes and actual UWP runtime validation would be required.

Microsoft documents [`VirtualAllocFromApp`](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualallocfromapp) and [`VirtualProtectFromApp`](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualprotectfromapp) as UWP APIs. The former does not create executable pages. A custom Wasmtime memory backend using these APIs for non-executable memory is a possible investigation for Pulley, but has not been implemented, strict-linked or behavior-tested here. Disabling signal-based traps requires explicit bounds checks in generated bytecode and corresponding compatible precompiled modules; it does not by itself make the stock Windows memory backend UWP-compatible.
