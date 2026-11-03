$ErrorActionPreference = 'Stop'
$linker = Join-Path $PSScriptRoot '..\rust-std\strict-link.ps1'
$objects = @('App.obj', 'Dx11Renderer.obj', 'CoreWindowEvents.obj', 'Srv.obj', 'Picker.obj')
$libraries = @('d3d11.lib', 'dxgi.lib')
if ($env:HORIZON_UWP_DX12) { $libraries += 'd3d12.lib' }
$output = Join-Path (Get-Location).Path 'HorizonUWP.exe'
$stackReserve = 8MB
if ($env:HORIZON_UWP_XAUDIO) { $objects += 'XAudioStream.obj'; $libraries += 'xaudio2.lib' }
if ($env:HORIZON_UWP_HTTP) { $objects += 'HttpBridge.obj' }
if ($env:HORIZON_UWP_ACCOUNT) {
    $objects += 'CredentialStore.obj'
    if ($env:HORIZON_UWP_XAML_SHELL) { $objects += 'XamlShell.obj' }
    else { $objects += 'NativeDialog.obj'; $libraries += @('d2d1.lib', 'dwrite.lib') }
}
if ($env:HORIZON_UWP_GAMEPAD) { $objects += 'Gamepad.obj' }
if ($env:HORIZON_UWP_DEVICE_VIBRATION) { $objects += 'DeviceVibration.obj' }
& $linker -Objects $objects `
    -RustLibrary $env:HORIZON_UWP_GAME_LIB -Output $output `
    -Machine $env:GAME_MACHINE -ExtraLibraries $libraries -StackReserve $stackReserve
if (-not $?) { exit 1 }

# A small UI-thread stack caused Game::new to overflow before the game could
# show an error. Check the PE header so either architecture cannot silently
# regress to the linker's 1 MiB default.
$stream = [IO.File]::OpenRead($output)
$reader = [IO.BinaryReader]::new($stream)
try {
    if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Linked output is not a PE file.' }
    $stream.Position = 0x3c
    $pe = $reader.ReadUInt32()
    if ($pe + 104 -gt $stream.Length) { throw 'Invalid PE header.' }
    $stream.Position = $pe
    if ($reader.ReadUInt32() -ne 0x4550) { throw 'Invalid PE signature.' }
    $optional = $pe + 24
    $stream.Position = $optional
    $magic = $reader.ReadUInt16()
    $stream.Position = $optional + 72
    $reserve = switch ($magic) {
        0x10b { $reader.ReadUInt32() }
        0x20b { $reader.ReadUInt64() }
        default { throw 'Unknown PE optional header.' }
    }
    if ($reserve -lt $stackReserve) {
        throw "Linked UWP game stack reserve is too small: $reserve"
    }
    Write-Host "Verified UWP game stack reserve: $reserve bytes"
} finally { $reader.Dispose() }
