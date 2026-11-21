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

## Checking the game surface

The primary XAML view samples `DisplayInformation.GetAdvancedColorInfo` after activation and on advanced-color or visibility changes. Game threads read the cached kind, SDR white, peak and minimum in nits. Kind 2 means that HDR is active. Kind 1 means wide-gamut SDR, kind 0 means SDR, and −1 means that the API or reading is unavailable. The query and event belong to UniversalApiContract v6, introduced in Windows 10 build 17134. [Windows API contract](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.display.displayinformation.getadvancedcolorinfo)

The render worker creates the DXGI composition swapchain and the host attaches it to the XAML panel on that panel's dispatcher. The host bridge records the swapchain descriptor, selected color space, the driver's support flags, the result of setting the space and the view reading in `LocalState\horizon-uwp-lifecycle.log`. DXGI exposes no getter for the selected swapchain color space. Inspect `set_hr` from the call on the same chain. The monitor's output color space can be PQ while the application's buffer uses scRGB.

For scRGB, verify format 10, space 1, opaque game composition, successful `set_hr`, the PRESENT bit in `support`, and `view_kind=2` while the game world and HUD are visible. In linear scRGB, a neutral value of 1.0 corresponds to 80 nits and 2.5 to 200 nits. Keep luminance above 1.0 through the final surface write. scRGB does not take HDR10 mastering metadata. For a 10-bit PQ path, verify format 24, space 12, opaque composition and the PQ/BT.2020 shader output. Windows advises adapting content to current display capabilities and does not guarantee propagation of swapchain HDR10 metadata. [Microsoft Advanced Color guidance](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range) · [DXGI metadata guidance](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgiswapchain4-sethdrmetadata)

Correlate the lifecycle records with a signed development package, renderer selection, visible world/HUD and the frame timestamp. Startup renderer probes also create swapchains. Repeat during resize, moving between the two desktop monitors, suspend/resume, HDR Off/On/Auto and device recovery. Moving onto SDR should produce SDR game output even if DXGI still allows an HDR-formatted composition buffer. An SDR screenshot alone cannot verify HDR.
