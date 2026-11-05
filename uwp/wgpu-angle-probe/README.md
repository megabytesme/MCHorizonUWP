# Experimental wgpu ANGLE probe

This diagnostic is not Horizon and is not a working UWP port. It uses the vendored wgpu-hal 30.0.1 patch to select EGL on UWP, load libEGL.dll using LoadPackagedLibrary, request ANGLE D3D11, and accept a WinRT CoreWindow handle. Non-UWP Windows continues to use WGL. The probe only requests the GL backend.

On this machine `build.cmd` built the x86_64-uwp Rust static library with stable Rust and `RUSTC_BOOTSTRAP=1` using `-Z build-std`. The path in Cargo.toml selects the local wgpu-hal patch. The script requires Visual Studio at the path in the script, Rust source and the 15063 SDK.

`host-build.cmd` is a **non-compliant diagnostic only**: it links the desktop kernel32 import library to resolve UWP-incompatible imports in Rust std. The earlier diagnostic exe linked but failed activation. A separate strict WindowsApp.lib link was subsequently made using the experimental patched std and link checks described in ../rust-std. On x64 and on the Surface Pro X (ARM64), the strict app created its wgpu device and surface, compiled Horizon's actual `screen.wgsl` using its entity bind group layout, and reported a successful clear and present through modern UWP ANGLE's D3D11 backend. The ARM64 probe also compiled the `terrain.wgsl` solid pipeline with its actual bindings and vertex formats. The displayed output has not been visually verified. This is still a shader and clear-color probe, not Horizon.

The probe renderer invokes both shader checks before presenting. A creation or
shader validation error causes initialization to fail and is available through
`wgpu_probe_error` and the Windows debugger. `wgpu_probe_info` reports the
adapter and limits; the Pro X reported 2048 texture array layers and no
compute/storage buffers on its GLES 3.0 adapter.

For a runtime experiment, stage the host manifest and assets from ../host/build/package, rename the exe to HorizonUWP.exe, give the manifest a distinct package identity, include matching UWP ANGLE libEGL.dll and libGLESv2.dll plus their dependencies, then register the staged manifest in Developer Mode. The first x64 test used deprecated ANGLE.WindowsStore 2.1.14 binaries solely for diagnosis. Later x64 and ARM64 tests used the modern vcpkg ANGLE builds described in ../host/README.md. No ANGLE binaries are committed here.

An ARM32 ANGLE build created a wgpu surface, presented a frame, and entered a
world on a Lumia 950 XL running Windows 10 Mobile build 16241. Gameplay was slow
and a render-pipeline error later occurred. Build 15063 remains untested. x86,
x64, and ARM64 have also passed a device and presentation probe. Wgpu requires
OpenGL ES 3; devices exposing only GLES 2 cannot run this path unchanged. See
`../angle-arm32/README.md` for the ARM32 and x86 runtime build.
