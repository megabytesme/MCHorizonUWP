param(
    [Parameter(Mandatory)][string]$WasmtimeSource,
    [Parameter(Mandatory)][string]$HostKit,
    [Parameter(Mandatory)][string]$Output
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $WasmtimeSource).Path
$kit = (Resolve-Path -LiteralPath $HostKit).Path
$out = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $out) { throw 'Pass a fresh output directory.' }
$manifest = Get-Content -LiteralPath (Join-Path $source 'Cargo.toml') -Raw
$revision = (Get-Content -LiteralPath (Join-Path $source '.cargo_vcs_info.json') -Raw | ConvertFrom-Json).git.sha1
if ($manifest -notmatch '(?m)^name = "wasmtime"\s*$' -or $manifest -notmatch '(?m)^version = "50\.0\.2"\s*$' -or
    $revision -ne 'f9a93b049a23bef9a33cfa3888f32af33a178749') {
    throw 'Use the registry source for Wasmtime 50.0.2.'
}

$vc = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207'
$sdk = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Windows Kits/10'
$sdkVersion = '10.0.19041.0'
$llvm = 'C:\Program Files\LLVM\bin'
$toolchain = '1.99.1-x86_64-pc-windows-msvc'
$sysroot = (& rustup run $toolchain rustc --print sysroot).Trim()
if ($LASTEXITCODE -ne 0) { throw "Rust $toolchain is unavailable." }
$lld = Join-Path $sysroot 'lib/rustlib/x86_64-pc-windows-msvc/bin/rust-lld.exe'
$tls = Join-Path $kit 'link/tls_index.obj'
$sentinel = Join-Path $kit 'dead-import-sentinels.obj'
$storeLib = Join-Path $vc 'lib/x64/store'
$ucrtLib = Join-Path $sdk "Lib/$sdkVersion/ucrt/x64"
$umLib = Join-Path $sdk "Lib/$sdkVersion/um/x64"
foreach ($path in $lld, $tls, $sentinel, "$storeLib/vcruntime.lib", "$ucrtLib/ucrt.lib",
         "$umLib/WindowsApp.lib", "$llvm/clang.exe", "$llvm/llvm-lib.exe", "$llvm/llvm-readobj.exe") {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing build input: $path" }
}

$null = New-Item -ItemType Directory -Path $out
$vendor = Join-Path $out 'wasmtime'
Copy-Item -LiteralPath $source -Destination $vendor -Recurse
foreach ($input in 'Cargo.toml', 'Cargo.lock', 'src') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $input) -Destination $out -Recurse
}
$patch = Join-Path $PSScriptRoot 'wasmtime-50.0.2-pulley-uwp.patch'
& git -C $vendor apply --whitespace=error-all $patch
if ($LASTEXITCODE -ne 0) { throw 'The Pulley patch did not apply.' }
& (Join-Path $PSScriptRoot '../../rust-std/prepare-source.ps1') -Destination (Join-Path $out 'rust-std') -Toolchain $toolchain

$env:PATH = "$llvm;$sdk\bin\$sdkVersion\x64;$env:PATH"
$env:INCLUDE = (@("$vc\include") + @('ucrt', 'um', 'shared', 'winrt', 'cppwinrt').ForEach({ "$sdk\Include\$sdkVersion\$_" })) -join ';'
$env:LIB = "$vc\lib\x64;$ucrtLib;$umLib"
$env:CC_x86_64_uwp_windows_msvc = "$llvm\clang.exe"
$env:AR_x86_64_uwp_windows_msvc = "$llvm\llvm-lib.exe"
$env:CC_x86_64_pc_windows_msvc = "$llvm\clang.exe"
$env:AR_x86_64_pc_windows_msvc = "$llvm\llvm-lib.exe"
$env:CFLAGS_x86_64_uwp_windows_msvc = '-DWINAPI_FAMILY=WINAPI_FAMILY_APP'
$env:CFLAGS_x86_64_pc_windows_msvc = '-DWINAPI_FAMILY=WINAPI_FAMILY_DESKTOP_APP'
$env:RUSTC_BOOTSTRAP = '1'
$env:__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS = 'nightly'
$env:__CARGO_TESTS_ONLY_SRC_ROOT = Join-Path $out 'rust-std/library'
$env:CARGO_PROFILE_DEV_DEBUG = '0'

$hostLink = Join-Path $out 'lld-host.cmd'
$hostLine = '"{0}" -flavor link /NOLOGO "/LIBPATH:{1}\lib\x64" "/LIBPATH:{2}" "/LIBPATH:{3}" %*' -f $lld, $vc, $ucrtLib, $umLib
@('@echo off', $hostLine, 'exit /b %ERRORLEVEL%') | Set-Content -LiteralPath $hostLink -Encoding ASCII
$env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_LINKER = $hostLink

$target = 'x86_64-uwp-windows-msvc'
$cargoArgs = @("+$toolchain", 'build', '--locked', '--offline', '--manifest-path', (Join-Path $out 'Cargo.toml'),
    '--target', $target, '-Z', 'build-std=std,panic_abort', '--target-dir', (Join-Path $out 'target'))
& cargo @cargoArgs
if ($LASTEXITCODE -ne 0) { throw "Wasmtime probe Cargo build failed (exit $LASTEXITCODE)." }

$library = Join-Path $out "target/$target/debug/horizon_wasmtime_uwp_probe.lib"
$dll = Join-Path $out 'horizon_wasmtime_uwp_probe.dll'
$import = Join-Path $out 'horizon_wasmtime_uwp_probe.lib'
$map = Join-Path $out 'probe.map'
$linkArgs = @('-flavor', 'link', '/NOLOGO', '/DLL', '/NOENTRY', '/APPCONTAINER', '/MACHINE:X64', '/OPT:REF',
    '/NODEFAULTLIB:kernel32.lib', '/NODEFAULTLIB:libucrt.lib', '/INCLUDE:_tls_used',
    '/DEFAULTLIB:msvcrt', '/DEFAULTLIB:vcruntime', '/DEFAULTLIB:ucrt', '/EXPORT:wasmtime_uwp_probe',
    "/LIBPATH:$storeLib", "/LIBPATH:$ucrtLib", "/LIBPATH:$umLib",
    "/OUT:$dll", "/IMPLIB:$import", "/MAP:$map", $tls, $library, $sentinel, 'WindowsApp.lib', 'ntdll.lib')
& $lld @linkArgs
if ($LASTEXITCODE -ne 0) { throw "Wasmtime probe strict link failed (exit $LASTEXITCODE)." }
if (Select-String -LiteralPath $map -Pattern 'dead-import-sentinels\.obj|forbidden_api' -Quiet) {
    throw 'A forbidden desktop API sentinel survived strict linking.'
}
$imports = (& "$llvm\llvm-readobj.exe" --coff-imports $dll) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0 -or $imports -match 'Symbol: (VirtualAlloc|VirtualProtect|AddVectored\w+|RtlAddFunctionTable)\s') {
    throw 'Unexpected desktop allocation or native exception imports.'
}
foreach ($api in 'VirtualAllocFromApp', 'VirtualProtectFromApp') {
    if ($imports -notmatch "Symbol: $api\s") { throw "Required UWP import missing: $api" }
}
$imports | Set-Content -LiteralPath (Join-Path $out 'imports.txt')
Get-FileHash -LiteralPath $dll
