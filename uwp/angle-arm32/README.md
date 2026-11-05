# UWP ANGLE for ARM32 and x86

The UWP host loads ANGLE only when its matching DLLs are packaged and a live
device, surface, and frame probe succeeds. The native Direct3D 11 renderer is
still available. ARM32 ANGLE initialized and entered a
world on a Lumia 950 XL running build 16241, but was slow and later hit a wgpu
render-pipeline error. A successful startup probe is not a gameplay guarantee.

`build.ps1` builds the vcpkg ANGLE chromium/7258 port, whose source revision is
`d9fc4a372074b1079c193c422fc4a180e79b6636`, with zlib 1.3.2. Provide the
prepared vcpkg source tree (including the port's CMake files and patches), a
zlib source tree, and a new output directory. The script validates key ANGLE
source hashes, builds in a fresh temporary directory, and leaves the workspace
there for inspection. It preserves the ANGLE and zlib redistribution notices.

Start it from a Visual Studio developer prompt using the appropriate UWP tools:

```bat
call "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\VC\Auxiliary\Build\vcvarsall.bat" x64_arm store 10.0.15063.0
powershell -NoProfile -File uwp\angle-arm32\build.ps1 -Architecture arm -AngleSource <prepared-angle> -ZlibSource <zlib-source> -Destination .local\vcpkg-arm32\installed\arm-uwp-local
```

For x86, use Visual Studio's `x64_x86 store 10.0.26100.0` environment and
`-Architecture x86 -Destination .local\vcpkg-x86\installed\x86-uwp-local`.
The script requires CMake, Ninja, and for ARM32 clang-cl. Override their paths
with `-CMake`, `-Ninja`, and `-Clang` as needed. The ARM32 compiler workaround
uses clang-cl only for a generated translation unit that triggers an internal
compiler error in the VS 2017 ARM compiler.

The small patch adapts UWP temporary paths, sets ANGLE's UWP platform define,
and avoids constant expressions unsupported by the older Microsoft compiler.
The WinRT window and swap-chain support is retained; the newer compositor path
is disabled because its SDK headers are unavailable on Windows 10 Mobile.
