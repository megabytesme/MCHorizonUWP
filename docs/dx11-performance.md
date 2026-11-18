# x64 D3D11 gameplay performance

The comparison uses the NVIDIA GeForce RTX 3090 Ti on Ben-Desktop with driver `32.0.16.2101`, an AMD Ryzen 9 5950X, and Windows build `26300`. The primary display is configured at 3840×2160, 240 Hz, with 150% scaling. The workload uses a 3840×2040 physical framebuffer.

## Build provenance

| Client | Source and build |
| --- | --- |
| Native Horizon | `e797c725ad99ef311db216de7f41a2e6593353db`, `cargo +stable build --release --locked --offline -p horizon-client -p horizon-client-lib --bins`, D3D12 selected with `HORIZON_GRAPHICS_API=dx12` |
| UWP baseline | Client 0.1.5, patch 0.1.3, package `2026.1118.1038.43`, wgpu `4c24e58de1ee12b9dd3a81cc4490662353011c30` |

The UWP measurements use a side-by-side registration with bit-identical host and game binaries. The game bridge SHA-256 is `9a6d4996270d280c092c76b4692c4319402c1aed336eb9ddfd43cc65730720b8`. Native uses the upstream release profile with ThinLTO and one codegen unit. Its executable SHA-256 is `2179ee49af062e93cf408201001fd1deb87a3f487a11e81e6aff6b52dc2630f5`. The UWP bridge uses its release profile with 32 codegen units and panic abort. The native scratch profile disables audio to follow upstream test guidance. These measurements include platform and build differences.

## Workload and capture

1. Start a private 26.3 Horizon dedicated server on `127.0.0.1:25567` from a scratch working directory with prepared resources, seed `1234`, creative mode, view distance 24, and simulation distance 12. Use the same server process and world for every pass. Keep the server listening on loopback and use an authorized package-scoped exemption for the scratch UWP client.
2. Set `gamerule minecraft:advance_time false`, `gamerule minecraft:advance_weather false`, `time set noon`, and `weather clear` in the server console. Load the column with `forceload add -960 80`, place a platform with `setblock -960 124 80 minecraft:stone`, and use `tp PLAYER -959.5 125 80.5 0 35`. This view includes village geometry, terrain, water, and naturally present entities. Run clients sequentially with the same offline player name. Entity movement remains a source of variation.
3. Use the same prepared resources and graphics options in fresh game directories. Use Ultra, 24-chunk render distance, 12-chunk simulation distance, FOV 70 (`fov:0.0`), no selected resource packs, `hdr:false`, native resolution scale, and windowed mode. Set `maxFps:260` (unlimited) and `inactivityFpsLimit:minimized`. Baseline UWP returns FIFO even with `enableVsync:false`. Set `enableVsync:true` on native Horizon and confirm the effective sync interval in the capture. Use a separate label for any uncapped presentation comparison.
4. Keep the game foregrounded, with chat, pause, and debug overlays closed. Wait for the loaded view to settle, and verify the adapter and actual framebuffer size in the renderer log. Winit launch size is in logical pixels. At 150% scaling, pass `--width 2560 --height 1360` for a 3840×2040 framebuffer.
5. Record three 60-second passes. Use separate process IDs and files. Collect per-thread CPU deltas over the timed window and GPU utilization alongside frame captures. Capture CPU stacks in a separate run to avoid distorting frame timing.

The dedicated server was Horizon 0.1.0 using the 26.3 protocol. The existing server executable used on Ben-Desktop had SHA-256 `41546e6bc92780c74f37390662284f0110c8a1959d58469a078eee0212999660`. Its scratch `server.properties` set `server-ip=127.0.0.1`, `online-mode=false`, `gamemode=creative`, `level-seed=1234`, `view-distance=24`, and `simulation-distance=12`.

```powershell
& $serverExe --resources $resources --dedicated --nogui --port 25567 `
    --world HorizonParity --universe $serverDir
```

```powershell
$env:HORIZON_GRAPHICS_API = 'dx12'
$env:HORIZON_NO_AUDIO = '1'
$env:HORIZON_TICK_SCRIPT = $sceneFile
'{"name":"horizon-parity-village","ticks":12000,"steps":{"0":["close","look 0 35"]}}' |
    Set-Content -Encoding ascii $sceneFile
& $nativeExe --resources $resources --game-dir $gameDir --server 127.0.0.1:25567 `
    --name Player96331 --control-socket $control --width 2560 --height 1360
```

Set the native game's VSync with `horizon-ctl --socket $control options-set enableVsync true`. Once it joins, create the scene file's sibling with the `.go` extension to start scripted input, then let the scene settle. Join via Direct Connect in the UWP client and reset the camera with the server teleport command before each warmed session.

```powershell
& $presentMon --process_id $gamePid --timed 60 --terminate_after_timed `
    --no_console_stats --no_track_input --v1_metrics --output_file $capture
python tools/summarize_dxgi_capture.py $capture --pid $gamePid
```

Use an elevated console if ETW capture requires it. Select the sustained gameplay swap chain if a resize or renderer probe causes multiple chains. The summary reports application present cadence and display cadence separately. The 1% low is the reciprocal of the mean of the slowest 1% of frame intervals, with the sample count rounded up. `msGPUActive` measures attributed GPU work. Device-wide utilization comes from `nvidia-smi`. [PresentMon 2.6.0 console release](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0)

## Baseline results from 18 November 2026

All six passes used DXGI Composed Flip with a sync interval of 1, flags 0 and tearing disabled. The UWP run used a hardware FL11_1 device with Horizon's FL9_1 compatibility renderer. Native Horizon used D3D12 with multi-draw and occlusion culling active. An independent D3D12 device query reported FL12_2 as the adapter's maximum. Both clients rendered the same live village with terrain, water, entities, and HUD visible.

| Client and pass | Avg FPS | Median FPS | 1% low FPS | p50 ms | p95 ms | p99 ms | p50 GPU active ms | Avg displayed FPS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| UWP 1 | 55.40 | 58.72 | 25.84 | 17.03 | 27.30 | 34.19 | 3.85 | 54.39 |
| UWP 2 | 58.35 | 60.07 | 26.39 | 16.65 | 24.34 | 32.25 | 3.74 | 58.04 |
| UWP 3 | 56.24 | 59.15 | 19.29 | 16.91 | 30.69 | 44.85 | 3.12 | 56.06 |
| Native 1 | 238.15 | 242.90 | 107.52 | 4.12 | 7.44 | 8.78 | 1.81 | 227.88 |
| Native 2 | 237.82 | 243.34 | 107.35 | 4.11 | 7.55 | 8.80 | 1.79 | 226.68 |
| Native 3 | 237.70 | 243.05 | 106.72 | 4.11 | 7.54 | 8.74 | 1.32 | 227.61 |

The median of the per-pass medians was 16.91 ms for UWP and 4.11 ms for native. The UWP baseline took 4.11 times as long per frame. UWP process CPU was about 3.0–3.2% of the 32 logical processors and its busiest thread used 81–83% of one logical processor. Native was about 2.0–2.1% overall and 62–65% on its busiest thread. UWP device-wide GPU utilization averaged 26–29%, compared with 36–39% for native. UWP's Present API call took about 0.11–0.12 ms median. The gap is visible in application frame timing and in frames reaching the display.

The native runs held camera and input steady with `HORIZON_TICK_SCRIPT`. The three timed captures recorded zero user inputs. An earlier native pass with chat input and Cemu activity was omitted. UWP and native both requested SDR in their settings, but native logged HDR10 during initial surface setup before applying its saved graphics configuration. Runtime screenshots show the same scene and visible features with some rendering differences. The two binaries use different compilation profiles and backend capability paths.

## CPU profiling

The exploratory CPU trace used `xperf -on PROC_THREAD+LOADER+PROFILE -stackwalk Profile`, `xperf -d <trace.etl>`, then `xperf -i <trace.etl> -a profile -detail`. Take this trace on the same warmed view. Leave one 3D client active during comparative runs.

The trace attributed 28.3% of UWP CPU samples to the game bridge, 22.7% to the C runtime, 17.3% to the kernel and 12.7% to the NVIDIA user-mode driver. Disassembly at the sampled C runtime addresses showed bulk copies and fills. The trace overlapped other workloads and lacks symbols for the bridge, so the module shares describe the profiled process and cannot isolate the cost of an individual Rust function.

Horizon remaps whole staging-belt chunks for writes after submission. The general frame belt starts at 1 MiB. Core invalidates noncoherent mappings, and the D3D11 HAL creates a readback buffer, submits a GPU copy, waits in `Map(D3D11_MAP_READ)`, and copies the results back to the mapping. The existing CPU shadow already retains the bytes for clean upload buffers. This makes the remap path an A/B candidate even in a scene that uploads little data each frame.

## Reuse the upload buffer CPU shadow

The HAL patch copies the requested mapped range from the clean CPU shadow for `MAP_WRITE | COPY_SRC` buffers, including the internal `COPY_DST` flag that core can add for initialization. Dirty shadows retain GPU readback. The hardware regression checks clipped ranges, partial flushes, preservation across remaps, and a dirty-shadow readback. It ran on the physical RTX 3090 Ti at FL11_1 and forced FL9_1. [HAL patch](../patches/wgpu-dx11/reuse-upload-buffer-shadow.patch)

| Candidate pass | Avg FPS | Median FPS | 1% low FPS | p50 ms | p95 ms | p99 ms | p50 GPU active ms | Avg displayed FPS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| UWP 1 | 112.72 | 115.63 | 72.32 | 8.65 | 11.46 | 12.98 | 2.20 | 112.68 |
| UWP 2 | 96.46 | 99.33 | 58.84 | 10.07 | 14.31 | 15.35 | 2.60 | 94.27 |
| UWP 3 | 88.91 | 91.66 | 58.44 | 10.91 | 14.80 | 16.05 | 2.95 | 83.59 |

The median of the three per-pass median frame times fell by **40.5%**, from 16.91 to 10.07 ms. The median p99 fell from 34.19 to 15.35 ms. The median 1% low rose from 25.84 to 58.84 FPS. Native's 4.11 ms median leaves a 2.45× gap. The candidate still used about 80–81% of one logical processor on its busiest thread, with roughly 38% device-wide GPU utilization. Its frame output varied across runs, including 2, 131, and 319 dropped presents.

The three baseline UWP passes contained 17 application frame intervals beyond 50 ms, with a worst interval of 73.33 ms. The candidate's worst interval was 37.43 ms. The worst native interval was 13.69 ms.

The candidate rendered terrain, HUD, entities, textures and water after resizing from 1779×1040 to 3840×2040. A separate installed run at 1779×1040 logged actual FL9_1 on the physical RTX, with `software=false`, and joined the same world. That compatibility run recorded 69.24 ms median and 82.71 ms p99. Both modes logged normal gameplay and resize with zero device-loss or shader-validation errors. Forced device removal and execution on legacy hardware still require separate validation.

The post-change CPU trace used the candidate linker map to resolve bridge addresses. Its hottest sampled call sites included `NativeDevice::map_buffer_shadow` allocation and zeroing, `BufferCpuShadow::copy_into_clean`, `BufferCpuShadow::write`, and driver work during `sync_buffer_shadow`. The median baseline UWP Present call itself took 0.12 ms. Native achieved its 4.11 ms median with the same sync interval. The evidence points to work before presentation as the priority for this scene.

The D3D11 SwapChainPanel path uses a two-buffer flip-sequential composition swap chain and attempts to use a waitable frame-latency object with maximum latency 1. It retries without that flag when DXGI rejects it for composition. The HWND path creates the swap chain directly for the window. The captures establish composition and present flags, while the source establishes the waitable-object fallback. The captures do not identify which branch ran or isolate time waiting in acquire.

### D3D11 capability limit

The RTX ran the modern hardware device at FL11_1. A separate HAL test exercised a 16,384-pixel texture limit and native 32-bit indexed draws on that device. Horizon's `Fl91Caps::from_adapter` still selects the compatibility renderer for every D3D11 adapter. It uses sprite sheets, compatibility shaders, and CPU fallbacks for sprite animation and entity pose work. The fork's adapter also advertises an FL9_1 capability projection. Its command encoder rejects indirect draws and compute dispatch, which rules out enabling those paths by changing capability flags alone. Native logged multi-draw and occlusion culling on. UWP logged both off. The remaining cost combines these renderer differences with the CPU upload and submission work observed in the trace.

For a diagnostic comparison, native D3D12 reran with `HORIZON_HDR_OUTPUT=sdr`, then with `HORIZON_GPU_CULL=0` as well. The native log confirmed the occlusion state and the same 3840×2040 BGRA8 framebuffer. Multi-draw stayed active. PresentMon recorded the same effective sync interval 1 for all four passes.

| Native SDR pass | Avg FPS | p50 ms | p95 ms | p99 ms | App mean GPU ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Occlusion on 1 | 236.24 | 4.13 | 7.38 | 8.65 | 0.88 |
| Occlusion on 2 | 238.21 | 4.12 | 7.41 | 8.67 | 0.95 |
| Occlusion off 1 | 238.15 | 4.06 | 7.99 | 9.12 | 0.77 |
| Occlusion off 2 | 238.23 | 4.09 | 7.77 | 8.81 | 0.79 |

Forcing SDR or disabling native occlusion alone barely changed the native median in this view near the display refresh limit. Disabling occlusion worsened the frame-time tails slightly while reducing instrumented GPU time. The comparison leaves CPU upload and submission overhead, and the wider renderer capability gap, as the next areas to isolate.

### Smaller upload belt experiment

A scratch-only switch changed the D3D11 general upload belt from 1 MiB to 64 KiB. Both settings ran from the same bridge binary. The section belt, render settings, and behaviour of oversized individual writes stayed the same. Other workloads that could interfere with the timed runs were checked at the beginning and end of each pass.

| General belt and pass | Avg FPS | 1% low FPS | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 MiB 1 | 110.97 | 67.92 | 8.87 | 12.31 | 13.93 |
| 1 MiB 2 | 102.46 | 65.89 | 9.77 | 13.09 | 14.36 |
| 64 KiB 1 | 87.01 | 55.92 | 11.37 | 14.93 | 16.15 |
| 64 KiB 2 | 82.62 | 54.70 | 12.21 | 15.14 | 16.67 |
| 64 KiB 3 | 83.24 | 57.27 | 12.15 | 15.01 | 16.19 |

The smaller belt regressed both median frame time and frame-time tails. The candidate keeps the 1 MiB belt.

### Reuse the mapping allocation experiment

A second scratch switch retained one backing allocation per upload buffer on FL11+ and zeroed it before the next mapping. Both settings used the same bridge with a 1 MiB belt. The focused hardware regression verified allocation reuse, zeroing, and preservation of bytes after a partial flush. Control and reuse sessions were interleaved.

| Allocation and pass | Avg FPS | 1% low FPS | p50 ms | p95 ms | p99 ms | Private MiB at end |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Control 1 | 116.79 | 73.64 | 8.54 | 11.31 | 12.59 | 2736.5 |
| Control 2 | 106.96 | 66.33 | 9.24 | 12.74 | 14.01 | 2730.4 |
| Reuse 1 | 112.79 | 72.11 | 8.63 | 11.79 | 13.18 | 2744.5 |
| Reuse 2 | 112.47 | 66.84 | 8.65 | 11.95 | 13.47 | 2745.6 |
| Reuse 3 | 115.79 | 68.36 | 8.45 | 11.20 | 13.11 | 2760.8 |

The overlapping frame-time ranges and higher private memory did not justify keeping the allocation cache. The signed candidate retains the clean-shadow optimisation alone.

### Host kit correction

The first x64 relink exited at activation with `0xC000007B`. The prebuilt MSVC TLS object had overlapping 64-bit relocations in a 32-bit directory layout. The existing Builder regenerated the object with Clang, which explains why the installed package launched. The host kit source now uses `_WIN64` for the native pointer width. The strict-link script also checks the directory payload against the PE image, and was checked against the original bridge, the corrected bridge, and the malformed artifact.

### Reproduce the candidate

Apply `patches/wgpu-dx11/reuse-upload-buffer-shadow.patch` to a private checkout of wgpu `4c24e58de1ee12b9dd3a81cc4490662353011c30`. Stage upstream Horizon `e797c725ad99ef311db216de7f41a2e6593353db` with `horizon-uwp-0.1.3.patch` and the Client 0.1.5 host kit. Point the staged bridge's wgpu-family overrides at that private checkout and regenerate its scratch `Cargo.lock`. The release catalog continues to pin its original inputs.

Use the Builder toolchain environment with Rust `1.99.1`, Visual C++ `14.44.35207`, SDK `10.0.19041.0`, and Clang. Run `uwp/rust-std/prepare-source.ps1` against a private Rust source copy, then set `RUSTC_BOOTSTRAP=1`, `__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS=nightly`, and `__CARGO_TESTS_ONLY_SRC_ROOT` to the patched `library` directory. Build the bridge from the staged Horizon source.

```powershell
$features = 'native-d3d11,uwp-dx11-renderer,uwp-dx12-renderer,native-touch-mod,native-device-vibration-mod,uwp-audio,uwp-https,uwp-controller,xaml-shell'
cargo +1.99.1 build --offline --manifest-path uwp/game-bridge/Cargo.toml `
    --features $features -Z build-std=std,panic_abort `
    --target x86_64-uwp-windows-msvc --release --target-dir $targetDir
```

Generate the TLS shim from the corrected `tls-index.c`, then run `uwp/host/strict-bridge-link.ps1` with the matching host kit, Rust LLD, SDK and MSVC roots. The 18 November x64 build took 10m 53s. After relinking, the tested game bridge hash was `68c479c88c90784462409044d40f0770df7625d025afebc11f9977c2ac8fbdd3`.

The benchmark bridge sets `WGPU_DX11_FORCE_FL9_1=1` and `WGPU_DX11_REQUIRE_HARDWARE=1` inside the UWP process when `LocalState\game\strict-dx11-bench.enabled` exists. It also accepts `LocalState\game\performance-trace.enabled` to direct `HORIZON_TRACE` to `performance-trace.json` in that directory. Both markers are optional and scoped to the scratch package's data.

From a Visual Studio developer shell in the patched wgpu checkout, use `WGPU_DX11_REQUIRE_HARDWARE=1` and run the focused test with `cargo +1.93.1 test --offline -p wgpu-hal --lib --no-default-features --features dx11 upload_mapping_uses_clean_shadow_and_falls_back_when_dirty -- --nocapture`. Repeat with `WGPU_DX11_FORCE_FL9_1=1` and verify the printed device level. The public regression is `public_dx11_async_write_map_updates_nonzero_slice_at_fl91` in the `dx11_public` test target of `wgpu` with `--no-default-features --features dx11,wgsl,std`.

The full strict HAL run passed 555 cases. The public suite passed 175 of 176, with `public_dx11_inter_stage_varying_limit_at_fl91` expecting eight varying variables while the adapter advertises seven. The same assertion fails on the pristine `4c24e58de` checkout on this GPU. The upload remap regression passes in both modes, and the Builder's setup checker tests pass.

## Local packages

| Architecture | Cargo target | Release build | Package |
| --- | --- | ---: | --- |
| x64 | `x86_64-uwp-windows-msvc` | 10m 53s | `MCHorizonUWP-Performance-x64.appx` |
| x86 | `i686-uwp-windows-msvc` | 13m 37s | `MCHorizonUWP-Performance-x86.appx` |
| ARM64 | `aarch64-uwp-windows-msvc` | 13m 52s | `MCHorizonUWP-Performance-arm64.appx` |
| ARM32 | `thumbv7a-uwp-windows-msvc` | 13m 37s | `MCHorizonUWP-Performance-arm.appx` |

The cross-builds used separate staged source and target directories. Use the matching Rust target triple, architecture-specific Clang and LLVM librarian settings, and the native host kit for each strict link. For ARM32, run `python uwp/arm32-deps/prepare.py --offline-arm32` and pass `--config .local/arm32-windows-sys/patches-offline-arm32.toml` to Cargo. Every bridge passed strict linking. The local packages use the separate identity `MCHorizonUWP.PerfCandidate` and passed `signtool verify /pa`. Their hashes and the benchmark data are in the ignored `deliverables/performance-2026-11-18` directory.

Deploy with the existing Client 0.1.5 dependencies for the selected architecture and the local signing certificate trusted on the test device. The manifests require the same Horizon.Asset 26.3 framework, Microsoft.VCLibs 14.0, and Microsoft.UI.Xaml 2.7 used by the source host kit. The local candidate packages share one package identity across architectures.

The Builder verified the checked-in signed catalog and its patch 0.1.2 and client 0.1.4 combination on four architectures. Patch 0.1.3 with client 0.1.5 was already outside that checked-in catalog's release rules. The local performance packages were validated separately with strict linking, APPX signature verification, and recorded SHA-256 hashes.

The signed builds contain the verified HAL remap optimisation. The x64 APPX contains the same bridge bytes used for the installed gameplay and resize tests. Both signed x64 and x86 APPXs installed on Ben-Desktop, passed the D3D11 render probe, and presented the game title on the physical RTX 3090 Ti. The x86 game logged an access-denied warning when writing the optional prepared-data cache, then presented its first frame and started audio. The x86 multiplayer workload and runtime on Surface Pro X and Lumia remain outstanding. Stock Windows RT 8.1 remains a separate target.
