param(
    [Parameter(Mandatory)][string[]]$Objects,
    [Parameter(Mandatory)][string]$RustLibrary,
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet('X86', 'X64', 'ARM64', 'ARM')][string]$Machine = 'X64',
    [string[]]$ExtraLibraries = @(),
    [ValidateRange(0, 1073741824)][long]$StackReserve = 0,
    [switch]$LargeAddressAware
)

# Run in a UWP MSVC developer environment. The sentinel object resolves
# references in Rust std objects which are discarded by /OPT:REF; it must
# never supply a function used by the resulting program.
$ErrorActionPreference = 'Stop'
$outputPath = [IO.Path]::GetFullPath($Output)
$outputDir = [IO.Path]::GetDirectoryName($outputPath)
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$stubObj = Join-Path $outputDir 'dead-import-sentinels.obj'
$mapPath = "$outputPath.map"
& cl /nologo /c /MD /O2 /Gy /Gw "/Fo$stubObj" (Join-Path $PSScriptRoot 'dead-import-sentinels.c')
if ($LASTEXITCODE -ne 0) { throw "Compiling link sentinels failed: $LASTEXITCODE" }

$linkArgs = @('/nologo', '/APPCONTAINER', '/SUBSYSTEM:WINDOWS', "/MACHINE:$Machine", '/OPT:REF',
    '/NODEFAULTLIB:kernel32.lib', "/MAP:$mapPath", "/OUT:$outputPath") +
    $Objects + @($stubObj, 'WindowsApp.lib', 'ntdll.lib') + $ExtraLibraries + @($RustLibrary)
# Rust's MSVC-target standard library can emit compiler-rt half-float helpers.
# The Rust static library intentionally leaves these intrinsic symbols for the
# final native link, so add the matching Microsoft compiler-rt archive when it
# is installed. Do not mix architectures or fall back to desktop CRT libraries.
$runtimeRelativePath = switch ($Machine) {
    'X86' { 'lib\x86\clang_rt.builtins-i386.lib' }
    'X64' { 'lib\x64\clang_rt.builtins-x86_64.lib' }
    'ARM64' { 'lib\arm64\clang_rt.builtins-aarch64.lib' }
    'ARM' { 'lib\arm\clang_rt.builtins-arm.lib' }
}
if ($runtimeRelativePath -and $env:VCToolsInstallDir) {
    $compilerRuntime = Join-Path $env:VCToolsInstallDir $runtimeRelativePath
    if (Test-Path -LiteralPath $compilerRuntime -PathType Leaf) {
        $linkArgs += $compilerRuntime
    }
}
if ($StackReserve -gt 0) { $linkArgs += "/STACK:$StackReserve" }
if ($LargeAddressAware) { $linkArgs += '/LARGEADDRESSAWARE' }
& link @linkArgs
if ($LASTEXITCODE -ne 0) { throw "Strict UWP link failed: $LASTEXITCODE" }
if (Select-String -Path $mapPath -Pattern 'dead-import-sentinels\.obj|forbidden_api' -Quiet) {
    # Make it impossible to mistake this artifact for a safe strict link.
    Remove-Item -LiteralPath $outputPath -Force
    throw "The linked program uses a forbidden desktop API sentinel; inspect $mapPath."
}
Write-Host "Strict UWP link succeeded; no diagnostic sentinel remains in $mapPath"
