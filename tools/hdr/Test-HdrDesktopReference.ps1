param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$Resources,
    [Parameter(Mandatory)][string]$FixtureWorld,
    [Parameter(Mandatory)][string]$RunDirectory,
    [Parameter(Mandatory)][string]$Display,
    [string]$GraphicsSettings,
    [int]$Width = 1200,
    [int]$Height = 850,
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$client = (Resolve-Path -LiteralPath (Join-Path $ReleaseDirectory 'horizon-client.exe')).Path
$ctl = (Resolve-Path -LiteralPath (Join-Path $ReleaseDirectory 'horizon-ctl.exe')).Path
$clientHash = (Get-FileHash -LiteralPath $client).Hash.ToLowerInvariant()
if ($clientHash -ne '2b685d64b5c253eef2a03f7cc5ec7c141984fc543d18cd009079444ab3ef2e21') {
    throw 'The supplied Windows client does not match the verified November 20 release.'
}
$resources = (Resolve-Path -LiteralPath $Resources).Path
if (!(Test-Path -LiteralPath (Join-Path $resources 'asset-index.json'))) { throw 'Prepared game resources are missing.' }
if (!(Test-Path -LiteralPath (Join-Path $FixtureWorld 'level.dat'))) { throw 'A test world with level.dat is required.' }
$run = [IO.Path]::GetFullPath($RunDirectory)
$game = Join-Path $run 'game'
$prepared = Join-Path $run 'prepared.json'
$socket = Join-Path $run 'control.json'
$capture = Join-Path $run 'world.png'
$stdout = Join-Path $run 'stdout.log'
$stderr = Join-Path $run 'stderr.log'
$dirs = @{
    HORIZON_HOME = 'home'; HORIZON_LAUNCHER_HOME = 'launcher'
    XDG_DATA_HOME = 'xdg-data'; XDG_CONFIG_HOME = 'xdg-config'; LOCALAPPDATA = 'local-app-data'
}
if (!(Test-Path -LiteralPath $prepared)) {
    if (Test-Path -LiteralPath $run) { throw 'Choose a new reference run directory.' }
    [void][IO.Directory]::CreateDirectory("$game/saves")
    foreach ($dir in $dirs.Values) { [void][IO.Directory]::CreateDirectory((Join-Path $run $dir)) }
    Copy-Item -LiteralPath $FixtureWorld -Destination "$game/saves/HdrFixture" -Recurse
    if ($GraphicsSettings) {
        Copy-Item -LiteralPath $GraphicsSettings -Destination "$game/horizon-graphics.txt"
    } else {
        @('graphicsApi:auto', 'hdr:auto', 'hdrLook:horizon', 'hdrPaperWhite:160', 'hdrUiNits:120',
          'hdrPeak:450', 'hdrBlackLevel:0', 'presentMode:fifo', 'resolutionScale:100',
          'dynamicResolution:false', 'taa:false', 'msaa4x:false'
        ) | Set-Content -LiteralPath "$game/horizon-graphics.txt" -Encoding ASCII
    }
    @('tutorialStep:none', 'enableVsync:true', 'pauseOnLostFocus:false', 'renderDistance:4',
      'simulationDistance:4', 'fullscreen:false', 'inactivityFpsLimit:"minimized"'
    ) | Set-Content -LiteralPath "$game/options.txt" -Encoding ASCII
    [ordered]@{ engine='0a875da91dbb0fdcb7ba8c42c11341aa229af51a'; client=$client; clientSha256=$clientHash
                resources=$resources; fixture=(Resolve-Path -LiteralPath $FixtureWorld).Path
                graphicsSha256=(Get-FileHash "$game/horizon-graphics.txt").Hash
                width=$Width; height=$Height; display=$Display
    } | ConvertTo-Json | Set-Content -LiteralPath $prepared -Encoding UTF8
}
$setup = Get-Content -Raw -LiteralPath $prepared | ConvertFrom-Json
if ($setup.display -ne $Display -or $setup.width -ne $Width -or $setup.height -ne $Height -or
    $setup.clientSha256 -ne $clientHash -or $setup.resources -ne $resources -or
    $setup.fixture -ne (Resolve-Path -LiteralPath $FixtureWorld).Path -or
    $setup.graphicsSha256 -ne (Get-FileHash "$game/horizon-graphics.txt").Hash) { throw 'Reference inputs changed.' }
if ($PrepareOnly) { Write-Output $prepared; return }
if (Test-Path -LiteralPath $stdout) { throw 'This run already started. Supply a fresh directory for another capture.' }

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HdrReferenceWindow {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
}
'@
$monitor = [Windows.Forms.Screen]::AllScreens | Where-Object DeviceName -EQ $Display | Select-Object -First 1
if (!$monitor) { throw "No active monitor named $Display." }
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = $client
$info.WorkingDirectory = $game
$info.UseShellExecute = $false
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
foreach ($key in $dirs.Keys) { $info.EnvironmentVariables[$key] = Join-Path $run $dirs[$key] }
foreach ($key in @('HORIZON_HDR_OUTPUT', 'HORIZON_HDR_DISPLAY', 'WGPU_BACKEND')) { $info.EnvironmentVariables.Remove($key) }
$info.EnvironmentVariables['HORIZON_GRAPHICS_API'] = 'dx12'
$info.EnvironmentVariables['HORIZON_NO_AUDIO'] = '1'
$info.EnvironmentVariables['HORIZON_NO_HEARTBEAT'] = '1'
$info.EnvironmentVariables['HORIZON_TELEMETRY_URL'] = 'off'
$info.EnvironmentVariables['HORIZON_LAUNCHER_NO_SELF_UPDATE'] = '1'
$info.Arguments = "--resources `"$resources`" --game-dir `"$game`" --quickPlaySingleplayer HdrFixture --no-pause --width $Width --height $Height --control-socket `"$socket`""
$process = [Diagnostics.Process]::Start($info)
$outRead = $process.StandardOutput.ReadToEndAsync()
$errRead = $process.StandardError.ReadToEndAsync()
[IO.File]::WriteAllText($stdout, '')
function Invoke-ReferenceControl([string[]]$Command, [int]$TimeoutSeconds = 30) {
    $response = & $ctl --socket $socket --timeout $TimeoutSeconds @Command
    if ($LASTEXITCODE) { throw "Control command failed ($LASTEXITCODE): $Command" }
    return ($response -join [Environment]::NewLine)
}
try {
    $limit = [DateTime]::UtcNow.AddSeconds(120)
    do {
        $process.Refresh()
        if ($process.HasExited -or [DateTime]::UtcNow -gt $limit) { throw 'Official client did not open its window and control socket.' }
        Start-Sleep -Milliseconds 250
    } until ($process.MainWindowHandle -ne [IntPtr]::Zero -and (Test-Path -LiteralPath $socket))
    $handle = $process.MainWindowHandle
    $area = $monitor.WorkingArea
    if (![HdrReferenceWindow]::SetWindowPos($handle, [IntPtr]::Zero, $area.Left + 80, $area.Top + 80, $Width, $Height, 0x14)) {
        throw 'Could not move the official client window to the selected monitor.'
    }
    if ([Windows.Forms.Screen]::FromHandle($handle).DeviceName -ne $Display) { throw 'Window is on another monitor.' }
    [void](Invoke-ReferenceControl @('wait-ready', '120') 130)
    # The upstream output monitor polls every two seconds. Let it observe the destination.
    Start-Sleep -Seconds 5
    $state = (Invoke-ReferenceControl @('status')) | ConvertFrom-Json
    $state | ConvertTo-Json -Depth 10 | Set-Content "$run/status.json" -Encoding UTF8
    if (!$state.player -or !$state.server.loaded -or $state.frames -le 0) { throw 'Official client has not drawn a loaded world.' }
    Invoke-ReferenceControl @('screenshot', $capture) 60 | Set-Content "$run/screenshot-reply.json" -Encoding UTF8
    if (!(Test-Path -LiteralPath $capture)) { throw 'Official client returned without a screenshot.' }
    [ordered]@{ expectedDisplay=$Display; observedDisplay=[Windows.Forms.Screen]::FromHandle($handle).DeviceName
                processId=$process.Id; clientSha256=$clientHash; sdrSha256=(Get-FileHash $capture).Hash
                hdrCompanion=(Test-Path -LiteralPath "$run/world-hdr.png"); utc=[DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content "$run/result.json" -Encoding UTF8
    Write-Output "$run/result.json"
} finally {
    if (!$process.HasExited -and (Test-Path -LiteralPath $socket)) {
        try { [void](Invoke-ReferenceControl @('quit') 8) } catch { Write-Warning $_ }
    }
    if (!$process.WaitForExit(15000)) { $process.Kill(); $process.WaitForExit() }
    [IO.File]::WriteAllText($stdout, $outRead.GetAwaiter().GetResult())
    [IO.File]::WriteAllText($stderr, $errRead.GetAwaiter().GetResult())
    $process.Dispose()
}
