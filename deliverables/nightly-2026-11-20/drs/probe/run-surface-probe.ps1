$ErrorActionPreference = 'Stop'
$name = 'MCHorizonUWP.WgpuWorldScaleProbe'
$evidence = Join-Path $PSScriptRoot 'surface-probe-status.txt'

try {
    $appx = Join-Path $PSScriptRoot 'WgpuWorldScaleProbe-arm64.signed.appx'
    $hash = (Get-FileHash -LiteralPath $appx -Algorithm SHA256).Hash
    $hash | Set-Content -LiteralPath $evidence
    if ($hash -ne '723181EA9D43D877EC3C947FC7425279BA2664671759ACEB6E1864D182F6141E') {
        throw 'Appx hash did not match the signed Ben-Desktop build.'
    }

    Add-AppxPackage -Path $appx
    $app = Get-AppxPackage -Name $name
    if ($app.Architecture -ne 'Arm64' -or $app.Status -ne 'Ok') {
        throw 'Probe package is missing or is not registered as ARM64.'
    }
    "$($app.PackageFullName) status=$($app.Status)" | Add-Content -LiteralPath $evidence

    Add-Type @'
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IRuntimeActivation {
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,
                             [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out int pid);
}

public static class DRSProbeActivation {
    public static int Start(string id) {
        var manager = (IRuntimeActivation)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c"), true));
        try { int pid; manager.ActivateApplication(id, "", 0, out pid); return pid; }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
}
'@
    $probePid = [DRSProbeActivation]::Start("$($app.PackageFamilyName)!App")
    "activated=$probePid" | Add-Content -LiteralPath $evidence
    $log = Join-Path $env:LOCALAPPDATA "Packages\$($app.PackageFamilyName)\LocalState\wgpu-world-scale-probe.txt"
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if (Test-Path -LiteralPath $log) {
            $status = Get-Content -Raw -LiteralPath $log
            if ($status -match '(?m)^overall=') {
                Copy-Item -LiteralPath $log -Destination (Join-Path $PSScriptRoot 'surface-world-scale.log')
                if ($status -notmatch '(?m)^overall=PASS$') { throw 'GPU probe reported failure.' }
                'overall=PASS' | Add-Content -LiteralPath $evidence
                exit 0
            }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if (Test-Path -LiteralPath $log) {
        Copy-Item -LiteralPath $log -Destination (Join-Path $PSScriptRoot 'surface-world-scale.log')
    }
    throw 'GPU probe did not produce a final result.'
} catch {
    "overall=FAIL $($_.Exception.Message)" | Add-Content -LiteralPath $evidence
    throw
}
