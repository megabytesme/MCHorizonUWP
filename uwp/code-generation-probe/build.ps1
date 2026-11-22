param(
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64', 'arm')][string]$Architecture,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$SigningThumbprint,
    [string]$WasmtimeDll,
    [string]$WasmtimeImportLibrary
)

$ErrorActionPreference = 'Stop'
$outputPath = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputPath) { throw 'Pass a fresh output directory.' }
$cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningThumbprint"
if (-not $cert.HasPrivateKey) { throw 'The signing certificate has no private key.' }
$publisher = [Security.SecurityElement]::Escape($cert.Subject)
$sdk = if ($Architecture -eq 'arm64') { '10.0.26100.0' } else { '10.0.19041.0' }
$bin = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\$sdk\x64"
$useWasmtime = $WasmtimeDll -or $WasmtimeImportLibrary
$nativeArgs = @()
if ($useWasmtime) {
    if ($Architecture -ne 'x64' -or !$WasmtimeDll -or !$WasmtimeImportLibrary) {
        throw 'Supply both strict-linked Wasmtime files for the x64 Pulley probe.'
    }
    $WasmtimeDll = (Resolve-Path -LiteralPath $WasmtimeDll).Path
    $nativeArgs = @((Resolve-Path -LiteralPath $WasmtimeImportLibrary).Path)
}
$native = New-Item -ItemType Directory -Path (Join-Path $outputPath 'native')
& (Join-Path $PSScriptRoot 'build-native.cmd') $Architecture $native.FullName $sdk @nativeArgs
if ($LASTEXITCODE -ne 0) { throw "Strict UWP probe build failed (exit $LASTEXITCODE)." }

$imports = Get-Content -LiteralPath (Join-Path $native.FullName 'imports.txt') -Raw
if ($useWasmtime) {
    $requiredImports = @('wasmtime_uwp_probe', [IO.Path]::GetFileName($WasmtimeDll))
    $probeName = 'Horizon Wasmtime Pulley probe'
    $variants = @('Pulley')
} else {
    $requiredImports = @('VirtualAllocFromApp', 'VirtualProtectFromApp', 'FlushInstructionCache', 'VirtualFree')
    $probeName = 'Horizon executable memory probe'
    $variants = @('Enabled', 'Control')
}
foreach ($api in $requiredImports) {
    $api = [regex]::Escape($api)
    if ($imports -notmatch "\b$api\b") { throw "Expected UWP import missing: $api" }
}
if ($imports -match '\b(VirtualAlloc|VirtualProtect)\b|(?im)^\s+KERNEL32\.dll\s*$') {
    throw 'Unexpected desktop memory import.'
}

foreach ($variant in $variants) {
    $identity = "MCHorizonUWP.CodeGenerationProbe.$variant.$Architecture"
    $capability = if ($variant -eq 'Enabled') {
        '<Capabilities><Capability Name="codeGeneration" /></Capabilities>'
    } else { '' }
    $layout = New-Item -ItemType Directory -Path (Join-Path $outputPath "$variant/layout")
    Copy-Item -LiteralPath (Join-Path $native.FullName 'Probe.exe') -Destination $layout.FullName
    if ($useWasmtime) { Copy-Item -LiteralPath $WasmtimeDll -Destination $layout.FullName }
    $assets = New-Item -ItemType Directory -Path (Join-Path $layout.FullName 'Assets')
    foreach ($logo in 'StoreLogo.png', 'Square150x150Logo.png', 'Square44x44Logo.png') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../host/Assets/$logo") -Destination $assets.FullName
    }
    @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" IgnorableNamespaces="uap">
  <Identity Name="$identity" Publisher="$publisher" Version="1.0.0.0" ProcessorArchitecture="$Architecture" />
  <Properties>
    <DisplayName>$probeName ($variant)</DisplayName>
    <PublisherDisplayName>Horizon</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.16299.0" MaxVersionTested="10.0.26100.0" />
    <PackageDependency Name="Microsoft.VCLibs.140.00" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" MinVersion="14.0.0.0" />
  </Dependencies>
  <Resources><Resource Language="en-us" /></Resources>
  <Applications>
    <Application Id="App" Executable="Probe.exe" EntryPoint="Probe.App">
      <uap:VisualElements DisplayName="$probeName ($variant)" Description="UWP runtime probe" BackgroundColor="#14243A" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>
  $capability
</Package>
"@ | Set-Content -LiteralPath (Join-Path $layout.FullName 'AppxManifest.xml') -Encoding UTF8
    $appx = Join-Path $outputPath "$identity.appx"
    & (Join-Path $bin 'makeappx.exe') pack /o /d $layout.FullName /p $appx
    if ($LASTEXITCODE -ne 0) { throw "Package creation failed (exit $LASTEXITCODE)." }
    & (Join-Path $bin 'signtool.exe') sign /fd SHA256 /sha1 $SigningThumbprint $appx
    if ($LASTEXITCODE -ne 0) { throw "Package signing failed (exit $LASTEXITCODE)." }
    & (Join-Path $bin 'signtool.exe') verify /pa $appx
    if ($LASTEXITCODE -ne 0) { throw "Package signature verification failed (exit $LASTEXITCODE)." }
    Get-FileHash -LiteralPath $appx
}
