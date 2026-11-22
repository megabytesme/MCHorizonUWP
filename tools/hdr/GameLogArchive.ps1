function Read-HdrLogPart([string]$Path) {
    if (!$Path.EndsWith('.gz')) { return ,([IO.File]::ReadAllBytes($Path)) }
    $file = [IO.File]::OpenRead($Path)
    try {
        $gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionMode]::Decompress)
        $output = [IO.MemoryStream]::new()
        try {
            $gzip.CopyTo($output)
            return ,($output.ToArray())
        } finally { $output.Dispose(); $gzip.Dispose() }
    } finally { $file.Dispose() }
}

function Save-HdrGameLog([string]$Latest, [string]$Destination, [DateTime]$Started, [string]$PreviousHash) {
    $directory = Split-Path -Parent $Latest
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { return }
    # The log formats its events in local civil time, to whole seconds.
    $localStart = $Started.ToLocalTime()
    $earliest = $localStart.AddTicks(-($localStart.Ticks % [TimeSpan]::TicksPerSecond))
    $today = [DateTime]::Now.Date
    $parts = @(Get-ChildItem -LiteralPath $directory -Filter '*.log.gz' -File | ForEach-Object {
        if ($_.Name -match '^(\d{4}-\d{2}-\d{2})-(\d+)\.log\.gz$') {
            $date = [DateTime]::ParseExact($Matches[1], 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
            if ($date -ge $earliest.Date -and $date -le $today) {
                @{file=$_; date=$date; index=[int]$Matches[2]}
            }
        }
    } | Sort-Object { $_.date }, { $_.index })
    if (Test-Path -LiteralPath $Latest -PathType Leaf) {
        $tail = Get-Item -LiteralPath $Latest
        $parts += @{file=$tail; date=$tail.LastWriteTime.Date; index=0}
    }
    $hash = [Security.Cryptography.SHA256]::Create()
    $combined = $null
    $copied = @()
    try {
        foreach ($part in $parts) {
            $data = Read-HdrLogPart $part.file.FullName
            if ($data.Length -eq 0) { continue }
            $prefix = [Text.Encoding]::ASCII.GetString($data, 0, [Math]::Min(10, $data.Length))
            if ($prefix -notmatch '^\[(\d{2}:\d{2}:\d{2})\]') { continue }
            $at = $part.date + [TimeSpan]::ParseExact($Matches[1], 'c', [Globalization.CultureInfo]::InvariantCulture)
            if ($at -lt $earliest) { continue }
            # Startup compresses the previous process's latest.log with a fresh mtime and
            # can reindex files. Match its uncompressed bytes, even within the same second.
            $digest = [BitConverter]::ToString($hash.ComputeHash($data)).Replace('-', '')
            if ($digest -eq $PreviousHash) { continue }
            if (!$combined) {
                $combined = [IO.File]::Create((Join-Path $Destination 'game.log'))
                $raw = [IO.Directory]::CreateDirectory((Join-Path $Destination 'game-log-parts')).FullName
            }
            Copy-Item -LiteralPath $part.file.FullName -Destination (Join-Path $raw $part.file.Name)
            $combined.Write($data, 0, $data.Length)
            $copied += $part.file.Name
        }
        $copied
    } finally {
        if ($combined) { $combined.Dispose() }
        $hash.Dispose()
    }
}
