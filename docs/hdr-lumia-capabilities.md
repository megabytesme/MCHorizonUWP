# Lumia 950 XL HDR capabilities

The current Windows 10 Mobile AppContainer path stays on SDR. There is physical evidence of D3D11 gameplay and ordinary SDR output on the Lumia 950 XL. The output color space, physical gamut and luminance have not been measured on the phone.

## Physical device evidence

| Item | Evidence |
| --- | --- |
| OS | Windows 10 Mobile `10.0.16241.1001`, ARM32, from the November 20 mod runtime record |
| GPU | Qualcomm Adreno 430, physical DXGI adapter, driver `11.18.1078.53` |
| D3D11 | Hardware feature level 11_1 with Horizon's FL9_1 compatibility profile in the product run. The separate forced FL9_1 probe also ran on hardware. |
| Presentable surface | XAML `SwapChainPanel`, swapchain `2561 × 1439` in landscape, `Bgra8Unorm`, `Fifo` |
| Previous ANGLE result | Earlier signed ARM32 testing created an EGL/wgpu surface and reached a world before a render-pipeline error. There is no ANGLE HDR proof on ARM32. |
| Latest device access | Pinned HTTPS Device Portal requests to `/api/os/devicefamily` and `/api/os/info` timed out before HTTP at `2026-11-21T06:50:23Z` and `06:50:39Z`. The Ethernet neighbor showed `Reachable` and TCP port 443 failed to open. |

[OS evidence](nightly-mod-runtime-2026-11-20.md) · [D3D11 and panel evidence](nightly-lumia-drs-2026-11-21.md) · [Device and surface log](../deliverables/nightly-2026-11-20/drs/lumia-runtime/fixed-50-game.log) · [Earlier ANGLE result](../uwp/wgpu-angle-probe/README.md) · [Device Portal API](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/device-portal-api-core)

The November 20 client uses the `10.0.15063.0` SDK and AppX minimum for ARM32. `DisplayInformation.GetAdvancedColorInfo` and `AdvancedColorInfoChanged` first appear in build `17134` and Universal API contract v6. The observed mobile build is older. This also agrees with the absent declarations in the installed 15063 and 16299 headers. Compile-time gating keeps the 15063 ARM32 build usable, and a capable UWP build must check availability before querying the current view. An unsupported contract or failed lookup gives the renderer no positive HDR display evidence. The DXGI 1.4 and 1.5 headers in the ARM32 SDK provide swapchain color-space and metadata interfaces. Actual support still depends on querying those COM interfaces on the phone and checking the candidate surface's presentation support. [WinRT API requirements](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.display.displayinformation.getadvancedcolorinfo?view=winrt-26100) · [Change event](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.display.displayinformation.advancedcolorinfochanged?view=winrt-26100) · [DXGI color-space check](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/nf-dxgi1_4-idxgiswapchain3-checkcolorspacesupport)

## Output classifications

| Mode | Status on this device |
| --- | --- |
| HDR presentation | Unsupported by the current product path. No positive runtime evidence for Advanced Color through the mobile compositor, an HDR-capable swapchain or actual HDR panel output. |
| Wide-gamut SDR | Unverified. Panel technology, camera HDR and an ICC profile provide no evidence of a wide-gamut UWP present on this driver. |
| FP16 internal lighting with SDR output | Unverified as a hardware configuration on the tested phone. Texture/RTV/filter/blend support would only characterize the renderer and still leave the final surface SDR. |
| Ordinary SDR | `Bgra8Unorm` used on the physical device in a world. Signed client `0.1.17` wrote a `2561 × 1439` PNG to `LocalState\game\screenshots`. |

[Signed SDR screenshot and repaired AppContainer save path](nightly-runtime-completion-2026-11-21.md) · [Windows Advanced Color presentation requirements](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)

At `2561 × 1439`, one unpitched color buffer is 14.06 MiB at 32 bits per pixel or 28.12 MiB at 64 bits per pixel. Two FP16 buffers add about 28.12 MiB over two BGRA8 buffers before padding, intermediate targets or compositor allocations. The phone has previously hit out-of-memory during world texture upload and lost its graphics device at higher settings. Preserve the SDR fallback and private `LocalState` screenshot path. The memory values are estimates for the two buffers. [Lumia memory and device-loss evidence](nightly-lumia-drs-2026-11-21.md) · [scRGB storage requirements](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range#option-1-use-fp16-pixel-format-and-scrgb-color-space)

## Remaining physical checks

Once the pinned Device Portal endpoint responds, confirm the OS and run an isolated signed ARM32 AppContainer probe with the 15063 linkage and actual `SwapChainPanel`. Capture physical driver identity, per-format D3D11 support and blend/readback checks, the created DXGI format and alpha mode, `QueryInterface`/color-space HRESULTs, and whether extended highlights survive composition. Use the same hardware and package for a signed in-game SDR regression at 100%, 75% and 50%, including F2, suspend/resume and memory/device loss. Only a verified end-to-end HDR path qualifies for an in-game HDR test. [Existing isolated ARM32 probe setup](../deliverables/nightly-2026-11-20/drs/probe/README.md)
