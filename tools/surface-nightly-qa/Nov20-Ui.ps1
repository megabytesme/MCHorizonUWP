param([Parameter(Mandatory)][int]$GamePid,[string]$Action,[string]$Value,[Parameter(Mandatory)][ValidateSet('arm64')][string]$Architecture)
$ErrorActionPreference = 'Stop'
$name = "MCHorizonUWP.NightlyNov20.$Architecture"
$package = Get-AppxPackage -Name $name
if (-not $package -or $package.Name -cne $name -or $package.Architecture -ne $Architecture -or
    (Get-Process -Id $GamePid).Path -ne (Join-Path $package.InstallLocation 'HorizonUWP.exe') -or
    !(Test-Path -LiteralPath (Join-Path $package.InstallLocation 'horizon-isolated-test.flag'))) {
    throw 'This UI helper only targets the November 20 isolated test package.'
}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$scope = [Windows.Automation.TreeScope]
$root = [Windows.Automation.AutomationElement]::RootElement
$all = [Windows.Automation.Condition]::TrueCondition
$owner = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $GamePid)
$win = $root.FindAll($scope::Children, $all) | Where-Object {
    $_.Current.ProcessId -eq $GamePid -or $_.FindFirst($scope::Descendants, $owner)
} | Where-Object { -not $_.Current.IsOffscreen } | Select-Object -First 1
if (-not $win) { throw 'Scratch UWP window unavailable.' }
$children = $win.FindAll($scope::Descendants, $all)
if (-not $Action) {
    foreach ($child in $children) {
        if ($child.Current.Name -or $child.Current.ControlType.ProgrammaticName -eq 'ControlType.Edit') {
            '{0} | {1} | {2}' -f $child.Current.ControlType.ProgrammaticName,$child.Current.Name,$child.Current.AutomationId
        }
    }
    return
}
$target = $children | Where-Object { $_.Current.Name -eq $Action -or $_.Current.AutomationId -eq $Action } | Select-Object -First 1
if (-not $target) { throw "Control unavailable: $Action" }
$pattern = $null
if ($PSBoundParameters.ContainsKey('Value')) {
    if (-not $target.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)) { throw 'Control has no value pattern.' }
    $pattern.SetValue($Value)
    if ($pattern.Current.Value -cne $Value) { throw 'The input field changed while setting its value.' }
} elseif ($target.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)) {
    $pattern.Invoke()
} elseif ($target.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)) {
    $pattern.Select()
} elseif ($target.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$pattern)) {
    $pattern.Expand()
} else { throw 'Control has no supported action.' }
