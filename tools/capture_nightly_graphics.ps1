[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ScratchRoot,
    [Parameter(Mandatory)][int]$GamePid,
    [Parameter(Mandatory)][ValidateSet('native-dx12', 'uwp-dx12', 'uwp-dx11', 'uwp-angle')][string]$Backend,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_]{1,16}$')][string]$PlayerName
)

$ErrorActionPreference = 'Stop'
$scratch = (Resolve-Path -LiteralPath $ScratchRoot).Path
$process = Get-Process -Id $GamePid
if ($Backend -eq 'native-dx12') {
    $exe = Join-Path $scratch 'target/fast/horizon-client.exe'
    $clientLog = Join-Path $scratch 'native/home/logs/latest.log'
} else {
    $name = if ($Backend -eq 'uwp-angle') { 'MCHorizonUWP.NightlyGraphicsANGLE' } else { 'MCHorizonUWP.NightlyGraphics' }
    $app = Get-AppxPackage -Name $name
    $exe = Join-Path $app.InstallLocation 'HorizonUWP.exe'
    $clientLog = Join-Path $env:LOCALAPPDATA "Packages/$($app.PackageFamilyName)/LocalState/game/isolated-test-home/logs/latest.log"
}
if ($process.Path -ne $exe) { throw 'The selected process is not the isolated benchmark client.' }
if ((Get-Item -LiteralPath $clientLog).LastWriteTimeUtc -lt $process.StartTime.ToUniversalTime()) {
    throw 'The renderer log does not belong to this process.'
}
$adapterPattern = switch ($Backend) {
    'native-dx12' { 'RTX 3090 Ti .*\(Dx12, DiscreteGpu\)' }
    'uwp-dx12' { 'RTX 3090 Ti .*backend=Dx12,.*hardware=true.*software=false' }
    'uwp-dx11' { 'RTX 3090 Ti .*backend=Dx11,.*hardware F?L11_.*hardware=true.*software=false' }
    'uwp-angle' { 'ANGLE.*RTX 3090 Ti.*(?:D3D11|Direct3D11).*backend=Gl,.*OpenGL ES 3\..*hardware=true.*software=false' }
}
$adapter = Select-String -LiteralPath $clientLog -Pattern "Horizon renderer:.*$adapterPattern" | Select-Object -Last 1
$surface = Select-String -LiteralPath $clientLog -Pattern 'configuring the surface' | Select-Object -Last 1
if (!$adapter -or $adapter.Line -match 'SwiftShader|llvmpipe|\bWARP\b|Microsoft Basic Render Driver') {
    throw "No matching hardware adapter for $Backend."
}
if (!$surface -or $surface.Line -notmatch '\(1800x1350 \w+Unorm, Fifo\)') {
    throw 'The latest renderer log does not show the required 1800x1350 SDR FIFO surface.'
}

$serverLog = Join-Path $scratch 'server/server-stdout.log'
$queue = Join-Path $scratch 'server/console.txt'
$port = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 25567 -State Listen
if (!$port -or (Get-Process -Id $port.OwningProcess).Path -ne (Join-Path $scratch 'target/fast/horizon-client.exe')) {
    throw 'Start the exact scratch server on 127.0.0.1:25567.'
}
if (-not ('ScratchKeyboardInput' -as [type])) {
    Add-Type -Path (Join-Path $scratch 'NightlyKeyboardInput.cs')
}
[ScratchKeyboardInput]::EnablePhysicalPixels()
$window = [ScratchKeyboardInput]::new($GamePid)
$window.CheckForeground()
$lineCount = @(Get-Content -LiteralPath $serverLog).Count
[IO.File]::AppendAllLines($queue, [string[]]@(
    "tp $PlayerName -959.5 125 80.5 0 35"
    "data get entity $PlayerName Pos"
    "data get entity $PlayerName Rotation"
), [Text.Encoding]::ASCII)
Start-Sleep -Seconds 45
$window.CheckForeground()
$confirmation = (Get-Content -LiteralPath $serverLog | Select-Object -Skip $lineCount) -join "`n"
if ($confirmation -notmatch 'has the following entity data: \[-959\.5d, 125\.0d, 80\.5d\]' -or
    $confirmation -notmatch 'has the following entity data: \[0\.0f, 35\.0f\]') {
    throw 'The scratch server did not confirm the camera position and rotation.'
}

$label = 'matched-{0}-{1}' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'), $Backend
$surfaceLine = $surface.Line
$adapterLine = $adapter.Line
Set-Content -LiteralPath (Join-Path $scratch "$label-renderer.txt") -Value @($adapterLine, $surfaceLine) -Encoding utf8
Set-Content -LiteralPath (Join-Path $scratch "$label-camera.txt") -Value $confirmation -Encoding utf8
$displayModes = Join-Path $env:TEMP 'mchorizon-nightly-2026-11-18-build/Get-PhysicalDisplayModes.ps1'
& powershell.exe -NoProfile -File $displayModes | Set-Content -LiteralPath (Join-Path $scratch "$label-display.txt") -Encoding utf8
if ($LASTEXITCODE) { throw 'Could not record the physical display modes.' }
Add-Type -AssemblyName System.Drawing
$bounds = $window.Bounds()
$bitmap = [Drawing.Bitmap]::new($bounds.Right - $bounds.Left, $bounds.Bottom - $bounds.Top)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path $scratch "$label-world.png"), [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
for ($pass = 1; $pass -le 3; $pass++) {
    $window.CheckForeground()
    if (Get-Process cargo,rustc,cl,ninja,Cemu -ErrorAction SilentlyContinue) {
        throw 'Wait for competing builds or emulator workloads to finish before capturing.'
    }
    $run = "$label-$pass"
    & powershell.exe -NoProfile -File (Join-Path $scratch 'capture.ps1') -TargetPid $GamePid -Label $run -Seconds 60
    if ($LASTEXITCODE) { throw "PresentMon capture failed for $run." }
    $window.CheckForeground()
    & python (Join-Path $PSScriptRoot 'summarize_dxgi_capture.py') (Join-Path $scratch "$run.csv") --pid $GamePid |
        Set-Content -LiteralPath (Join-Path $scratch "$run-summary.json") -Encoding utf8
    if ($LASTEXITCODE) { throw "PresentMon summary failed for $run." }
}
