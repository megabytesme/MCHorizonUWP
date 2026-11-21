# UWP D3D12 HDR validation

## Desktop measurements

Ben-Desktop's GeForce RTX 3090 Ti exposed two active outputs during the November 21 probe. Windows reported HDR active on the PG32UCDM with 10 bits per channel, PQ/BT.2020 output, 455-nit peak, 265-nit maximum full-frame luminance, 0-nit minimum and a 480-nit SDR-white setting. The U32J59x reported 8 bits per channel, SDR, a 270-nit peak and an 80-nit SDR-white setting. These are OS and driver reports. Physical luminance was not measured.

On the same adapter a native D3D12 command queue created flip-model composition swapchains with the following results. These chains had no attached XAML panel or game scene.

| DXGI backbuffer | DXGI color space | Native alpha modes tested | Support flags | SetColorSpace1 HRESULT |
| --- | --- | --- | --- | --- |
| B8G8R8A8_UNORM (87) | RGB_FULL_G22_NONE_P709 (0) | IGNORE and PREMULTIPLIED | 0x1 (PRESENT) | S_OK |
| R16G16B16A16_FLOAT (10) | RGB_FULL_G10_NONE_P709 (1) | IGNORE and PREMULTIPLIED | 0x1 (PRESENT) | S_OK |
| R10G10B10A2_UNORM (24) | RGB_FULL_G2084_NONE_P2020 (12) | IGNORE and PREMULTIPLIED | 0x1 (PRESENT) | S_OK |

The probe ran from `%TEMP%\mchorizon-hdr-nov20` with Visual Studio 2022 Build Tools MSVC 14.44 and SDK 26100. In a Developer Command Prompt, run `cl /nologo /std:c++17 /W4 /EHsc /O2 /Fe:probe.exe <product>\tools\hdr_dxgi_probe.cpp /link dxgi.lib d3d11.lib d3d12.lib user32.lib`, then run `probe.exe` with the desktop GPU lock held. The executable SHA-256 was `f18187dc74dc905b4f87bf87015233c1bf30cff36c3f0b8d1fed0765e25904bb`. The raw log SHA-256 was `7f88d50373a85fe1d0d75bf5acb4f4219cbb8e2ee8e47cdbc0f7cf0b035447bf`. [Probe source](../tools/hdr_dxgi_probe.cpp) · [Raw log](../deliverables/hdr/dxgi-baseline.log) · [Machine-readable results](../deliverables/hdr/swapchain-probe-results.json)

## Native host verification

Ben-Desktop built each host from `C:\dev\personal\MCHorizonUWP-HDR-Nov20` with `tools/build_host_artifact_kit.ps1 -Architecture <arch> -OutputDirectory "$env:TEMP/mchorizon-hdr-nov20/host-kit-<arch>"`. All three strict AppContainer links succeeded. `dumpbin /exports` found both `horizon_uwp_get_advanced_color` and `horizon_uwp_record_swapchain_color` on each executable.

| Architecture | Build SDK | HorizonUWP.exe SHA-256 |
| --- | --- | --- |
| x64 | 10.0.19041.0 | `936369d1676fb1fab76b9a52c900c712164d50e930807e8b6216eb58b5883746` |
| ARM32 | 10.0.19041.0 | `d9c320fd0ada9c8ff39ceeadfa42c9b0bec37fa3eb63833cc026412f60fc671f` |
| ARM64 | 10.0.26100.0 | `9176bdaf1b3db1b3203a84923e464604a801a88c2778131ae6ca5a18872fcad8` |

## Signed x64 development candidate

The offline release build ran in `%TEMP%\mchorizon-hdr-nov20\assembled` on Ben-Desktop with 32 jobs, a separate `target-x64` and patched Rust 1.99.1 standard-library sources. Cargo finished in 19 minutes 10 seconds. The strict x64 game-bridge link passed. The package `MCHorizonUWP.HdrDev.x64` version `1.0.0.0` contains patch `0.1.16` and client `0.1.18`. SignTool verified the development APPX and its preserved copy. [Build receipt](../deliverables/hdr/x64/build-receipt.json) · [Package hashes and source commits](../deliverables/hdr/x64/package.json) · [APPX](../deliverables/hdr/x64/HorizonUWP-HDR-x64.appx)

At 07:29:58 UTC, the signed x64 candidate opened the local test world using the physical RTX 3090 Ti. The game reported its first HDR frame. The live XAML panel used DXGI format 10, opaque alpha 3, scRGB space 1, PRESENT support and a successful color-space call. The active view reported HDR throughout the initial resize. The game applied 160-nit paper white and 450-nit peak. [Signed presentation receipt](../deliverables/hdr/x64/first-presentation.json)

The signed Auto 100% and On 75% TAA and 50% terrain MSAA runs drew the test world and HUD. The On runs with Shader API 0.2 `horizon_hdr 1.0.0` and classic `terrain-test` loaded their pipelines and saved HDR companions. Off on the same monitor, D3D11 On, and ANGLE Auto each saved one SDR image. These scale checks used fixed resolution settings. The fixture's magenta HUD marker confirms that its Wasm hook ran. [Trial receipts and PNG checks](../deliverables/hdr/x64/gameplay/index.json) · [Signed world and HUD](../deliverables/hdr/x64/gameplay/auto-100/world-sdr.png)

The game saved RGBA8 SDR PNGs and RGB16 HDR PNGs tagged BT.2020/PQ, with the pinned ICC profile and cLLI. Pixel decoding reproduced the recorded peak and average light levels. The Shader API 0.2 capture peaked at 302.30 nits with three pixels above 160-nit paper white. The classic fixture peaked at 160.39 nits. These values describe encoded game pixels. [Shader API 0.2 HDR PNG](../deliverables/hdr/x64/gameplay/shader-api-02/world-hdr.png) · [Decoded pixel statistics](../deliverables/hdr/x64/gameplay/shader-api-02/hdr-png.json)

The U32J59x was inactive by the time the signed matrix ran. On the remaining PG32UCDM, the test temporarily disabled Windows HDR while the world was running, then restored the original setting. The same process switched from DXGI format 10 and space 1 in view kind 2 to format 87 and space 0 in view kind 0. It returned to format 10 and space 1 after restoration. All three calls succeeded and held PRESENT support. The test also verified that the OS HDR setting was back on afterward. [Mode transition receipt](../deliverables/hdr/x64/gameplay/mode-transition/result.json)

An On run with adaptive resolution enabled and a 240 FPS target presented HDR and saved both PNGs. Windows PLMDebug reported the isolated package suspended. After resuming it, the same process resized the live game panel and successfully reapplied scRGB with the HDR view active. The harness released the debug reference on exit. The test exercised the adaptive rendering path and lifecycle. It did not measure scale convergence or trigger GPU device loss. [Adaptive and lifecycle receipt](../deliverables/hdr/x64/gameplay/adaptive-plm/result.json) · [Windows lifecycle debugging](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/plmdebug)

The current-build screenshot fixture exercised the game's screenshot path under AppContainer. When the foreground was available, direct F2 on the same signed candidate saved both the RGBA8 SDR PNG and the tagged RGB16 HDR PNG during terrain load. The first game presentation had already configured the live FP16 scRGB chain with an HDR view. The previous captures provide the fully loaded world and HUD evidence. [Direct F2 receipt](../deliverables/hdr/x64/f2/result.json) · [HDR metadata check](../deliverables/hdr/x64/f2/hdr-png.json)

After the F2 run, Windows switched to an active RDP session. The PG32UCDM and U32J59x had no active display paths in that session, and the user kept RDP connected. The physical monitor-move check awaits an active console with both displays.

## Forced D3D12 removal and memory

An independently signed `MCHorizonUWP.HdrDev.DeviceLoss.x64` diagnostic build used the exact Rust bridge from the signed HDR game and a separate host kit. While the world rendered on the RTX 3090 Ti under RDP, the app held a 256 MiB local allocation for two seconds. `QueryVideoMemoryInfo` reported 275,640,320 bytes before allocation and 541,650,944 bytes while held, an increase of about 254 MiB. The diagnostic released the allocation, then removed only the game's D3D12 device. DXGI reported `DXGI_ERROR_DEVICE_REMOVED`. The same process recovered the renderer, began rebuilding the world meshes, configured a fresh opaque BGRA8/sRGB chain and reached its first present call. RDP went inactive during the F2 repeat, before the test could take a fresh post-recovery screenshot. The isolated game was closed. [Signed diagnostic and test receipts](../deliverables/hdr/x64/recovery/index.json)

Five WDDM per-process samples accompanied direct F2 on the physical HDR monitor. A separate run on the same signed game with HDR Off sampled another five times under RDP. Dedicated memory was about 347 MiB in the HDR run and about 350 MiB in the SDR run. The scene, viewport and Windows session differed. A matched HDR/SDR memory comparison and D3D12 device removal in an active HDR view remain pending. [HDR counters](../deliverables/hdr/x64/f2/result.json) · [SDR counters](../deliverables/hdr/x64/memory/result.json)

Build the isolated diagnostic with `tools/hdr/Build-HdrRecoveryCandidate.ps1`, the prior assembled source, the hash-checked signed HDR game bridge and the matching ANGLE DLLs. Follow the development packaging procedure and independently verify the signed package. With the desktop GPU lock held, run `tools/hdr/Test-HdrGame.ps1` against identity `MCHorizonUWP.HdrDev.DeviceLoss.x64` using the isolated world, `-Display Current -Hdr On -SampleMemory -ForceDx12Removal` and the nonautomatic Wasm fixture. Add `-ExpectHdr` when the game is on an active physical HDR display. [Build script](../tools/hdr/Build-HdrRecoveryCandidate.ps1) · [Harness](../tools/hdr/Test-HdrGame.ps1)

## Surface Pro X

The strict ARM64 release built, signed and installed on the physical Adreno 680 Surface. Its internal panel reported 8-bit SDR only. The native DXGI probe accepted FP16/scRGB and RGB10/PQ formats on the same machine, which confirms that swapchain-format support alone cannot establish HDR output. The signed game's startup configured BGRA8/sRGB with an SDR view. Windows suspended the 31-thread game process in the locked console before a world frame. [Signed ARM64 APPX](../deliverables/hdr/arm64/HorizonUWP-HDR-arm64.appx) · [Surface build, transfer and hardware record](../deliverables/hdr/arm64/README.md) · [Locked activation receipt](../deliverables/hdr/arm64/locked-game-attempt.json)

After the Surface was reported unlocked, the fixture transfer timed out before launch. A separate connection retry to TCP 22 also timed out. Gameplay still requires a reachable, unlocked Surface, and ARM64 HDR requires a connected HDR display.

## Official Windows comparison

The immutable November 20 Windows client loaded the same fixture on the PG32UCDM with the same 160-nit paper-white and 450-nit peak settings. It selected an HDR10 PQ RGB10A2 swapchain, consistent with the official desktop path. Its 16-bit PQ game screenshot reached 369.87 nits in the captured scene. Independent decode reproduced the embedded MaxCLL and MaxFALL, and the capture showed the world and HUD. The presentation formats differ across the desktop window and the UWP compositor, while both use Horizon's pinned rendering and HDR screenshot encoding. [Reference run and image hashes](../deliverables/hdr/official-desktop/receipt.json) · [HDR PNG](../deliverables/hdr/official-desktop/world-hdr.png)

## Checking the game surface

The primary XAML view samples `DisplayInformation.GetAdvancedColorInfo` after activation and on advanced-color or visibility changes. Game threads read the cached kind, SDR white, peak and minimum in nits. Kind 2 means that HDR is active. Kind 1 means wide-gamut SDR, kind 0 means SDR, and −1 means that the API or reading is unavailable. The query and event belong to UniversalApiContract v6, introduced in Windows 10 build 17134. [Windows API contract](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.display.displayinformation.getadvancedcolorinfo)

The render worker creates the DXGI composition swapchain and the host attaches it to the XAML panel on that panel's dispatcher. The host bridge records the swapchain descriptor, selected color space, the driver's support flags, the result of setting the space and the view reading in `LocalState\horizon-uwp-lifecycle.log`. DXGI exposes no getter for the selected swapchain color space. Inspect `set_hr` from the call on the same chain. The monitor's output color space can be PQ while the application's buffer uses scRGB.

For scRGB, verify format 10, space 1, opaque game composition, successful `set_hr`, the PRESENT bit in `support`, and `view_kind=2` while the game world and HUD are visible. In linear scRGB, a neutral value of 1.0 corresponds to 80 nits and 2.5 to 200 nits. Keep luminance above 1.0 through the final surface write. scRGB does not take HDR10 mastering metadata. For a 10-bit PQ path, verify format 24, space 12, opaque composition and the PQ/BT.2020 shader output. Windows advises adapting content to current display capabilities and does not guarantee propagation of swapchain HDR10 metadata. [Microsoft Advanced Color guidance](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range) · [DXGI metadata guidance](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgiswapchain4-sethdrmetadata)

Correlate the lifecycle records with the signed package identity, renderer selection, world and HUD, and frame timestamp. Startup renderer probes also create swapchains. Use the gameplay color records and the first HDR frame to identify the game panel. [Signed world test harness](../tools/hdr/Test-HdrGame.ps1)
