# ANGLE HDR presentation validation

The current Horizon GLES backend exposes SDR color spaces only. The signed x64 probe on Ben-Desktop verified that the packaged ANGLE D3D11 runtime can create a native 16-bit floating-point composition swapchain. The same runtime rejected explicit scRGB, HDR10 PQ and Display P3 EGL window-surface requests. End-to-end ANGLE HDR gameplay remains unverified.

## Signed UWP and DXGI results

The probe used the exact ANGLE DLLs from the signed November 20 x64 QA package, copied into a separate development-signed `MCHorizonUWP.Hdr.AngleProbe` AppX. It used the game's D3D11-backed GLES 3 `SwapChainPanel` window class and WinRT physical-size property. Ben-Desktop ran Windows 11 Pro build `26300`, an NVIDIA GeForce RTX 3090 Ti with driver `32.0.16.2101`, and ANGLE `2.1.7258` (`d9fc4a372074`). This probe created a small separate panel and recorded EGL, GL and DXGI evidence. The prior signed Horizon run reached a saved world with both HUD markers on `Bgra8Unorm` at `1800 × 1350`. [Runtime hashes](../deliverables/hdr/angle/x64-runtime.sha256) · [Probe log](../deliverables/hdr/angle/x64-swapchainpanel-egl.txt) · [Signed in-game baseline](../deliverables/nightly-2026-11-20/runtime-0.1.17/qa/x64-angle/result.json)

| EGL window request | EGL and GL result | Actual DXGI backing |
| --- | --- | --- |
| 8/8/8/8 fixed, default | Linear encoding, present succeeded | `B8G8R8A8_UNORM` |
| 8/8/8/8 fixed, sRGB | sRGB encoding, present succeeded | `B8G8R8A8_UNORM` |
| 8/8/8/8 fixed, Display P3 | `EGL_BAD_ATTRIBUTE` (`0x3004`) | No swapchain created |
| 10/10/10/2 fixed, default | Linear encoding, present succeeded | `R10G10B10A2_UNORM` |
| 10/10/10/2 fixed, BT.2020 PQ | `EGL_BAD_ATTRIBUTE` | No swapchain created |
| 16/16/16/16 float, default | Linear encoding, present succeeded | `R16G16B16A16_FLOAT` |
| 16/16/16/16 float, scRGB linear | `EGL_BAD_ATTRIBUTE` | No swapchain created |
| 16/16/16/16 float, BT.2020 linear | `EGL_BAD_ATTRIBUTE` | No swapchain created |

The ANGLE EGL 1.5 display reported `EGL_EXT_pixel_format_float` and `EGL_ANGLE_windows_ui_composition`. Its display extension list omitted the scRGB, BT.2020 and Display P3 color-space extensions. The GLES extension list includes `GL_EXT_color_buffer_half_float` and `GL_EXT_color_buffer_float`. DXGI ETW recorded every successful surface as a redirected 320 × 240, two-buffer `FLIP_SEQUENTIAL` swapchain. All four `Present` results were `S_OK`, and the process had no `SetColorSpace1` events in the trace. The probe did not measure the per-view Advanced Color state, compositor pixel values or physical luminance. [DXGI ETW receipt](../deliverables/hdr/angle/x64-dxgi-swapchains.json)

## Source path and implications

The source pins are product `5c26a8060c1cf2dce20ee5707e84699ed0879e5c`, wgpu `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157` and ANGLE `d9fc4a372074b1079c193c422fc4a180e79b6636`.

- `horizon_uwp_angle_window_properties` wraps the native XAML panel in `EGLNativeWindowTypeProperty` and passes its physical size as `EGLRenderSurfaceSizeProperty`. ANGLE's `NativeWindow11WinRT` dispatches to the `SwapChainPanelNativeWindow` implementation, which creates a DXGI composition swapchain and calls `SetSwapChain` on the XAML UI thread. The chain uses premultiplied alpha when its EGL config has alpha. [UWP host](../uwp/host/App.cpp) · [ANGLE WinRT window](https://github.com/google/angle/blob/d9fc4a372074b1079c193c422fc4a180e79b6636/src/libANGLE/renderer/d3d/d3d11/winrt/NativeWindow11WinRT.cpp#L25-L68) · [ANGLE panel swapchain](https://github.com/google/angle/blob/d9fc4a372074b1079c193c422fc4a180e79b6636/src/libANGLE/renderer/d3d/d3d11/winrt/SwapChainPanelNativeWindow.cpp#L265-L324)
- The pinned GLES HAL advertises only `SurfaceColorSpaces::SRGB`, including its conditional `Rgba16Float` entry. It picks an EGL config independently of Horizon's wgpu target format, creates the native surface with default color space or standard sRGB, and blits the renderbuffer into that window. The presence of the wgpu float format or a GL color-buffer extension is insufficient to select the actual FP16 config and commit the game's HDR encoding. See `wgpu-hal/src/gles/adapter.rs:1464–1500` and `wgpu-hal/src/gles/egl.rs:98–157`, `:1210–1290` and `:1497–1585` at the pinned wgpu revision.
- ANGLE's D3D11 renderer creates `GL_RGBA16F` and `GL_RGB10_A2` EGL configs only when the device reaches feature level 10_0 and supports the formats. `SwapChain11` maps these to native `R16G16B16A16_FLOAT` and `R10G10B10A2_UNORM`. The D3D11 backend leaves the extended color-space extensions disabled, so EGL's attribute validator rejects explicit P3, scRGB and PQ. The active D3D11 panel implementation never calls DXGI `SetColorSpace1` or `SetHDRMetaData`. [ANGLE config and display extensions](https://github.com/google/angle/blob/d9fc4a372074b1079c193c422fc4a180e79b6636/src/libANGLE/renderer/d3d/d3d11/Renderer11.cpp#L1232-L1437) · [EGL attribute validation](https://github.com/google/angle/blob/d9fc4a372074b1079c193c422fc4a180e79b6636/src/libANGLE/validationEGL.cpp#L482-L556) · [DXGI format mapping](https://github.com/google/angle/blob/d9fc4a372074b1079c193c422fc4a180e79b6636/src/libANGLE/renderer/d3d/d3d11/SwapChain11.cpp#L559-L583)
- The `EGL_ANGLE_query_surface_pointer` extension here returns an optional D3D11 shared texture handle or keyed mutex. The texture is ANGLE's offscreen backing resource when available. The public query provides no access to the native `IDXGISwapChain` for `CheckColorSpaceSupport` or `SetColorSpace1`. [ANGLE query implementation](https://github.com/google/angle/blob/d9fc4a372074b1079c193c422fc4a180e79b6636/src/libANGLE/renderer/d3d/SurfaceD3D.cpp#L416-L430) · [Offscreen share handle](https://github.com/google/angle/blob/d9fc4a372074b1079c193c422fc4a180e79b6636/src/libANGLE/renderer/d3d/d3d11/SwapChain11.cpp#L260-L311)

Microsoft documents default FP16 DXGI composition as linear scRGB on supporting Windows systems. The proven ANGLE FP16 swapchain makes that route worth testing. It would require selecting the actual FP16 window config before the D3D11 chain is created, verifying the GLES blit and compositor preserve negative and over-white values, and feeding the official Horizon scRGB output only when the display reports active HDR. Confirm extended highlights, primaries and HUD brightness in a signed game capture before enabling it. The current pinned GLES HAL does not implement that path. RGB10A2 with its default color space remains sRGB, and enabling the 10-bit format alone does not give HDR10. [DXGI Advanced Color defaults](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range#setting-up-your-directx-swap-chain)

## Reproduction

Build the separate probe with `tools/hdr/build-angle-panel-probe.ps1`. Give it the `include` directory from the pinned ANGLE source, the staged x64 package directory with ANGLE and its `z.dll` dependency, a fresh output directory and the existing project development certificate's publisher subject. Sign the AppX with the existing development signer and verify it with `signtool verify /pa` before installing. Start `MCHorizonUWP.Hdr.AngleProbe` and read `LocalState\angle-panel-probe.txt` after it exits.

For native evidence, capture the `Microsoft-Windows-DXGI` ETW provider with an elevated local `logman` session, keyword mask `0x3` and level `5`. Filter to the activation PID. Read the `Format`, size, backbuffer count and swap effect from event `10`, and the `Result` of `Present` from event `43`. Task `25` is `SetColorSpace1` on the recorded desktop build. [Probe build script](../tools/hdr/build-angle-panel-probe.ps1) · [Probe source](../tools/hdr/angle-panel-probe.cpp)

Windows 10 Mobile uses the older WinRT window path and its ARM32 ANGLE build disables the newer ANGLE compositor-native-window option. The desktop high-precision results do not establish support on that device. [ARM build recipe](../uwp/angle-arm32/README.md) · [Lumia evidence](hdr-lumia-capabilities.md)

## November 21 FP16 bridge candidate

A new signed x64 probe used the same three ANGLE runtime DLL hashes. It drew the exactly representable
linear values `(-0.125, 0.5, 4.0, 1.0)` into an RGBA16F renderbuffer and used the Y-flipped
`GL_NEAREST` blit used by the GLES HAL. `GL_RGBA/GL_FLOAT` readback returned the four exact channels
from both the offscreen renderbuffer and the default FP16 window framebuffer. Both GL readbacks,
the blit, and `eglSwapBuffers` succeeded. This proves the GLES copy preserves negative gamut
components and values above reference white up through the EGL window framebuffer. The result
does not measure the Windows compositor or monitor output.

The same package also used `EGL_KHR_no_config_context` to create one GLES context, bind an 8-bit
window, destroy it, bind a default FP16 window, verify the exact values again, then destroy the
FP16 window and restore the 8-bit window. Every swap succeeded. This establishes that the product
can retain its EGL rendering context across the surface format transition. Explicit scRGB and
BT.2020 requests still returned `EGL_BAD_ATTRIBUTE`. [Configless switch receipt](../deliverables/hdr/angle/x64-fp16-configless-switch.txt)

The isolated candidate fork `codex/hdr-angle-hal-nov21` at
`3cd220017fad76d11e6939984f42917cee9fa5a7` implements the experiment behind
`WGPU_UWP_ANGLE_HDR=1`. It requires the explicit ANGLE D3D11 EGL platform, the two EGL
extensions, a matching non-MSAA FP16 GLES3 window config, successful configless context creation,
and the existing GL half-float color-target capability. It selects the FP16 EGL config only
when the application requests `Rgba16Float` with `ExtendedSrgbLinear` and keeps the existing
EGL config for SDR. An x64 UWP GLES-only `cargo check` passed. The shipped Horizon GL backend
continues to select SDR. A signed game integration still needs to drive the active-view HDR
selection and verify world/HUD composition, in-process fallback, and signed captures on an
active HDR output. Missing EGL support keeps the default SDR path available.

A second signed AppX extended the hold time of the final FP16 surface for Windows Graphics
Capture, with per-view Windows Advanced Color telemetry. The RDP view reported SDR, with 80 nits
reference white. The first scoped WGC diagnostic found the CoreWindow during startup, then
rejected the capture frame pool with `E_INVALIDARG`. The helper now waits for the desktop frame
and checks the capture dimensions before creating a FP16 pool. WGC and monitor proof require
a usable target window and an active HDR view. [Panel receipt](../deliverables/hdr/angle/x64-fp16-capture-panel.txt) · [WGC diagnostic](../deliverables/hdr/angle/x64-fp16-wgc-first-diagnostic.txt)

Build the new package with the command above and pass `-PackageName MCHorizonUWP.Hdr.AngleFp16CaptureProbe`
to retain the original probe installation. Build `tools/hdr/angle-window-capture.cpp` with MSVC
x64, C++20, the Windows SDK `cppwinrt` include path, and `d3d11.lib windowsapp.lib ole32.lib user32.lib`.
After checking that an HDR output is active, run the helper with the new package's application user
model ID. It launches the probe, targets only its window, and checks the captured FP16 samples for
the expected pattern. The probe logs its active view and exits after twelve seconds.
