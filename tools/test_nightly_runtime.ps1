param(
    [Parameter(Mandatory)][ValidatePattern('^MCHorizonUWP[.-][A-Za-z0-9.-]+$')][string]$Identity,
    [Parameter(Mandatory)][version]$Version,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$WasmFixture,
    [ValidatePattern('^[0-9a-f]{64}$')][string]$WasmFixtureSha256 = 'dfa34da1970cea7df2b7e9031fd17c56f9f47d591912f542a08275d82c5d1194',
    [switch]$Native,
    [switch]$DependentNative,
    [ValidateSet('enabled','disabled','re-enabled')][string[]]$States = @('enabled', 'disabled', 're-enabled'),
    [ValidateRange(30,600)][int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
if (-not $Native -and -not $WasmFixture) { throw 'Select at least one fixture.' }
if ($DependentNative -and -not $Native) { throw 'The dependent fixture requires -Native.' }
$package = @(Get-AppxPackage -Name $Identity | Where-Object { $_.Name -ceq $Identity -and [version]$_.Version -eq $Version })
if ($package.Count -ne 1) { throw 'The exact signed test package is required.' }
$package = $package[0]
$exe = Join-Path $package.InstallLocation 'HorizonUWP.exe'
if (-not (Test-Path -LiteralPath "$($package.InstallLocation)/horizon-isolated-test.flag" -PathType Leaf)) {
    throw 'This test requires a package with its private development identity.'
}
if (Get-Process HorizonUWP -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
    throw 'Close the isolated game before testing.'
}
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a fresh evidence directory.' }
$null = New-Item -ItemType Directory -Path $evidence
$game = Join-Path $env:LOCALAPPDATA "Packages/$($package.PackageFamilyName)/LocalState/game"
$settings = Join-Path $game 'horizon-mods.txt'
$log = Join-Path $game 'isolated-test-home/logs/latest.log'
$null = New-Item -ItemType Directory -Force -Path $game
$hadConfig = Test-Path -LiteralPath $settings -PathType Leaf
$oldConfig = if ($hadConfig) { ,([IO.File]::ReadAllBytes($settings)) } else { $null }
$baseConfig = @(if (Test-Path -LiteralPath $settings) {
    Get-Content -LiteralPath $settings | Where-Object {
        $_ -notmatch '^mod\.uwp_(wasm|native)_hook_probe:' -and
        (-not $DependentNative -or $_ -notmatch '^mod\.uwp_native_after_probe:')
    }
})
if ($WasmFixture) {
    $digest = (Get-FileHash -LiteralPath $WasmFixture -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($digest -ne $WasmFixtureSha256) {
        throw 'The Nightly Wasm hook fixture does not match its expected SHA-256.'
    }
    $mods = Join-Path $game 'horizon-mods'
    $null = New-Item -ItemType Directory -Force -Path $mods
    $destination = Join-Path $mods 'uwp_wasm_hook_probe.hmod'
    if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -ne $digest) {
        throw 'The isolated game already contains a different fixture.'
    }
    if (-not (Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath $WasmFixture -Destination $destination }
}

Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IRuntimeActivation {
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,
                             [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out int pid);
}

public static class RuntimeTestWindow {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    public static int Start(string id) {
        var manager = (IRuntimeActivation)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c"), true));
        try { int pid; manager.ActivateApplication(id, "", 0, out pid); return pid; }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
}
'@
[void][RuntimeTestWindow]::SetProcessDpiAwarenessContext([IntPtr](-4))
$all = [Windows.Automation.Condition]::TrueCondition
$tree = [Windows.Automation.TreeScope]
$root = [Windows.Automation.AutomationElement]::RootElement
function Find-GameWindow([int]$GamePid) {
    $owner = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $GamePid)
    $root.FindAll($tree::Children, $all) | Where-Object {
        -not $_.Current.IsOffscreen -and ($_.Current.ProcessId -eq $GamePid -or $_.FindFirst($tree::Descendants, $owner))
    } | Select-Object -First 1
}
function Invoke-GameButton($Window, [string]$Name) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name)
    $element = $Window.FindFirst($tree::Descendants, $condition)
    $pattern = $null
    if ($element -and $element.Current.IsEnabled -and
        $element.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        return $true
    }
    return $false
}
function Capture-Markers($Window, [string]$Path) {
    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    if ([RuntimeTestWindow]::GetForegroundWindow() -ne $handle) {
        [void][RuntimeTestWindow]::SetForegroundWindow($handle)
        Start-Sleep -Milliseconds 250
    }
    if ([RuntimeTestWindow]::GetForegroundWindow() -ne $handle) { throw 'The test window does not have foreground focus.' }
    $bounds = $Window.Current.BoundingRectangle
    $inset = 12
    $image = [Drawing.Bitmap]::new([int]$bounds.Width - 2 * $inset, [int]$bounds.Height - 2 * $inset)
    $draw = [Drawing.Graphics]::FromImage($image)
    try {
        $draw.CopyFromScreen([int]$bounds.Left + $inset, [int]$bounds.Top + $inset, 0, 0, $image.Size)
        $visible = 0
        for ($row = 2; $row -le 6; $row++) {
            for ($column = 2; $column -le 6; $column++) {
                $sample = $image.GetPixel([int]($image.Width * $column / 8), [int]($image.Height * $row / 8))
                if (([int]$sample.R + [int]$sample.G + [int]$sample.B) -gt 50) { $visible++ }
            }
        }
        if ($visible -lt 8) { return $null }
        $image.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
        $cyan = 0
        $green = 0
        $magenta = 0
        for ($y = 0; $y -lt [Math]::Min(300, $image.Height); $y++) {
            for ($x = [Math]::Max(0, ($image.Width - 300)); $x -lt $image.Width; $x++) {
                $color = $image.GetPixel($x, $y)
                if ($color.R -eq 0 -and $color.G -eq 255 -and $color.B -eq 255) { $cyan++ }
                if ($color.R -eq 0 -and $color.G -eq 255 -and $color.B -eq 0) { $green++ }
                if ($color.R -eq 255 -and $color.G -eq 0 -and $color.B -eq 255) { $magenta++ }
            }
        }
        [pscustomobject]@{ Cyan = $cyan; Green = $green; Magenta = $magenta; Image = $Path }
    } finally { $draw.Dispose(); $image.Dispose() }
}

$results = [Collections.Generic.List[object]]::new()
$previousForeground = [RuntimeTestWindow]::GetForegroundWindow()
try {
    foreach ($mode in $States) {
        $enabled = $mode -ne 'disabled'
        $value = $enabled.ToString().ToLowerInvariant()
        $lines = @($baseConfig)
        if ($DependentNative) { $lines += 'mod.uwp_native_hook_probe:true', "mod.uwp_native_after_probe:$value" }
        elseif ($Native) { $lines += "mod.uwp_native_hook_probe:$value" }
        if ($WasmFixture) { $lines += "mod.uwp_wasm_hook_probe:$value" }
        [IO.File]::WriteAllLines($settings, [string[]]$lines, [Text.UTF8Encoding]::new($false))
        $begin = [DateTime]::UtcNow
        $gamePid = [RuntimeTestWindow]::Start("$($package.PackageFamilyName)!App")
        $window = $null
        $case = $null
        $gameProcess = Get-Process -Id $gamePid
        try {
            if ($gameProcess.Path -ne $exe) { throw 'Activation returned the wrong executable.' }
            $started = $false
            do {
                if (-not (Get-Process -Id $gamePid -ErrorAction SilentlyContinue)) { throw 'The test game exited unexpectedly.' }
                try { $window = Find-GameWindow $gamePid }
                catch [Windows.Automation.ElementNotAvailableException] { $window = $null }
                if ($window -and -not $started) {
                    if (-not (Invoke-GameButton $window 'Not now')) {
                        $started = Invoke-GameButton $window 'Start game'
                    }
                }
                $ready = (Test-Path -LiteralPath $log) -and
                    (Get-Item -LiteralPath $log).LastWriteTimeUtc -ge $begin -and
                    (Select-String -LiteralPath $log -Pattern 'Horizon: first frame after' -SimpleMatch -Quiet)
                if (-not $ready) { Start-Sleep -Milliseconds 500 }
            } while (-not $ready -and ([DateTime]::UtcNow - $begin).TotalSeconds -lt $TimeoutSeconds)
            if (-not $ready -or -not $window) { throw 'The game did not finish preparing its renderer.' }
            $result = $null
            for ($frameAttempt = 0; $frameAttempt -lt 20 -and -not $result; $frameAttempt++) {
                Start-Sleep -Seconds 1
                $result = Capture-Markers $window (Join-Path $evidence "$mode.png")
            }
            if (-not $result) { throw 'The test window never showed a visible game frame.' }
            $case = [pscustomobject]@{
                State = $mode; Package = $package.PackageFullName; Pid = $gamePid
                Cyan = $result.Cyan; Green = $result.Green; Magenta = $result.Magenta
                Capture = $result.Image; Exit = $null
            }
            $results.Add($case)
            $expectCyan = if ($DependentNative) { -not $enabled } else { $enabled }
            if ($Native -and (($expectCyan -and $result.Cyan -lt 16) -or (-not $expectCyan -and $result.Cyan -ne 0))) {
                throw "Native marker did not match $mode. Cyan pixels $($result.Cyan)."
            }
            if ($DependentNative -and (($enabled -and $result.Green -lt 16) -or (-not $enabled -and $result.Green -ne 0))) {
                throw "Dependent native marker did not match $mode. Green pixels $($result.Green)."
            }
            if ($WasmFixture -and (($enabled -and $result.Magenta -lt 16) -or (-not $enabled -and $result.Magenta -ne 0))) {
                throw "Wasm marker did not match $mode. Magenta pixels $($result.Magenta)."
            }
        } finally {
            if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).LastWriteTimeUtc -ge $begin) {
                Copy-Item -LiteralPath $log -Destination (Join-Path $evidence "$mode.log")
            }
            if ($window) {
                [void][RuntimeTestWindow]::PostMessage([IntPtr]$window.Current.NativeWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
                [void]$gameProcess.WaitForExit(10000)
            }
            $resident = Get-Process -Id $gamePid -ErrorAction SilentlyContinue
            if ($resident -and $resident.Path -eq $exe) {
                Stop-Process -InputObject $resident
                [void]$gameProcess.WaitForExit(10000)
                if ($case) { $case.Exit = 'resident after close; test process stopped' }
            } elseif ($case) {
                $case.Exit = 'process exited'
            }
        }
        if (Get-Process -Id $gamePid -ErrorAction SilentlyContinue) { throw 'The isolated game did not exit cleanly.' }
    }
} finally {
    if ($hadConfig) { [IO.File]::WriteAllBytes($settings, $oldConfig) }
    elseif (Test-Path -LiteralPath $settings) { Remove-Item -LiteralPath $settings }
    ConvertTo-Json -InputObject @($results.ToArray()) | Set-Content -LiteralPath "$evidence/results.json" -Encoding utf8
    if ($previousForeground -ne [IntPtr]::Zero) { [void][RuntimeTestWindow]::SetForegroundWindow($previousForeground) }
}
$results
