param(
    [ValidateSet(100,75,67,50)][int]$Scale = 50,
    [switch]$Adaptive,
    [ValidateSet(4,16)][int]$TextureBudgetMiB = 16,
    [switch]$CollectAlreadyRunning,
    [ValidateSet('DrsSmoke','DrsTrace')][string]$Variant = 'DrsSmoke',
    [version]$AppVersion = '1.0.0.0',
    [ValidateRange(120,600)][int]$TimeoutSeconds = 420,
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Label,
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'horizon-lumia-drs-cases')
)

$ErrorActionPreference='Stop'
. "$env:USERPROFILE/.codex/lumia-device-portal/portal.ps1"
$identity = "MCHorizonUWP.NightlyNov20.$Variant.arm"
$package = "${identity}_${AppVersion}_arm__4pd4jeb6snrqw"
$appId = "${identity}_4pd4jeb6snrqw!App"
$storage = "knownfolderid=LocalAppData&packagefullname=$package"
$game = '%5CLocalState%5Cgame'
$logs = "$game%5Cisolated-test-home%5Clogs"
$screens = "$game%5Cscreenshots"
$period = Get-Date -Format yyyyMMdd-HHmmss
if(!$Label){ $Label = if($Adaptive){ "adaptive-$Scale" } else { "fixed-$Scale" } }
$out = Join-Path $OutputDirectory "$period-$Label"
New-Item -ItemType Directory -Path $out | Out-Null
$encoding = [Text.UTF8Encoding]::new($false, $true)

function Encode([string]$Value) {
    [Uri]::EscapeDataString([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Value)))
}
function Read-HorizonProcesses {
    (Invoke-LumiaPortal -Path '/api/resourcemanager/processes' | ConvertFrom-Json).Processes |
        Where-Object ImageName -ceq 'HorizonUWP.exe'
}
function Read-GameProcess {
    Read-HorizonProcesses | Where-Object PackageFullName -ceq $package
}
function Read-Folder([string]$Path) {
    (Invoke-LumiaPortal -Path "/api/filesystem/apps/files?$storage&path=$Path" | ConvertFrom-Json).Items
}
function Read-File([string]$Folder, [string]$File, [string]$Destination = $File) {
    $name = [uri]::EscapeDataString($File)
    Invoke-LumiaPortal -Path "/api/filesystem/apps/file?$storage&path=$Folder&filename=$name" -OutFile (Join-Path $out $Destination)
}
function Update-Setting([string]$Text, [string]$Key, [string]$Value) {
    $entry = [regex]('(?m)^' + [regex]::Escape($Key) + ':[^\r\n]*(?=\r?$)')
    if($entry.Matches($Text).Count -ne 1){throw "Required single diagnostic setting was missing: $Key"}
    $entry.Replace($Text, ('{0}:{1}' -f $Key,$Value), 1)
}
function Save-Notes([string]$Message) {
    $line = "{0:o} {1}" -f [DateTime]::UtcNow, $Message
    Add-Content -LiteralPath "$out/run.log" -Value $line
    Write-Output $line
}
function Read-OptionalFile([string]$Path,[string]$Name,[string]$Local = $Name) {
    try { Read-File $Path $Name $Local }
    catch {
        Save-Notes "$Name not downloaded. $($_.Exception.Message)"
    }
}

$began = [DateTime]::UtcNow
$current = @(Read-GameProcess)
Read-File $game 'horizon-graphics.txt' 'graphics-before.txt'
Read-File $game 'options.txt'
Read-File $game 'horizon-mods.txt'
$settings = [IO.File]::ReadAllText("$out/graphics-before.txt",$encoding)
$options = [IO.File]::ReadAllText("$out/options.txt",$encoding)
if($options -notmatch '(?m)^renderDistance:2\r?$'){ throw 'The isolated game is not at two-chunk render distance.' }
if($settings -notmatch '(?m)^textureBudgetMiB:(?:4|16)\r?$'){ throw 'The isolated game is missing its diagnostic texture budget.' }
$toggle = $Adaptive.IsPresent.ToString().ToLowerInvariant()
$previousScreens = @{}
$previousErrors = @{}
$previousArchives = @{}
function Read-DeviceSnapshot([string]$When) {
    foreach ($kind in 'systemperf','battery') {
        $endpoint = if($kind -eq 'systemperf'){ '/api/resourcemanager/systemperf' } else { '/api/power/battery' }
        try {
            $reply = Invoke-LumiaPortal -Path $endpoint | ConvertFrom-Json
            $fields = if($kind -eq 'systemperf'){
                $reply | Select-Object AvailablePages,CommittedPages,PageSize,TotalInstalledInKb,CpuLoad,GPUData
            } else {
                $reply | Select-Object AcOnline,BatteryPresent,Charging,MaximumCapacity,RemainingCapacity
            }
            [ordered]@{ utc = [DateTime]::UtcNow.ToString('o'); values = $fields } |
                ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$out/$When-$kind.json" -Encoding UTF8
        } catch { Save-Notes "Device $kind query failed. $($_.Exception.Message)" }
    }
}
Read-DeviceSnapshot 'start'
if(!$CollectAlreadyRunning) {
    try {
        Read-Folder $logs | Where-Object Name -Match '^\d{4}-\d{2}-\d{2}-\d+\.log\.gz$' |
            ForEach-Object { $previousArchives[$_.Name] = $_.DateCreated }
    } catch { Save-Notes "Previous rotated-log query failed. $($_.Exception.Message)" }
    try {
        Read-Folder '%5CLocalState' | Where-Object { $_.Name -match '^horizon-error-[0-9_-]+\.txt$' } |
            ForEach-Object { $previousErrors[$_.Name] = $_.DateCreated }
    } catch { Save-Notes "Previous crash-file query failed. $($_.Exception.Message)" }
    if($current.Count){ throw 'Wait for the isolated test to quit before changing its graphics settings.' }
    $target = Update-Setting $settings resolutionScale $Scale
    $target = Update-Setting $target dynamicResolution $toggle
    $target = Update-Setting $target targetFps 30
    $target = Update-Setting $target textureBudgetMiB $TextureBudgetMiB
    [IO.File]::WriteAllText("$out/horizon-graphics.txt",$target,$encoding)
    # Query process state again before changing the existing isolated settings file.
    if (Read-HorizonProcesses) { throw 'Another Horizon process is active. Wait for it to close before editing or activating the diagnostic.' }
    Invoke-LumiaPortal -Path "/api/filesystem/apps/file?$storage&path=$game&extract=false" -Method POST -UploadFiles @("$out/horizon-graphics.txt")
    Read-File $game 'horizon-graphics.txt' 'graphics-verified.txt'
    if((Get-FileHash -LiteralPath "$out/graphics-verified.txt").Hash -cne (Get-FileHash -LiteralPath "$out/horizon-graphics.txt").Hash){
        throw 'The isolated scale settings did not round-trip.'
    }
    Read-OptionalFile $game 'performance-trace.json' 'trace-before'
    try {
        Read-Folder $screens | ForEach-Object { $previousScreens[$_.Name] = $_.DateCreated }
    } catch { Save-Notes 'No previous isolated screenshot folder was readable.' }
    Invoke-LumiaPortal -Method POST -Path "/api/taskmanager/app?appid=$(Encode $appId)&package=$(Encode $package)"
    Save-Notes "Activated $package, resolutionScale=$Scale, dynamicResolution=$toggle, textureBudgetMiB=$TextureBudgetMiB"
} else {
    if($settings -notmatch "(?m)^resolutionScale:$Scale\r?$" -or $settings -notmatch "(?m)^dynamicResolution:$toggle\r?$") {
        throw 'The already running or completed case has a different scale.'
    }
    Save-Notes "Collecting existing $package"
}

$seen = $current.Count -gt 0
$initialPid = if ($seen) { [int]$current[0].ProcessId } else { 0 }
$failures = 0
$deadline = $began.AddSeconds($TimeoutSeconds)
do {
    try {
        $samples = @(Read-GameProcess)
        $failures = 0
        if($samples.Count -gt 1){ throw 'The test package has more than one game process.' }
        if($samples.Count) {
            $seen = $true
            $sample = $samples[0]
            if ($initialPid -eq 0) { $initialPid = [int]$sample.ProcessId }
            if ($initialPid -ne [int]$sample.ProcessId) {
                Save-Notes "The original game process $initialPid ended and diagnostic PID $($sample.ProcessId) started."
                break
            }
            [pscustomobject]@{
                utc = [DateTime]::UtcNow.ToString('o')
                pid = $sample.ProcessId
                working_set_bytes = $sample.WorkingSetSize
                private_working_set_bytes = $sample.PrivateWorkingSet
                pagefile_bytes = $sample.PageFileUsage
                cpu_percent = $sample.CPUUsage
            } | Export-Csv -LiteralPath "$out/memory.csv" -Append -NoTypeInformation
        } elseif($seen -or $CollectAlreadyRunning) {
            Save-Notes 'Game process is no longer resident.'
            break
        }
    } catch {
        $failures++
        Save-Notes "Portal process sample failed. $($_.Exception.Message)"
        if($failures -ge 3){ break }
    }
    Start-Sleep -Seconds 5
} while([DateTime]::UtcNow -lt $deadline)

Read-DeviceSnapshot 'end'
Read-OptionalFile '%5CLocalState' 'horizon-uwp-lifecycle.log' 'lifecycle.log'
Read-OptionalFile $logs 'latest.log' 'game.log'
try {
    $archives = @(Read-Folder $logs | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}-\d+\.log\.gz$' -and
        $_.FileSize -gt 0 -and (!$previousArchives.ContainsKey($_.Name) -or $previousArchives[$_.Name] -ne $_.DateCreated) } |
        Sort-Object DateCreated)
    foreach ($archive in $archives) {
        Read-File $logs $archive.Name
    }
} catch { Save-Notes "Rotated-log query failed. $($_.Exception.Message)" }
foreach($name in 'performance-trace.json','performance-trace.hzperf') {
    try {
        $item = Read-Folder $game | Where-Object Name -ceq $name
        if($item.FileSize -gt 0){ Read-File $game $name 'trace.capture'; break }
    } catch { Save-Notes "Capture query failed. $($_.Exception.Message)" }
}
if((Test-Path "$out/trace-before") -and (Test-Path "$out/trace.capture") -and
   (Get-FileHash -LiteralPath "$out/trace-before").Hash -ceq (Get-FileHash -LiteralPath "$out/trace.capture").Hash){
    Rename-Item -LiteralPath "$out/trace.capture" -NewName 'trace.stale'
    Save-Notes 'The capture file did not change during this launch.'
}

try {
    Read-Folder $screens | Where-Object { $_.Name -match '^[A-Za-z0-9_.-]+\.png$' -and $_.FileSize -gt 0 -and
        (!$previousScreens.ContainsKey($_.Name) -or $previousScreens[$_.Name] -ne $_.DateCreated) } |
        Sort-Object DateCreated -Descending | Select-Object -First 1 | ForEach-Object {
            Read-File $screens $_.Name 'world.png'
            Save-Notes "Downloaded $($_.Name) from the isolated screenshot folder."
        }
} catch { Save-Notes "No app screenshot was collected. $($_.Exception.Message)" }

try {
    Read-Folder '%5CLocalState' | Where-Object { $_.Name -match '^horizon-error-[0-9_-]+\.txt$' -and
        (!$previousErrors.ContainsKey($_.Name) -or $previousErrors[$_.Name] -ne $_.DateCreated) } |
        Sort-Object DateCreated -Descending | ForEach-Object { Read-File '%5CLocalState' $_.Name }
} catch { Save-Notes "Crash-file query failed. $($_.Exception.Message)" }
Save-Notes "Evidence directory $out"
