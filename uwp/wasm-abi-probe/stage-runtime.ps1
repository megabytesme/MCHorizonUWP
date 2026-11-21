param(
    [Parameter(Mandatory)][string]$Workspace,
    [Parameter(Mandatory)][string]$WasmtimeArchive,
    [string]$Git = 'git',
    [switch]$VendorOnly,
    [switch]$LegacyArm32
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $Workspace).Path
$archive = (Resolve-Path -LiteralPath $WasmtimeArchive).Path
$wasmtimeHash = '9e32e3ce9a15d2ddedfeae38437a4fe52ed9c59ac878e3131e5a840068faf44e'
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant() -ne $wasmtimeHash) {
    throw 'Use the authenticated crates.io archive for Wasmtime 50.0.2.'
}
$enginePatch = Join-Path $PSScriptRoot 'horizon-uwp-wasm.patch'
$runtimePatch = Join-Path $PSScriptRoot '../code-generation-probe/wasmtime/wasmtime-50.0.2-pulley-uwp.patch'
$legacyPatch = Join-Path $PSScriptRoot 'wasmtime-pulley-legacy-arm32.patch'
$vendorRoot = Join-Path $source 'third_party'
$vendor = Join-Path $vendorRoot 'wasmtime-50.0.2'
$config = Join-Path $source '.cargo/wasmtime-uwp.toml'
$lock = Join-Path $source 'uwp/game-bridge/Cargo.lock'
$lockText = [IO.File]::ReadAllText($lock).Replace("`r`n", "`n")
$local = "name = `"wasmtime`"`nversion = `"50.0.2`"`n"
$registry = $local + "source = `"registry+https://github.com/rust-lang/crates.io-index`"`nchecksum = `"$wasmtimeHash`"`n"
if (!$lockText.Contains($registry) -and !$lockText.Contains($local + 'dependencies = [')) {
    throw 'The UWP bridge lockfile must pin Wasmtime 50.0.2.'
}
# Apply only within the supplied assembly or vendored crate, even under a parent Git worktree.
$previousCeiling = $env:GIT_CEILING_DIRECTORIES
try {
    $env:GIT_CEILING_DIRECTORIES = Split-Path -Parent $source
    if (!$VendorOnly) {
        & $Git -C $source apply --check --ignore-space-change $enginePatch 2>$null
        if ($LASTEXITCODE -eq 0) {
            & $Git -C $source apply --ignore-space-change --whitespace=error-all $enginePatch
            if ($LASTEXITCODE -ne 0) { throw 'Unable to apply the Horizon Wasm runtime integration.' }
        } else {
            & $Git -C $source apply --reverse --check --ignore-space-change $enginePatch
            if ($LASTEXITCODE -ne 0) { throw 'Use a fresh pinned 0.1.6 assembly for the Horizon Wasm overlay.' }
        }
    }
    $env:GIT_CEILING_DIRECTORIES = $vendorRoot
    if (!(Test-Path -LiteralPath $vendor)) {
        $stageRoot = Join-Path $vendorRoot ('.wasmtime-incoming-' + [Guid]::NewGuid().ToString('N'))
        $stagedVendor = Join-Path $stageRoot 'wasmtime-50.0.2'
        $null = New-Item -ItemType Directory -Path $stageRoot
        try {
            & tar.exe -xf $archive -C $stageRoot
            if ($LASTEXITCODE -ne 0) { throw 'Could not unpack the authenticated Wasmtime archive.' }
            & $Git -C $stagedVendor apply --whitespace=error-all $runtimePatch
            if ($LASTEXITCODE -ne 0) { throw 'Could not apply the Wasmtime Pulley patch.' }
            if ($LegacyArm32) {
                & $Git -C $stagedVendor apply --whitespace=error-all $legacyPatch
                if ($LASTEXITCODE -ne 0) { throw 'Could not apply the legacy ARM32 Pulley patch.' }
            }
            [IO.Directory]::Move($stagedVendor, $vendor)
        } finally {
            $actualStage = (Resolve-Path -LiteralPath $stageRoot).Path
            if ($actualStage -eq [IO.Path]::GetFullPath($stageRoot) -and
                (Split-Path -Parent $actualStage) -eq (Resolve-Path -LiteralPath $vendorRoot).Path) {
                Remove-Item -LiteralPath $actualStage -Force -Recurse
            }
        }
    } else {
        $expectedPatch = if ($LegacyArm32) { $legacyPatch } else { $runtimePatch }
        & $Git -C $vendor apply --reverse --check $expectedPatch
        if ($LASTEXITCODE -ne 0) { throw 'The existing Wasmtime directory has an unknown patch state.' }
    }
} finally {
    $env:GIT_CEILING_DIRECTORIES = $previousCeiling
}

$encoding = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($config, "[patch.crates-io]`nwasmtime = { path = `"third_party/wasmtime-50.0.2`" }`n", $encoding)
[IO.File]::WriteAllText($lock, $lockText.Replace($registry, $local), $encoding)
Write-Output $config
