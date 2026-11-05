param(
    [Parameter(Mandatory)][string]$AngleSource,
    [Parameter(Mandatory)][string]$ZlibSource,
    [Parameter(Mandatory)][string]$Destination,
    [ValidateSet('arm', 'x86')][string]$Architecture = 'arm',
    [string]$CMake = 'C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe',
    [string]$Ninja = 'C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe',
    [string]$Clang = 'C:\Program Files\LLVM\bin\clang-cl.exe'
)
$ErrorActionPreference = 'Stop'
$sdk = if ($Architecture -eq 'arm') { '10.0.15063.0' } else { '10.0.26100.0' }
if ($env:VSCMD_ARG_TGT_ARCH -ne $Architecture -or $env:WindowsSDKVersion -ne "$sdk\") {
    throw "Run in the $Architecture UWP developer environment with Windows SDK $sdk."
}
$requiredFiles = @($CMake, $Ninja, (Join-Path $AngleSource 'LICENSE'), (Join-Path $ZlibSource 'LICENSE'))
if ($Architecture -eq 'arm') { $requiredFiles += $Clang }
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required file not found: $file" }
}
$inputs = @{
    'CMakeLists.txt' = '34F2FF17943A653FC129384480249ED9F6A918874570566ABCA842AA1DA8F842'
    'src\common\system_utils_win.cpp' = '9CD828A112E9296A2205D5973686370B3754E42423776CCFC074DF8FDD6BF023'
    'src\libANGLE\Context.inl.h' = '1D864F7E5271B666B98AFDB938CD5583CED758F55355D79260B4752D7651149B'
}
foreach ($file in $inputs.Keys) {
    $stream = [IO.File]::OpenRead((Join-Path $AngleSource $file))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
    if ($hash -ne $inputs[$file]) {
        throw "ANGLE source does not match the pinned vcpkg port: $file"
    }
}
if (Test-Path -LiteralPath $Destination) { throw 'Choose a new destination; existing files are not overwritten.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('horizon-angle-arm32-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$zbuild = Join-Path $work 'zlib-build'
$build = Join-Path $work 'angle-build'
New-Item -ItemType Directory -Path $source -Force | Out-Null
Write-Host "Build workspace: $work"
& robocopy $AngleSource $source /E /MT:16 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying ANGLE source failed: $LASTEXITCODE" }
& git -C $source apply --check (Join-Path $PSScriptRoot 'angle-vcpkg-7258.patch')
if ($LASTEXITCODE -ne 0) { throw 'The ARM32 ANGLE patch does not apply.' }
& git -C $source apply (Join-Path $PSScriptRoot 'angle-vcpkg-7258.patch')
if ($LASTEXITCODE -ne 0) { throw 'Applying the ARM32 ANGLE patch failed.' }
$common = @('-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$Ninja", '-DCMAKE_SYSTEM_NAME=WindowsStore',
    "-DCMAKE_SYSTEM_VERSION=$sdk", '-DCMAKE_BUILD_TYPE=Release',
    '-DCMAKE_C_FLAGS=/DWINAPI_FAMILY=WINAPI_FAMILY_APP')
& $CMake -S $ZlibSource -B $zbuild @common
if ($LASTEXITCODE -ne 0) { throw 'zlib configuration failed.' }
& $CMake --build $zbuild --target zlibstatic --parallel ([Environment]::ProcessorCount)
if ($LASTEXITCODE -ne 0) { throw 'zlib build failed.' }
& $CMake -S $source -B $build @common '-DCMAKE_CXX_FLAGS=/DWINAPI_FAMILY=WINAPI_FAMILY_APP' `
    '-DPORT=Win' '-DBUILD_SHARED_LIBS=ON' '-DANGLE_IS_32_BIT_CPU=1' `
    '-DANGLE_USE_D3D11_COMPOSITOR_NATIVE_WINDOW=OFF' "-DZLIB_INCLUDE_DIR=$ZlibSource" `
    "-DZLIB_LIBRARY=$(Join-Path $zbuild 'zs.lib')"
if ($LASTEXITCODE -ne 0) { throw 'ANGLE configuration failed.' }
# The VS 2017 ARM compiler crashes on this generated file. Compile just that
# translation unit with clang-cl against the same UWP headers and runtime.
if ($Architecture -eq 'arm') {
    $rules = Join-Path $build 'CMakeFiles\rules.ninja'
    $rule = @'

rule CXX_COMPILER__ANGLE_CLANG_Release
  deps = msvc
  command = "CLANG_PATH" --target=thumbv7a-pc-windows-msvc /nologo /TP $DEFINES $INCLUDES $FLAGS /showIncludes /Fo$out /Fd$TARGET_COMPILE_PDB /FS -c $in
  description = Building ARM32 CXX object $out with clang-cl
'@
    Add-Content -LiteralPath $rules -Value $rule.Replace('CLANG_PATH', $Clang)
    $ninjaFile = Join-Path $build 'build.ninja'
    $ninjaText = [IO.File]::ReadAllText($ninjaFile)
    $old = 'SymbolTable_autogen.cpp.obj: CXX_COMPILER__ANGLE_unscanned_Release'
    if (-not $ninjaText.Contains($old)) { throw 'The expected ANGLE Ninja rule was not generated.' }
    [IO.File]::WriteAllText($ninjaFile, $ninjaText.Replace($old, 'SymbolTable_autogen.cpp.obj: CXX_COMPILER__ANGLE_CLANG_Release'))
}
& $CMake --build $build --parallel ([Environment]::ProcessorCount)
if ($LASTEXITCODE -ne 0) { throw "ANGLE build failed; workspace retained at $work" }
$bin = Join-Path $Destination 'bin'
$angleNotice = Join-Path $Destination 'share\angle'
$zlibNotice = Join-Path $Destination 'share\zlib'
New-Item -ItemType Directory -Path $bin, $angleNotice, $zlibNotice -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'bin\libEGL_angle.dll') -Destination (Join-Path $bin 'libEGL.dll')
Copy-Item -LiteralPath (Join-Path $build 'bin\libGLESv2_angle.dll') -Destination (Join-Path $bin 'libGLESv2.dll')
Copy-Item -LiteralPath (Join-Path $AngleSource 'LICENSE') -Destination (Join-Path $angleNotice 'copyright')
Copy-Item -LiteralPath (Join-Path $ZlibSource 'LICENSE') -Destination (Join-Path $zlibNotice 'copyright')
Write-Host "$Architecture ANGLE and notices: $Destination"
Write-Host "Build workspace retained for inspection: $work"
