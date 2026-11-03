$ErrorActionPreference = 'Stop'
$linker = Join-Path $PSScriptRoot '..\rust-std\strict-link.ps1'
$objects = @('App.obj', 'Dx11Renderer.obj', 'CoreWindowEvents.obj')
$libraries = @('d3d11.lib', 'dxgi.lib')
if ($env:HORIZON_UWP_XAUDIO) { $objects += 'XAudioStream.obj'; $libraries += 'xaudio2.lib' }
if ($env:HORIZON_UWP_HTTP) { $objects += 'HttpBridge.obj' }
if ($env:HORIZON_UWP_GAMEPAD) { $objects += 'Gamepad.obj' }
if ($env:HORIZON_UWP_DEVICE_VIBRATION) { $objects += 'DeviceVibration.obj' }
& $linker -Objects $objects `
    -RustLibrary $env:HORIZON_UWP_GAME_LIB -Output (Join-Path (Get-Location).Path 'HorizonUWP.exe') `
    -Machine $env:GAME_MACHINE -ExtraLibraries $libraries
if (-not $?) { exit 1 }
