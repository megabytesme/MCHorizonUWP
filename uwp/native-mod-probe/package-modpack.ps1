param(
    [Parameter(Mandatory)][string]$MainAppx,
    [Parameter(Mandatory)][string]$StagedSources,
    [Parameter(Mandatory)][string]$DllDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][version]$Version,
    [Parameter(Mandatory)][string]$SdkRoot,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$SigningThumbprint,
    [switch]$VerifyTrustedChain,
    [switch]$AllowSameMainVersion
)

$ErrorActionPreference = 'Stop'
$sdkVersion = '10.0.19041.0'
$makeappx = Join-Path $SdkRoot "bin\$sdkVersion\x64\makeappx.exe"
$signtool = Join-Path $SdkRoot "bin\$sdkVersion\x64\signtool.exe"
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningThumbprint"
if (-not $certificate.HasPrivateKey) { throw 'The selected certificate has no signing key.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path -Leaf $Program) exited with code $LASTEXITCODE." }
}

function Assert-Machine([string]$Path, [int]$Machine) {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($Path))
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Invalid PE file $Path" }
        $reader.BaseStream.Position = 0x3c
        $offset = $reader.ReadUInt32()
        if ($offset -lt 0x40 -or $offset + 6 -gt $reader.BaseStream.Length) {
            throw "Invalid PE header location in $Path"
        }
        $reader.BaseStream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne $Machine) {
            throw "Invalid native DLL architecture in $Path"
        }
    } finally { $reader.Dispose() }
}

function Assert-Signed([string]$Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        if (-not $archive.GetEntry('AppxSignature.p7x')) { throw "Missing package signature in $Path" }
    } finally { $archive.Dispose() }
    if ($VerifyTrustedChain) { Invoke-Checked $signtool @('verify', '/pa', $Path) }
}

function Sign-Package([string]$Path) {
    Invoke-Checked $signtool @('sign', '/fd', 'SHA256', '/sha1', $SigningThumbprint, $Path)
    Assert-Signed $Path
}

function Measure-MainBinaries([string]$Path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($name in @('HorizonUWP.exe', 'horizon_uwp_game_bridge.dll')) {
            $entry = $zip.GetEntry($name)
            if (-not $entry) { throw "The main package is missing $name" }
            $stream = $entry.Open()
            try { $hash = [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose() }
            [pscustomobject]@{ File = $name; Sha256 = $hash }
        }
    } finally { $sha256.Dispose(); $zip.Dispose() }
}

$main = (Resolve-Path -LiteralPath $MainAppx).Path
$staged = (Resolve-Path -LiteralPath $StagedSources).Path
$dlls = (Resolve-Path -LiteralPath $DllDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Choose a fresh output directory $output" }
if ($Version.Revision -lt 0 -or $Version.Major -lt 1 -or
    @($Version.Major, $Version.Minor, $Version.Build, $Version.Revision).Where({ $_ -gt 65535 }).Count) {
    throw 'Use an increasing four-field APPX version.'
}
$record = Get-Content -LiteralPath "$staged\build-record.json" -Raw | ConvertFrom-Json
$machines = @{ x86 = 0x14c; x64 = 0x8664; arm64 = 0xaa64 }
$architecture = $record.architecture
$buildPattern = '^mchorizonuwp-[0-9]+\.[0-9]+\.[0-9]+-patch-[0-9]+\.[0-9]+\.[0-9]+-' +
    $architecture + '-0b14f8acfd97fa2c750e42795249dcc981178ca4$'
if ($architecture -notin $machines.Keys -or $record.game_build_id -cnotmatch $buildPattern -or
    $record.rust_toolchain -cne '1.99.1' -or -not $record.mods.Count) {
    throw 'Expected a staged x64, x86 or ARM64 trusted native modpack build.'
}

Assert-Signed $main
$baseBinaries = @(Measure-MainBinaries $main)
New-Item -Path $output -ItemType Directory | Out-Null
$mainStage = Join-Path $output 'main'
Invoke-Checked $makeappx @('unpack', '/p', $main, '/d', $mainStage)
$mainManifest = Join-Path $mainStage 'AppxManifest.xml'
[xml]$manifest = Get-Content -LiteralPath $mainManifest -Raw
$identity = $manifest.Package.Identity
$minimum = [version]'10.0.16299.0'
$baseVersion = [version]$identity.Version
if ($Version -lt $baseVersion -or ($Version -eq $baseVersion -and -not $AllowSameMainVersion) -or
    $identity.Publisher -cne $certificate.Subject -or
    $identity.ProcessorArchitecture -cne $architecture) {
    throw 'The version must increase and the architecture and publisher must match the signed game.'
}
$optionalName = "$($identity.Name).NativeMods"
if ($optionalName.Length -gt 50) { throw 'The game identity is too long for an optional modpack identity.' }
$identity.Version = $Version.ToString()
foreach ($family in $manifest.Package.Dependencies.TargetDeviceFamily) {
    if ([version]$family.MinVersion -lt $minimum) { $family.MinVersion = $minimum.ToString() }
    if ([version]$family.MaxVersionTested -lt $minimum) { $family.MaxVersionTested = $minimum.ToString() }
}
$manifest.Save($mainManifest)
foreach ($file in @('AppxSignature.p7x', 'AppxBlockMap.xml', '[Content_Types].xml', 'AppxMetadata\CodeIntegrity.cat')) {
    $path = Join-Path $mainStage $file
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}

$optionalStage = Join-Path $output 'optional'
New-Item -Path "$optionalStage\Assets" -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\..\host\Assets\StoreLogo.png" -Destination "$optionalStage\Assets"
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$libraries = @(foreach ($module in $record.mods) {
    $id = $module.id
    if ($id -cnotmatch '^[a-z0-9_]{1,64}$' -or -not $seen.Add($id) -or $module.version -cnotmatch '^\S+$' -or
        $module.dll -cne "horizon_uwp_mod_$id.dll" -or
        $module.identity -cne "$($record.game_build_id) $id $($module.version) 1.99.1") {
        throw 'Unexpected or duplicate native build record entry.'
    }
    $dll = Join-Path $dlls $module.dll
    Assert-Machine $dll $machines[$architecture]
    Copy-Item -LiteralPath $dll -Destination $optionalStage
    $snapshot = Join-Path $optionalStage "horizon-native-mods\$id"
    New-Item -Path "$snapshot\src" -ItemType Directory -Force | Out-Null
    foreach ($relative in @('Cargo.toml', 'horizon-mod.toml', 'src/lib.rs')) {
        $from = Join-Path $staged "horizon-native-mods\$id\$relative"
        if ((Get-FileHash -LiteralPath $from).Hash.ToLowerInvariant() -cne $module.source_sha256.$relative) {
            throw "Staged source hash mismatch for $id/$relative."
        }
        Copy-Item -LiteralPath $from -Destination (Join-Path $snapshot $relative)
    }
    [ordered]@{ mod = $id; identity = $module.identity
       sha256 = (Get-FileHash -LiteralPath (Join-Path $optionalStage $module.dll)).Hash.ToLowerInvariant() }
})
$inventory = @{ libraries = $libraries } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText("$optionalStage\native-mods.json", $inventory, [Text.UTF8Encoding]::new($false))
$publisher = [Security.SecurityElement]::Escape($identity.Publisher)
@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3" IgnorableNamespaces="uap3">
  <Identity Name="$optionalName" Publisher="$publisher" Version="$Version" ProcessorArchitecture="$architecture" />
  <Properties>
    <DisplayName>Horizon UWP native mods</DisplayName>
    <PublisherDisplayName>Horizon UWP</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="$minimum" MaxVersionTested="$sdkVersion" />
    <uap3:MainPackageDependency Name="$($identity.Name)" />
  </Dependencies>
  <Resources><Resource Language="en-us" /></Resources>
</Package>
"@ | Set-Content -LiteralPath "$optionalStage\AppxManifest.xml" -Encoding UTF8

Push-Location -LiteralPath $output
try {
    Invoke-Checked $makeappx @('pack', '/d', $optionalStage, '/p', 'optional.appx')
    @('[Files]', '"optional.appx" "optional.appx"') | Set-Content -LiteralPath 'optional.txt' -Encoding ASCII
    Invoke-Checked $makeappx @('bundle', '/bv', $Version.ToString(), '/f', 'optional.txt', '/p', 'optional.appxbundle')
    Sign-Package (Join-Path $output 'optional.appxbundle')
    Invoke-Checked $makeappx @('pack', '/d', $mainStage, '/p', 'main.appx')
    $relatedBinaries = @(Measure-MainBinaries (Join-Path $output 'main.appx'))
    if (Compare-Object $baseBinaries $relatedBinaries -Property File,Sha256) {
        throw 'The related-set update changed a main game binary.'
    }
    $relatedBinaries | Export-Csv -LiteralPath 'main-binary-sha256.csv' -NoTypeInformation
    @('[Files]', '"main.appx" "main.appx"', '', '[ExternalPackages]',
        '"optional.appxbundle" "optional.appxbundle"') | Set-Content -LiteralPath 'main.txt' -Encoding ASCII
    Invoke-Checked $makeappx @('bundle', '/bv', $Version.ToString(), '/f', 'main.txt', '/p', 'main.appxbundle')
    Sign-Package (Join-Path $output 'main.appxbundle')
    Get-Item -LiteralPath 'main.appxbundle', 'optional.appxbundle' | Get-FileHash -Algorithm SHA256 |
        Export-Csv -LiteralPath 'sha256.csv' -NoTypeInformation
} finally { Pop-Location }
