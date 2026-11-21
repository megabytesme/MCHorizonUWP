param(
    [Parameter(Mandatory)][ValidatePattern('^MCHorizonUWP\.HdrDev\.[A-Za-z0-9.]+$')][string]$Identity,
    [Parameter(Mandatory)][string]$FixtureWorld,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$Display,
    [string]$FallbackDisplay,
    [string]$DisplayModeTool,
    [string]$MonitorFriendlyName,
    [string]$PlmDebugTool,
    [ValidateSet('Off', 'On', 'Auto')][string]$Hdr = 'Auto',
    [ValidateSet('Dx12', 'Dx11', 'Gl')][string]$Backend = 'Dx12',
    [ValidateSet(50, 75, 100)][int]$Scale = 100,
    [switch]$AdaptiveResolution,
    [ValidateRange(30, 240)][int]$TargetFps = 60,
    [ValidateSet('None', 'Taa', 'Msaa4x')][string]$AntiAlias = 'None',
    [switch]$ExpectHdr,
    [string]$WasmFixture,
    [switch]$AutomaticCapture,
    [string]$ShaderFixture,
    [ValidatePattern('^(horizon:)?[A-Za-z0-9_-]+$')][string]$ShaderPack
)

$ErrorActionPreference = 'Stop'
if ($DisplayModeTool -and (!$MonitorFriendlyName -or $FallbackDisplay -or !$ExpectHdr)) {
    throw 'The display-mode test requires an HDR game, a named monitor and no monitor move.'
}
$package = @(Get-AppxPackage -Name $Identity | Where-Object Name -CEQ $Identity)
if ($package.Count -ne 1) { throw 'Install the signed HDR development package.' }
$package = $package[0]
$exe = Join-Path $package.InstallLocation 'HorizonUWP.exe'
if (!(Test-Path -LiteralPath "$($package.InstallLocation)/horizon-isolated-test.flag" -PathType Leaf)) {
    throw 'The package must have the isolated development marker.'
}
if (Get-Process HorizonUWP -ErrorAction SilentlyContinue | Where-Object Path -EQ $exe) {
    throw 'The isolated HDR candidate is already running.'
}
if (!(Test-Path -LiteralPath (Join-Path $FixtureWorld 'level.dat') -PathType Leaf)) {
    throw 'Provide a prepared throwaway test world.'
}
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh evidence directory.' }
$destination = [IO.Directory]::CreateDirectory($Output).FullName
$state = Join-Path $env:LOCALAPPDATA "Packages/$($package.PackageFamilyName)/LocalState"
$game = Join-Path $state 'game'
[void][IO.Directory]::CreateDirectory("$game/saves")
[void][IO.Directory]::CreateDirectory("$game/telemetry")
if (!(Test-Path -LiteralPath "$game/saves/HdrFixture")) {
    Copy-Item -LiteralPath $FixtureWorld -Destination "$game/saves/HdrFixture" -Recurse
}
$noTelemetry = @{performance=$false; crashes=$false; bugs=$false; benchmarks=$false}
@{
    version=2; answered=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds(); uwp_confirmed=$true
    official_live=$false; self_hosted_live=$false; choices=$noTelemetry; self_hosted_choices=$noTelemetry
} | ConvertTo-Json -Compress | Set-Content -LiteralPath "$game/telemetry/consent.json" -Encoding ASCII
@(
    'tutorialStep:none', 'enableVsync:true', 'pauseOnLostFocus:false', 'renderDistance:4',
    'simulationDistance:4', 'fullscreen:false', 'inactivityFpsLimit:"minimized"'
) | Set-Content -LiteralPath "$game/options.txt" -Encoding ASCII
@(
    'graphicsApi:auto', "hdr:$($Hdr.ToLowerInvariant())", 'hdrLook:horizon', 'hdrPaperWhite:160',
    'hdrUiNits:120', 'hdrPeak:450', 'hdrBlackLevel:0', 'presentMode:fifo', "resolutionScale:$Scale",
    "dynamicResolution:$($AdaptiveResolution.IsPresent.ToString().ToLowerInvariant())", "targetFps:$TargetFps",
    "taa:$($AntiAlias -eq 'Taa')".ToLowerInvariant(),
    "msaa4x:$($AntiAlias -eq 'Msaa4x')".ToLowerInvariant(), "pickedShaderPack:$ShaderPack"
) | Set-Content -LiteralPath "$game/horizon-graphics.txt" -Encoding ASCII
Copy-Item -LiteralPath "$game/horizon-graphics.txt" -Destination $destination
if ($WasmFixture) {
    [void][IO.Directory]::CreateDirectory("$game/horizon-mods")
    Copy-Item -LiteralPath $WasmFixture -Destination "$game/horizon-mods/uwp_wasm_hook_probe.hmod"
    'mod.uwp_wasm_hook_probe:true' | Set-Content -LiteralPath "$game/horizon-mods.txt" -Encoding ASCII
}
if ($AutomaticCapture -and !$WasmFixture) { throw 'Automatic capture requires the screenshot Wasm fixture.' }
if ($ShaderFixture) {
    if (!$ShaderPack) { throw 'Supply the matching shader-pack ID.' }
    [void][IO.Directory]::CreateDirectory("$game/shaderpacks")
    $stagedShader = Join-Path "$game/shaderpacks" (Split-Path -Leaf $ShaderFixture)
    if (!(Test-Path -LiteralPath $stagedShader)) {
        Copy-Item -LiteralPath $ShaderFixture -Destination $stagedShader -Recurse
    }
}

if (!('ScratchKeyboardInput' -as [type])) {
    Add-Type -Path "$PSScriptRoot/../surface-nightly-qa/NightlyKeyboardInput.cs"
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
if (!('HorizonHdrTest' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IHorizonHdrActivation {
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,
        [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out int pid);
}
public static class HorizonHdrTest {
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
    public static int Activate(string app) {
        var manager = (IHorizonHdrActivation)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c"), true));
        try { int pid; manager.ActivateApplication(app, "world:HdrFixture", 0, out pid); return pid; }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
}
'@
}
[ScratchKeyboardInput]::EnablePhysicalPixels()
$data = [Windows.Management.Core.ApplicationDataManager,Windows.Management.Core,ContentType=WindowsRuntime]::CreateForPackageFamily($package.PackageFamilyName)
$property = [System.Collections.Generic.IDictionary[string,object]].GetProperty('Item')
$property.SetValue($data.LocalSettings.Values, [uint32]@{Dx11=3; Dx12=2; Gl=1}[$Backend], [object[]]@('game-renderer'))

$begin = [DateTime]::UtcNow
$pidGame = [HorizonHdrTest]::Activate("$($package.PackageFamilyName)!App")
$process = Get-Process -Id $pidGame
if ($process.Path -ne $exe) { throw 'Activation returned an unrelated executable.' }
$lifecycle = Join-Path $state 'horizon-uwp-lifecycle.log'
$log = Join-Path $game 'isolated-test-home/logs/latest.log'
$report = [ordered]@{package=$package.PackageFullName; pid=$pidGame; started=$begin.ToString('o');
    mode=$Hdr; backend=$Backend; scale=$Scale; adaptiveResolution=[bool]$AdaptiveResolution; targetFps=$TargetFps;
    antiAlias=$AntiAlias; shaderPack=$ShaderPack;
    requestedDisplay=$Display; expectedHdr=[bool]$ExpectHdr;
    captureMethod=if($AutomaticCapture){'Wasm screenshot fixture'}else{'F2'};
    wasmFixtureSha256=if($WasmFixture){(Get-FileHash -LiteralPath $WasmFixture).Hash}; checks=[ordered]@{}}
$gameWindow = $null
$restoreDisplayHdr = $false
$plmEnabled = $false
$packageSuspended = $false

function Wait-For([string]$Description, [int]$Seconds, [scriptblock]$Test) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if ($process.HasExited) { throw "Game exited while waiting for $Description." }
        $value = & $Test
        if ($value) { return $value }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Description."
}
function Move-ToDisplay([string]$Name, [int]$Width, [int]$Height) {
    $screen = [Windows.Forms.Screen]::AllScreens | Where-Object DeviceName -EQ $Name
    if (!$screen) { throw "Display $Name was not attached." }
    $frame = $gameWindow.FrameHandle()
    $area = $screen.WorkingArea
    if (![HorizonHdrTest]::MoveWindow($frame, $area.Left + 80, $area.Top + 80, $Width, $Height, $true)) {
        throw "Could not position the isolated game on $Name."
    }
    Start-Sleep -Milliseconds 800
    if ([Windows.Forms.Screen]::FromHandle($frame).DeviceName -ne $Name) { throw "The game did not move to $Name." }
    return $frame
}
function Find-RecentLine([string]$Path, [string]$Pattern, [DateTime]$Since) {
    if (!(Test-Path -LiteralPath $Path) -or (Get-Item -LiteralPath $Path).LastWriteTimeUtc -lt $Since) { return }
    [string](Get-Content -LiteralPath $Path | Where-Object { $_ -match $Pattern } | Select-Object -Last 1)
}
function Find-ChainAfter([string]$Pattern, [DateTime]$Since) {
    if (!(Test-Path -LiteralPath $lifecycle)) { return }
    [string](Get-Content -LiteralPath $lifecycle | Where-Object {
        $_ -match "^(\S+).*DXGI color configure:.*$Pattern" -and
            [DateTimeOffset]::Parse($Matches[1]).UtcDateTime -ge $Since
    } | Select-Object -Last 1)
}
function Capture-Game([string]$Label, [bool]$HdrExpected) {
    $trigger = $begin
    if (!$AutomaticCapture) {
        $captureWindow = Wait-For 'focus on the isolated game' 25 {
            try {
                $window = [ScratchKeyboardInput]::new($pidGame)
                $window.Focus()
                return $window
            } catch {}
        }
        $key = $captureWindow.Scan(0x71)
        $trigger = [DateTime]::UtcNow
        try { $captureWindow.Key($key, $true, $false); Start-Sleep -Milliseconds 120 }
        finally { $captureWindow.Key($key, $false, $true) }
    }
    $files = @(Wait-For 'game screenshot' 45 {
        $new = @(Get-ChildItem -LiteralPath "$game/screenshots" -Filter '*.png' -File -ErrorAction SilentlyContinue |
            Where-Object LastWriteTimeUtc -GT $trigger | Sort-Object Name)
        if ($new.Count -eq 0) { return }
        $hasHdr = @($new | Where-Object Name -Like '*-hdr.png').Count -ne 0
        $hasSdr = @($new | Where-Object Name -NotLike '*-hdr.png').Count -ne 0
        if ($hasSdr -and (!$HdrExpected -or $hasHdr)) { return $new }
    })
    $index = 0
    foreach ($file in $files) {
        $index++
        $name = "$Label-$index.png"
        Copy-Item -LiteralPath $file.FullName -Destination "$destination/$name"
        @{name=$name; original=$file.Name; sha256=(Get-FileHash -LiteralPath "$destination/$name").Hash}
    }
}
function Invoke-PackageLifecycle([string]$Action) {
    $lines = & $PlmDebugTool $Action $package.PackageFullName
    if ($LASTEXITCODE) { throw "PLMDebug $Action failed with exit code $LASTEXITCODE. $lines" }
    [string]($lines -join [Environment]::NewLine)
}

try {
    $gameWindow = Wait-For 'isolated game window' 45 {
        try { [ScratchKeyboardInput]::new($pidGame) } catch {}
    }
    $name = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Not now')
    $report.checks.renderer = Wait-For 'hardware renderer and local world' 360 {
        $adapter = Find-RecentLine $log "backend=$Backend,.*hardware=true.*software=false" $begin
        $world = Find-RecentLine $log 'Starting integrated minecraft server version 26\.3' $begin
        $firstFrame = Find-RecentLine $log 'Horizon: first frame' $begin
        if ($adapter -and $world -and $firstFrame) { return $adapter }
        try {
            $candidateWindow = [ScratchKeyboardInput]::new($pidGame)
            $frameWindow = [Windows.Automation.AutomationElement]::FromHandle($candidateWindow.FrameHandle())
            $button = $frameWindow.FindFirst([Windows.Automation.TreeScope]::Descendants, $name)
            if ($button -and $frameWindow.Current.Name -match 'Horizon') {
                $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            }
        } catch {}
    }
    $gameWindow = [ScratchKeyboardInput]::new($pidGame)
    $frame = Move-ToDisplay $Display 1200 850
    if ($Backend -eq 'Dx12') {
        $chainPattern = if ($ExpectHdr) { 'format=10 alpha=3 .*space=1 set_hr=0x00000000.*view_kind=2' }
            else { 'format=87 alpha=3 .*space=0 set_hr=0x00000000' }
        $report.checks.swapchain = Wait-For 'requested DXGI configuration' 30 {
            Find-ChainAfter $chainPattern $begin
        }
    }
    if ($ExpectHdr) {
        $report.checks.firstHdrFrame = Wait-For 'first HDR world frame' 30 {
            Find-RecentLine $log 'HDR output presented its first frame' $begin
        }
    }
    if ($ShaderPack) {
        $packName = [regex]::Escape(($ShaderPack -split ':')[-1])
        $report.checks.shaderPack = Wait-For 'requested shader pack' 40 {
            Find-RecentLine $log "shader pack $packName(?: |:)" $begin
        }
    }
    Start-Sleep -Seconds 5
    $report.checks.capture = @(Capture-Game 'world' $ExpectHdr)
    $hasHdrPng = @($report.checks.capture | Where-Object original -Like '*-hdr.png').Count -ne 0
    if ($hasHdrPng -ne [bool]$ExpectHdr) { throw 'The screenshot files disagree with the requested presentation mode.' }
    if ($FallbackDisplay) {
        $report.fallbackDisplay = $FallbackDisplay
        $transition = [DateTime]::UtcNow
        $frame = Move-ToDisplay $FallbackDisplay 1050 720
        $report.checks.fallback = Wait-For 'SDR swapchain after monitor transition' 35 {
            Find-ChainAfter 'format=87 alpha=3 .*space=0 set_hr=0x00000000.*view_kind=0' $transition
        }
        if (!$AutomaticCapture) {
            $report.checks.fallbackCapture = @(Capture-Game 'sdr-transition' $false)
            if ($report.checks.fallbackCapture | Where-Object original -Like '*-hdr.png') {
                throw 'F2 still produced an HDR file after moving to SDR.'
            }
        }
        $transition = [DateTime]::UtcNow
        $frame = Move-ToDisplay $Display 1200 850
        $report.checks.recoveredHdr = Wait-For 'HDR swapchain after returning to the HDR display' 35 {
            Find-ChainAfter 'format=10 alpha=3 .*space=1 set_hr=0x00000000.*view_kind=2' $transition
        }
    }
    if ($DisplayModeTool) {
        $originalMode = & $DisplayModeTool $MonitorFriendlyName read
        if ($LASTEXITCODE -or $originalMode -ne 'on') { throw 'The named display must start with HDR enabled.' }
        $report.modeTransitionMonitor = $MonitorFriendlyName
        $restoreDisplayHdr = $true
        $transition = [DateTime]::UtcNow
        & $DisplayModeTool $MonitorFriendlyName off
        if ($LASTEXITCODE) { throw 'Windows rejected the temporary HDR disable.' }
        $report.checks.fallback = Wait-For 'SDR configuration after the Windows HDR mode changed' 35 {
            Find-ChainAfter 'format=87 alpha=3 .*space=0 set_hr=0x00000000.*view_kind=[01]' $transition
        }
        $transition = [DateTime]::UtcNow
        & $DisplayModeTool $MonitorFriendlyName on
        if ($LASTEXITCODE) { throw 'Windows rejected HDR restoration.' }
        $report.checks.recoveredHdr = Wait-For 'HDR configuration after restoring Windows HDR' 35 {
            Find-ChainAfter 'format=10 alpha=3 .*space=1 set_hr=0x00000000.*view_kind=2' $transition
        }
    }
    if ($PlmDebugTool) {
        $null = Invoke-PackageLifecycle '/enableDebug'
        $plmEnabled = $true
        $null = Invoke-PackageLifecycle '/suspend'
        $packageSuspended = $true
        $report.checks.suspended = Wait-For 'PLM suspended state' 20 {
            $state = Invoke-PackageLifecycle '/query'
            if ($state -match 'Package state:\s*Suspended') { $state }
        }
        $null = Invoke-PackageLifecycle '/resume'
        $packageSuspended = $false
        $resumed = [DateTime]::UtcNow
        $frame = Move-ToDisplay $Display 1120 790
        $report.checks.resumeSwapchain = Wait-For 'HDR configuration after package resume' 30 {
            Find-ChainAfter 'format=10 alpha=3 .*space=1 set_hr=0x00000000.*view_kind=2' $resumed
        }
    }
    $report.passed = $true
} catch {
    $report.failure = $_.Exception.Message
    throw
} finally {
    if ($plmEnabled) {
        try {
            try {
                if ($packageSuspended) { $null = Invoke-PackageLifecycle '/resume' }
            } finally {
                $null = Invoke-PackageLifecycle '/disableDebug'
            }
        } catch {
            $report.lifecycleCleanupError = $_.Exception.Message
        }
    }
    if ($restoreDisplayHdr) {
        $mode = & $DisplayModeTool $MonitorFriendlyName read
        if ($LASTEXITCODE -or $mode -ne 'on') {
            & $DisplayModeTool $MonitorFriendlyName on
        }
        $restored = & $DisplayModeTool $MonitorFriendlyName read
        $report.displayHdrRestored = !$LASTEXITCODE -and $restored -eq 'on'
    }
    if (Test-Path -LiteralPath $lifecycle) { Copy-Item -LiteralPath $lifecycle -Destination "$destination/lifecycle.log" }
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination "$destination/game.log" }
    if ($gameWindow -and !$process.HasExited) {
        try {
            ([ScratchKeyboardInput]::new($pidGame)).Close()
            $report.closed = $process.WaitForExit(10000)
        }
        catch { $report.closeError = $_.Exception.Message }
    }
    if (!$process.HasExited -and (Get-Process -Id $pidGame -ErrorAction SilentlyContinue).Path -eq $exe) {
        Stop-Process -Id $pidGame
        $null = $process.WaitForExit(10000)
        $report.forcedIsolatedShutdown = $true
    }
    $report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath "$destination/result.json" -Encoding UTF8
}
