param(
    [Parameter(Mandatory)][string]$Scratch,
    [Parameter(Mandatory)][string]$PriorAssembly,
    [Parameter(Mandatory)][string]$Bridge,
    [Parameter(Mandatory)][string]$AngleBin,
    [string]$Revision = 'cee29bb65e6a3acc468e63b951ea20ee681094ac'
)
$ErrorActionPreference = 'Stop'
$product = (Resolve-Path -LiteralPath "$PSScriptRoot/../..").Path
$root = [IO.Path]::GetFullPath($Scratch)
if (Test-Path -LiteralPath $root) { throw 'Use an unused recovery-candidate staging directory.' }
if ((Get-FileHash -LiteralPath $Bridge).Hash -ne 'D29CD8BDE0F7CEEB2720B540D6CEA908C81BD19E22C07606F5BDB3E2A4DE02E2') {
    throw 'The bridge must match the previously verified signed D3D12 HDR bridge.'
}
$source = [IO.Directory]::CreateDirectory("$root/source").FullName
$temp = [IO.Directory]::CreateDirectory("$root/temp").FullName
& git -C $product archive --format=tar "--output=$root/source.tar" $Revision -- uwp tools/build_host_artifact_kit.ps1
if ($LASTEXITCODE) { throw 'Could not archive the pinned UWP host inputs.' }
& tar.exe -xf "$root/source.tar" -C $source
if ($LASTEXITCODE) { throw 'Could not unpack the pinned UWP host inputs.' }
$shell = Join-Path $source 'uwp/pregame/host/XamlShell.cpp'
$text = [IO.File]::ReadAllText($shell)
$signature = 'extern "C" void __cdecl horizon_uwp_record_swapchain_color('
if ([regex]::Matches($text, [regex]::Escape($signature)).Count -ne 1) {
    throw 'Could not locate the pinned diagnostics callback.'
}
$text = $text.Replace($signature, ('#include "Dx12RecoveryProbe.h"' + [Environment]::NewLine + $signature))
$insertAfter = '    horizon_uwp_trace_shell_event(detail);'
$start = $text.IndexOf($signature)
$at = $text.IndexOf($insertAfter, $start)
if ($at -lt 0) { throw 'Could not locate the pinned swapchain trace.' }
$text = $text.Insert($at + $insertAfter.Length, ([Environment]::NewLine + '    RunDx12RecoveryProbe(chain.Get(), space, set_hr);'))
[IO.File]::WriteAllText($shell, $text, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath "$PSScriptRoot/Dx12RecoveryProbe.h" -Destination (Split-Path -Parent $shell)

$oldTemp = $env:TEMP
$oldTmp = $env:TMP
try {
    $env:TEMP = $env:TMP = $temp
    & "$source/tools/build_host_artifact_kit.ps1" -SourceRoot $source -Architecture x64 -OutputDirectory "$root/host-kit"
} finally {
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
}
$options = @{
    Source = $PriorAssembly; Bridge = $Bridge; Output = "$root/package"
    Architecture = 'x64'; Identity = 'MCHorizonUWP.HdrDev.DeviceLoss.x64'; Version = '1.0.0.0'
    PatchVersion = '0.1.16'; ClientVersion = '0.1.18'; UpstreamCommit = '0a875da91dbb0fdcb7ba8c42c11341aa229af51a'
    ResourceFramework = 'Horizon.Asset.26-3.98ccbead789d397639d5badd0bb805'; AngleBin = $AngleBin
    HostExecutable = "$root/host-kit/x64/HorizonUWP.exe"
    WasmStandardLibrary = "$PriorAssembly/uwp/wasm-abi-probe/horizon-wasm-std.wasm"
}
& "$product/tools/package_nightly_runtime.ps1" @options
