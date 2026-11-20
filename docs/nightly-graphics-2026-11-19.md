# Nightly graphics validation, 19 November 2026

Horizon `0b14f8acfd97fa2c750e42795249dcc981178ca4` was the comparison baseline. The UWP test reused the strict-linked x64 bridge from product commit `25e508f8a69cb4524a183a9ce91ed6e399f85c56`, built with the wgpu fork at `4c24e58de1ee12b9dd3a81cc4490662353011c30` and the recorded upload-shadow patch.

| Test | Native Horizon DX12 | UWP DX12 | UWP wgpu D3D11 | UWP ANGLE GLES3 |
| --- | --- | --- | --- | --- |
| Status | Gameplay verified | Gameplay verified | Gameplay verified | Signed x64 test package installed |
| Runtime | Exact Nightly built and joined the scratch server on hardware DX12 | Signed Nightly rendered the title, a new survival world, and the multiplayer world on hardware DX12 | Earlier signed Nightly gameplay verified on the same RTX 3090 Ti | ANGLE-enabled host strict-linked. Installed x64 EGL, GLES and zlib files verified. Activation and game rendering pending. |
| Capture | Three background diagnostics, unsuitable for comparison | Three background diagnostics with the game menu and F3 overlay visible, unsuitable for comparison | Earlier Nightly FIFO capture, unsuitable for comparison | Pending hardware GLES3 gameplay |

## UWP DX12 on Ben-Desktop

The isolated `MCHorizonUWP.NightlyGraphics` x64 APPX passed `signtool verify /pa` and installed. Its SHA-256 is `4aa1b55708e8bcea2607bf391d156fe90f7744cf2122095906271e6a7d87cac5`. The bridge SHA-256 is `05b14fee7cb59c84a87a0bf72ce2d24b80adc869adccc515d865507b77c2cf4d`, identical to the source package's strict-linked bridge. Only the test package identity and display names changed.

The XAML shell's probe created the DX12 instance, selected the adapter, requested a device, configured the panel surface, acquired a frame and presented it. Selecting Direct3D 12 in the shell then started renderer 2. The game logged `backend=Dx12`, `device_type=DiscreteGpu`, `hardware=true`, and `software=false` for the GeForce RTX 3090 Ti. It configured a 1800×1350 `Bgra8Unorm` Mailbox surface, reported its first game frame, and displayed the Nightly 2026-11-18 title menu. A fresh local survival world rendered its bamboo terrain, blocks, clouds, player hand and HUD. The same package also joined the exact dedicated server. Its multiplayer screen and server log both confirmed the camera position and rotation. The images, raw captures and complete logs are in `deliverables/nightly-2026-11-18/graphics/`.

## Desktop reference and measurement conditions

The native client and dedicated server use the same executable from a clean detached worktree at `0b14f8acfd97fa2c750e42795249dcc981178ca4`. Build directory was `%TEMP%/horizon-nightly-graphics-20261119-123308/source` with a separate `target` directory under the same scratch root. The build used the VS 2022 x64 developer environment, SDK 10.0.19041.0, Rust LLD and `cargo +1.99.1-x86_64-pc-windows-msvc build --locked --offline --profile fast -p horizon-game --bin horizon-client --no-default-features`. The recovered build passed after recreating missing Cargo fingerprint directories in the isolated target. Its executable SHA-256 is `089a49e06d4103aeb8ad8006e39a85b36997293e86ff205f10174e71959522dd`. The UWP bridge uses its own release profile, UWP standard library and platform features.

The dedicated server bound to `127.0.0.1:25567` and used a fresh creative seed-1234 world with view and simulation distances 24 and 12. Time was fixed at noon and the weather was clear. Each client used the same prepared resources, camera position `(-959.5, 125, 80.5)`, yaw `0`, pitch `35`, view and simulation distances 24 and 12, and a 1800×1350 SDR viewport at 100% resolution scale. Native's logical viewport was 1200×900 at 150% desktop scaling. The configured frame cap of 260 means unlimited in this build. VSync was disabled. Both backends configured Mailbox when asked for Immediate. The display configuration was 3840×2160 at 240 Hz on the primary display and 3840×2160 at 60 Hz on the secondary display. [PresentMon 2.6.0](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0)

## Background capture results

Each pass observed 60 seconds of game-process presents with PresentMon 2.6.0. Each sustained swap chain reported `Composed: Flip` and sync interval `0`. The desktop remained available. Other foreground apps and composition workloads changed between passes. The UWP game opened its pause menu when focus changed and its screenshot also shows the F3 overlay. Native used its control-socket launch path to create a window without taking focus. Compare within these observations only for diagnostic purposes. A matched gameplay rerun needs a visible, unpaused UWP session and a stable composition workload. The exact Nightly supports `pauseOnLostFocus:false` in the isolated `options.txt`. Applying it to a subsequent UWP run requires activating that package again.

| Pass | Avg FPS | Median FPS | 1% low FPS | p50 ms | p95 ms | p99 ms | Max ms | Frames >33.33 ms | Dropped / presents |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Native DX12 1 | 163.32 | 233.51 | 36.51 | 4.28 | 15.94 | 22.10 | 64.17 | 8 | 1,500 / 9,798 |
| Native DX12 2 | 194.57 | 258.28 | 41.29 | 3.87 | 13.23 | 19.04 | 54.71 | 10 | 2,558 / 11,669 |
| Native DX12 3 | 285.03 | 388.69 | 51.44 | 2.57 | 8.51 | 16.06 | 40.97 | 1 | 6,320 / 17,096 |
| UWP DX12 1 | 187.70 | 204.14 | 87.05 | 4.90 | 8.65 | 10.43 | 16.60 | 0 | 7,838 / 11,254 |
| UWP DX12 2 | 187.65 | 202.93 | 89.93 | 4.93 | 8.71 | 10.38 | 15.72 | 0 | 5,796 / 11,255 |
| UWP DX12 3 | 177.04 | 194.88 | 75.05 | 5.13 | 9.81 | 11.93 | 20.73 | 0 | 3,127 / 10,617 |

The FPS and frame-time columns describe application presents. For native, displayed-frame median intervals were 4.22, 4.21 and 4.18 ms. For UWP, they were 16.66, 8.35 and 8.31 ms. The composition differences and changing background workloads make a DX12 performance ratio or a conclusion about the 20% target unwarranted.

| Pass | Process CPU, busy logical cores | Busiest thread, % of one core | Private GiB at end | Peak working set GiB | Device GPU utilisation | Attributed median active GPU ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Native DX12 1 | 0.94 | 91.7 | 2.09 | 1.03 | 30.2% | 1.08 |
| Native DX12 2 | 0.94 | 92.7 | 2.12 | 1.03 | 33.5% | 1.01 |
| Native DX12 3 | 0.97 | 95.8 | 2.08 | 1.03 | 36.9% | 0.77 |
| UWP DX12 1 | 0.76 | 73.6 | 2.15 | 1.22 | 37.2% | 1.65 |
| UWP DX12 2 | 0.77 | 73.8 | 2.15 | 1.22 | 35.8% | 1.49 |
| UWP DX12 3 | 0.78 | 74.1 | 2.15 | 1.22 | 37.8% | 1.36 |

The process snapshots show the busiest thread's observed CPU time. A named-thread profile is still needed to assign the time to the main/render thread or distinguish driver submission from game work. The NVIDIA percentages include the whole GPU. The DX12 engine logs confirm multi-draw, occlusion culling and GPU timing on both clients. The UWP D3D11 renderer has a FL9_1 capability projection on modern hardware, with separate shaders and CPU fallbacks, so identical options alone cannot isolate HAL overhead. The archived Nightly run selected hardware FL11_1 on the RTX 3090 Ti. The forced hardware FL9_1 run was a separate compatibility test. Earlier wgpu D3D11 measurements isolated upload-buffer re-mapping as a significant cost, and the signed baseline includes that fix. The previous 10.07 ms checkpoint and earlier Nightly 15.89 ms FIFO sample describe separate configurations. [D3D11 performance investigation](dx11-performance.md)

## ANGLE implementation feasibility

The pinned HAL uses `LoadPackagedLibrary` to load `libEGL.dll`, selects EGL's ANGLE D3D11 platform and gives `eglCreateWindowSurface` the host's WinRT window handle. The host passes the XAML SwapChainPanel and supplies `EGLRenderSurfaceSizeProperty` in physical pixels. The shell's ANGLE selector is compiled with `HORIZON_UWP_ANGLE=1`. The package needs architecture-matched `libEGL.dll`, `libGLESv2.dll` and any runtime dependencies, with redistribution notices. The Builder checks for those files before enabling the host selector. [Pinned runtime recipe](../uwp/angle-arm32/README.md) · [Microsoft vcpkg ANGLE port](https://vcpkg.io/en/package/angle.html)

Earlier ANGLE probes reached shader compilation and presentation on x64 and ARM64. The ARM64 probe reported 2,048 texture array layers on GLES 3.0 and unavailable compute/storage buffers. An earlier ARM32 Lumia package reached a world on Windows Mobile build 16241 before a render-pipeline failure. Hardware exposing only GLES 2 cannot satisfy wgpu's GLES 3 requirement. [Earlier probe record](../uwp/wgpu-angle-probe/README.md)

The scratch dedicated server shut down after saving the test world. The loopback exemption for `MCHorizonUWP.NightlyGraphics` was removed and its absence verified.

## Signed x64 ANGLE package

Ben-Desktop built ANGLE chromium_7258#2 from `d9fc4a372074b1079c193c422fc4a180e79b6636` with vcpkg `817892b0ecbc6af994dd6df999c73faf795adbf2`, VS 2022 Store MSVC 14.44.35207 and SDK 10.0.26100.0. The successful `angle-build.cmd` run in `E:\Temp\mchorizon-mod-runtime-mission\angle` used the release-only `x64-uwp-graphics` triplet with WindowsStore and dynamic CRT/library linkage. Vcpkg reported 6.7 minutes and package ABI `ec0aa934b4536945134026153644d3496ad733be968c4ac9697adf4cdbb0a47d`. The build kit and log include the host triplet, Python setup and package source revisions. [Build and import evidence](../deliverables/nightly-2026-11-19/graphics-angle)

The new ANGLE package declares `MCHorizonUWP.NightlyGraphicsANGLE` 1.0.1.0 and the same neutral resource framework as the DX12 comparison. It uses the original strict game bridge `05b14fee7cb59c84a87a0bf72ce2d24b80adc869adccc515d865507b77c2cf4d` and the strict-linked ANGLE host `72064fabf0a87e4e71234b46aed501c060e5a520dcf027d9dc935fd121482e63`. SignTool `verify /pa /v` passed with zero warnings. Windows reported the installed x64 package as `Ok`. The manifest's 10.0.15063.0 minimum does not establish ANGLE runtime support on older Windows builds.

| File | SHA-256 |
| --- | --- |
| `HorizonUWP-x64.appx` | `b097055a3bf4c39c18b59d2ba252dc754c9671aae815dcd122a06d5890f5a4b6` |
| `libEGL.dll` | `45e766c3bca08f1e82c33e2c3e1447f38e21b0e061c5d76755d1987abaa3f49e` |
| `libGLESv2.dll` | `d06a223b1e3a9077a58191847b77c5814bdedd3e8c15c0f90770d0892028aa31` |
| `z.dll` | `2be65de4d1b4ab34231386d0e048d76ec9a546c0b3174559fbae16a16a64b1c5` |

`dumpbin /headers /imports` confirmed all three runtime DLLs are x64 AppContainer images. `libEGL.dll` imports the packaged `libGLESv2.dll`, which imports the packaged `z.dll`, D3D11, DXGI and D3DCompiler 47. The imported C++ runtime uses the App variants provided by the installed x64 VCLibs dependency. The package carries the ANGLE and zlib notices. Staged and installed host, bridge and ANGLE DLL hashes match. Package activation, loaded driver identity, hardware GLES3 gameplay and resize remain to be observed.

## Matched foreground capture prerequisites

The preserved scratch executable, detached source, resource framework, two signed UWP identities, PresentMon 2.6.0 and NVIDIA sampler passed a noninteractive preflight. Ben-Desktop reported an RTX 3090 Ti with driver 621.01. Port 25567 was free and neither isolated UWP package had a loopback exemption. The native client from the previous test still had a window, so the preflight preserved its process and generated the matched inputs separately. The old server queue ends with `stop`. The preparation script backs up and replaces it once the scratch clients are closed. [Preflight record and prepared inputs](../deliverables/nightly-2026-11-19/graphics-angle)

Use the saved seed-1234 scratch world with the same dedicated server for all four runs. The prepared profiles use FIFO, VSync on, a frame cap of 260 (unlimited in this Nightly), 100% SDR resolution and `pauseOnLostFocus:false`. FIFO is the common presentation mode advertised by the pinned D3D11, D3D12 and GLES backends. Record the actual presentation mode and swap chain for each pass. The older captures used the separate Immediate/Mailbox configuration.

During an approved foreground window, close the previously running scratch client, stage the prepared profiles and start the pinned server. Use an elevated shell to grant loopback temporarily to the two isolated package families and remove the added entries after all captures. Keep each client on the same 240 Hz monitor and use the logged physical 1800×1350 surface for validation. Configure the scratch client before launch.

```powershell
$scratch = Join-Path $env:TEMP 'horizon-nightly-graphics-20261119-123308'
.\tools\prepare_nightly_graphics.ps1 -ScratchRoot $scratch -Prepare
& (Join-Path $scratch 'Start-Bench.ps1') -Role Server
```

- For Native DX12, use `Start-Bench.ps1 -Role Native` and the `NightlyBench` player name. For UWP, open the correct isolated package, choose Direct3D 12, Direct3D 11 (wgpu, feature level 9_1), or ANGLE (OpenGL ES 3.0), then join `127.0.0.1:25567`. Record the UWP player's name from the scratch server's join message. Keep a single benchmark client connected.
- Verify ANGLE's first game frame, live world and resize. The game log must identify `backend=Gl`, OpenGL ES 3.x, ANGLE with a D3D11 renderer on the RTX 3090 Ti and hardware rendering. Keep the shell probe log and world screenshot with the game log. Return to 1800×1350 for measurement.
- Keep the player motionless with the same loaded chunks and clear the debug and pause overlays. Use the capture script from the game foreground. It checks the expected adapter and FIFO surface, teleports the named player to the saved camera pose, waits 45 seconds, checks the server's pose confirmation, then records display modes, a world screenshot and three PresentMon runs of 60 seconds each. The capture aborts if the client loses foreground at a check. It never requests foreground focus. Discard interrupted passes and preserve application and displayed cadence as separate metrics.

```powershell
.\tools\capture_nightly_graphics.ps1 -ScratchRoot $scratch -GamePid $gamePid -Backend uwp-angle -PlayerName $player
```

The capture backend values are `native-dx12`, `uwp-dx12`, `uwp-dx11` and `uwp-angle`. The script uses the retained PresentMon, CPU and GPU collector in the scratch root and writes fresh timestamped evidence. The existing diagnostic CSV summarizer was rerun successfully. The foreground capture script passed PowerShell syntax validation and awaits a live approved run.
