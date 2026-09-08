using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Server;
using Server.MirEnvir;

if (args.Length < 2) throw new ArgumentException("runtime-copy-directory output-jsonl [single]");
var output = Path.GetFullPath(args[1]);
Directory.SetCurrentDirectory(Path.GetFullPath(args[0]));
Packet.IsServer = true;
Settings.Load();
Settings.IPAddress = "127.0.0.1";
Settings.Port = ushort.Parse(Environment.GetEnvironmentVariable("PERF_PORT") ?? "17000");
Settings.StartHTTPService = false;
Settings.CheckVersion = false;
if (args.Contains("single")) Settings.Multithreaded = false;
var envir = Envir.Main;
typeof(Envir).GetField("StatusPortEnabled", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(envir, false);
using var writer = new StreamWriter(output) { AutoFlush = true };
var clock = Stopwatch.StartNew();
var proc = Process.GetCurrentProcess();
Console.WriteLine($"PID={proc.Id} Multi={Settings.Multithreaded} Threads={Settings.ThreadLimit}");
envir.Start();
try
{
    for (int i = 0; i < int.Parse(Environment.GetEnvironmentVariable("PERF_SECONDS") ?? "150"); i++)
    {
        Thread.Sleep(1000);
        proc.Refresh();
        writer.WriteLine(JsonSerializer.Serialize(new {
            seconds=clock.Elapsed.TotalSeconds, cpuSeconds=proc.TotalProcessorTime.TotalSeconds,
            workingSet=proc.WorkingSet64, privateBytes=proc.PrivateMemorySize64,
            managedBytes=GC.GetTotalMemory(false), allocatedBytes=GC.GetTotalAllocatedBytes(),
            gen0=GC.CollectionCount(0), gen1=GC.CollectionCount(1), gen2=GC.CollectionCount(2),
            heapBytes=GC.GetGCMemoryInfo().HeapSizeBytes, fragmentedBytes=GC.GetGCMemoryInfo().FragmentedBytes,
            running=envir.Running, maps=envir.MapList.Count, monsters=envir.MonsterCount,
            players=envir.PlayerCount, lastRunMs=Envir.LastRunTime,
            multi=Settings.Multithreaded, threads=Settings.ThreadLimit
        }));
        while (MessageQueue.Instance.MessageLog.TryDequeue(out var message)) Console.Write(message);
        if (!envir.Running) break;
    }
    // Census after sampling: deliberately excluded from the timed baseline.
    long cells=0, occupiedCells=0, indexCapacity=0, indexedObjects=0, walkable=0, respawnPoints=0;
    int maximumCellObjects=0;
    long overflowEvents=0;
    ulong terrainChecksum=14695981039346656037;
    foreach(var map in envir.MapList)
    {
        cells += (long)map.Width * map.Height;
        var stats = map.GetCellStatistics();
        occupiedCells += stats.Occupied;
        indexCapacity += stats.Capacity;
        indexedObjects += stats.Objects;
        maximumCellObjects = Math.Max(maximumCellObjects, stats.Maximum);
        overflowEvents += stats.Overflows;
        terrainChecksum = (terrainChecksum ^ map.GetTerrainChecksum()) * 1099511628211;
        walkable += map.WalkableCells?.Count ?? 0;
        foreach(var spawn in map.Respawns) respawnPoints += spawn.WalkableCells?.Count ?? 0;
    }
    var pool = CellObjectPool.Statistics;
    var missingObjects = envir.Objects.Count(obj => obj.CurrentMap == null ||
        !obj.CurrentMap.ContainsObjectAt(obj.CurrentLocation, obj));
    File.WriteAllText(output+".census.json", JsonSerializer.Serialize(new {cells,occupiedCells,indexCapacity,indexedObjects,
        maximumCellObjects,overflowEvents,pool.Retained,pool.Limit,pool.Rents,pool.Hits,walkable,respawnPoints,
        terrainChecksum,globalObjects=envir.Objects.Count,missingObjects}));
}
finally { envir.Stop(); }
