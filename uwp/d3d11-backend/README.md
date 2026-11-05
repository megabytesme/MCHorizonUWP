# Native Direct3D 11 backend

This Rust library provides the first rendering primitives for the phone path:
a CoreWindow swap chain and hardware device forced to feature level 9_1,
indexed 16-bit meshes, a 2D RGBA atlas, depth buffering, alpha blending,
forward and reversed depth conventions, and vertex/pixel
shaders compiled with the 9_1 profiles. It does not yet replace Horizon's
wgpu renderer or load its game data.

The development probe in `probe/` uses these primitives in a UWP host. It
draws textured quads with depth, translucent and overlay passes; before
presenting it reads back pixels after each pass and writes the result to
`LocalState/rust-d3d11-probe.txt`. On x64 UWP the expected pixels and depth
occlusion in both depth conventions, alpha blending, atlas rectangle updates
and depth-independent overlays passed, and the host
passed the strict UWP link. The same Rust code
and host also compiled and passed the strict link for ARM32 using VS 2017 and
the 15063 SDK. This is not a phone or full-game verification.

The shaders are compiled at build time by the Windows SDK's `fxc.exe`; run
Cargo from an MSVC developer environment with `WindowsSdkDir` and
`WindowsSDKVersion` set. For a UWP build, prepare the patched Rust sources as
described in `../rust-std/README.md` and build the probe static library for the
desired UWP target with `-Z build-std=std,panic_abort`. The probe's native
`build-host.cmd x64` and `build-host.cmd arm` link the respective host and
require `HORIZON_D3D11_PROBE_LIB` to name that target's static library.

The current pipeline is intentionally minimal: it uses point sampling,
alpha cutout, and has no lighting, texture animation,
UI geometry integration or device-loss recovery. Native terrain
integration must account for those missing operations and convert Horizon's
sprite-array and vertex data to the supported formats.

The renderer and its immediate context are bound to their creating thread; the
Rust type is intentionally neither `Send` nor `Sync`. The safe CoreWindow
owner must validate the UI thread before constructing it.
