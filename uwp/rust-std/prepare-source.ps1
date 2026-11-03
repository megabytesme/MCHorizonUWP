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
$patches = @('stdio-uwp.patch', 'file-open-uwp.patch', 'module-uwp.patch', 'runtime-uwp.patch') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$previousCeiling = $env:GIT_CEILING_DIRECTORIES
# If the copy lives inside the Horizon checkout, prevent git apply from
# silently ignoring patch paths outside its repository-relative prefix.
$env:GIT_CEILING_DIRECTORIES = Split-Path -Parent $root
Push-Location $root
try {
    foreach ($patch in $patches) {
        # Git for Windows may check out patch files with CRLF while rustup's
        # source uses LF. Normalize a private copy, never the tracked patch.
        $normalized = Join-Path $root ("horizon-" + [IO.Path]::GetFileName($patch))
        $patchText = [IO.File]::ReadAllText($patch).Replace("`r`n", "`n")
        $stream = [IO.File]::Open($normalized, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try { $writer.Write($patchText) } finally { $writer.Dispose() }
        & git apply --check $normalized
        if ($LASTEXITCODE -ne 0) { throw "The UWP patch does not match this rust-src: $patch" }
        & git apply $normalized
        if ($LASTEXITCODE -ne 0) { throw "Could not apply the UWP patch: $patch" }
        & git apply --reverse --check $normalized
        if ($LASTEXITCODE -ne 0) { throw "The UWP patch was not applied: $patch" }
    }
} finally {
    Pop-Location
    $env:GIT_CEILING_DIRECTORIES = $previousCeiling
}
Write-Host "Patched Rust source copy: $library"
Write-Host "Set __CARGO_TESTS_ONLY_SRC_ROOT to this directory before using cargo -Z build-std."
