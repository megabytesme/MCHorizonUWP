param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$Toolchain = 'stable'
)

# Never modify the installed rust-src or overwrite a previous source copy.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Destination)
$library = Join-Path $root 'library'
if (Test-Path -LiteralPath $library) {
    throw "A Rust source copy already exists at $library. Choose another destination."
}
$sysroot = (& rustup run $Toolchain rustc --print sysroot).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not locate toolchain $Toolchain" }
$source = Join-Path $sysroot 'lib\rustlib\src\rust\library'
if (-not (Test-Path -LiteralPath $source)) { throw "rust-src is not installed in $Toolchain" }
New-Item -ItemType Directory -Force -Path $root | Out-Null
Copy-Item -LiteralPath $source -Destination $library -Recurse
$patch = Join-Path $PSScriptRoot 'stdio-uwp.patch'
Push-Location $root
try {
    & git apply --check $patch
    if ($LASTEXITCODE -ne 0) { throw 'The UWP stdio patch does not match this rust-src.' }
    & git apply $patch
    if ($LASTEXITCODE -ne 0) { throw 'Could not apply the UWP stdio patch.' }
} finally { Pop-Location }
Write-Host "Patched Rust source copy: $library"
Write-Host "Set __CARGO_TESTS_ONLY_SRC_ROOT to this directory before using cargo -Z build-std."
