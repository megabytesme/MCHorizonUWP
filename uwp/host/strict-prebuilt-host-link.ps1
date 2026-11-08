param(
    [ValidateSet('x86', 'x64', 'arm64', 'arm')][string]$Architecture = 'x64',
    [Parameter(Mandatory)][string]$BridgeImportLibrary
)

$ErrorActionPreference = 'Stop'
$machine = @{ x86 = 'X86'; x64 = 'X64'; arm64 = 'ARM64'; arm = 'ARM' }[$Architecture]
$objects = @('App.obj', 'Dx11Renderer.obj', 'CoreWindowEvents.obj', 'Srv.obj', 'Picker.obj')
$libraries = @('d3d11.lib', 'dxgi.lib')
if ($env:HORIZON_UWP_DX12) { $libraries += 'd3d12.lib' }
if ($env:HORIZON_UWP_XAUDIO) { $objects += 'XAudioStream.obj'; $libraries += 'xaudio2.lib' }
if ($env:HORIZON_UWP_HTTP) { $objects += 'HttpBridge.obj' }
if ($env:HORIZON_UWP_ACCOUNT) {
    $objects += 'CredentialStore.obj'
    if ($env:HORIZON_UWP_XAML_SHELL) { $objects += 'XamlShell.obj' }
    else { $objects += 'NativeDialog.obj'; $libraries += @('d2d1.lib', 'dwrite.lib') }
}
if ($env:HORIZON_UWP_TOUCH_MOD) { $objects += 'Gyrometer.obj' }
if ($env:HORIZON_UWP_DEVICE_VIBRATION) { $objects += 'DeviceVibration.obj' }
$objects += 'dead-import-sentinels.obj'
foreach ($object in $objects) {
    if (-not (Test-Path -LiteralPath $object -PathType Leaf)) { throw "Prebuilt host object is missing: $object" }
}
$bridge = [IO.Path]::GetFullPath($BridgeImportLibrary)
if (-not (Test-Path -LiteralPath $bridge -PathType Leaf)) { throw 'The Rust bridge import library is missing.' }
$hostDefinition = Join-Path $PSScriptRoot 'host-exports.def'
$output = Join-Path (Get-Location).Path 'HorizonUWP.exe'
$map = "$output.map"
$importLibrary = Join-Path (Get-Location).Path 'HorizonUWPHost.lib'
$arguments = @('/NOLOGO', '/APPCONTAINER', '/SUBSYSTEM:WINDOWS', "/MACHINE:$machine", '/OPT:REF',
    '/NODEFAULTLIB:kernel32.lib', '/STACK:8388608', "/MAP:$map", "/OUT:$output",
    "/WINMD", '/WINMDFILE:HorizonUWP.winmd', "/DEF:$hostDefinition", "/IMPLIB:$importLibrary") + $objects + @('WindowsApp.lib', 'ntdll.lib') + $libraries + @($bridge)
if ($machine -eq 'X86') { $arguments += '/LARGEADDRESSAWARE' }
& link.exe @arguments
if ($LASTEXITCODE -ne 0) { throw "Prebuilt UWP host link failed (exit code $LASTEXITCODE)." }
if (-not (Test-Path -LiteralPath (Join-Path (Get-Location).Path 'HorizonUWP.winmd') -PathType Leaf)) {
    Remove-Item -LiteralPath $output -Force
    throw 'The prebuilt host did not emit its HorizonUWP activation metadata.'
}
if (Select-String -LiteralPath $map -Pattern 'dead-import-sentinels\.obj|forbidden_api' -Quiet) {
    Remove-Item -LiteralPath $output -Force
    throw 'The prebuilt UWP host uses a forbidden desktop API sentinel.'
}
$imports = (& dumpbin.exe /nologo /dependents $output) -join "`n"
if ($LASTEXITCODE -ne 0 -or $imports -notmatch '(?i)horizon_uwp_game_bridge\.dll') {
    Remove-Item -LiteralPath $output -Force
    throw 'The prebuilt host does not import the versioned Rust game bridge.'
}
Write-Host "Strict prebuilt UWP host link succeeded for $Architecture."
