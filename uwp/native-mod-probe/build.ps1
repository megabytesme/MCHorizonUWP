param(
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64', 'arm')][string]$Architecture,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$MsvcToolset,
    [Parameter(Mandatory)][string]$SdkRoot,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$SigningThumbprint
)

$ErrorActionPreference = 'Stop'
$sdkVersion = '10.0.19041.0'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Choose a fresh output directory: $output" }
New-Item -Path $output -ItemType Directory | Out-Null
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningThumbprint"
if (-not $certificate.HasPrivateKey) { throw 'The selected certificate has no signing key.' }
$publisher = [Security.SecurityElement]::Escape($certificate.Subject)
$identity = 'HorizonNativeModProbe'
$optionalIdentity = "$identity.Optional"
$minimum = if ($Architecture -eq 'arm') { '10.0.15063.0' } else { '10.0.16299.0' }
$msvcBin = Join-Path $MsvcToolset "bin\Hostx64\$Architecture"
$sdkBin = Join-Path $SdkRoot "bin\$sdkVersion\x64"
$cl = Join-Path $msvcBin 'cl.exe'
$link = Join-Path $msvcBin 'link.exe'
$lib = Join-Path $msvcBin 'lib.exe'
$dumpbin = Join-Path $msvcBin 'dumpbin.exe'
$makeappx = Join-Path $sdkBin 'makeappx.exe'
$signtool = Join-Path $sdkBin 'signtool.exe'
$machine = $Architecture.ToUpperInvariant()
$sdkInclude = Join-Path $SdkRoot "Include\$sdkVersion"
$sdkLib = Join-Path $SdkRoot "Lib\$sdkVersion"
$msvcLib = Join-Path $MsvcToolset "lib\$Architecture"
$includes = @("$MsvcToolset\include", "$sdkInclude\ucrt", "$sdkInclude\um", "$sdkInclude\shared", "$sdkInclude\winrt") |
    ForEach-Object { "/I$_" }
$commonCompile = @('/nologo', '/c', '/O2', '/DWINAPI_FAMILY=WINAPI_FAMILY_APP', '/D_WIN32_WINNT=0x0A00') + $includes
$libpaths = @("$msvcLib\store", $msvcLib, "$sdkLib\ucrt\$Architecture", "$sdkLib\um\$Architecture") |
    ForEach-Object { "/LIBPATH:$_" }

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path -Leaf $Program) failed with exit code $LASTEXITCODE." }
}

function Write-MainManifest([string]$Path, [string]$Version) {
    @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" IgnorableNamespaces="uap">
  <Identity Name="$identity" Publisher="$publisher" Version="$Version" ProcessorArchitecture="$Architecture"/>
  <Properties>
    <DisplayName>Horizon native DLL probe</DisplayName>
    <PublisherDisplayName>Horizon test build</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="$minimum" MaxVersionTested="$sdkVersion"/>
    <PackageDependency Name="Microsoft.VCLibs.140.00" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" MinVersion="14.0.0.0"/>
  </Dependencies>
  <Resources><Resource Language="en-us"/></Resources>
  <Applications>
    <Application Id="App" Executable="HorizonNativeModProbe.exe" EntryPoint="HorizonNativeModProbe.App">
      <uap:VisualElements DisplayName="Horizon native DLL probe" Description="Load a test DLL from the package graph"
                          BackgroundColor="#14243A" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png"/>
    </Application>
  </Applications>
</Package>
"@ | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-OptionalManifest([string]$Path) {
    @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3" IgnorableNamespaces="uap3">
  <Identity Name="$optionalIdentity" Publisher="$publisher" Version="1.0.0.0" ProcessorArchitecture="$Architecture"/>
  <Properties>
    <DisplayName>Horizon native DLL probe payload</DisplayName>
    <PublisherDisplayName>Horizon test build</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="$minimum" MaxVersionTested="$sdkVersion"/>
    <uap3:MainPackageDependency Name="$identity"/>
  </Dependencies>
  <Resources><Resource Language="en-us"/></Resources>
</Package>
"@ | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Sign-Package([string]$Path) {
    Invoke-Checked $signtool @('sign', '/fd', 'SHA256', '/sha1', $SigningThumbprint, $Path)
}

Push-Location -LiteralPath $output
try {
    $sdkWinmd = Join-Path $SdkRoot "UnionMetadata\$sdkVersion"
    Invoke-Checked $cl ($commonCompile + @('/MD', '/EHsc', '/ZW', "/AI$sdkWinmd",
        "/AI$MsvcToolset\lib\x86\store\references", '/Foapp.obj', (Join-Path $PSScriptRoot 'app.cpp')))
    $loader = @()
    if ($Architecture -eq 'arm') {
        $definition = Join-Path $PSScriptRoot 'phone-loader.def'
        Invoke-Checked $lib @('/NOLOGO', '/MACHINE:ARM', "/DEF:$definition", '/OUT:phone-loader.lib')
        $loader = @('phone-loader.lib')
    }
    Invoke-Checked $link (@('/NOLOGO', '/SUBSYSTEM:WINDOWS', '/APPCONTAINER', '/NODEFAULTLIB:kernel32.lib',
        "/MACHINE:$machine", '/OUT:HorizonNativeModProbe.exe', '/MAP:app.map', 'app.obj') +
        $loader + @('WindowsApp.lib') + $libpaths)
    Invoke-Checked $cl ($commonCompile + @('/GS-', '/Fopayload.obj', (Join-Path $PSScriptRoot 'payload.cpp')))
    Invoke-Checked $link @('/NOLOGO', '/DLL', '/NOENTRY', '/APPCONTAINER', '/NODEFAULTLIB',
        "/MACHINE:$machine", '/OUT:HorizonNativeModProbePayload.dll', 'payload.obj')
    Invoke-Checked $dumpbin @('/nologo', '/headers', '/imports', 'HorizonNativeModProbe.exe') |
        Set-Content -LiteralPath 'app-pe.txt'
    Invoke-Checked $dumpbin @('/nologo', '/headers', '/imports', '/exports', 'HorizonNativeModProbePayload.dll') |
        Set-Content -LiteralPath 'payload-pe.txt'
    if ($Architecture -eq 'arm' -and -not (Select-String -LiteralPath 'app-pe.txt' -Pattern 'PhoneAppModelHost\.dll' -Quiet)) {
        throw 'The Mobile probe did not link the PhoneAppModelHost DLL loader.'
    }

    $main = New-Item -ItemType Directory -Path 'main'
    $logos = Join-Path $PSScriptRoot '..\host\Assets'
    $mainAssets = New-Item -ItemType Directory -Path (Join-Path $main 'Assets')
    foreach ($logo in @('StoreLogo.png', 'Square150x150Logo.png', 'Square44x44Logo.png')) {
        Copy-Item -LiteralPath (Join-Path $logos $logo) -Destination $mainAssets.FullName
    }
    Copy-Item -LiteralPath 'HorizonNativeModProbe.exe' -Destination $main.FullName
    $mainManifest = Join-Path $main 'AppxManifest.xml'
    $mainAppx = Join-Path $output 'main.appx'
    if ($Architecture -eq 'arm') {
        Copy-Item -LiteralPath 'HorizonNativeModProbePayload.dll' -Destination $main.FullName
        Write-MainManifest $mainManifest '1.0.0.0'
        Invoke-Checked $makeappx @('pack', '/d', $main.FullName, '/p', $mainAppx)
        Sign-Package $mainAppx
    } else {
        Write-MainManifest $mainManifest '1.0.0.0'
        $standalone = Join-Path $output 'standalone.appx'
        Invoke-Checked $makeappx @('pack', '/d', $main.FullName, '/p', $standalone)
        Sign-Package $standalone

        Write-MainManifest $mainManifest '1.0.1.0'
        Invoke-Checked $makeappx @('pack', '/d', $main.FullName, '/p', $mainAppx)
        $optional = New-Item -ItemType Directory -Path 'optional'
        $optionalAssets = New-Item -ItemType Directory -Path (Join-Path $optional 'Assets')
        Copy-Item -LiteralPath (Join-Path $logos 'StoreLogo.png') -Destination $optionalAssets.FullName
        Copy-Item -LiteralPath 'HorizonNativeModProbePayload.dll' -Destination $optional.FullName
        Write-OptionalManifest (Join-Path $optional 'AppxManifest.xml')
        $optionalAppx = Join-Path $output 'optional.appx'
        Invoke-Checked $makeappx @('pack', '/d', $optional.FullName, '/p', $optionalAppx)

        $optionalBundle = Join-Path $output 'optional.appxbundle'
        @('[Files]', '"optional.appx" "optional.appx"') | Set-Content -LiteralPath 'optional.txt' -Encoding ASCII
        Invoke-Checked $makeappx @('bundle', '/bv', '1.0.0.0', '/f', 'optional.txt', '/p', $optionalBundle)
        Sign-Package $optionalAppx
        Sign-Package $optionalBundle

        $mainBundle = Join-Path $output 'main.appxbundle'
        @('[Files]', '"main.appx" "main.appx"', '', '[ExternalPackages]',
          '"optional.appxbundle" "optional.appxbundle"') | Set-Content -LiteralPath 'main.txt' -Encoding ASCII
        Invoke-Checked $makeappx @('bundle', '/bv', '1.0.1.0', '/f', 'main.txt', '/p', $mainBundle)
        Sign-Package $mainBundle
    }
    Get-ChildItem -File *.appx,*.appxbundle | Get-FileHash -Algorithm SHA256 |
        Export-Csv -LiteralPath 'sha256.csv' -NoTypeInformation
} finally {
    Pop-Location
}
