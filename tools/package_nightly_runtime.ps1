param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Bridge,
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet('x64', 'x86', 'arm64', 'arm')][string]$Architecture = 'x64',
    [ValidatePattern('^MCHorizonUWP[.-][A-Za-z0-9.-]+$')][string]$Identity = 'MCHorizonUWP.NightlyModRuntime',
    [string]$Version = '1.0.1.0',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$PatchVersion = '0.1.7',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$ClientVersion = '0.1.8',
    [Parameter(Mandatory)][string]$ResourceFramework,
    [string]$WasmStandardLibrary,
    [string]$AngleBin,
    [string]$HostExecutable
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $Source).Path
$bridge = (Resolve-Path -LiteralPath $Bridge).Path
$output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
if (-not (Test-Path -LiteralPath "$bridge.map" -PathType Leaf) -or
    (Select-String -LiteralPath "$bridge.map" -Pattern 'dead-import-sentinels\.obj|forbidden_api' -Quiet)) {
    throw 'A successful strict bridge link and its map are required.'
}
if (-not $HostExecutable) { $HostExecutable = Join-Path $source "uwp/host/prebuilt/$Architecture/HorizonUWP.exe" }
$framework = @(Get-AppxPackage -Name $ResourceFramework | Where-Object { $_.Name -ceq $ResourceFramework -and $_.IsFramework })
if ($framework.Count -ne 1) { throw 'The exact resource framework must be installed for this account.' }
$certificates = @(Get-ChildItem Cert:/CurrentUser/My | Where-Object {
    $_.Subject -ceq $framework[0].Publisher -and $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date)
})
if ($certificates.Count -ne 1) { throw 'A single usable resource-framework signing certificate is required.' }
$certificate = $certificates[0]
$null = New-Item -ItemType Directory -Path $output
$release = [ordered]@{
    schema_version = 1
    patch_version = $PatchVersion
    client_version = $ClientVersion
    upstream_commit = '0b14f8acfd97fa2c750e42795249dcc981178ca4'
}
$metadata = Join-Path $output 'HorizonRelease.json'
$release | ConvertTo-Json | Set-Content -LiteralPath $metadata -Encoding utf8
$options = @{
    Architecture = $Architecture
    Game = $true
    Executable = $HostExecutable
    GameBridge = $bridge
    OutputDirectory = $output
    ResourceFrameworkName = $ResourceFramework
    IdentityName = $Identity
    Publisher = $certificate.Subject
    WinUIFramework = '2.7'
    PackageVersion = $Version
    ReleaseMetadataPath = $metadata
}
if ($WasmStandardLibrary) { $options.WasmStandardLibrary = (Resolve-Path -LiteralPath $WasmStandardLibrary).Path }
if ($AngleBin) {
    $options.AngleBin = (Resolve-Path -LiteralPath $AngleBin).Path
    $options.RequireAngle = $true
}
& "$source/uwp/host/package.ps1" @options
'Private Nightly runtime test' | Set-Content -LiteralPath "$output/package/horizon-isolated-test.flag" -Encoding ascii
$sdkBin = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin/10.0.26100.0/x64'
$appx = Join-Path $output "HorizonUWP-$Architecture.appx"
& "$sdkBin/makeappx.exe" pack /o /d "$output/package" /p $appx *> "$output/pack-isolated.log"
if ($LASTEXITCODE) { throw "Isolated APPX packing failed. See $output/pack-isolated.log." }
& "$sdkBin/signtool.exe" sign /fd SHA256 /s My /sha1 $certificate.Thumbprint $appx
if ($LASTEXITCODE) { throw 'APPX signing failed.' }
& "$sdkBin/signtool.exe" verify /pa $appx
if ($LASTEXITCODE) { throw 'APPX signature verification failed.' }
Get-FileHash -LiteralPath $appx,$bridge,$HostExecutable -Algorithm SHA256 |
    Export-Csv -LiteralPath "$output/sha256.csv" -NoTypeInformation
Get-Item -LiteralPath $appx | Select-Object FullName,Length
