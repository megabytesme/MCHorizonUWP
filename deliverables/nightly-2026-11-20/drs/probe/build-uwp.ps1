param(
    [ValidateSet('arm','arm64')][string]$Architecture = 'arm',
    [string]$Publisher = 'CN=MCHorizonWgpuWorldScaleProbe',
    [Parameter(Mandatory)][string]$SourceRoot,
    [string]$OutputRoot = (Join-Path $env:TEMP 'mchorizon-arm-hud-wgpu')
)
$ErrorActionPreference = 'Stop'
$sdk = 'C:\Program Files (x86)\Windows Kits\10'
$sdkVersion = '10.0.19041.0'
$linkSdkVersion = if ($Architecture -eq 'arm') { '10.0.15063.0' } else { $sdkVersion }
$vc = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207'
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$rustLib = Join-Path $OutputRoot "wgpu-world-scale-probe-$Architecture.lib"
$hostKit = Join-Path $SourceRoot "uwp\host\prebuilt\$Architecture"
$LogoDirectory = Join-Path $SourceRoot 'uwp\host\Assets'
$sysroot = & rustup run 1.99.1-x86_64-pc-windows-msvc rustc --print sysroot
if ($LASTEXITCODE) { throw 'Pinned rustc is unavailable.' }
$lld = "$sysroot\lib\rustlib\x86_64-pc-windows-msvc\bin\rust-lld.exe"
$out = Join-Path $OutputRoot "uwp-$Architecture"
$stage = Join-Path $out 'stage'
$assets = Join-Path $stage 'Assets'
$null = New-Item -ItemType Directory -Force -Path $assets
$env:TMP = $out
$env:TEMP = $out
$env:INCLUDE = "$vc\include;$sdk\Include\$sdkVersion\ucrt;$sdk\Include\$sdkVersion\um;$sdk\Include\$sdkVersion\shared;$sdk\Include\$sdkVersion\winrt"
$env:LIB = "$hostKit\link;$vc\lib\$Architecture\store;$vc\lib\$Architecture;$sdk\Lib\$linkSdkVersion\ucrt\$Architecture;$sdk\Lib\$linkSdkVersion\um\$Architecture"
$tools = "$vc\bin\Hostx64\$Architecture"
$compile = @('/nologo','/c','/std:c++17','/EHsc','/O2','/MD','/DWINAPI_FAMILY=WINAPI_FAMILY_APP','/D_WIN32_WINNT=0x0A00')
$machine = $Architecture.ToUpperInvariant()
$minimum = if ($Architecture -eq 'arm') { '10.0.15063.0' } else { '10.0.16299.0' }
$publisherXml = [Security.SecurityElement]::Escape($Publisher)
$package = Join-Path $out "WgpuWorldScaleProbe-$Architecture.unsigned.appx"

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path -Leaf $Program) failed with exit code $LASTEXITCODE."
    }
}

Push-Location -LiteralPath $out
try {
    Invoke-Checked "$tools\cl.exe" ($compile + @('/ZW', "/AI$sdk\UnionMetadata\$sdkVersion",
        "/AI$vc\lib\x86\store\references", '/Fouwp_shell.obj', "$PSScriptRoot\uwp_shell.cpp"))
    Invoke-Checked $lld @('-flavor','link','/NOLOGO','/Brepro','/SUBSYSTEM:WINDOWS','/APPCONTAINER',
        '/INCLUDE:_tls_used','/NODEFAULTLIB:kernel32.lib','/NODEFAULTLIB:libucrt.lib','/OPT:REF',
        "/MACHINE:$machine", '/OUT:stage\WgpuWorldScaleProbe.exe', '/MAP:app.map', 'uwp_shell.obj',
        $rustLib, "$hostKit\dead-import-sentinels.obj", 'WindowsApp.lib', 'ntdll.lib',
        'd3d11.lib','dxgi.lib','legacy_stdio_definitions.lib')
    if (Select-String -LiteralPath 'app.map' -Pattern 'dead-import-sentinels\.obj|forbidden_api' -Quiet) {
        throw 'Replay probe retained a forbidden API sentinel.'
    }
    Invoke-Checked "$tools\dumpbin.exe" @('/nologo','/headers','/imports','stage\WgpuWorldScaleProbe.exe') |
        Set-Content -LiteralPath 'app-pe.txt'
    foreach ($logo in @('StoreLogo.png','Square150x150Logo.png','Square44x44Logo.png')) {
        Copy-Item -LiteralPath (Join-Path $LogoDirectory $logo) -Destination $assets
    }
    @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" IgnorableNamespaces="uap">
  <Identity Name="MCHorizonUWP.WgpuWorldScaleProbe" Publisher="$publisherXml"
            Version="1.0.0.0" ProcessorArchitecture="$Architecture"/>
  <Properties>
    <DisplayName>Horizon world scale probe</DisplayName>
    <PublisherDisplayName>Horizon test build</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="$minimum" MaxVersionTested="$sdkVersion"/>
    <PackageDependency Name="Microsoft.VCLibs.140.00"
      Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" MinVersion="14.0.0.0"/>
  </Dependencies>
  <Resources><Resource Language="en-us"/></Resources>
  <Applications>
    <Application Id="App" Executable="WgpuWorldScaleProbe.exe" EntryPoint="WgpuWorldScaleProbe.App">
      <uap:VisualElements DisplayName="Horizon world scale probe" Description="Check world and HUD target sizes"
                          BackgroundColor="#14243A" Square150x150Logo="Assets\Square150x150Logo.png"
                          Square44x44Logo="Assets\Square44x44Logo.png"/>
    </Application>
  </Applications>
</Package>
"@ | Set-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Encoding UTF8
    Invoke-Checked "$sdk\bin\$sdkVersion\x64\makeappx.exe" @('pack','/o','/d',$stage,'/p',$package)
    Get-FileHash -LiteralPath $package -Algorithm SHA256
} finally {
    Pop-Location
}
