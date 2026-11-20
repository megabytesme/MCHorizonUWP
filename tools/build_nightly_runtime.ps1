param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$TargetDirectory,
    [ValidateSet('x64', 'x86', 'arm64', 'arm')][string]$Architecture = 'x64',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$PatchVersion = '0.1.7',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$ClientVersion = '0.1.8',
    [string[]]$AdditionalFeatures = @(),
    [ValidateRange(1, 256)][int]$Jobs = [Environment]::ProcessorCount,
    [string]$PreparedRustSource,
    [switch]$Check,
    [switch]$NativeMods
)

$ErrorActionPreference = 'Stop'
$commit = '0b14f8acfd97fa2c750e42795249dcc981178ca4'
$source = (Resolve-Path -LiteralPath $Source).Path
$output = [IO.Path]::GetFullPath($Output)
$targetDirectory = [IO.Path]::GetFullPath($TargetDirectory)
$kit = Join-Path $source "uwp/host/prebuilt/$Architecture"
$targets = @{
    x64 = 'x86_64-uwp-windows-msvc'
    x86 = 'i686-uwp-windows-msvc'
    arm64 = 'aarch64-uwp-windows-msvc'
    arm = 'thumbv7a-uwp-windows-msvc'
}
$target = $targets[$Architecture]
$targetKey = $target.Replace('-', '_')
$buildId = "mchorizonuwp-$ClientVersion-patch-$PatchVersion-$Architecture-$commit"
$toolchain = '1.99.1-x86_64-pc-windows-msvc'
$programX86 = [Environment]::GetFolderPath('ProgramFilesX86')
$vcRoot = Join-Path $programX86 'Microsoft Visual Studio/2022/BuildTools/VC'
$vc = Join-Path $vcRoot 'Tools/MSVC/14.44.35207'
$sdk = Join-Path $programX86 'Windows Kits/10'
$sdkVersion = '10.0.19041.0'
$llvm = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'LLVM/bin'
$sysroot = (& rustup run $toolchain rustc --print sysroot)
if ($LASTEXITCODE) { throw "Rust $toolchain is unavailable." }
$lld = Join-Path $sysroot 'lib/rustlib/x86_64-pc-windows-msvc/bin/rust-lld.exe'

foreach ($inputPath in @(
    "$source/uwp/game-bridge/Cargo.lock", "$kit/HorizonUWP.exe", "$kit/HorizonUWPHost.lib",
    "$kit/dead-import-sentinels.obj", "$kit/link/libcmt.lib", "$kit/link/tls_index.obj",
    "$vc/lib/$Architecture/store/vcruntime.lib", "$sdk/Lib/$sdkVersion/um/$Architecture/WindowsApp.lib",
    "$llvm/clang.exe", "$llvm/llvm-lib.exe", $lld
)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Missing build input: $inputPath" }
}
$features = @(
    'uwp-dx11-renderer', 'uwp-dx12-renderer', 'native-touch-mod', 'native-device-vibration-mod',
    'uwp-audio', 'uwp-https', 'uwp-controller', 'xaml-shell'
) + $AdditionalFeatures
if ($features.Where({ $_ -cnotmatch '^[a-z][a-z0-9-]+$' })) { throw 'A Cargo feature name is invalid.' }
$nativeModsRecord = $null
if ($NativeMods) {
    if ($Architecture -eq 'arm' -or $AdditionalFeatures.Count) {
        throw 'Build staged optional native mods for x64, x86 or ARM64 without game bridge feature switches.'
    }
    $nativeRecordPath = "$source/uwp/game-bridge/packaged-native/build-record.json"
    $nativeModsRecord = Get-Content -Raw -LiteralPath $nativeRecordPath | ConvertFrom-Json
    $lockHash = (Get-FileHash -LiteralPath "$source/uwp/game-bridge/Cargo.lock").Hash.ToLowerInvariant()
    if ($nativeModsRecord.game_build_id -cne $buildId -or $nativeModsRecord.rust_target -cne $target -or
        $nativeModsRecord.cargo_lock_sha256 -cne $lockHash -or -not $nativeModsRecord.mods.Count) {
        throw 'Stage native mod projects for this precise build and architecture before compiling.'
    }
    foreach ($mod in $nativeModsRecord.mods) {
        $id = $mod.id
        if ($id -cnotmatch '^[a-z0-9_]{1,64}$' -or $mod.cargo_package -cne ('horizon-uwp-native-' + $id.Replace('_', '-')) -or
            $mod.static_library -cne "horizon_uwp_mod_$id.lib" -or $mod.dll -cne "horizon_uwp_mod_$id.dll") {
            throw 'Unexpected staged native mod build output.'
        }
    }
}
$arguments = @("+$toolchain")
$wasmConfig = '.cargo/wasmtime-uwp.toml'
if (Test-Path -LiteralPath (Join-Path $source $wasmConfig) -PathType Leaf) {
    $arguments += @('--config', $wasmConfig)
} elseif ($features -contains 'uwp-wasm') {
    throw 'Stage the pinned Wasmtime source and bridge lockfile before building with uwp-wasm.'
}
if ($Architecture -eq 'arm') {
    $armConfig = '.local/arm32-windows-sys/patches-offline-arm32.toml'
    if (-not (Test-Path -LiteralPath (Join-Path $source $armConfig) -PathType Leaf)) {
        throw 'Prepare the ARM32 overrides and matching bridge Cargo.lock in a private ARM32 source snapshot first.'
    }
    $arguments += @('--config', $armConfig)
}
$command = if ($Check) { 'check' } else { 'build' }
$arguments += @($command, '--locked', '--offline', '--manifest-path', 'uwp/game-bridge/Cargo.toml',
    '-Z', 'build-std=std,panic_abort', '--target', $target,
    '--release', '--target-dir', $targetDirectory)
if ($NativeMods) {
    foreach ($mod in $nativeModsRecord.mods) { $arguments += @('-p', $mod.cargo_package) }
} else {
    $arguments += @('--features', ($features -join ','))
}

$null = New-Item -ItemType Directory -Force -Path $output, "$output/temp"
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$log = Join-Path $output "cargo-$command-$stamp.log"
$rustSource = if ($PreparedRustSource) { (Resolve-Path -LiteralPath $PreparedRustSource).Path } else { Join-Path $output 'rust-src' }
if (-not (Test-Path -LiteralPath "$rustSource/library/Cargo.toml" -PathType Leaf)) {
    & "$source/uwp/rust-std/prepare-source.ps1" -Destination $rustSource -Toolchain $toolchain
}
$env:TEMP = "$output/temp"
$env:TMP = $env:TEMP
$env:PATH = "$llvm;$sdk/bin/$sdkVersion/x64;$env:PATH"
$headers = @('ucrt', 'um', 'shared', 'winrt', 'cppwinrt') | ForEach-Object { "$sdk/Include/$sdkVersion/$_" }
$env:INCLUDE = (@("$vc/include") + $headers) -join ';'
$env:LIB = "$vc/lib/x64;$sdk/Lib/$sdkVersion/ucrt/x64;$sdk/Lib/$sdkVersion/um/x64"
$env:VCINSTALLDIR = "$vcRoot\"
$env:VCToolsInstallDir = "$vc\"
$env:WindowsSdkDir = "$sdk\"
$env:WindowsSDKVersion = "$sdkVersion\"
$env:UniversalCRTSdkDir = "$sdk\"
$env:UCRTVersion = $sdkVersion
$env:CC_x86_64_pc_windows_msvc = "$llvm/clang.exe"
$env:AR_x86_64_pc_windows_msvc = "$llvm/llvm-lib.exe"
$env:CFLAGS_x86_64_pc_windows_msvc = '-DWINAPI_FAMILY=WINAPI_FAMILY_DESKTOP_APP'
[Environment]::SetEnvironmentVariable("CC_${targetKey}", "$llvm/clang.exe")
[Environment]::SetEnvironmentVariable("AR_${targetKey}", "$llvm/llvm-lib.exe")
[Environment]::SetEnvironmentVariable("CFLAGS_${targetKey}", '-DWINAPI_FAMILY=WINAPI_FAMILY_APP')
$hostLink = Join-Path $output 'lld-host.cmd'
$uwpLink = Join-Path $output 'lld-uwp.cmd'
$hostFlags = "`"/LIBPATH:$vc/lib/x64`" `"/LIBPATH:$sdk/Lib/$sdkVersion/ucrt/x64`" `"/LIBPATH:$sdk/Lib/$sdkVersion/um/x64`""
$uwpFlags = "/APPCONTAINER /NODEFAULTLIB:kernel32.lib `"/LIBPATH:$kit/link`" `"/LIBPATH:$vc/lib/$Architecture/store`" `"/LIBPATH:$vc/lib/$Architecture`" `"/LIBPATH:$sdk/Lib/$sdkVersion/ucrt/$Architecture`" `"/LIBPATH:$sdk/Lib/$sdkVersion/um/$Architecture`""
@('@echo off', 'setlocal DisableDelayedExpansion', "`"$lld`" -flavor link /NOLOGO $hostFlags %*", 'exit /b %ERRORLEVEL%') |
    Set-Content -Encoding ascii -LiteralPath $hostLink
@('@echo off', 'setlocal DisableDelayedExpansion', "`"$lld`" -flavor link /NOLOGO $uwpFlags %*", 'exit /b %ERRORLEVEL%') |
    Set-Content -Encoding ascii -LiteralPath $uwpLink
$env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_LINKER = $hostLink
[Environment]::SetEnvironmentVariable("CARGO_TARGET_$($targetKey.ToUpperInvariant())_LINKER", $uwpLink)
$env:CARGO_BUILD_JOBS = [string]$Jobs
$env:RUSTC_BOOTSTRAP = '1'
$env:__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS = 'nightly'
$env:__CARGO_TESTS_ONLY_SRC_ROOT = Join-Path $rustSource 'library'
$env:HORIZON_VERSION = 'nightly-2026-11-18-0b14f8a'
$env:HORIZON_NIGHTLY = '2026-11-18 0b14f8a'
$env:HORIZON_UPSTREAM_COMMIT = $commit
$env:HORIZON_UWP_PORT_REF = "client-v$ClientVersion"
$env:HORIZON_GAME_BUILD_ID = $buildId
$env:HORIZON_UWP_XAML_SHELL = '1'

$record = [ordered]@{
    source = $source
    target = $target
    build_id = $env:HORIZON_GAME_BUILD_ID
    cargo_arguments = $arguments
    cargo_log = $log
}
$recordPath = Join-Path $output "build-$command-$stamp.json"
$record | ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8
Push-Location -LiteralPath $source
try {
    $ErrorActionPreference = 'Continue'
    try { & cargo @arguments *> $log; $code = $LASTEXITCODE } finally { $ErrorActionPreference = 'Stop' }
    if ($code) {
        Get-Content -LiteralPath $log -Tail 80 | Write-Host
        throw "Cargo exited with $code. See $log"
    }
    if (-not $Check -and $NativeMods) {
        $hashes = [ordered]@{}
        foreach ($mod in $nativeModsRecord.mods) {
            $library = Join-Path $targetDirectory "$target/release/$($mod.static_library)"
            $dll = Join-Path $output $mod.dll
            & "$source/uwp/host/strict-bridge-link.ps1" -Architecture $Architecture -RustLibrary $library -HostKit $kit `
                -MsvcToolsetRoot $vc -SdkRoot $sdk -RustLld $lld -SentinelObject "$kit/dead-import-sentinels.obj" -Output $dll -NativeMod
            $hashes[$mod.dll] = (Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant()
        }
        $record.native_dlls_sha256 = $hashes
    } elseif (-not $Check) {
        $bridge = Join-Path $output 'horizon_uwp_game_bridge.dll'
        $library = Join-Path $targetDirectory "$target/release/horizon_uwp_game_bridge.lib"
        & "$source/uwp/host/strict-bridge-link.ps1" -Architecture $Architecture -RustLibrary $library -HostKit $kit `
            -MsvcToolsetRoot $vc -SdkRoot $sdk -RustLld $lld -SentinelObject "$kit/dead-import-sentinels.obj" -Output $bridge
        $record.bridge_sha256 = (Get-FileHash -LiteralPath $bridge).Hash.ToLowerInvariant()
    }
    $record.result = 'passed'
} finally {
    Pop-Location
    $record | ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8
}
$record | ConvertTo-Json
