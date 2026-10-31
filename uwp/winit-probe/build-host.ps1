# Run from a UWP MSVC developer prompt after building the Rust static library.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = Join-Path $repo 'uwp/host/build/winit-probe'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$metadata = Join-Path ${env:WindowsSdkDir} 'UnionMetadata/10.0.15063.0'
$sources = @('uwp/host/App.cpp', 'uwp/dx11/Dx11Renderer.cpp', 'uwp/window-bridge/CoreWindowEvents.cpp')
$objects = @()
foreach ($source in $sources) {
    $name = [IO.Path]::GetFileNameWithoutExtension($source)
    $obj = Join-Path $out "$name.obj"
    & cl /nologo /EHsc /ZW /std:c++17 /O2 /MD /DHORIZON_UWP_GAME_BRIDGE /DHORIZON_UWP_FRAME_PROBE /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 "/AI$metadata" "/I$(Join-Path $repo 'uwp/dx11')" /c (Join-Path $repo $source) "/Fo$obj"
    if ($LASTEXITCODE -ne 0) { throw "Compiling $source failed ($LASTEXITCODE)" }
    $objects += $obj
}
$lib = Join-Path $repo 'uwp/window-bridge/rust/target/x86_64-uwp-windows-msvc/release/horizon_uwp_winit_probe.lib'
& (Join-Path $repo 'uwp/rust-std/strict-link.ps1') -Objects $objects -RustLibrary $lib -Output (Join-Path $out 'HorizonUWP.exe') -ExtraLibraries @('d3d11.lib', 'dxgi.lib')
