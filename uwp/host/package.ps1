param(
    [ValidateSet('x64', 'arm64', 'arm')][string]$Architecture = 'x64',
    [string]$AngleBin,
    [switch]$RequireAngle,
    [switch]$Game
)
$ErrorActionPreference = 'Stop'
function Assert-PEArchitecture([string]$Path, [string]$TargetArchitecture) {
    $machine = @{ x64 = 0x8664; arm64 = 0xaa64; arm = 0x1c4 }[$TargetArchitecture]
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Not a PE file: $Path" }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 0 -or $offset + 6 -gt $stream.Length) { throw "Invalid PE header: $Path" }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne $machine) {
            throw "PE architecture does not match ${TargetArchitecture}: $Path"
        }
    } finally { $reader.Dispose() }
}
$hostDir = $PSScriptRoot
$build = Join-Path $hostDir 'build'
if ($Architecture -ne 'x64') { $build = Join-Path $build $Architecture }
$stage = Join-Path $build 'package'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $hostDir)
if (-not $AngleBin) {
    $candidate = switch ($Architecture) {
        'x64' { Join-Path $repositoryRoot '.local\vcpkg-x64\installed\x64-uwp-local\bin' }
        'arm64' { Join-Path $repositoryRoot '.local\vcpkg\installed\arm64-uwp-local\bin' }
    }
    if ($candidate -and (Test-Path -LiteralPath $candidate)) { $AngleBin = $candidate }
}
$angleFiles = @()
if ($AngleBin) {
    foreach ($name in @('libEGL.dll', 'libGLESv2.dll')) {
        $file = Join-Path $AngleBin $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing ANGLE DLL: $file" }
        Assert-PEArchitecture $file $Architecture
        $angleFiles += $file
    }
    $zlib = Join-Path $AngleBin 'z.dll'
    if (Test-Path -LiteralPath $zlib -PathType Leaf) {
        Assert-PEArchitecture $zlib $Architecture
        $angleFiles += $zlib
    }
} elseif ($RequireAngle) {
    throw "ANGLE DLLs were not found for $Architecture. Build them or pass -AngleBin."
}
$executable = if ($Game) {
    Join-Path (Join-Path (Join-Path $hostDir 'build') "game-$Architecture") 'HorizonUWP.exe'
} else {
    Join-Path $build 'HorizonUWP.exe'
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Host executable not found: $executable" }
Assert-PEArchitecture $executable $Architecture
New-Item -ItemType Directory -Force -Path $stage,(Join-Path $stage 'Assets') | Out-Null
Copy-Item -LiteralPath $executable -Destination $stage -Force
# Staging persists between builds; remove stale DLLs before copying validated inputs.
foreach ($name in @('libEGL.dll', 'libGLESv2.dll', 'z.dll')) {
    $old = Join-Path $stage $name
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
}
foreach ($file in $angleFiles) { Copy-Item -LiteralPath $file -Destination $stage -Force }
if ($AngleBin) { Write-Host "Staged ANGLE DLLs from $AngleBin" }
$manifest = (Get-Content (Join-Path $hostDir 'AppxManifest.xml') -Raw).Replace('ProcessorArchitecture="x64"', "ProcessorArchitecture=`"$Architecture`"")
Set-Content -Path (Join-Path $stage 'AppxManifest.xml') -Value $manifest -Encoding UTF8
Add-Type -AssemblyName System.Drawing
foreach ($logo in @(@('StoreLogo.png',50), @('Square150x150Logo.png',150), @('Square44x44Logo.png',44))) {
    $bitmap = [System.Drawing.Bitmap]::new($logo[1], $logo[1])
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::FromArgb(20, 36, 58))
    $bitmap.Save((Join-Path $stage (Join-Path 'Assets' $logo[0])), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
$makeAppx = '${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
$makeAppx = [Environment]::ExpandEnvironmentVariables($makeAppx.Replace('${env:ProgramFiles(x86)}', '%ProgramFiles(x86)%'))
$package = Join-Path $build "HorizonUWP-$Architecture.appx"
& $makeAppx pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed ($LASTEXITCODE)" }
Write-Host "Unsigned package: $package"
