[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ScratchRoot,
    [string]$OutputDirectory,
    [switch]$Prepare
)

$ErrorActionPreference = 'Stop'
$scratch = (Resolve-Path -LiteralPath $ScratchRoot).Path
$upstream = '0b14f8acfd97fa2c750e42795249dcc981178ca4'
$bridgeHash = '05B14FEE7CB59C84A87A0BF72CE2D24B80ADC869ADCCC515D865507B77C2CF4D'
$nativeExe = Join-Path $scratch 'target/fast/horizon-client.exe'
$source = Join-Path $scratch 'source'
if ((git -C $source rev-parse HEAD) -ne $upstream -or
    (git -C $source status --porcelain) -or
    (Get-FileHash -LiteralPath $nativeExe).Hash -ne '089A49E06D4103AEB8AD8006E39A85B36997293E86FF205F10174E71959522DD') {
    throw 'The scratch desktop executable or source differs from the pinned Nightly.'
}

$clientPaths = @($nativeExe)
$profilePaths = @(Join-Path $scratch 'native/game')
$packages = foreach ($identity in 'MCHorizonUWP.NightlyGraphics', 'MCHorizonUWP.NightlyGraphicsANGLE') {
    $app = Get-AppxPackage -Name $identity
    $family = "${identity}_4pd4jeb6snrqw"
    if ($app.PackageFamilyName -ne $family -or $app.Architecture -ne 'X64' -or $app.Status -ne 'Ok' -or
        !(Test-Path -LiteralPath (Join-Path $app.InstallLocation 'horizon-isolated-test.flag') -PathType Leaf) -or
        (Get-FileHash -LiteralPath (Join-Path $app.InstallLocation 'horizon_uwp_game_bridge.dll')).Hash -ne $bridgeHash) {
        throw "Install the verified isolated x64 package $identity."
    }
    $release = Get-Content -Raw -LiteralPath (Join-Path $app.InstallLocation 'HorizonRelease.json') | ConvertFrom-Json
    if ($release.upstream_commit -ne $upstream -or $release.patch_version -ne '0.1.5' -or $release.client_version -ne '0.1.6') {
        throw "Unexpected release identity for $identity."
    }
    $clientPaths += Join-Path $app.InstallLocation 'HorizonUWP.exe'
    $profilePaths += Join-Path $env:LOCALAPPDATA "Packages/$family/LocalState/game"
    $app
}

$asset = Get-AppxPackage -Name 'Horizon.Asset.26-3.98ccbead789d397639d5badd0bb805'
if (!$asset.IsFramework -or $asset.Architecture -ne 'Neutral' -or
    !(Test-Path -LiteralPath (Join-Path $asset.InstallLocation 'resources/horizon-resources.json') -PathType Leaf)) {
    throw 'Install the exact neutral resource framework before staging benchmark inputs.'
}
$server = Join-Path $scratch 'server'
$properties = Get-Content -LiteralPath (Join-Path $server 'server.properties')
foreach ($expected in 'server-port=25567', 'server-ip=127.0.0.1', 'level-seed=1234', 'level-name=HorizonParity',
    'view-distance=24', 'simulation-distance=12', 'gamemode=creative', 'online-mode=false') {
    if ($properties -notcontains $expected) { throw "The scratch server must have $expected." }
}

function Read-Profile([string]$File, [hashtable]$Overrides) {
    $lines = @(Get-Content -LiteralPath (Join-Path $scratch $File))
    foreach ($key in ($Overrides.Keys | Sort-Object)) {
        $prefix = "^${key}:"
        $lines = @($lines | Where-Object { $_ -notmatch $prefix })
        $lines += "${key}:$($Overrides[$key])"
    }
    return $lines
}

$profiles = @{
    'options.txt' = Read-Profile 'options.txt' @{ pauseOnLostFocus = 'false'; enableVsync = 'true' }
    'horizon-graphics.txt' = Read-Profile 'horizon-graphics.txt' @{ presentMode = 'fifo' }
}
$queue = Join-Path $server 'console.txt'
$commands = @('gamerule minecraft:advance_time false', 'gamerule minecraft:advance_weather false',
    'gamerule minecraft:spawn_mobs false', 'gamerule minecraft:random_tick_speed 0',
    'time set noon', 'weather clear')
$running = @(Get-Process HorizonUWP, horizon-client -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -in $clientPaths })
$listener = @(Get-NetTCPConnection -LocalPort 25567 -State Listen -ErrorAction SilentlyContinue)
if ($OutputDirectory) {
    $inputs = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
    foreach ($name in $profiles.Keys) {
        $profiles[$name] | Set-Content -LiteralPath (Join-Path $inputs $name) -Encoding ascii
    }
    $commands | Set-Content -LiteralPath (Join-Path $inputs 'console.txt') -Encoding ascii
}
$backup = $null
if ($Prepare) {
    if ($running.Count -or $listener.Count) { throw 'Close the scratch clients and server before staging benchmark profiles.' }
    $backup = Join-Path $scratch ('bench-inputs-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    for ($i = 0; $i -lt $profilePaths.Count; $i++) {
        $profile = $profilePaths[$i]
        New-Item -ItemType Directory -Path $profile -Force | Out-Null
        foreach ($name in $profiles.Keys) {
            $target = Join-Path $profile $name
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                Copy-Item -LiteralPath $target -Destination (Join-Path $backup "$i-$name")
            }
            $profiles[$name] | Set-Content -LiteralPath $target -Encoding ascii
        }
    }
    if (Test-Path -LiteralPath $queue -PathType Leaf) {
        Copy-Item -LiteralPath $queue -Destination (Join-Path $backup 'console.txt')
    }
    $commands | Set-Content -LiteralPath $queue -Encoding ascii
}

$matches = foreach ($profile in $profilePaths) {
    $equal = $true
    foreach ($name in $profiles.Keys) {
        $file = Join-Path $profile $name
        if (!(Test-Path -LiteralPath $file -PathType Leaf) -or
            ((Get-Content -Raw -LiteralPath $file).Trim() -replace '\r', '') -ne ($profiles[$name] -join "`n")) {
            $equal = $false
        }
    }
    [pscustomobject]@{ Path = $profile; MatchedProfile = $equal }
}
[pscustomobject]@{
    Upstream = $upstream
    Packages = @($packages | ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            Version = $_.Version.ToString()
            Architecture = $_.Architecture.ToString()
            Status = $_.Status.ToString()
        }
    })
    ServerPortFree = !$listener.Count
    RunningScratchProcesses = $running.Count
    StaleStopCommand = (Test-Path -LiteralPath $queue -PathType Leaf) -and
        (Select-String -LiteralPath $queue -Pattern '^\s*stop\s*$' -Quiet)
    PresentMonAvailable = Test-Path -LiteralPath (Join-Path $env:TEMP 'horizon-uwp-perf-20261118-160132/PresentMon-2.6.0-x64.exe')
    GpuSamplerAvailable = [bool](Get-Command nvidia-smi -ErrorAction SilentlyContinue)
    ProfilePaths = @($matches)
    PreparedInputs = $inputs
    Backup = $backup
} | ConvertTo-Json -Depth 4
