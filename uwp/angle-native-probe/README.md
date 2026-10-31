# Native ANGLE diagnostic

These sources adapt the UWP host's renderer interface to an EGL/OpenGL ES 3 clear using ANGLE's explicitly requested D3D11 backend. They do not render Horizon. In the local experiment they were built in place of ../dx11 using ANGLE.WindowsStore 2.1.14 headers and x64 libraries, staged with its libEGL.dll and libGLESv2.dll, and registered under a separate identity. The app remained running; inspection showed both ANGLE DLLs, d3d11.dll and dxgi.dll loaded. This is evidence of a basic EGL/CoreWindow path, not evidence that wgpu or Horizon works.

ANGLE.WindowsStore 2.1.14 is deprecated and was used only for this diagnostic. Obtain a supported UWP ANGLE build for further work. No ANGLE binaries are included here.
