# Desktop graphics on Ben-Desktop, 20 November 2026

The isolated ANGLE x64 APPX joined the same multiplayer world as the native baseline. The game rendered terrain and HUD through hardware OpenGL ES 3.0 on the NVIDIA GeForce RTX 3090 Ti. The game log identifies ANGLE 2.1.7258, revision `d9fc4a372074`, and Direct3D 11 Shader Model 5.0 underneath it. EGL selected a window-presentable configuration. The wgpu log identifies `backend=Gl`, `hardware=true` and `software=false`. The screenshot and server log confirm that the GLES-backed renderer drew gameplay and the package presented it in the UWP window. [Renderer evidence](../deliverables/nightly-2026-11-20/desktop-graphics/angle-game.log) · [Village screenshot](../deliverables/nightly-2026-11-20/desktop-graphics/angle-parity-probe.png) · [Server evidence](../deliverables/nightly-2026-11-20/desktop-graphics/angle-join-and-camera.log)

## Test conditions

| Input | Setting |
| --- | --- |
| Computer | AMD Ryzen 9 5950X, 16 cores / 32 logical, Windows 11 Pro build 26300 |
| GPU and driver | GeForce RTX 3090 Ti, NVIDIA 621.01 |
| Horizon revision | `0b14f8acfd97fa2c750e42795249dcc981178ca4` |
| Resources | Neutral framework `Horizon.Asset.26-3.98ccbead789d397639d5badd0bb805` |
| UWP package identities | `MCHorizonUWP.NightlyGraphics` 1.0.0.0, `MCHorizonUWP.NightlyGraphicsANGLE` 1.0.1.0 |
| Window and display | Foreground windowed, 1800×1350 SDR render surface. Current primary desktop 3240×2160 at 60 Hz |
| Game setup | Scratch multiplayer world `HorizonParity`, seed 1234, creative, 24-chunk render distance and 12-chunk simulation distance, fixed noon and clear weather |
| Camera | `(-959.5, 125, 80.5)`, yaw 0°, pitch 35° |
| Graphics | 100% native resolution, VSync on, FIFO requested, `maxFps:260` (unlimited at this revision), dynamic resolution and frame generation off |
| Warmup and overlay | 45 seconds after the server teleports the player. Tutorial and diagnostic overlays disabled |

The 60 Hz display mode is the mode observed during this test. The 19 November background experiments ran with a 240 Hz primary display and are outside this comparison. The final runs used the same source revision, assets, camera, configured distances and present interval. The screenshots show the same terrain and HUD. The server permitted existing mobs to move and their positions varied between runs. The native and UWP build configurations enable different renderer features. This measures the shipped paths as configured and does not isolate AppContainer overhead. [Original graphics preflight](nightly-graphics-2026-11-19.md) · [Display evidence](../deliverables/nightly-2026-11-20/desktop-graphics/display.txt)

## Frame measurement

PresentMon 2.6.0 returned successful start and stop status without a CSV for the ANGLE PID, an all-process session, an elevated session within the active interactive desktop, and the native DX12 pilot. Windows' raw DXGI provider did record 487 native DX12 Present start/stop pairs from the exact live game PID over an 8-second pilot. The inspected native events use sync interval 1, flags 0. The initial native and UWP DX12 surface creation logs say Mailbox. The pinned renderer calls `set_present_mode` again when it adopts the saved world settings, so steady-state DXGI fields are the check for VSync in this run. [PresentMon release](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0)

DXGI event 42 identifies the application Present start and includes the swap chain, its flags and sync interval. A series of these events measures how regularly the app calls Present. It cannot establish display cadence, compositor mode, dropped frames or per-frame GPU work. NVIDIA utilisation is whole-device activity. Process CPU and memory snapshots cover the timed recording and recorder setup and teardown. [PresentMon DXGI event definition](https://github.com/GameTechDev/PresentMon/blob/main/PresentData/PresentMonTraceConsumer.cpp)


## Recorded application present cadence

Each row gives the median of three 60-second runs. Each of the 12 samples used one game swap chain throughout, with sync interval 1 and flags 0. All four renderers submitted at about 60 presents per second on the 60 Hz display.

| Renderer | Mean app FPS | Median ms | P95 ms | P99 ms | 1% low FPS | CPU (% of one core) | GPU utilisation (%) | Working set / private MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Native DX12 | 60.00 | 16.64 | 19.51 | 19.89 | 49.80 | 18.38 | 20.0 | 740 / 1,809 |
| UWP DX12 | 60.00 | 16.70 | 19.49 | 19.91 | 49.78 | 20.76 | 20.0 | 817 / 1,615 |
| UWP DX11 | 60.00 | 16.60 | 17.72 | 18.09 | 54.51 | 27.98 | 20.0 | 1,617 / 2,707 |
| UWP ANGLE GLES3 | 59.86 | 16.64 | 18.49 | 19.09 | 43.70 | 42.97 | 25.0 | 1,620 / 2,572 |

The 1% low is 1,000 divided by the mean of the slowest 1% of app present intervals. GPU percentage is the median of whole-device samples at one-second intervals. The UWP DX11 log reports requested FL9_1 on physical hardware L11_1. The DX11 captures had the lowest P95 and P99 for this setting. ANGLE used more process CPU than the other paths under the measured load. The CPU, GPU and memory observations apply to this scene with VSync and each backend's enabled features. They cannot establish an uncapped throughput ceiling, compositor behaviour, dropped-frame rates, GPU execution bottlenecks or a general performance winner. [Selected raw-metric summaries](../deliverables/nightly-2026-11-20/desktop-graphics/selected-matrix.json) · [UWP DX11 renderer log](../deliverables/nightly-2026-11-20/desktop-graphics/uwp-dx11-game.log)

A Windows notification overlapped the earlier UWP DX11 and DX12 pre-capture screenshots. I excluded those samples from the table, moved the isolated test window away from the notification for the replacement runs, verified its size and restored its original position. Selected pre-timing screenshots showed the game area clear. The final DXGI samples do not identify which other surfaces the Windows compositor drew. The scene images crop the window edge and lower foreground area. [Native DX12](../deliverables/nightly-2026-11-20/desktop-graphics/dxgi-native-dx12-20261120-132924-scene-crop.png) · [UWP DX12](../deliverables/nightly-2026-11-20/desktop-graphics/dxgi-uwp-dx12-20261120-143608-scene-crop.png) · [UWP DX11](../deliverables/nightly-2026-11-20/desktop-graphics/dxgi-uwp-dx11-20261120-144024-scene-crop.png) · [UWP ANGLE](../deliverables/nightly-2026-11-20/desktop-graphics/dxgi-uwp-angle-20261120-120857-scene-crop.png)

## Reproducing the capture

The build and test scratch directory was `%TEMP%\horizon-nightly-graphics-20261119-123308`. The preflight command was `tools/prepare_nightly_graphics.ps1 -ScratchRoot <scratch> -Prepare`. I disabled the first-run movement tutorial in every test profile and used the pinned native client as the scratch multiplayer server. The isolated UWP packages joined through the launcher using Direct connect. The modern package's renderer was selected through the Windows ApplicationData settings API and checked against its game log.

The recorder ran `logman start <session> -ets -p Microsoft-Windows-DXGI 0x2 5 -o <file.etl> -f bincirc -max 16` and stopped the trace after 60 seconds. It exported event 42 via `Get-WinEvent -Path <file.etl> -Oldest`, filtered to the exact game PID and kept each chain separate. Before and after each group, the server confirmed the camera position and rotation. Foreground checks bracketed each pass. The server used the same save throughout. [Evidence directory](../deliverables/nightly-2026-11-20/desktop-graphics)
