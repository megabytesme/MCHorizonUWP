$ErrorActionPreference = 'Stop'
$linker = Join-Path $PSScriptRoot '..\strict-link.ps1'
$library = Join-Path $PSScriptRoot 'target\x86_64-uwp-windows-msvc\release\uwp_fs_probe.lib'
& $linker -Objects @('App.obj', 'Dx11Renderer.obj') -RustLibrary $library `
    -Output (Join-Path (Get-Location).Path 'HorizonUWP.exe')
