# Surface Pro X Nightly follow-up, 20 November 2026

Ben-Tablet answered pinned-key SSH at `192.168.1.227:22`. `quser` reported the interactive console in session 1. The device had the isolated `MCHorizonUWP.NightlyCross.arm64` 1.0.0.2 package registered as native ARM64 with status `Ok`. The staged game APPX and neutral framework matched the original verified SHA-256 values. The installed game identified Horizon `0b14f8acfd97fa2c750e42795249dcc981178ca4`, UWP patch 0.1.5 and client 0.1.6. [Original signatures and package hashes](nightly-development-2026-11-19.md)

The existing interactive scheduled task started the app in session 1. Activation took about 65 seconds and exceeded the 30-second outer command deadline. The app opened on the next process check. UI Automation read the XAML controls, dismissed the optional diagnostics prompt with `Not now` and invoked `Start game`. The game used renderer 3 on the physical Qualcomm Adreno 680, with the wgpu Direct3D 11 FL9_1 compatibility path on hardware feature level 11_1 and driver 27.20.2160.0. It configured a 2048×1536 `Bgra8Unorm` FIFO surface. The shell also passed its DX12 device, surface and frame startup checks.

| Observation | Local time or measured duration |
| --- | --- |
| Game worker started | 00:17:59.823 |
| UI bundle ready | 7.063 s |
| World renderer ready | 47.581 s |
| World resources ready | 81.797 s |
| First game frame logged | 00:19:22 |

The resource loader reported 35,723 block states, 1,658 items and 67 biomes. At the loading-screen capture, the game process used 598,990,848 bytes of working set, had peaked at 1,124,843,520 bytes and was running in session 1. A later process read during startup measured 741,801,984 bytes of working set, 1,480,892,416 bytes of peak working set and 764,739,584 private bytes. The isolated package logs `HORIZON_NO_AUDIO`, which leaves sound unverified. [Loading-screen screenshot](../deliverables/nightly-2026-11-19/devices/surface-2026-11-20/surface-startup-spinner.png) · [Selected device log lines](../deliverables/nightly-2026-11-19/devices/surface-2026-11-20/surface-log-excerpts.txt)

The next screenshot request was interrupted by an SSH connection reset. An eight-second port 22 retry timed out, and the device did not respond to an ICMP echo. The source, installed package and previous saves were preserved. World entry, multiplayer, gameplay input, physical touch and controller, audio, suspend/resume, and both actual code-mod routes remain open. The next device run needs Ben-Tablet awake and unlocked through the roughly 82-second game startup and world loading.

## Signed code-mod follow-up

The isolated 1.0.1.1 ARM64 main and optional packages passed the strict link and SignTool. The files were copied from Ben-Desktop's `D:\Temp\mchorizon-mod-runtime-mission\arm64-related-set` to `C:\Users\ben\AppData\Local\Temp\MCHorizonUWP-NightlyMods-20261120` on Ben-Tablet. The first noninteractive Add-AppxPackage call returned `0x80070005`. Installation from the signed-in session succeeded. `-ExternalPackages` registered the executable optional package at the matching version. [Hashes and install evidence](../deliverables/nightly-2026-11-20/arm64/)

The earlier package and the first 1.0.1.1 run displayed the D3D11 title panorama without game UI. The 1.0.1.1 log confirms the Wasm package was loaded, the UI bundle and world resources were ready, and the first game frame rendered. The same physical GPU and driver rendered the welcome menu and code hooks after switching the 1.0.1.1 launcher to Direct3D 12. A full-image count found 9,216 exact pixels of each marker color. The log identifies the packaged native fixture and one instantiated standard Wasm mod. The game closed cleanly after capture. [D3D11 screenshot](../deliverables/nightly-2026-11-20/arm64/surface-first-hooks/enabled.png) · [DX12 screenshot](../deliverables/nightly-2026-11-20/arm64/surface-dx12-hooks/enabled.png) · [DX12 game log](../deliverables/nightly-2026-11-20/arm64/surface-dx12-hooks/enabled.log)

The DX12 run copied 23,219 prepared files totaling 248,353,373 bytes in 52.128 seconds and reported UI-ready in 57.101 seconds. It loaded world resources in 108.3 seconds. These are startup observations from a code-only mod set. A playable world, multiplayer, sound, touch/controller input, suspend/resume and marker toggles still require separate validation.

The Adreno 680 passed all 36 direct D3D11 FL11_1 HUD shader checks and all 18 checks on a device explicitly created at FL9_1. The signed full-game 1.0.1.2 update and isolated wgpu replay probe are ready for the next Surface session. [ARM HUD investigation](nightly-arm-hud-2026-11-20.md)
