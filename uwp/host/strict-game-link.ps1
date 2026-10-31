$ErrorActionPreference = 'Stop'
$linker = Join-Path $PSScriptRoot '..\rust-std\strict-link.ps1'
& $linker -Objects @('App.obj', 'Dx11Renderer.obj', 'CoreWindowEvents.obj') `
    -RustLibrary $env:HORIZON_UWP_GAME_LIB -Output (Join-Path (Get-Location).Path 'HorizonUWP.exe') `
    -Machine $env:GAME_MACHINE -ExtraLibraries @('d3d11.lib', 'dxgi.lib')
if (-not $?) { exit 1 }
