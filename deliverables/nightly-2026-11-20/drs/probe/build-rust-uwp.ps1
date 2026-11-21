param(
    [ValidateSet('arm','arm64')][string]$Architecture = 'arm64',
    [Parameter(Mandatory)][string]$CargoTargetRoot,
    [string]$OutputRoot = (Join-Path $env:TEMP 'mchorizon-arm-hud-wgpu')
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $CargoTargetRoot).Path
$target = if ($Architecture -eq 'arm64') { 'aarch64-uwp-windows-msvc' } else { 'thumbv7a-uwp-windows-msvc' }
$deps = Join-Path $root "$target\release\deps"
$hostDeps = Join-Path $root 'release\deps'
$extern = @()
foreach ($crate in @('std','panic_abort','log','pollster','wgpu')) {
    $files = @(Get-ChildItem -LiteralPath $deps -Filter "lib$crate-*.rlib" -File)
    if ($files.Count -ne 1) { throw "Expected one pinned $crate rlib for $Architecture, got $($files.Count)." }
    $extern += @('--extern', "$crate=$($files[0].FullName)")
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$null = New-Item -ItemType Directory -Force -Path $OutputRoot
$out = Join-Path $OutputRoot "wgpu-world-scale-probe-$Architecture.lib"
& rustup run 1.99.1-x86_64-pc-windows-msvc rustc "$PSScriptRoot\world-scale-probe.rs" --edition 2024 --crate-type staticlib --target $target -C panic=abort -C opt-level=2 -L "dependency=$deps" -L "dependency=$hostDeps" @extern --print native-static-libs -o $out
if ($LASTEXITCODE) { throw "rustc failed with $LASTEXITCODE." }
