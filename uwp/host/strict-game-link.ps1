$ErrorActionPreference = 'Stop'
$linker = Join-Path $PSScriptRoot '..\rust-std\strict-link.ps1'
$objects = @('App.obj', 'Dx11Renderer.obj', 'CoreWindowEvents.obj')
$libraries = @('d3d11.lib', 'dxgi.lib')
if ($env:HORIZON_UWP_XAUDIO) { $objects += 'XAudioStream.obj'; $libraries += 'xaudio2.lib' }
& $linker -Objects $objects `
    -RustLibrary $env:HORIZON_UWP_GAME_LIB -Output (Join-Path (Get-Location).Path 'HorizonUWP.exe') `
    -Machine $env:GAME_MACHINE -ExtraLibraries $libraries
if (-not $?) { exit 1 }
