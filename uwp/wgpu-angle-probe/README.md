# Experimental wgpu ANGLE probe

This diagnostic is not Horizon and is not a working UWP port. It uses the vendored wgpu-hal 30.0.1 patch to select EGL on UWP, load libEGL.dll using LoadPackagedLibrary, request ANGLE D3D11, and accept a WinRT CoreWindow handle. Non-UWP Windows continues to use WGL. The probe only requests the GL backend.

On this machine `build.cmd` built the x86_64-uwp Rust static library with stable Rust and `RUSTC_BOOTSTRAP=1` using `-Z build-std`. The path in Cargo.toml selects the local wgpu-hal patch. The script requires Visual Studio at the path in the script, Rust source and the 15063 SDK.

`host-build.cmd` is a **non-compliant diagnostic only**: it links the desktop kernel32 import library to resolve UWP-incompatible imports in Rust std. For a strict WindowsApp.lib link, omit desktop kernel32; the attempted debug link then failed with 23 missing imports, including CreateProcessW and console APIs. The diagnostic exe linked, but a registered app made with it failed activation with TWinUI event 5961. No wgpu frame has been demonstrated. A Rust std experiment is in ../rust-std; it was not applied to this probe.

For a runtime experiment, stage the host manifest and assets from ../host/build/package, rename the exe to HorizonUWP.exe, give the manifest a distinct package identity, include UWP-compatible ANGLE libEGL.dll and libGLESv2.dll, then register the staged manifest in Developer Mode. The local test used deprecated ANGLE.WindowsStore 2.1.14 binaries solely for diagnosis; Microsoft discourages their production use. Its package has x64, x86 and ARM32 libraries, but no ARM64. Do not ship those diagnostic binaries.

Phone compatibility is unverified. Microsoft documents that Windows Phone must import LoadPackagedLibrary from PhoneAppModelHost.dll rather than Kernel32.dll: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-loadpackagedlibrary . The vendored patch links WindowsApp.lib and has not been checked for the actual ARM32 import mapping or tested on build 15063. The UWP patch is compile-checked on x64 only. Also, wgpu requests OpenGL ES 3; devices exposing only GLES 2 through ANGLE cannot run this path unchanged.
