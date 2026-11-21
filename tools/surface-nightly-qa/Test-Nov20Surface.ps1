param(
    [Parameter(Mandatory)][ValidateSet("arm64")][string]$Architecture,
    [Parameter(Mandatory)][ValidateSet("Title","World")][string]$Mode,
    [Parameter(Mandatory)][ValidateSet("Dx11","Dx12","Gl")][string]$Backend,
    [ValidateSet("Enabled","NativeOnly","WasmOnly")][string]$Hooks="Enabled",
    [ValidateRange(25,100)][int]$Scale=100,
    [switch]$Content,
    [switch]$Audio,
    [switch]$ExerciseControls,
    [switch]$AdaptiveResolution,
    [switch]$StrictFl91,
    [Parameter(Mandatory)][version]$PackageVersion,
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [string]$EvidenceDirectory=(Join-Path $env:TEMP 'HorizonSurfaceQa'),
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ExpectedClientVersion='0.1.14',
    [switch]$LeaveRunning,
    [ValidateRange(30,600)][int]$TimeoutSeconds=240
)
$ErrorActionPreference='Stop'
$fixtures=(Resolve-Path -LiteralPath $FixtureDirectory).Path
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
$null=New-Item -ItemType Directory -Force -Path $evidence
$identity="MCHorizonUWP.NightlyNov20.$Architecture"
$p=@(Get-AppxPackage -Name $identity | Where-Object { $_.Name -ceq $identity -and $_.Architecture -eq $Architecture -and [version]$_.Version -eq $PackageVersion -and $_.Status -eq 'Ok' })
if($p.Count -ne 1) { throw "Install exact signed candidate $identity $PackageVersion first." }
$p=$p[0]
$native=@(Get-AppxPackage -Name "$identity.NativeMods" -PackageTypeFilter Optional | Where-Object {
    $_.Name -ceq "$identity.NativeMods" -and $_.Architecture -eq $Architecture -and
        [version]$_.Version -eq $PackageVersion -and $_.Status -eq 'Ok'
})
if($native.Count -ne 1) { throw 'Install the matching signed native-mod related set first.' }
$root=$p.InstallLocation
$exe=Join-Path $root 'HorizonUWP.exe'
if (!(Test-Path -LiteralPath "$root/horizon-isolated-test.flag")) { throw 'The installed app is missing the isolated test marker.' }
$release=Get-Content -LiteralPath "$root/HorizonRelease.json" -Raw | ConvertFrom-Json
if ($release.schema_version -ne 1 -or $release.patch_version -ne '0.1.13' -or $release.client_version -ne $ExpectedClientVersion -or
    $release.upstream_commit -ne '0a875da91dbb0fdcb7ba8c42c11341aa229af51a') { throw 'Installed release metadata mismatch.' }
if (Get-Process HorizonUWP -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) { throw 'The isolated candidate is already running.' }
if($StrictFl91 -and $Backend -ne 'Dx11') { throw 'Physical FL9_1 requires Dx11.' }
if($Mode -eq 'World') {
    $world=Join-Path $fixtures 'world-fixture-bootstrap/Nov20Smoke'
    $inventory=Get-Content -LiteralPath (Join-Path $fixtures 'fixtures.json') -Raw | ConvertFrom-Json
    if (!$inventory) { throw 'The copied world fixture manifest is empty.' }
    foreach ($file in $inventory) {
        if ($file.path -notmatch '^world-fixture-bootstrap/Nov20Smoke/[A-Za-z0-9_./-]+$' -or
            ($file.path -split '/') -contains '..' -or $file.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
            (Get-FileHash -LiteralPath (Join-Path $fixtures $file.path)).Hash -ine $file.sha256) {
            throw 'The copied world fixture failed path or digest validation.'
        }
    }
    if ((Get-ChildItem -LiteralPath $world -Recurse -File | Measure-Object).Count -ne @($inventory).Count) {
        throw 'The copied world fixture contains a file absent from its digest manifest.'
    }
}
$report=Join-Path $evidence ('capture-'+(Get-Date -Format yyyyMMdd-HHmmss-fff)+"-$Architecture-$Backend-$Mode-$Hooks-$Scale")
$null=New-Item -ItemType Directory -Path $report
$state=Join-Path $env:LOCALAPPDATA "Packages/$($p.PackageFamilyName)/LocalState"
$game=Join-Path $state 'game'
$mods=Join-Path $game 'horizon-mods'
$null=New-Item -ItemType Directory -Force -Path $mods
function Update-Settings([string]$Name,[hashtable]$Settings) {
    $file=Join-Path $game $Name
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
        $prior=Join-Path $fixtures $Name
        if (Test-Path -LiteralPath $prior -PathType Leaf) { Copy-Item -LiteralPath $prior -Destination $file }
    }
    $existing=@(if(Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file })
    foreach ($key in $Settings.Keys) {
        $existing=@($existing | Where-Object { $_ -notmatch ('^'+[regex]::Escape($key)+':') })
        $existing+="$($key):$($Settings[$key])"
    }
    [IO.File]::WriteAllLines($file,[string[]]$existing,[Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $file -Destination $report
}
Update-Settings 'options.txt' @{tutorialStep='none'; enableVsync='true'; pauseOnLostFocus='false'; renderDistance=4; simulationDistance=4}
$graphicSettings=@{graphicsApi='auto'; hdr='off'; presentMode='fifo'; resolutionScale=$Scale; dynamicResolution=$AdaptiveResolution.IsPresent.ToString().ToLowerInvariant()}
if($AdaptiveResolution) { $graphicSettings.targetFps=144 }
Update-Settings 'horizon-graphics.txt' $graphicSettings
$nativeExpected=$Hooks -in @('Enabled','NativeOnly')
$wasmExpected=$Hooks -in @('Enabled','WasmOnly')
$nativeEnabled=$nativeExpected.ToString().ToLowerInvariant()
$wasmEnabled=$wasmExpected.ToString().ToLowerInvariant()
Update-Settings 'horizon-mods.txt' @{
    'mod.uwp_wasm_hook_probe'=$wasmEnabled; 'mod.uwp_native_hook_probe'=$nativeEnabled;
    'mod.uwp_native_after_probe'='false'; 'mod.horizon_gyro_aim'='false';
    'mod.uwp_smoke'=$Content.IsPresent.ToString().ToLowerInvariant()
}
$fixture=Join-Path $fixtures 'uwp_wasm_hook_probe.hmod'
if ((Get-FileHash -LiteralPath $fixture).Hash -ine '295326eb0dca7d0ffd9e7ca45b6a4aa31a7284ea818fafc50c804225dd89f3bc') {
    throw 'The Wasm fixture failed digest validation.'
}
Copy-Item -LiteralPath $fixture -Destination $mods
if ($Content) {
    $fixture=Join-Path $fixtures 'uwp-smoke.hmod'
    if ((Get-FileHash -LiteralPath $fixture).Hash -ine '795c0567250d9230e04ca82120aa2c91f91c2b47436ae5f82fdd5c7b62aa63c2') {
        throw 'The content fixture failed digest validation.'
    }
    Copy-Item -LiteralPath $fixture -Destination $mods
}
$audioMarker=Join-Path $game 'audio-test.enabled'
if ($Audio) { [IO.File]::WriteAllBytes($audioMarker,[byte[]]@()) }
elseif (Test-Path -LiteralPath $audioMarker -PathType Leaf) { Remove-Item -LiteralPath $audioMarker }
$fl91=Join-Path $game 'strict-dx11-bench.enabled'
if ($StrictFl91) { [IO.File]::WriteAllBytes($fl91,[byte[]]@()) }
elseif (Test-Path -LiteralPath $fl91 -PathType Leaf) { Remove-Item -LiteralPath $fl91 }
if ($Mode -eq 'World') {
    $saves=Join-Path $game 'saves'
    if(!(Test-Path -LiteralPath "$world/level.dat" -PathType Leaf)) { throw 'Generate the exact local Nov20 test world.' }
    if(!(Test-Path -LiteralPath "$saves/Nov20Smoke" -PathType Container)) {
        $null=New-Item -ItemType Directory -Force -Path $saves
        Copy-Item -LiteralPath $world -Destination "$saves/Nov20Smoke" -Recurse
    }
}
[IO.File]::WriteAllText("$report/metadata.json",($release | ConvertTo-Json))
if (-not ('Nov20Activation' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface INov20Activation {
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,
        [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out int pid);
}
public static class Nov20Activation {
    public static int Start(string app) {
        var clsid = Type.GetTypeFromCLSID(new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c"), true);
        var manager = (INov20Activation)Activator.CreateInstance(clsid);
        try { int pid; manager.ActivateApplication(app, "", 0, out pid); return pid; }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
}
"@
}
if (-not ('ScratchKeyboardInput' -as [type])) { Add-Type -Path "$PSScriptRoot/NightlyKeyboardInput.cs" }
[ScratchKeyboardInput]::EnablePhysicalPixels()
$begin=[DateTime]::UtcNow
$gamePid=[Nov20Activation]::Start("$($p.PackageFamilyName)!App")
$process=Get-Process -Id $gamePid
if ($process.Path -ne $exe) { throw 'Activation returned an unrelated process.' }
$log=Join-Path $game 'isolated-test-home/logs/latest.log'
$result=[ordered]@{
    Name=$identity; Family=$p.PackageFamilyName; Package=$p.PackageFullName
    Pid=$gamePid; BeginUtc=$begin; Mode=$Mode; Backend=$Backend; Scale=$Scale; Hooks=$Hooks
    StrictFl91=$StrictFl91.IsPresent; Content=$Content.IsPresent; AudioTest=$Audio.IsPresent; AdaptiveResolution=$AdaptiveResolution.IsPresent; Ready=$false
}
$window=$null
function Get-Ui { & "$PSScriptRoot/Nov20-Ui.ps1" -GamePid $gamePid -Architecture $Architecture }
function Send-Ui([string]$Action,[string]$Value) {
    $params=@{GamePid=$gamePid;Architecture=$Architecture;Action=$Action}
    if($PSBoundParameters.ContainsKey('Value')) { $params.Value=$Value }
    & "$PSScriptRoot/Nov20-Ui.ps1" @params
}
function Wait-Ui([string]$Name,[int]$Seconds=20) {
    $until=[DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if ($process.HasExited) { throw 'Candidate exited in the launcher.' }
        try {
            $controls=@(Get-Ui)
            $match=$controls -match ('\| '+[regex]::Escape($Name)+' \|')
            if($match) { return $controls }
        } catch [Windows.Automation.ElementNotAvailableException] { }
        catch { if ($_.Exception.Message -notmatch 'window unavailable') { throw } }
        Start-Sleep -Milliseconds 400
    } while([DateTime]::UtcNow -lt $until)
    throw "Launcher control was unavailable: $Name"
}
function Save-Capture([string]$Label) {
    $window.CheckForeground()
    $bounds=$window.Bounds()
    $bmp=[Drawing.Bitmap]::new($bounds.Right-$bounds.Left,$bounds.Bottom-$bounds.Top)
    $capture=[Drawing.Graphics]::FromImage($bmp)
    $out=Join-Path $report "$Label.png"
    try {
        $capture.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bmp.Size)
        $bmp.Save($out,[Drawing.Imaging.ImageFormat]::Png)
        $cyan=0; $magenta=0
        for($y=0;$y -lt [Math]::Min(300,$bmp.Height);$y++) {
            for($x=[Math]::Max(0,$bmp.Width-300);$x -lt $bmp.Width;$x++) {
                $rgb=$bmp.GetPixel($x,$y).ToArgb() -band 0xffffff
                if($rgb -eq 0x00ffff){ $cyan++ }
                if($rgb -eq 0xff00ff){ $magenta++ }
            }
        }
        [pscustomobject]@{Path=$out;Width=$bmp.Width;Height=$bmp.Height;Cyan=$cyan;Magenta=$magenta}
    } finally { $capture.Dispose(); $bmp.Dispose() }
}
try {
    $prelaunch=[DateTime]::UtcNow.AddSeconds(90)
    do {
        if ($process.HasExited) { throw 'Candidate exited before launcher.' }
        try {
            $controls=@(Get-Ui)
            if($controls -match '\| Not now \|') { Send-Ui 'Not now' }
            elseif($controls -match '\| Settings \|') { break }
        } catch { if ($_.Exception.Message -notmatch 'window unavailable|element is not available') { throw } }
        Start-Sleep -Milliseconds 400
    } while([DateTime]::UtcNow -lt $prelaunch)
    Send-Ui 'Settings'
    $controls=Wait-Ui 'Renderer'
    $rendererName=@{Dx11='Direct3D 11 (wgpu, feature level 9_1)';Dx12='Direct3D 12';Gl='ANGLE (OpenGL ES 3.0)'}[$Backend]
    $proof=[regex]::Escape("$rendererName passed this device's graphics check.")
    if(-not ($controls -match $proof)) {
        Send-Ui 'Renderer'
        $null=Wait-Ui $rendererName
        Send-Ui $rendererName
        Start-Sleep -Milliseconds 700
        if(-not (@(Get-Ui) -match $proof)) { throw 'Requested renderer did not pass the device check.' }
    }
    Send-Ui 'Home'
    switch($Mode) {
        'Title' { $null=Wait-Ui 'Start game'; Send-Ui 'Start game' }
        'World' {
            $null=Wait-Ui 'Worlds'; Send-Ui 'Worlds'
            $controls=Wait-Ui 'Play'
            if(-not ($controls -match 'Nov20Smoke')) { throw 'Isolated test save is missing from the picker.' }
            Send-Ui 'Play'
        }
    }
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if($process.HasExited) { throw 'Candidate exited during game startup.' }
        if((Test-Path -LiteralPath $log -PathType Leaf) -and (Get-Item -LiteralPath $log).LastWriteTimeUtc -ge $begin) {
            $lines=Get-Content -LiteralPath $log
            $adapter=$lines | Where-Object { $_ -match "Horizon renderer:.*backend=$Backend,.*hardware=true.*software=false" } | Select-Object -Last 1
            $stageReady=($lines -match 'Horizon: first frame').Count -gt 0
            if($Mode -eq 'World') { $stageReady=$stageReady -and ($lines -match 'Starting integrated minecraft server version 26\.3') }
            if ($stageReady -and $adapter) {
                if ($StrictFl91 -and $adapter -notmatch 'driver_info="FL9_1; physical DXGI adapter') { throw 'The GPU is not running at actual FL9_1.' }
                $result.Renderer=[string]::new(([string]$adapter).ToCharArray())
                break
            }
        }
        Start-Sleep -Seconds 1
    } while([DateTime]::UtcNow -lt $deadline)
    if (!$result.Renderer) { throw "Renderer or world did not reach the requested stage. Evidence: $report" }
    Add-Type -AssemblyName System.Drawing
    $window=[ScratchKeyboardInput]::new($gamePid)
    $window.Focus()
    $startCapture=[DateTime]::UtcNow
    $captureDeadline=$startCapture.AddSeconds($(if($Mode -eq 'Title'){45}else{160}))
    Start-Sleep -Seconds $(if($Mode -eq 'Title'){3}else{8})
    $markerCount=$false
    do {
        if($process.HasExited) { throw 'Candidate exited before the requested mod markers appeared.' }
        $result.Capture=Save-Capture $Mode.ToLowerInvariant()
        $markerCount=($(if($nativeExpected){$result.Capture.Cyan -ge 256}else{$result.Capture.Cyan -eq 0}) -and
                      $(if($wasmExpected){$result.Capture.Magenta -ge 256}else{$result.Capture.Magenta -eq 0}))
        if($markerCount) { break }
        Start-Sleep -Seconds 7
    } while ([DateTime]::UtcNow -lt $captureDeadline)
    $result.CaptureWaitSeconds=[Math]::Round(([DateTime]::UtcNow-$startCapture).TotalSeconds,1)
    if(!$markerCount) { throw "Visible native/Wasm markers did not match $Hooks before the capture deadline." }
    if($Mode -ne 'Title') {
        $scan=$window.Scan(0x72)
        try { $window.Key($scan,$true,$false); Start-Sleep -Milliseconds 120 }
        finally { $window.Key($scan,$false,$true) }
        Start-Sleep -Seconds 1
        $result.DebugCapture=Save-Capture 'f3'
        if($AdaptiveResolution) {
            $process.Refresh()
            $cpuBefore=$process.TotalProcessorTime.TotalSeconds
            $sampleStart=[DateTime]::UtcNow
            try {
                $before=$window.Bounds()
                $window.ResizeForTest([int](($before.Right-$before.Left)*0.60), [int](($before.Bottom-$before.Top)*0.60))
                Start-Sleep -Seconds 20
                $result.AdaptiveResizeCapture=Save-Capture 'f3-adaptive-smaller'
            } finally {
                $window.RestoreTestPlacement()
                Start-Sleep -Seconds 20
            }
            $result.AdaptiveRestoredCapture=Save-Capture 'f3-adaptive-restored'
            $process.Refresh()
            $result.AdaptiveObservationSeconds=[Math]::Round(([DateTime]::UtcNow-$sampleStart).TotalSeconds,2)
            $result.AdaptiveProcessCpuSeconds=[Math]::Round(($process.TotalProcessorTime.TotalSeconds-$cpuBefore),2)
            $result.PeakWorkingSetBytes=[long]$process.PeakWorkingSet64
            $result.WorkingSetBytes=[long]$process.WorkingSet64
        }

        if ($ExerciseControls) {
            function Send-Key([int]$code,[int]$duration=120) {
                $key=$window.Scan($code)
                try { $window.Key($key,$true,$false); Start-Sleep -Milliseconds $duration }
                finally { $window.Key($key,$false,$true) }
            }
            try {
                Send-Key 0x57 1300
                Start-Sleep -Seconds 2
                $result.KeyboardMoveCapture=Save-Capture 'f3-after-keyboard-move'
                Send-Key 0x72
                Send-Key 0x45
                Start-Sleep -Seconds 2
                $result.InventoryCapture=Save-Capture 'inventory'
                Send-Key 0x1b
                Send-Key 0x54
                Start-Sleep -Seconds 2
                $result.ChatCapture=Save-Capture 'chat'
                Send-Key 0x1b
                $window.Minimize()
                Start-Sleep -Seconds 4
                $window.Focus()
                Start-Sleep -Seconds 3
                $result.ForegroundRestoreCapture=Save-Capture 'after-foreground-restore'
                try {
                    $before=$window.Bounds()
                    $width=$before.Right-$before.Left
                    $height=$before.Bottom-$before.Top
                    $window.ResizeForTest([int]($width*0.75), [int]($height*0.75))
                    Start-Sleep -Seconds 3
                    $result.ResizeCapture=Save-Capture 'resized'
                    $result.ResizeDimensionsChanged=$result.ResizeCapture.Width -ne $width -or $result.ResizeCapture.Height -ne $height
                } catch {
                    $result.ResizeError=[string]$_.Exception.Message
                } finally {
                    $window.RestoreTestPlacement()
                    Start-Sleep -Seconds 2
                }
                $result.PlacementRestoreCapture=Save-Capture 'restored-size'

                $result.PeakWorkingSetBytes=[long]$process.PeakWorkingSet64
                $result.WorkingSetBytes=[long]$process.WorkingSet64
            } catch {
                $result.ControlError=[string]$_.Exception.Message
            }
        }
    }
    if($Content) {
        $staging=Join-Path $game '.horizon-cache/mod-content'
        $contentEvidence=@(Get-ChildItem -LiteralPath $staging -Directory | ForEach-Object {
            $folder=$_.FullName
            $stamp=Join-Path $folder 'horizon-mod-content.stamp'
            $inventory=Join-Path $folder 'horizon-enabled-mods.json'
            if((Test-Path -LiteralPath $stamp -PathType Leaf) -and
               (Test-Path -LiteralPath $inventory -PathType Leaf) -and
               (Select-String -LiteralPath $inventory -Pattern '"uwp_smoke"' -SimpleMatch -Quiet)) {
                foreach($name in 'entity-types.json','command-registries.json') {
                    $table=Join-Path $folder $name
                    if((Test-Path -LiteralPath $table) -and
                      (Select-String -LiteralPath $table -Pattern 'uwp_smoke:probe' -SimpleMatch -Quiet)) {
                        [pscustomobject]@{Generation=$_.Name;Table=$name;Hash=(Get-FileHash -LiteralPath $table).Hash}
                    }
                }
            }
        })
        if(!$contentEvidence) { throw 'The registered test entity was not found in a completed generated table.' }
        $result.ContentEvidence=$contentEvidence
    }
    $targetLog=Get-Content -LiteralPath $log | Where-Object { $_ -match 'FL9_1 world [0-9]|render scale|world resolution|resolution target|scaled world|frame pressure' } | Select-Object -Last 10
    $result.ResolutionEvidence=@($targetLog | ForEach-Object {[string]::new(([string]$_).ToCharArray())})
    if($Mode -ne 'Title' -and $Backend -eq 'Dx11') {
        $pattern='FL9_1 world (\d+)x(\d+), output (\d+)x(\d+), selected scale '+$Scale+'%'
        $sample=$targetLog | Where-Object { $_ -match $pattern } | Select-Object -Last 1
        if(!$sample -or $sample -notmatch $pattern) { throw 'The requested D3D11 scale was not observed.' }
        $dimensions=[int[]]@($Matches[1],$Matches[2],$Matches[3],$Matches[4])
        if($Scale -lt 100 -and !($dimensions[0] -lt $dimensions[2] -and $dimensions[1] -lt $dimensions[3])) {
            throw 'The D3D11 compatibility renderer did not lower the world dimensions.'
        }
        $result.RenderArea=[ordered]@{World=$dimensions[0..1];Output=$dimensions[2..3]}
    }
    if($Audio) {
        $audioLines=@(Get-Content -LiteralPath $log | Where-Object { $_ -match 'XAudio2 default output|XAudio2 is playing non-silent game audio' })
        $result.AudioOutput=@($audioLines | ForEach-Object {[string]::new(([string]$_).ToCharArray())})
        $result.AudioNonSilent=[bool]($audioLines -match 'non-silent game audio')
        if(!$audioLines) { throw 'The requested XAudio2 output did not initialize.' }
    }
    $result.Ready=$true
} finally {
    if((Test-Path -LiteralPath $log -PathType Leaf) -and (Get-Item -LiteralPath $log).LastWriteTimeUtc -ge $begin) {
        Copy-Item -LiteralPath $log -Destination "$report/game.log"
    }
    if (Test-Path -LiteralPath "$state/horizon-uwp-lifecycle.log") {
        Copy-Item -LiteralPath "$state/horizon-uwp-lifecycle.log" -Destination "$report/lifecycle.log"
    }
    Get-ChildItem -LiteralPath $state -Filter 'horizon-error-*.txt' -File -ErrorAction SilentlyContinue |
        Where-Object LastWriteTimeUtc -ge $begin | Copy-Item -Destination $report
    if(!$LeaveRunning -or !$result.Ready) {
        if(!$process.HasExited) {
            try {
                if(!$window) { $window=[ScratchKeyboardInput]::new($gamePid) }
                $window.Close()
                $null=$process.WaitForExit(10000)
            } catch { $result.CloseFailure=$_.Exception.Message }
        }
        if(!$process.HasExited -and (Get-Process -Id $gamePid).Path -eq $exe) {
            $result.Exit='forced isolated candidate shutdown'
            Stop-Process -Id $gamePid
        } else { $result.Exit='closed' }
    } else { $result.Exit='left running' }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$report/result.json" -Encoding utf8
    Write-Output "Surface evidence: $report"
}
$result