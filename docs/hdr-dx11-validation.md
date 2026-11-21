# D3D11 HDR validation

The November 20 game exposes a BGRA8/sRGB D3D11 surface. Its experimental wgpu backend projects an FL9_1 renderer on the selected physical adapter, including when the native device reports FL11_1. HDR game presentation is unavailable on that backend. The desktop probe below measures which additional operations the hardware can support.

## Ben-Desktop native probe

On November 21, 2026, the test used a GeForce RTX 3090 Ti (PCI 10DE:2203), Windows 11 Pro build 26300 and NVIDIA driver 32.0.16.2101. Windows reported the PG32UCDM in 10-bit HDR mode and the U32J59x in 8-bit SDR mode. The probe filtered out software adapters, created a device twice on the same physical adapter with one requested feature level per call, and created unattached opaque flip-sequential composition chains. Both device creations returned S_OK. [Probe source](../tools/hdr_dxgi_probe.cpp)

| Native level | Buffer / requested color space | CheckFormatSupport | Chain creation | Actual format / alpha | Color check / SetColorSpace1 |
| --- | --- | --- | --- | --- | --- |
| FL11_1 | BGRA8 / sRGB | S_OK, 0x33FEF3F3 | S_OK | 87 / 3 | 0x3 / S_OK |
| FL11_1 | RGBA16_FLOAT / scRGB | S_OK, 0x12FEF3F3 | S_OK | 10 / 3 | 0x3 / S_OK |
| FL11_1 | RGB10A2 / PQ BT.2020 | S_OK, 0x33FEF3F3 | S_OK | 24 / 3 | 0x3 / S_OK |
| FL9_1 | BGRA8 / sRGB | S_OK, 0x312EF3E0 | S_OK | 87 / 3 | 0x3 / S_OK |
| FL9_1 | RGBA16_FLOAT / scRGB | E_FAIL | E_INVALIDARG | — | — |
| FL9_1 | RGB10A2 / PQ BT.2020 | E_FAIL | E_INVALIDARG | — | — |

For every successful row, DXGI returned the requested buffer descriptor, the backbuffer and an RTV. Alpha 3 is IGNORE. The driver's flags included texture2D, shader sampling, render target, blend and display. Color-space support 0x3 includes PRESENT and OVERLAY_PRESENT. The FL11_1 FP16 test cleared the backbuffer to RGBA (−0.25, 1, 4, 1), copied it to a staging texture and mapped binary16 words B400, 3C00, 4400, 3C00. This verifies the extended values survived storage. The probe did not draw a blended or sampled pixel, attach to XAML, call Present, or measure panel luminance.

`QueryInterface(IDXGISwapChain4)` succeeded on the created chains. The probe left metadata untouched. An scRGB chain carries linear BT.709-primary data, with 1.0 neutral equal to 80 nits. An HDR10 path would require opaque output with the correct PQ/BT.2020 encoding. Windows advises apps to adapt to current display conditions and does not guarantee transport of static metadata to the monitor. [Microsoft Advanced Color guidance](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range) · [DXGI metadata guidance](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgiswapchain4-sethdrmetadata)

## Product gate and integration points

The source baseline is wgpu `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157` and UWP `5c26a8060c1cf2dce20ee5707e84699ed0879e5c`. Paths below are relative to that wgpu revision.

| Area | Current behavior | Evidence needed for a higher-tier candidate |
| --- | --- | --- |
| `wgpu-hal/src/dx11/instance.rs`, `capabilities.rs` | The exposed adapter uses the implemented FL9_1 projection. | Review a distinct higher-tier projection. Intersect native format queries, backend operations and the shader model actually used. |
| `wgpu-hal/src/dx11/adapter.rs`, `surface.rs` | Advertises BGRA8/sRGB; configuration accepts only that pair, opaque FIFO and one frame of latency. | Offer each HDR pair only when the full path works. Exercise acquisition, typed views, actual sampling and blending, and clean rejection/fallback. |
| `wgpu-hal/src/dx11/swapchain.rs` | Initial creation and resize both request BGRA8. The panel path attaches through the UI dispatcher. | Carry the chosen native format through creation and resize. Check the PRESENT flag and set the color space on the same chain. Reapply after reconfiguration and preserve the latency and panel-scale paths. |
| `wgpu-hal/src/dx11/hal_device.rs`, `hal_pipeline.rs` | Allocation, view translation and pipeline validation implement a restricted format set. Vertex formats have separate mappings. | Verify texture, RTV and SRV mapping in both directions, blending and the game's actual float world, HUD and final pass. Test invalid format/usage combinations. |

Validation still requires the revised pinned game on a real SwapChainPanel, an active-view HDR reading, a successfully configured and presented HDR surface, a visible in-game world and HUD, and matching SDR transitions. Resize, suspend/resume, display migration and device loss must restore the correct pair. A higher native feature-level result does not certify any subsequent D3D11 tier. The FL9_1 projection and its SDR path remain the compatibility baseline under the existing certification process.

## November 21 native HDR candidate

The isolated `codex/hdr-dx11-native-nov21` candidate at `a52f1988412e20b7ef45f58e9483d6245260baa7` adds the opt-in `wgpu-hal/dx11-native-hdr` feature. The native FL11_0+ API offers opaque FP16 scRGB for UWP XAML and FP16 scRGB or RGB10A2 PQ for desktop HWND. It requires the DXGI PRESENT bit, selects the requested color space on the same chain, and exposes typed readback and format-aware resize. The PQ fixture keeps HDR10 metadata active through its first accepted present. The existing public wgpu projection still offers BGRA8/sRGB.

Desktop and x64 UWP checks, Clippy and seven unit tests passed. Two forced hardware FL9_1 SDR regressions passed on predecessor `b023f9c495e2d986f915a47563b156cd52e56aad`. The native HDR test required an active physical HDR path and stopped before swapchain creation under RDP. The final candidate still needs the physical HWND fixture, signed UWP XAML with world and HUD, resize rollback and the inherited lower-tier suite before promotion.
