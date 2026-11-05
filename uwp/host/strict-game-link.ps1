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
if ($env:HORIZON_UWP_TOUCH_MOD) { $objects += 'Gyrometer.obj' }
if ($env:HORIZON_UWP_DEVICE_VIBRATION) { $objects += 'DeviceVibration.obj' }
& $linker -Objects $objects `
    -RustLibrary $env:HORIZON_UWP_GAME_LIB -Output $output `
    -Machine $env:GAME_MACHINE -ExtraLibraries $libraries -StackReserve $stackReserve `
    -LargeAddressAware:($env:GAME_MACHINE -eq 'X86')
if (-not $?) { exit 1 }

# The renderer is optional at runtime. A static D3D12 import would make the
# whole app fail before the availability probe on devices without that DLL.
if ($env:HORIZON_UWP_DX12) {
    $imports = & dumpbin.exe /nologo /imports $output
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect UWP executable imports.' }
    $forbidden = @('d3d12.dll')
    if ($env:GAME_MACHINE -eq 'ARM') {
        # Mobile builds target older Windows versions and use XAML surfaces;
        # desktop HWND queries and a static DXGI dependency are not needed.
        $forbidden += @('user32.dll', 'dxgi.dll')
    }
    if ($env:GAME_MACHINE -eq 'ARM' -and ($imports -match '^\s*[0-9A-F]+\s+SetThreadErrorMode\s*$')) {
        throw 'The ARM renderer introduced a static SetThreadErrorMode import unavailable to older UWP versions.'
    }
    foreach ($dll in $forbidden) {
        if ($imports -match ('^\s*' + [regex]::Escape($dll) + '\s*$')) {
            throw "Optional renderer introduced a static $dll import."
        }
    }
    Write-Host 'Verified optional renderer has no unsupported static imports.'
}

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
    $stream.Position = $pe + 22
    $characteristics = $reader.ReadUInt16()
    if ($env:GAME_MACHINE -eq 'X86' -and -not ($characteristics -band 0x20)) {
        throw 'The x86 game must be large-address-aware.'
    }
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
    if ($env:GAME_MACHINE -eq 'X86') { Write-Host 'Verified x86 large-address-aware flag.' }
} finally { $reader.Dispose() }
