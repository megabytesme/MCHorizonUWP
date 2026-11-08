param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$SourceRoot,
    [string]$VisualStudioRoot = 'C:\Program Files\Microsoft Visual Studio\18\Community',
    [string]$Arm32VisualStudioRoot = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = Split-Path -Parent $PSScriptRoot }
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Choose a new output directory; existing data is left untouched: $output" }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("MCHorizonHostKit-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null

function Get-Toolset([string]$VsRoot) {
    $root = Join-Path $VsRoot 'VC\Tools\MSVC'
    $toolset = Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $toolset) { throw "No MSVC toolset found under $root" }
    return $toolset.FullName
}

function Copy-Required([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Required host-kit input is missing: $Source" }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination
}

function Write-BatchLiteral([string]$Value) {
    if ($Value.Contains('%')) { throw 'Build paths cannot contain percent signs.' }
    return $Value
}

$modernToolset = Get-Toolset $VisualStudioRoot
$armToolset = Get-Toolset $Arm32VisualStudioRoot
$architectures = @(
    @{ Name = 'x86'; Arch = 'x64_x86'; Machine = 'X86'; Vs = $VisualStudioRoot; Toolset = $modernToolset; Sdk = '10.0.19041.0' },
    @{ Name = 'x64'; Arch = 'x64'; Machine = 'X64'; Vs = $VisualStudioRoot; Toolset = $modernToolset; Sdk = '10.0.19041.0' },
    @{ Name = 'arm64'; Arch = 'x64_arm64'; Machine = 'ARM64'; Vs = $VisualStudioRoot; Toolset = $modernToolset; Sdk = '10.0.26100.0' },
    @{ Name = 'arm'; Arch = 'x64_arm'; Machine = 'ARM'; Vs = $Arm32VisualStudioRoot; Toolset = $armToolset; Sdk = '10.0.19041.0' }
)

try {
    foreach ($target in $architectures) {
        $name = $target.Name
        $architectureRoot = Join-Path $temporary $name
        $linkRoot = Join-Path $architectureRoot 'link'
        $buildRoot = Join-Path $temporary "native-$name"
        New-Item -ItemType Directory -Force -Path $linkRoot, $buildRoot | Out-Null
        $runtimeRoot = Join-Path $target.Toolset "lib\$name\store"
        foreach ($library in @('msvcrt.lib', 'vcruntime.lib', 'oldnames.lib')) {
            Copy-Required (Join-Path $runtimeRoot $library) (Join-Path $linkRoot $library)
        }
        $emptyCrt = Join-Path $temporary 'empty-libcmt.def'
        if (-not (Test-Path -LiteralPath $emptyCrt)) { Set-Content -LiteralPath $emptyCrt -Value "LIBRARY libcmt.lib`nEXPORTS`n" -Encoding ASCII }

        $buildBatch = Join-Path $temporary "build-$name.cmd"
        $vsRoot = Write-BatchLiteral $target.Vs
        $sourceRoot = Write-BatchLiteral $source
        $buildDirectory = Write-BatchLiteral $buildRoot
        $runtimeDirectory = Write-BatchLiteral $linkRoot
        $batch = @(
            '@echo off',
            "call `"$vsRoot\VC\Auxiliary\Build\vcvarsall.bat`" $($target.Arch) store $($target.Sdk)",
            'if errorlevel 1 exit /b %ERRORLEVEL%',
            'set HORIZON_UWP_PREPARED_ENV=1',
            "lib.exe /nologo /def:`"$sourceRoot\uwp\host\game-bridge-exports.def`" /machine:$($target.Machine) /out:`"$buildDirectory\horizon_uwp_game_bridge.lib`"",
            'if errorlevel 1 exit /b %ERRORLEVEL%',
            "set `"HORIZON_UWP_BUILD_DIR=$buildDirectory`"",
            "set `"HORIZON_UWP_VSROOT=$vsRoot`"",
            'set HORIZON_UWP_DYNAMIC_HOST=1',
            "set `"HORIZON_UWP_GAME_LIB=$buildDirectory\horizon_uwp_game_bridge.lib`"",
            'set HORIZON_UWP_XAML_SHELL=1',
            'set HORIZON_UWP_ACCOUNT=1',
            'set HORIZON_UWP_XAUDIO=1',
            'set HORIZON_UWP_HTTP=1',
            'set HORIZON_UWP_NATIVE_D3D11=1',
            'set HORIZON_UWP_ANGLE=1',
            'set HORIZON_UWP_TOUCH_MOD=1',
            'set HORIZON_UWP_DX12=1',
            'set HORIZON_UWP_DEVICE_VIBRATION=1',
            "call `"$sourceRoot\uwp\host\build-game.cmd`" $name",
            'if errorlevel 1 exit /b %ERRORLEVEL%',
            "cl.exe /nologo /c /Zl /GS- /Fo`"$runtimeDirectory\tls_index.obj`" `"$sourceRoot\uwp\host\tls-index.c`"",
            'if errorlevel 1 exit /b %ERRORLEVEL%',
            "lib.exe /nologo /def:`"$emptyCrt`" /machine:$($target.Machine) /out:`"$runtimeDirectory\libcmt.lib`"",
            'if errorlevel 1 exit /b %ERRORLEVEL%',
            'exit /b 0'
        )
        Set-Content -LiteralPath $buildBatch -Value $batch -Encoding ASCII
        $log = Join-Path $temporary "build-$name.log"
        $stderr = "$log.stderr"
        $process = Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" `
            -ArgumentList @('/d', '/c', "`"$buildBatch`"") -WorkingDirectory $source -Wait -PassThru -NoNewWindow `
            -RedirectStandardOutput $log -RedirectStandardError $stderr
        $buildExitCode = $process.ExitCode
        if ($buildExitCode -ne 0) {
            Get-Content -LiteralPath $log -Tail 80 | Write-Output
            if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Tail 40 | Write-Output }
            throw "The $name prebuilt UWP host build failed (exit code $buildExitCode)."
        }
        foreach ($file in @('HorizonUWP.exe', 'HorizonUWP.winmd', 'HorizonUWPHost.lib', 'dead-import-sentinels.obj')) {
            Copy-Required (Join-Path $buildRoot $file) (Join-Path $architectureRoot $file)
        }
        Write-Host "Generated the $name prebuilt host and runtime link kit."
    }

    # Only the compiled application hosts and project-owned link helpers are
    # distributed. MSVC headers and runtime libraries are provisioned from the
    # user's own Visual Studio Build Tools installation by the builder.
    foreach ($target in $architectures) {
        $linkRoot = Join-Path (Join-Path $temporary $target.Name) 'link'
        foreach ($name in @('msvcrt.lib', 'vcruntime.lib', 'oldnames.lib', '*.exp')) {
            Get-ChildItem -LiteralPath $linkRoot -Filter $name -File | Remove-Item -Force
        }
    }
    foreach ($path in @((Join-Path $temporary 'native-*'), (Join-Path $temporary 'build-*.cmd'),
                        (Join-Path $temporary 'build-*.log'), (Join-Path $temporary 'build-*.log.stderr'),
                        (Join-Path $temporary 'empty-libcmt.def'))) {
        Get-ChildItem -Path $path -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    }
    Set-Content -LiteralPath (Join-Path $temporary 'README.txt') -Encoding UTF8 -Value @'
MCHorizonUWP prebuilt host artifacts

This kit contains four project-built UWP host executables and project-owned
import/link helper artifacts. It intentionally excludes the Microsoft compiler,
headers, and runtime libraries. The builder provisions the selected target's
MSVC link libraries from Microsoft's installer on the user's device.
'@
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
    Move-Item -LiteralPath $temporary -Destination $output
    Write-Host "Host artifact kit ready: $output"
}
catch {
    if (Test-Path -LiteralPath $temporary) {
        Move-Item -LiteralPath $temporary -Destination (Join-Path ([IO.Path]::GetTempPath()) ("MCHorizonHostKit-failed-" + [guid]::NewGuid().ToString('N')))
    }
    throw
}
