param([Parameter(Mandatory)][string]$Directory)

$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath($Directory)
$identity = 'HorizonNativeModProbe'
if (Get-AppxPackage -Name "$identity*" -PackageTypeFilter Main,Optional,Bundle) {
    throw 'The isolated probe identity is already installed. Use a fresh test account or remove the previous probe.'
}

Add-Type @"
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IActivateNativeProbe {
    uint ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string args, uint options);
}

public static class NativeProbeActivator {
    public static uint Launch(string id) {
        var manager = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"));
        return ((IActivateNativeProbe)Activator.CreateInstance(manager)).ActivateApplication(id, null, 0);
    }
}
"@

function Invoke-Probe([string]$Stage, [int]$ExpectedLoad, [bool]$ExpectedOptional) {
    $package = Get-AppxPackage -Name $identity
    $log = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState\native-mod-probe.txt"
    $begin = [DateTime]::UtcNow
    $processId = [NativeProbeActivator]::Launch("$($package.PackageFamilyName)!App")
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).LastWriteTimeUtc -ge $begin) {
            $evidence = Get-Content -LiteralPath $log -Raw
            if ($evidence -match '(?m)^load_win32=\d+\r?$') {
                $evidence | Tee-Object -FilePath (Join-Path $directory "$Stage.txt")
                if ($evidence -notmatch "(?m)^load_win32=$ExpectedLoad\r?$") {
                    throw "$Stage returned an unexpected loader result."
                }
                $optionalPresent = $evidence -match '(?m)^package=HorizonNativeModProbe\.Optional_'
                if ($optionalPresent -ne $ExpectedOptional) { throw "$Stage returned an unexpected package graph." }
                if ($ExpectedLoad -eq 0 -and ($evidence -notmatch '(?m)^value=2706020\r?$' -or
                        $evidence -notmatch '(?m)^released=1\r?$')) {
                    throw "$Stage did not return the expected DLL value and clean unload."
                }
                Wait-Process -Id $processId -Timeout 5 -ErrorAction SilentlyContinue
                return
            }
        }
        Start-Sleep -Milliseconds 200
    }
    throw "$Stage did not produce a complete probe result."
}

Add-AppxPackage -Path (Join-Path $directory 'standalone.appx')
Invoke-Probe 'standalone' 126 $false
Add-AppxPackage -Path (Join-Path $directory 'optional.appxbundle')
Invoke-Probe 'optional-alone' 126 $true
Add-AppxPackage -Path (Join-Path $directory 'main.appxbundle') -ExternalPackages (Join-Path $directory 'optional.appxbundle')
Invoke-Probe 'related-set' 0 $true
