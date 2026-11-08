param(
    [ValidateSet('x86', 'x64', 'arm64', 'arm')][string]$Architecture = 'x64',
    [string]$AngleBin,
    [string]$AngleLicense,
    [string]$ZlibLicense,
    [switch]$RequireAngle,
    [switch]$Game,
    [string]$Executable,
    [string]$GameBridge,
    [string]$OutputDirectory,
    [string]$Resources,
    [ValidatePattern('^Horizon\.Asset\.[0-9-]+\.[0-9a-f]{20,30}$')][string]$ResourceFrameworkName,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$')][string]$IdentityName,
    [string]$Publisher,
    [ValidateSet('2.7', '2.8')][string]$WinUIFramework,
    [string]$PackageVersion,
    [string]$ReleaseNotes,
    [string]$ReleaseMetadataPath
)
$ErrorActionPreference = 'Stop'
if (-not $WinUIFramework) { $WinUIFramework = if ($Architecture -eq 'arm') { '2.7' } else { '2.8' } }
if ($Resources -and $ResourceFrameworkName) { throw 'Choose embedded resources or a resource framework, not both.' }
if ($ResourceFrameworkName -and $ResourceFrameworkName.Length -gt 50) { throw 'Resource framework identity is too long.' }
function Assert-PEArchitecture([string]$Path, [string]$TargetArchitecture) {
    $machine = @{ x86 = 0x14c; x64 = 0x8664; arm64 = 0xaa64; arm = 0x1c4 }[$TargetArchitecture]
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Not a PE file: $Path" }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 0 -or $offset + 6 -gt $stream.Length) { throw "Invalid PE header: $Path" }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne $machine) {
            throw "DLL architecture does not match ${TargetArchitecture}: $Path"
        }
    } finally { $reader.Dispose() }
}
$hostDir = $PSScriptRoot
$build = Join-Path $hostDir 'build'
if ($Architecture -ne 'x64') { $build = Join-Path $build $Architecture }
if ($OutputDirectory) { $build = [IO.Path]::GetFullPath($OutputDirectory) }
if (-not $Executable) {
    if ($Game) { $Executable = Join-Path (Join-Path (Join-Path $hostDir 'build') "game-$Architecture") 'HorizonUWP.exe' }
    else { $Executable = Join-Path $build 'HorizonUWP.exe' }
}
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Missing executable: $Executable" }
Assert-PEArchitecture $Executable $Architecture
$metadata = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($Executable))) 'HorizonUWP.winmd'
if (-not (Test-Path -LiteralPath $metadata -PathType Leaf)) {
    throw "Missing HorizonUWP.winmd activation metadata next to the UWP host: $metadata"
}
$stage = Join-Path $build 'package'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $hostDir)
if (-not $AngleBin) {
    $candidate = switch ($Architecture) {
        'arm' { Join-Path $repositoryRoot '.local\vcpkg-arm32\installed\arm-uwp-local\bin' }
        'x86' { Join-Path $repositoryRoot '.local\vcpkg-x86\installed\x86-uwp-local\bin' }
        'x64' { Join-Path $repositoryRoot '.local\vcpkg-x64\installed\x64-uwp-local\bin' }
        'arm64' { Join-Path $repositoryRoot '.local\vcpkg\installed\arm64-uwp-local\bin' }
    }
    if ($candidate -and (Test-Path -LiteralPath $candidate)) { $AngleBin = $candidate }
}
$angleFiles = @()
if ($AngleBin) {
    $anglePrefix = Split-Path -Parent ([IO.Path]::GetFullPath($AngleBin))
    if (-not $AngleLicense) { $AngleLicense = Join-Path $anglePrefix 'share\angle\copyright' }
    if (-not (Test-Path -LiteralPath $AngleLicense -PathType Leaf)) { throw 'ANGLE license notice missing; pass -AngleLicense.' }
    foreach ($name in @('libEGL.dll', 'libGLESv2.dll')) {
        $file = Join-Path $AngleBin $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing ANGLE DLL: $file" }
        Assert-PEArchitecture $file $Architecture
        $angleFiles += $file
    }
    $zlib = Join-Path $AngleBin 'z.dll'
    # The ARM32 and x86 builds link zlib statically; its notice is still
    # required even though there is no separate z.dll in the package.
    if ($Architecture -in @('arm', 'x86')) {
        if (-not $ZlibLicense) { $ZlibLicense = Join-Path $anglePrefix 'share\zlib\copyright' }
        if (-not (Test-Path -LiteralPath $ZlibLicense -PathType Leaf)) { throw 'ANGLE zlib license notice missing; pass -ZlibLicense.' }
    }
    if (Test-Path -LiteralPath $zlib -PathType Leaf) {
        if (-not $ZlibLicense) { $ZlibLicense = Join-Path $anglePrefix 'share\zlib\copyright' }
        if (-not (Test-Path -LiteralPath $ZlibLicense -PathType Leaf)) { throw 'zlib license notice missing; pass -ZlibLicense.' }
        Assert-PEArchitecture $zlib $Architecture
        $angleFiles += $zlib
    }
} elseif ($RequireAngle) {
    throw "ANGLE DLLs were not found for $Architecture. Build them or pass -AngleBin."
}
New-Item -ItemType Directory -Force -Path $stage,(Join-Path $stage 'Assets') | Out-Null
Copy-Item -LiteralPath $Executable -Destination (Join-Path $stage 'HorizonUWP.exe') -Force
Copy-Item -LiteralPath $metadata -Destination (Join-Path $stage 'HorizonUWP.winmd') -Force
if ($GameBridge) {
    Assert-PEArchitecture $GameBridge $Architecture
    Copy-Item -LiteralPath $GameBridge -Destination (Join-Path $stage 'horizon_uwp_game_bridge.dll') -Force
}
$notesStage = Join-Path $stage 'horizon-release-notes.txt'
if (Test-Path -LiteralPath $notesStage) { throw 'Release-note staging already exists; use a fresh OutputDirectory.' }
if ($ReleaseNotes) {
    $notes = Get-Item -LiteralPath $ReleaseNotes -ErrorAction Stop
    if ($notes.PSIsContainer -or $notes.Length -gt 1MB) { throw 'Release notes must be a text file no larger than 1 MiB.' }
    Copy-Item -LiteralPath $notes.FullName -Destination $notesStage
}
$releaseMetadataStage = Join-Path $stage 'HorizonRelease.json'
if (Test-Path -LiteralPath $releaseMetadataStage) { Remove-Item -LiteralPath $releaseMetadataStage -Force }
if ($ReleaseMetadataPath) {
    $metadata = Get-Item -LiteralPath $ReleaseMetadataPath -ErrorAction Stop
    if ($metadata.PSIsContainer -or $metadata.Length -gt 4096) { throw 'Release metadata must be a JSON file no larger than 4 KiB.' }
    try { $release = Get-Content -LiteralPath $metadata.FullName -Raw | ConvertFrom-Json -ErrorAction Stop }
    catch { throw 'Release metadata is not valid JSON.' }
    if ($release.schema_version -ne 1 -or
        $release.patch_version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $release.client_version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $release.upstream_commit -cnotmatch '^[0-9a-fA-F]{40}$') {
        throw 'Release metadata does not contain a supported patch, client and upstream identity.'
    }
    Copy-Item -LiteralPath $metadata.FullName -Destination $releaseMetadataStage
}
# Staging persists between builds; remove stale DLLs before copying validated inputs.
foreach ($name in @('libEGL.dll', 'libGLESv2.dll', 'z.dll')) {
    $old = Join-Path $stage $name
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
}
foreach ($file in $angleFiles) { Copy-Item -LiteralPath $file -Destination $stage -Force }
if ($AngleBin) { Write-Host "Staged ANGLE DLLs from $AngleBin" }
$resourceStage = Join-Path $stage 'resources'
if (-not $Resources -and (Test-Path -LiteralPath $resourceStage)) {
    throw "Resource staging already exists; use a fresh OutputDirectory: $resourceStage"
}
if ($Resources) {
    $resourceRoot = [IO.Path]::GetFullPath($Resources)
    if (-not (Test-Path -LiteralPath (Join-Path $resourceRoot 'horizon-resources.json') -PathType Leaf)) {
        throw "Prepared resource manifest not found under $resourceRoot"
    }
    if (Test-Path -LiteralPath $resourceStage) {
        throw "Resource staging already exists; use a fresh OutputDirectory: $resourceStage"
    }
    Copy-Item -LiteralPath $resourceRoot -Destination $resourceStage -Recurse
    Write-Host "Staged user-provided resources from $resourceRoot"
}
# Include the notices for the controller code and identification databases in
# the binary distribution, including the BSD-3-Clause redistribution terms.
$licenseStage = Join-Path $stage 'licenses'
New-Item -ItemType Directory -Force -Path $licenseStage | Out-Null
$licenseFiles = @{
    'gilrs-core-MIT.txt' = 'third_party/gilrs-core/LICENSE-MIT'
    'SDL-GameControllerDB.txt' = 'third_party/controller-mappings/LICENSE'
    'USB-ID-Repository.txt' = 'third_party/usb-ids/LICENSE'
    'Pixelarticons-MIT.txt' = 'crates/horizon-theme/icons/pixel/LICENSE-pixelarticons.txt'
}
foreach ($name in $licenseFiles.Keys) {
    $source = Join-Path $repositoryRoot $licenseFiles[$name]
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing license: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $licenseStage $name) -Force
}
if ($AngleBin) {
    Copy-Item -LiteralPath $AngleLicense -Destination (Join-Path $licenseStage 'ANGLE.txt') -Force
    if ($ZlibLicense) { Copy-Item -LiteralPath $ZlibLicense -Destination (Join-Path $licenseStage 'zlib.txt') -Force }
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $stage 'THIRD_PARTY_NOTICES.md') -Force
$manifest = (Get-Content (Join-Path $hostDir 'AppxManifest.xml') -Raw).Replace('ProcessorArchitecture="x64"', "ProcessorArchitecture=`"$Architecture`"")
if ($IdentityName) {
    $manifest = $manifest.Replace('Name="HorizonUWPPrototype"', "Name=`"$IdentityName`"")
}
if ($Publisher) {
    $null = [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
    $manifestXml = [xml]$manifest
    $manifestXml.Package.Identity.Publisher = $Publisher
    $manifest = $manifestXml.OuterXml
}
if ($PackageVersion) {
    $parts = $PackageVersion.Split('.')
    if ($parts.Count -ne 4) { throw 'Package version must contain four numeric fields.' }
    foreach ($part in $parts) {
        [uint16]$value = 0
        if ($part -notmatch '^(0|[1-9][0-9]*)$' -or -not [uint16]::TryParse($part, [ref]$value)) {
            throw 'Package version fields must be numbers from 0 to 65535.'
        }
    }
    $manifestXml = [xml]$manifest
    $manifestXml.Package.Identity.Version = $PackageVersion
    $manifest = $manifestXml.OuterXml
}
if ($WinUIFramework) {
    $manifestXml = [xml]$manifest
    $namespace = $manifestXml.DocumentElement.NamespaceURI
    $dependency = $manifestXml.CreateElement('PackageDependency', $namespace)
    $dependency.SetAttribute('Name', "Microsoft.UI.Xaml.$WinUIFramework")
    $dependency.SetAttribute('Publisher', 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US')
    $dependency.SetAttribute('MinVersion', $(if ($WinUIFramework -eq '2.7') { '7.2208.15002.0' } else { '8.2501.31001.0' }))
    $manifestXml.Package.Dependencies.AppendChild($dependency) | Out-Null
    $manifest = $manifestXml.OuterXml
}
if ($ResourceFrameworkName) {
    $manifestXml = [xml]$manifest
    $namespace = $manifestXml.DocumentElement.NamespaceURI
    $dependency = $manifestXml.CreateElement('PackageDependency', $namespace)
    $dependency.SetAttribute('Name', $ResourceFrameworkName)
    $dependency.SetAttribute('Publisher', $manifestXml.Package.Identity.Publisher)
    $dependency.SetAttribute('MinVersion', '1.0.0.0')
    $manifestXml.Package.Dependencies.AppendChild($dependency) | Out-Null
    $manifest = $manifestXml.OuterXml
}
Set-Content -Path (Join-Path $stage 'AppxManifest.xml') -Value $manifest -Encoding UTF8
$brandAssets = Join-Path $hostDir 'Assets'
foreach ($name in @('StoreLogo.png', 'Square150x150Logo.png', 'Square44x44Logo.png', 'Wide310x150Logo.png', 'Square310x310Logo.png', 'WorldPlaceholder.png', 'ServerPlaceholder.png')) {
    $sourceLogo = Join-Path $brandAssets $name
    if (-not (Test-Path -LiteralPath $sourceLogo -PathType Leaf)) { throw "Missing Horizon logo asset: $sourceLogo" }
    Copy-Item -LiteralPath $sourceLogo -Destination (Join-Path $stage (Join-Path 'Assets' $name)) -Force
}
if ($env:HORIZON_UWP_PREPARED_ENV) {
    if (-not $env:WindowsSdkDir -or -not $env:WindowsSDKVersion) { throw 'The prepared SDK environment is incomplete.' }
    $sdkVersion = $env:WindowsSDKVersion.TrimEnd('\', '/')
    $makeAppx = Join-Path $env:WindowsSdkDir "bin\$sdkVersion\x64\makeappx.exe"
} else {
    $makeAppx = '${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
    $makeAppx = [Environment]::ExpandEnvironmentVariables($makeAppx.Replace('${env:ProgramFiles(x86)}', '%ProgramFiles(x86)%'))
}
$package = Join-Path $build "HorizonUWP-$Architecture.appx"
$makeLog = Join-Path $build "makeappx-$Architecture.log"
& $makeAppx pack /d $stage /p $package /o *> $makeLog
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed ($LASTEXITCODE); see $makeLog" }
Write-Host "Unsigned package: $package"
