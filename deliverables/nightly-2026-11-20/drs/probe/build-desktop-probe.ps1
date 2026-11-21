param([ValidateSet('x64', 'x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP 'wgpu-dx11-depth-x86-x64-20261120'
$target = 'x86_64-pc-windows-msvc'
if ($Architecture -eq 'x64') {
    $root = Join-Path $root 'target'
} else {
    $root = Join-Path $root 'target-x86'
    $target = 'i686-pc-windows-msvc'
}
$deps = Join-Path $root "$target/debug/deps"
$toolset = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207"
$sdk = "${env:ProgramFiles(x86)}\Windows Kits\10\Lib\10.0.19041.0"
$env:Path = "$toolset\bin\Hostx64\$Architecture;$env:Path"
$env:LIB = "$toolset\lib\$Architecture;$sdk\ucrt\$Architecture;$sdk\um\$Architecture"
$log = (Get-ChildItem "$deps/liblog-*.rlib" | Select-Object -First 1).FullName
$wgpu = (Get-ChildItem "$deps/libwgpu-*.rlib" | Select-Object -First 1).FullName
$pollsterSource = (Get-Item "$env:USERPROFILE/.cargo/registry/src/*/pollster-0.4.0/src/lib.rs" | Select-Object -First 1).FullName
$output = Join-Path $PSScriptRoot "desktop-$Architecture"
New-Item -ItemType Directory -Path $output -Force | Out-Null
rustc +1.93 --target $target --edition 2018 --crate-name pollster --crate-type rlib $pollsterSource --out-dir $output
if ($LASTEXITCODE) { throw 'pollster compilation failed' }
$program = Join-Path $output "world-scale-probe-$Architecture.exe"
rustc +1.93 --target $target --edition 2024 -L "dependency=$deps" -L "dependency=$root/debug/deps" --extern "log=$log" --extern "wgpu=$wgpu" --extern "pollster=$output/libpollster.rlib" "$PSScriptRoot/world-scale-probe.rs" -o $program
if ($LASTEXITCODE) { throw 'Probe compilation failed' }
$env:WGPU_DX11_REQUIRE_HARDWARE = '1'
foreach ($mode in 'fl91', 'native') {
    if ($mode -eq 'fl91') {
        $env:WGPU_DX11_FORCE_FL9_1 = '1'
    } else {
        Remove-Item Env:WGPU_DX11_FORCE_FL9_1
    }
    & $program 2>&1 | Tee-Object "$output/world-scale-probe-$Architecture-$mode.log"
    if ($LASTEXITCODE) { throw "Probe failed on $Architecture/$mode" }
}
