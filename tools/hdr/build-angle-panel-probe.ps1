param(
    [Parameter(Mandatory)][string]$AngleInclude,
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$Publisher,
    [string]$VisualCpp = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207',
    [string]$WindowsKits = 'C:\Program Files (x86)\Windows Kits\10',
    [string]$SdkVersion = '10.0.26100.0'
)

$ErrorActionPreference = 'Stop'
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path -Leaf $Program) failed ($LASTEXITCODE)." }
}

$AngleInclude = (Resolve-Path -LiteralPath $AngleInclude).Path
$RuntimeDirectory = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory.' }
$stage = Join-Path $OutputDirectory 'stage'
$assets = Join-Path $stage 'Assets'
$null = New-Item -ItemType Directory -Path $assets

$sdkInclude = Join-Path $WindowsKits "Include\$SdkVersion"
$sdkLib = Join-Path $WindowsKits "Lib\$SdkVersion"
$sdkBin = Join-Path $WindowsKits "bin\$SdkVersion\x64"
$msvcBin = Join-Path $VisualCpp 'bin\Hostx64\x64'
$oldInclude = $env:INCLUDE
$oldLib = $env:LIB
$env:INCLUDE = "$VisualCpp\include;$sdkInclude\um;$sdkInclude\ucrt;$sdkInclude\shared;$sdkInclude\winrt"
$env:LIB = "$VisualCpp\lib\x64\store;$sdkLib\um\x64;$sdkLib\ucrt\x64"

Push-Location -LiteralPath $OutputDirectory
try {
    Invoke-Checked "$msvcBin\cl.exe" @('/nologo', '/c', '/std:c++17', '/EHsc', '/O2', '/MD', '/ZW',
        '/DWINAPI_FAMILY=WINAPI_FAMILY_APP', '/D_WIN32_WINNT=0x0A00', "/I$AngleInclude",
        "/AI$WindowsKits\UnionMetadata\$SdkVersion", "/AI$VisualCpp\lib\x86\store\references",
        '/Foangle-panel-probe.obj', "$PSScriptRoot\angle-panel-probe.cpp")
    Invoke-Checked "$msvcBin\link.exe" @('/NOLOGO', '/SUBSYSTEM:WINDOWS', '/APPCONTAINER', '/MACHINE:X64',
        '/NODEFAULTLIB:kernel32.lib', '/OUT:stage\AnglePanelProbe.exe', 'angle-panel-probe.obj', 'WindowsApp.lib')

    $runtimeDlls = @('libEGL.dll', 'libGLESv2.dll')
    if (Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'z.dll')) { $runtimeDlls += 'z.dll' }
    foreach ($dll in $runtimeDlls) { Copy-Item -LiteralPath (Join-Path $RuntimeDirectory $dll) -Destination $stage }
    Get-FileHash -LiteralPath ($runtimeDlls | ForEach-Object { Join-Path $stage $_ }) -Algorithm SHA256 |
        ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path) } |
        Set-Content -LiteralPath 'runtime.sha256'
    foreach ($logo in @('StoreLogo.png', 'Square150x150Logo.png', 'Square44x44Logo.png')) {
        Copy-Item -LiteralPath "$PSScriptRoot\..\..\uwp\host\Assets\$logo" -Destination $assets
    }
    $publisherXml = [Security.SecurityElement]::Escape($Publisher)
    @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" IgnorableNamespaces="uap">
  <Identity Name="MCHorizonUWP.Hdr.AngleProbe" Publisher="$publisherXml" Version="1.0.0.0" ProcessorArchitecture="x64"/>
  <Properties><DisplayName>Horizon ANGLE panel probe</DisplayName><PublisherDisplayName>Horizon test build</PublisherDisplayName><Logo>Assets\StoreLogo.png</Logo></Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.16299.0" MaxVersionTested="$SdkVersion"/>
    <PackageDependency Name="Microsoft.VCLibs.140.00" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" MinVersion="14.0.0.0"/>
  </Dependencies>
  <Resources><Resource Language="en-us"/></Resources>
  <Applications><Application Id="App" Executable="AnglePanelProbe.exe" EntryPoint="AnglePanelProbe.App">
    <uap:VisualElements DisplayName="Horizon ANGLE panel probe" Description="Inspect EGL window-surface color support"
      BackgroundColor="#14243A" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png"/>
  </Application></Applications>
</Package>
"@ | Set-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Encoding UTF8
    Invoke-Checked "$sdkBin\makeappx.exe" @('pack', '/d', $stage, '/p', 'AnglePanelProbe.appx')
} finally {
    Pop-Location
    $env:INCLUDE = $oldInclude
    $env:LIB = $oldLib
}
