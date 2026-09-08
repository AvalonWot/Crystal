param([string]$Path)
$samples = @(Get-Content -LiteralPath $Path | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.seconds -ge 30 -and $_.seconds -le 140 })
$first = $samples[0]
$last = $samples[-1]
$duration = $last.seconds - $first.seconds
$loops = @($samples.lastRunMs | Sort-Object)
[pscustomobject]@{
    Samples=$samples.Count
    DurationSeconds=$duration
    CpuCores=($last.cpuSeconds-$first.cpuSeconds)/$duration
    WorkingSetMiB=($samples.workingSet | Measure-Object -Average).Average/1MB
    ManagedMiB=($samples.managedBytes | Measure-Object -Average).Average/1MB
    AllocationKiBPerSecond=($last.allocatedBytes-$first.allocatedBytes)/$duration/1KB
    Gen0=$last.gen0-$first.gen0
    Gen1=$last.gen1-$first.gen1
    Gen2=$last.gen2-$first.gen2
    LoopMedianMs=$loops[[int][Math]::Floor($loops.Count*0.5)]
    LoopP95Ms=$loops[[int][Math]::Floor(($loops.Count-1)*0.95)]
    Maps=$last.maps
    Monsters=$last.monsters
    Players=$last.players
} | ConvertTo-Json
