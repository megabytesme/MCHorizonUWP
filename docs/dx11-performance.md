# x64 D3D11 gameplay performance

The comparison uses the NVIDIA GeForce RTX 3090 Ti on Ben-Desktop with driver `32.0.16.2101`, an AMD Ryzen 9 5950X, and Windows build `26300`. The primary display is configured at 3840×2160, 240 Hz, with 150% scaling. The workload uses a 3840×2040 physical framebuffer.

## Build provenance

| Client | Source and build |
| --- | --- |
| Native Horizon | `e797c725ad99ef311db216de7f41a2e6593353db`, `cargo +stable build --release --locked --offline -p horizon-client -p horizon-client-lib --bins`, D3D12 selected with `HORIZON_GRAPHICS_API=dx12` |
| UWP baseline | Client 0.1.5, patch 0.1.3, package `2026.1118.1038.43`, wgpu `4c24e58de1ee12b9dd3a81cc4490662353011c30` |

The UWP measurements use a side-by-side registration with bit-identical host and game binaries. The game bridge SHA-256 is `9a6d4996270d280c092c76b4692c4319402c1aed336eb9ddfd43cc65730720b8`. Native uses the upstream release profile with ThinLTO and one codegen unit. The UWP bridge uses its release profile with 32 codegen units and panic abort. The native scratch profile disables audio to follow upstream test guidance. These measurements include platform and build differences.

## Workload and capture

1. Start a private 26.3 Horizon dedicated server on `127.0.0.1:25567` from a scratch working directory with prepared resources, seed `1234`, creative mode, view distance 24, and simulation distance 12. Use the same server process and world for every pass. Keep the server listening on loopback and use an authorized package-scoped exemption for the scratch UWP client.
2. Set `gamerule minecraft:advance_time false`, `gamerule minecraft:advance_weather false`, `time set noon`, and `weather clear` in the server console. Load the column with `forceload add -960 80`, place a platform with `setblock -960 124 80 minecraft:stone`, and use `tp PLAYER -959.5 125 80.5 0 35`. This view includes village geometry, terrain, water, and naturally present entities. Run clients sequentially with the same offline player name. Entity movement remains a source of variation.
3. Use the same prepared resources and graphics options in fresh game directories. Use Ultra, 24-chunk render distance, 12-chunk simulation distance, FOV 70 (`fov:0.0`), no selected resource packs, `hdr:false`, native resolution scale, and windowed mode. Set `maxFps:260` (unlimited) and `inactivityFpsLimit:minimized`. Baseline UWP returns FIFO even with `enableVsync:false`. Set `enableVsync:true` on native Horizon and confirm the effective sync interval in the capture. Use a separate label for any uncapped presentation comparison.
4. Keep the game foregrounded, with chat, pause, and debug overlays closed. Wait for the loaded view to settle, and verify the adapter and actual framebuffer size in the renderer log. Winit launch size is in logical pixels. At 150% scaling, pass `--width 2560 --height 1360` for a 3840×2040 framebuffer.
5. Record three 60-second passes. Use separate process IDs and files. Collect per-thread CPU deltas over the timed window and GPU utilization alongside frame captures. Capture CPU stacks in a separate run to avoid distorting frame timing.

```powershell
& $presentMon --process_id $gamePid --timed 60 --terminate_after_timed `
    --no_console_stats --no_track_input --v1_metrics --output_file $capture
python tools/summarize_dxgi_capture.py $capture --pid $gamePid
```

Use an elevated console if ETW capture requires it. Select the sustained gameplay swap chain if a resize or renderer probe causes multiple chains. The summary reports application present cadence and display cadence separately. The 1% low is the reciprocal of the mean of the slowest 1% of frame intervals, with the sample count rounded up. `msGPUActive` measures attributed GPU work. Device-wide utilization comes from `nvidia-smi`. [PresentMon 2.6.0 console release](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0)

## Baseline results from 18 November 2026

All six passes used DXGI Composed Flip with a sync interval of 1, flags 0 and tearing disabled. The UWP run used a hardware FL11_1 device with Horizon's FL9_1 compatibility renderer. Native Horizon used D3D12 with multi-draw and occlusion culling active. Both rendered the same live village with terrain, water, entities, and HUD visible.

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

The candidate rendered terrain, HUD, entities, textures and water after resizing from 1779×1040 to 3840×2040. A separate installed run at 1779×1040 logged actual FL9_1 on the physical RTX, with `software=false`, and joined the same world. Neither run logged device-loss or shader-validation errors. This covers normal gameplay and resize on the test GPU. It leaves forced device removal and execution on legacy hardware for separate validation.

The post-change CPU trace used the candidate linker map to resolve bridge addresses. Its hottest sampled call sites included `NativeDevice::map_buffer_shadow` allocation and zeroing, `BufferCpuShadow::copy_into_clean`, `BufferCpuShadow::write`, and driver work during `sync_buffer_shadow`. A smaller general upload belt is the next experiment. The section upload belt and the rendering options stay at their existing sizes and settings for that experiment.

### Host kit correction

The first x64 relink exited at activation with `0xC000007B`. The prebuilt MSVC TLS object had overlapping 64-bit relocations in a 32-bit directory layout. The existing Builder regenerated the object with Clang, which explains why the installed package launched. The host kit source now uses `_WIN64` for the native pointer width. The strict-link script also checks the directory payload against the PE image, and was checked against the original bridge, the corrected bridge, and the malformed artifact.
