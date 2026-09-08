param([string]$Path)
# Speedscope export contains explicit CPU_TIME / UNMANAGED_CODE_TIME markers.
# Exclude waiting/unmanaged markers; report sampled active managed leaf time.
$trace = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
$totals = @{}
$cpu = 0.0
foreach ($profile in $trace.profiles) {
    $stack = [Collections.Generic.List[int]]::new()
    $previous = $profile.startValue
    foreach ($event in $profile.events) {
        if ($stack.Count -gt 1 -and $trace.shared.frames[$stack[$stack.Count-1]].name -eq 'CPU_TIME') {
            $key = $trace.shared.frames[$stack[$stack.Count-2]].name
            $delta = $event.at - $previous
            $totals[$key] += $delta
            $cpu += $delta
        }
        if ($event.type -eq 'O') { $stack.Add($event.frame) }
        else { $stack.RemoveAt($stack.Count-1) }
        $previous = $event.at
    }
}
if ($cpu -le 0) { throw 'No CPU_TIME samples found in this export.' }
$totals.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20 Name,
    @{n='SampledActiveManagedPercent';e={$_.Value / $cpu * 100}}
