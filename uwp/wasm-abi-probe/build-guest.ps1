param(
    [Parameter(Mandatory)][string]$HorizonSource,
    [string]$CargoHorizonMod,
    [ValidateSet('Hook', 'Trap', 'Limits')][string]$Fixture = 'Hook',
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
$fixtures = @{
    Hook = @{ Directory = 'guest'; Id = 'uwp_wasm_hook_probe' }
    Trap = @{ Directory = 'guest-trap'; Id = 'uwp_wasm_trap_probe' }
    Limits = @{ Directory = 'guest-limits'; Id = 'uwp_wasm_limits_probe' }
}
$fixtureDirectory = $fixtures[$Fixture].Directory
$fixtureId = $fixtures[$Fixture].Id
$fixtureSource = Join-Path $PSScriptRoot $fixtureDirectory
Copy-Item -LiteralPath $fixtureSource -Destination $work -Recurse
if ($PrepareOnly) {
    Get-Item -LiteralPath $work
    return
}

$guestStd = Join-Path $work 'target/horizon-mod/horizon-wasm-std.wasm'
$guestPackage = Join-Path $work "target/horizon-mod/$fixtureId.hmod"
Push-Location -LiteralPath $work
try {
    & $tool horizon-mod wasm-std $guestStd
    if ($LASTEXITCODE -ne 0) { throw 'Building the Horizon shared Wasm standard library failed.' }
    & $tool horizon-mod build --wasm
    if ($LASTEXITCODE -ne 0) { throw 'Building the Horizon Wasm fixture failed.' }
    Resolve-Path -LiteralPath $guestStd, $guestPackage
} finally {
    Pop-Location
}
