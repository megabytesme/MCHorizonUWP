# Experimental `wgpu` D3D11 (`FL9_1`) Integration

This document records how `MCHorizonUWP` integrates the experimental `wgpu` Direct3D 11 (`FL9_1`) backend from `C:\dev\personal\wgpu` (`megabytesme/wgpu-dx11`, `uwp` branch for UWP changes; `uwp-dx11` branch retains non-UWP desktop work), covering the Windows desktop build, strict-linked UWP builds, verification results, and remaining physical-device platform requirements.

## Repositories and Artifacts

- **`wgpu` fork**: `C:\dev\personal\wgpu` (`https://github.com/megabytesme/wgpu-dx11.git`, branch `uwp` at `4c24e58de1ee12b9dd3a81cc4490662353011c30`, layered cleanly on top of non-UWP branch `uwp-dx11` at `440895454850d54435c5bca3e99bab36e57f8137`).
- **`MCHorizonUWP` working tree**: `C:\dev\personal\MCHorizonUWP\.local\horizon-dx11` (assembled `main-2026-11-09` source + `horizon-uwp-0.1.2.patch` + FL9_1/D3D11 renderer & UWP bridge changes).
- **Synced UWP host/bridge files**: `uwp\game-bridge\Cargo.toml`, `uwp\game-bridge\Cargo.lock`, `uwp\game-bridge\src\lib.rs`, `uwp\host\build-game.cmd`, `uwp\host\strict-game-link.ps1`, `uwp\pregame\host\XamlShell.cpp`, `uwp\pregame\host\build-shell.cmd`, and `uwp\RENDERER_BACKENDS.md`. Client `0.1.5` / Builder `1.0.3` prebuilt host kits define `HORIZON_UWP_WGPU_D3D11=1` and omit `HORIZON_UWP_NATIVE_D3D11` so the older native DX11 renderer (`id = 0`) is replaced by the `wgpu` D3D11 `FL9_1` renderer (`id = 3`).
- **Source patches**: [`patches/main-2026-11-09/horizon-uwp-0.1.3.patch`](../patches/main-2026-11-09/horizon-uwp-0.1.3.patch) (combined single patch from pinned upstream commit `e797c725ad99ef311db216de7f41a2e6593353db`) and [`patches/main-2026-11-09/horizon-wgpu-dx11-fl91.patch`](../patches/main-2026-11-09/horizon-wgpu-dx11-fl91.patch) (delta over `0.1.2`).

## Key Integration Changes

### Horizon Renderer (`FL9_1` & D3D11 Selection)

- `Cargo.toml` and `uwp\game-bridge\Cargo.toml` override `wgpu`, `wgpu-core`, `wgpu-core-deps-*`, `wgpu-hal`, `wgpu-types`, `wgpu-naga-bridge`, `naga`, and `naga-types` to `https://github.com/megabytesme/wgpu-dx11.git` at pinned revision `4c24e58de1ee12b9dd3a81cc4490662353011c30` (`uwp` branch).
- `crates\horizon-client\Cargo.toml`, `crates\horizon-client-lib\Cargo.toml`, and `crates\horizon-client-render\Cargo.toml` enable `wgpu`'s `dx11` feature (and define `uwp-dx11-renderer` for UWP builds).
- `HORIZON_GRAPHICS_API=dx11` selects `Backends::DX11` (`wgpu`), requiring a real hardware adapter when `WGPU_DX11_REQUIRE_HARDWARE=1` is set and never silently falling back to DX12, Vulkan, GLES, or WARP.
- `crates\horizon-client-render` adapts incompatible operations for D3D11 Feature Level `9_1`:
  - Terrain 2D atlas fallback (`crates\horizon-client-render\src\gfx\fl91.rs`) where `FL9_1` lacks full texture-array support.
  - Non-MSAA `Rgba8Unorm` scene target with software sRGB handling and `u32` vertex attribute expansion.
  - Dedicated `FL9_1` WGSL shader variants (`*_fl91.wgsl` across border, entity, HUD, screen, terrain, panorama, stars, and world primitives).
- `crates\horizon-system\src\lib.rs` gates `emulating_on()` (`GetModuleHandleA`) under `#[cfg(all(windows, not(target_vendor = "uwp")))]` with a `None` UWP stub so strict UWP linking does not pull raw `kernel32.dll` imports against `dead-import-sentinels.obj`.

### `wgpu` Fork & UWP Host Wiring

- **`wgpu-hal/Cargo.toml`**: Scoped `Win32_UI_WindowsAndMessaging` to non-UWP and `Win32_{Devices_*,System_Diagnostics_Debug,System_Kernel}` off ARM32 UWP.
- **`wgpu-hal/src/dx11/device.rs` & `instance.rs`**: Under `#[cfg(target_vendor = "uwp")]`, `enumerate_native_adapters()` probes `D3D11CreateDevice(D3D_DRIVER_TYPE_HARDWARE)` across `FeatureLevel::ALL_DESCENDING` (`L9_1..=L11_1`, with `WGPU_DX11_FORCE_FL9_1=1` override and `E_INVALIDARG` `L11_1` fallback) -> `IDXGIDevice::GetAdapter()` -> optional `GetParent::<IDXGIFactory1>()`, avoiding static `CreateDXGIFactory2` (`dxgi.dll`) imports while formatting `driver_info` as `FL9_1 (hardware L11_1)` so `16384` 2D targets and native `R32_UINT` indexed draws are unlocked on `L9_2+` hardware.
- **`wgpu-hal/src/dx11/shader.rs`**: Under `#[cfg(target_vendor = "uwp")]`, `compile_hlsl` resolves `D3DCompile` dynamically via `OnceLock` (`LoadPackagedLibrary` from `WindowsApp` -> `LoadLibraryExW` from `raw-dylib kernel32.dll` -> `GetProcAddress` from `WindowsApp`), avoiding static `d3dcompiler_47.dll` and `SetThreadErrorMode` imports.
- **`wgpu-hal/src/dx11/{surface.rs,swapchain.rs,instance.rs}`, `wgpu-core`, `wgpu`**: Gated Win32 `HWND` APIs off UWP, enabled `SurfaceTargetUnsafe::SwapChainPanel` for `cfg(any(dx12, dx11))`, wired `RawWindowHandle::WinRt` via `horizon_uwp_acquire_game_swapchain_panel`, `horizon_uwp_attach_game_swapchain`, `horizon_uwp_detach_game_swapchain`, and inverse DPI scale `SetMatrixTransform` (`horizon_uwp_get_surface_metrics`), and added a `0`-flag fallback if `CreateSwapChainForComposition` rejects `DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT`.
- **UWP renderer selector (`id = 3`)**: `uwp\game-bridge\src\lib.rs`, `uwp\pregame\host\XamlShell.cpp`, `uwp\pregame\host\build-shell.cmd`, `uwp\host\build-game.cmd`, and `uwp\host\strict-game-link.ps1` register renderer `3` (`HORIZON_UWP_WGPU_D3D11`, `"Direct3D 11 (wgpu, feature level 9_1)"`, `HORIZON_UWP_DEFAULT_RENDERER=wgpu-dx11`, `HORIZON_UWP_HAS_SWAPCHAIN_PANEL`), reject `DeviceType::Cpu`, verify `driver_info.contains("FL9_1")`, and set `WGPU_BACKEND=dx11` / `HORIZON_GRAPHICS_API=dx11` on launch.

## Desktop Build, Launch, and Test Commands

All commands below assume an x64 MSVC environment (`"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64`).

### Build and Run the Desktop Client (`FL9_1` on Physical GPU)

```cmd
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
cd /d C:\dev\personal\MCHorizonUWP\.local\horizon-dx11
set "CARGO_TARGET_DIR=C:\dev\personal\MCHorizonUWP\.local\target"
cargo +stable build --profile fast -p horizon-client

set "HORIZON_GRAPHICS_API=dx11"
set "WGPU_DX11_REQUIRE_HARDWARE=1"
C:\dev\personal\MCHorizonUWP\.local\target\fast\horizon-client.exe
```

Verified live desktop execution:
- Adapter selected: `NVIDIA GeForce RTX 3090 Ti` (`feature_level=FL9_1`, `hardware=true`, `software=false`).
- Observed rendered output: title screen, 3D terrain geometry, translucent water, held items, and HUD.

### Regression Tests (Desktop, Forced Hardware `FL9_1`)

```cmd
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
set "WGPU_DX11_REQUIRE_HARDWARE=1"

cd /d C:\dev\personal\wgpu
cargo test -p wgpu --features dx11 --lib dx11
cargo test -p wgpu-hal --features dx11 --lib dx11::device

cd /d C:\dev\personal\MCHorizonUWP\.local\horizon-dx11
set "CARGO_TARGET_DIR=C:\dev\personal\MCHorizonUWP\.local\target"
cargo +stable test --offline -p horizon-client-render --lib fl91
cargo +stable test --offline -p horizon-client-render --lib shaders
cargo +stable test --offline -p horizon-client-render --lib graphics::tests
cargo +stable test --offline -p horizon-client-render --lib hud::tests
```

Observed results after final edits:
- `wgpu --features dx11 --lib dx11`: `1 passed; 0 failed` (`dx11_only_instance_initializes`)
- `wgpu-hal --features dx11 --lib dx11::device`: `2 passed; 0 failed`
- `horizon-client-render --lib fl91`: `3 passed; 0 failed`
- `horizon-client-render --lib shaders`: `16 passed; 0 failed`
- `horizon-client-render --lib graphics::tests`: `16 passed; 0 failed`
- `horizon-client-render --lib hud::tests`: `71 passed; 0 failed`

## UWP Build and Strict-Link Verification

Do **not** pass `store` to `vcvarsall.bat` when running Cargo for `x86_64-uwp-windows-msvc`: Cargo builds host `build.rs` binaries for `x86_64-pc-windows-msvc` and fails with `LNK2019: __imp_RoInitialize` if store libraries are in `LIB`. Use `build-game.cmd` for the UWP C++/CX compilation and strict link.

### x64 UWP (`x86_64-uwp-windows-msvc`)

```cmd
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
cd /d C:\dev\personal\MCHorizonUWP\.local\horizon-dx11
set "CARGO_BUILD_JOBS=32"
set "CL=/MP2"
set "RUSTC_BOOTSTRAP=1"
set "__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS=nightly"
set "__CARGO_TESTS_ONLY_SRC_ROOT=C:\Users\ben\.huwp\b\horizon-uwp-complete-20261104\rust-std\library"

cargo +stable fetch --manifest-path uwp\game-bridge\Cargo.toml --target x86_64-uwp-windows-msvc --target x86_64-pc-windows-msvc -Z build-std=std,panic_abort --locked
cargo +stable build --offline --locked --manifest-path uwp\game-bridge\Cargo.toml --features native-d3d11,uwp-dx11-renderer,uwp-dx12-renderer,native-touch-mod,native-device-vibration-mod,uwp-audio,uwp-https,uwp-controller,xaml-shell -Z build-std=std,panic_abort --target x86_64-uwp-windows-msvc --release --target-dir C:\dev\personal\MCHorizonUWP\.local\target-uwp

set "HORIZON_UWP_WGPU_D3D11=1"
set "HORIZON_UWP_DX12=1"
set "HORIZON_UWP_ANGLE=1"
set "HORIZON_UWP_NATIVE_D3D11=1"
set "HORIZON_UWP_DEFAULT_RENDERER=wgpu-dx11"
set "HORIZON_UWP_ACCOUNT=1"
set "HORIZON_UWP_XAML_SHELL=1"
set "HORIZON_UWP_XAUDIO=1"
set "HORIZON_UWP_HTTP=1"
set "HORIZON_UWP_TOUCH_MOD=1"
set "HORIZON_UWP_DEVICE_VIBRATION=1"
set "HORIZON_UWP_BUILD_DIR=C:\dev\personal\MCHorizonUWP\.local\uwp-build-x64"
set "HORIZON_UWP_GAME_LIB=C:\dev\personal\MCHorizonUWP\.local\target-uwp\x86_64-uwp-windows-msvc\release\horizon_uwp_game_bridge.lib"
call uwp\host\build-game.cmd x64
```

Observed output:
- Built `C:\dev\personal\MCHorizonUWP\.local\uwp-build-x64\HorizonUWP.exe` (`8664 machine (x64)`, `0x800000` / `8388608` byte stack reserve).
- `strict-game-link.ps1`: zero diagnostic sentinels in `HorizonUWP.exe.map`, zero unsupported optional static imports, and `dumpbin /dependents` shows no `d3d12.dll`, `d3dcompiler_47.dll`, `dxgi.dll`, or `user32.dll`.

### ARM32 UWP (`thumbv7a-uwp-windows-msvc`)

Run `python uwp\arm32-deps\prepare.py --offline-arm32` once before building ARM32 so `.local\arm32-windows-sys\patches-offline-arm32.toml` is generated (do **not** run simultaneous builds against `.local\target-uwp`). Because `--config patches-offline-arm32.toml` modifies `uwp\game-bridge\Cargo.lock` with `.local\arm32-windows-sys` paths, keep the unpatched lockfile clean when committing/syncing.

```cmd
call "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\VC\Auxiliary\Build\vcvarsall.bat" x64_arm store 10.0.15063.0
cd /d C:\dev\personal\MCHorizonUWP\.local\horizon-dx11
set "CARGO_BUILD_JOBS=32"
set "CL=/MP2"
set "RUSTC_BOOTSTRAP=1"
set "__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS=nightly"
set "__CARGO_TESTS_ONLY_SRC_ROOT=C:\Users\ben\.huwp\b\horizon-uwp-complete-20261104\rust-std\library"
set "CC_thumbv7a_uwp_windows_msvc=C:\Program Files\LLVM\bin\clang.exe"
set "CFLAGS_thumbv7a_uwp_windows_msvc=-DWINAPI_FAMILY=WINAPI_FAMILY_APP"

cargo +stable fetch --config .local\arm32-windows-sys\patches-offline-arm32.toml --manifest-path uwp\game-bridge\Cargo.toml --target thumbv7a-uwp-windows-msvc --target x86_64-pc-windows-msvc -Z build-std=std,panic_abort
cargo +stable build --config .local\arm32-windows-sys\patches-offline-arm32.toml --offline --manifest-path uwp\game-bridge\Cargo.toml --features native-d3d11,uwp-dx11-renderer,native-touch-mod,native-device-vibration-mod,uwp-audio,uwp-https,uwp-controller,xaml-shell -Z build-std=std,panic_abort --target thumbv7a-uwp-windows-msvc --release --target-dir C:\dev\personal\MCHorizonUWP\.local\target-uwp

set "HORIZON_UWP_WGPU_D3D11=1"
set "HORIZON_UWP_NATIVE_D3D11=1"
set "HORIZON_UWP_DEFAULT_RENDERER=wgpu-dx11"
set "HORIZON_UWP_ACCOUNT=1"
set "HORIZON_UWP_XAML_SHELL=1"
set "HORIZON_UWP_XAUDIO=1"
set "HORIZON_UWP_HTTP=1"
set "HORIZON_UWP_TOUCH_MOD=1"
set "HORIZON_UWP_DEVICE_VIBRATION=1"
set "HORIZON_UWP_BUILD_DIR=C:\dev\personal\MCHorizonUWP\.local\uwp-build-arm"
set "HORIZON_UWP_GAME_LIB=C:\dev\personal\MCHorizonUWP\.local\target-uwp\thumbv7a-uwp-windows-msvc\release\horizon_uwp_game_bridge.lib"
call uwp\host\build-game.cmd arm
```

Observed output:
- Built `C:\dev\personal\MCHorizonUWP\.local\uwp-build-arm\HorizonUWP.exe` (`1C4 machine (ARM)`, `32 bit word machine`, `0x800000` / `8388608` byte stack reserve).
- `strict-game-link.ps1`: zero diagnostic sentinels in `HorizonUWP.exe.map`.
- `dumpbin /imports` confirmed `NONE_FOUND` for `dxgi.dll`, `user32.dll`, `d3d12.dll`, `d3dcompiler_47.dll`, and `SetThreadErrorMode`.

### `d3dcompiler_47.dll` Runtime Resolution on UWP

Because `wgpu`'s DX11 backend compiles translated HLSL at runtime via `D3DCompile`, `wgpu-hal/src/dx11/shader.rs` probes `LoadPackagedLibrary("d3dcompiler_47.dll", 0)` first and falls back to `LoadLibraryExW("d3dcompiler_47.dll", 0, 0)` (matching `wgpu`'s DX12 backend). On desktop Windows 10/11 (x64, x86, ARM64), inbox `System32\d3dcompiler_47.dll` is present in the OS image; if a device image (such as Windows 10 Mobile ARM32) does not expose `d3dcompiler_47.dll` to packaged apps, an architecture-matching target DLL (`0x01c4` ARM32 PE binary, **not** the `0x8664` x64 cross-compiler host DLL in MSVC `bin\Hostx64\arm\`) would need to be packaged in the AppX root or tested on the target device first.

## Target Device Environments (Not Yet Physical-Device Verified)

> **Status**: Neither Windows 10 Mobile ARM32 nor Surface RT physical-device execution has been tested or verified at runtime.

### Windows 10 Mobile ARM32 (e.g., Lumia 950 XL)

- **OS / ABI**: Windows 10 Mobile UWP ARM32 (`thumbv7a-uwp-windows-msvc`, SDK `10.0.15063.0` / `WindowsApp.lib`), XAML `SwapChainPanel` (`ISwapChainPanelNative`, WinUI 2.x).
- **Compatibility fixes already applied in binaries**:
  - `SetThreadErrorMode` (missing on pre-16299 builds) is avoided by dynamic `LoadPackagedLibrary` / `LoadLibraryExW` resolution.
  - Static `dxgi.dll`, `user32.dll`, `d3d12.dll`, and `d3dcompiler_47.dll` imports are eliminated (`NONE_FOUND` under `dumpbin /imports`).
- **Outstanding runtime validation**:
  - Deploy the signed ARM32 APPX via Device Portal to the Lumia 950 XL and confirm whether inbox `d3dcompiler_47.dll` resolves under UWP (`LoadPackagedLibrary` / `LoadLibraryExW`).
  - Validate Adreno 430 (`FL9_1` / `9_3` hardware) shader compilation throughput (`D3DCompile`), `CreateSwapChainForComposition` presentation via `SwapChainPanel`, touch/controller input, and 3 GiB RAM / virtual memory headroom under real gameplay.

### Surface RT

- **Stock OS (`Windows RT 8.1 ARM32`)**: Stock Surface RT does **not** run Windows 10 UWP (`10.0.15063+`). Its environment differs substantially from Windows 10 Mobile ARM32:
  - **Missing Windows 10 APIs / contracts**: No `WindowsApp.lib` umbrella library, no `api-ms-win-core-synch-l1-2-0.dll` (`WaitOnAddress` / `WakeByAddressAll` introduced in Windows 8/10 sets differently under Store 8.1 rules), no Windows 10 `XAudio2_9.dll`, no Windows 10 `ISwapChainPanelNative` / WinUI 2.7 contracts, and MSVC `14.44` `*_APP.dll` / UCRT app sets target Windows 10 UWP rather than Windows Store 8.1.
  - **Hardware constraints**: NVIDIA Tegra 3 is native `FL9_1` (not `9_3`) with 2 GiB shared RAM and tighter instruction/constant/sampler limits than desktop or Adreno 430 GPUs.
  - **What is still required for stock Windows RT 8.1**: A dedicated Windows 8.1 Store SDK/toolchain host target (Windows 8.1 CRT, XAML host, XAudio 2.8, Win8.1 `std` compatibility, and jailbreak/dev-cert packaging), plus ARM32 `d3dcompiler_47.dll` packaging and physical Tegra 3 `FL9_1` shader/memory testing.
- *(If a Surface RT device is running an unofficial Windows 10 ARM32 Insider build instead of stock Windows RT 8.1, its UWP ABI is closer to Windows 10 ARM32, though Tegra 3 `FL9_1` hardware limits and 2 GiB RAM still require physical validation.)*
