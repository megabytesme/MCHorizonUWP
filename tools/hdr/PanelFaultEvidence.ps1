function Get-PanelFaultEvidence {
    param(
        [ValidateSet('FreshCheck', 'FreshSet', 'ReusedSetFromSdr', 'ReusedSetDetachHdrRetry')][string]$Case,
        [string[]]$Lifecycle,
        [string[]]$Game,
        [DateTimeOffset]$RequestedAt
    )

    $site = @{FreshCheck=0; FreshSet=2; ReusedSetFromSdr=3; ReusedSetDetachHdrRetry=3}[$Case]
    $events = @($Lifecycle | Where-Object {
        $stamp = [DateTimeOffset]::MinValue
        $_ -match '^(\S+) tid=\d+ ' -and [DateTimeOffset]::TryParse($Matches[1], [ref]$stamp) -and
            $stamp -ge $RequestedAt
    })
    $result = [ordered]@{requestUtc=$RequestedAt.ToString('o'); site=$site; events=$events.Count; complete=$false}
    $consumed = @($events | Where-Object { $_ -match " HDR panel fault: consumed request site=$site$" })
    if ($consumed.Count -eq 0) { return $result }
    if ($consumed.Count -ne 1) { throw "Panel fault request site $site was consumed more than once." }
    $result.consumed = $consumed[0]
    $tid = [regex]::Match($result.consumed, '^\S+ tid=(\d+) ').Groups[1].Value
    $onGameThread = "^\S+ tid=$tid "
    $faultIndex = [array]::IndexOf($events, $result.consumed)
    if ($Case -eq 'ReusedSetFromSdr') {
        $result.priorSwapchain = $events | Select-Object -First $faultIndex | Where-Object {
            $_ -match "${onGameThread}DXGI color configure: desc_hr=0x00000000 format=\d+ alpha=3 .*set_hr=0x00000000"
        } | Select-Object -Last 1
        if ($result.priorSwapchain -notmatch 'format=87 .*space=0 ') { return $result }
    }
    $after = @($events | Select-Object -Skip ($faultIndex + 1))
    if ($Case -eq 'FreshCheck') {
        $result.nativeRejection = $Game | Where-Object {
            $support = [regex]::Match($_, 'lacks DXGI present support: (Err\(.+\)|Ok\((\d+)\))\s*$')
            $support.Success -and (!$support.Groups[2].Success -or (([uint32]$support.Groups[2].Value -band 1) -eq 0))
        } | Select-Object -First 1
        $next = $after
    } else {
        $nativeSet = $after | Where-Object {
            $_ -match "${onGameThread}DXGI color configure: desc_hr=0x00000000 format=10 alpha=3 .*space=-1 set_hr=0x([0-9A-F]{8})\b.*view_kind=2"
        } | Select-Object -First 1
        if (!$nativeSet) { return $result }
        if ($nativeSet -notmatch 'set_hr=0x[89A-F][0-9A-F]{7}\b') { throw 'DXGI accepted the custom color space.' }
        $result.nativeRejection = $nativeSet
        $next = @($after | Select-Object -Skip ([array]::IndexOf($after, $nativeSet) + 1))
    }
    if (!$result.nativeRejection) { return $result }
    if ($Case -eq 'ReusedSetDetachHdrRetry') {
        $detach = @($next | Where-Object { $_ -match "${onGameThread}HDR panel fault: consumed request site=4$" })
        if ($detach.Count -eq 0) { return $result }
        if ($detach.Count -ne 1) { throw 'The rollback detach fault was consumed more than once.' }
        $result.detachFault = $detach[0]
        $injected = $Game | Where-Object { $_ -match 'Injected one-shot UWP DX12 SwapChainPanel rollback detach failure' } | Select-Object -First 1
        if (!$injected) { return $result }
        $result.detachError = $injected
        $result.detachRetry = $Game | Select-Object -Skip ([array]::IndexOf($Game, $injected) + 1) |
            Where-Object { $_ -match 'UWP DX12 SwapChainPanel pending detach retry succeeded' } | Select-Object -First 1
        if (!$result.detachRetry) { return $result }
        $next = @($next | Select-Object -Skip ([array]::IndexOf($next, $detach[0]) + 1))
    }
    $expectedOutput = if ($Case -eq 'ReusedSetDetachHdrRetry') { '10:1' } else { '87:0' }
    $recovered = $next | Where-Object {
        $_ -match "${onGameThread}DXGI color configure: desc_hr=0x00000000 format=(?:87|10) alpha=3 .*space=[01] set_hr=0x00000000 check_hr=0x00000000 support=0x[0-9A-F]*[13579BDF]\b view_kind=2"
    } | Select-Object -First 1
    if (!$recovered) { return $result }
    $result.restoredSwapchain = $recovered
    $output = [regex]::Match($recovered, 'format=(\d+) .*space=(\d+) ')
    $result.restoredOutput = "$($output.Groups[1].Value):$($output.Groups[2].Value)"
    if ($result.restoredOutput -ne $expectedOutput) { return $result }
    $restoredIndex = [array]::IndexOf($after, $recovered)
    if ($after | Select-Object -First $restoredIndex | Where-Object { $_ -match "${onGameThread}game swap chain attachment requested$" }) {
        throw 'A new chain requested XAML attachment before the post-fault configuration.'
    }
    $afterRestored = @($after | Select-Object -Skip ($restoredIndex + 1))
    $result.restoredAttachRequest = $afterRestored | Where-Object { $_ -match "${onGameThread}game swap chain attachment requested$" } |
        Select-Object -First 1
    if (!$result.restoredAttachRequest) { return $result }
    $result.restoredAttach = $afterRestored | Select-Object -Skip ([array]::IndexOf($afterRestored, $result.restoredAttachRequest) + 1) |
        Where-Object { $_ -match ' game SwapChainPanel SetSwapChain (?:completed|failed)$' } | Select-Object -First 1
    if (!$result.restoredAttach) { return $result }
    if ($result.restoredAttach -match 'failed$') { throw 'Post-fault XAML attachment failed.' }
    if ($expectedOutput -eq '87:0' -and ($afterRestored | Where-Object {
        $_ -match "${onGameThread}DXGI color configure: desc_hr=0x00000000 format=10 alpha=3 .*space=1 set_hr=0x00000000"
    })) { throw 'The panel re-entered HDR after the diagnostic SDR fallback.' }
    $result.complete = $true
    return $result
}
