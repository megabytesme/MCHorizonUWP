param(
    [Parameter(Mandatory)][string]$HorizonSource,
    [string]$CargoHorizonMod,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'

$source = (Resolve-Path -LiteralPath $HorizonSource).Path
foreach ($relative in @('Cargo.toml', 'crates/horizon-mod-api/Cargo.toml', 'crates/horizon-mod-wasm/Cargo.toml')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $relative) -PathType Leaf)) {
        throw "The selected Horizon source snapshot is missing $relative."
    }
}

if (!$PrepareOnly) {
    if (!$CargoHorizonMod) { throw 'Pass the cargo-horizon-mod executable built from this Nightly.' }
    $tool = (Resolve-Path -LiteralPath $CargoHorizonMod).Path
    if (!(Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'cargo-horizon-mod is not a file.' }
}

$local = Join-Path $source '.local'
$null = New-Item -Path $local -ItemType Directory -Force
$work = Join-Path $local ('uwp-wasm-abi-probe-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $PSScriptRoot 'guest'
Copy-Item -LiteralPath $fixture -Destination $work -Recurse
if ($PrepareOnly) {
    Get-Item -LiteralPath $work
    return
}

$guestStd = Join-Path $work 'target/horizon-mod/horizon-wasm-std.wasm'
$guestPackage = Join-Path $work 'target/horizon-mod/uwp_wasm_hook_probe.hmod'
Push-Location -LiteralPath $work
try {
    & $tool horizon-mod wasm-std $guestStd
    if ($LASTEXITCODE -ne 0) { throw 'Building the Horizon shared Wasm standard library failed.' }
    & $tool horizon-mod build --wasm
    if ($LASTEXITCODE -ne 0) { throw 'Building the Horizon GUI hook package failed.' }
    Resolve-Path -LiteralPath $guestStd, $guestPackage
} finally {
    Pop-Location
}
