using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Server.Tests;

public sealed class CellStorageTests : IDisposable
{
    public CellStorageTests() => CellObjectPool.Configure(0);
    public void Dispose() => CellObjectPool.Configure(4096);

    private static Map CreateMap(int width = 32, int height = 32)
    {
        var map = new Map(new MapInfo { Index = 1 });
        map.InitializeTerrain(width, height);
        return map;
    }

    private static DecoObject CreateObject(Map map, Point location) => new()
    {
        CurrentMap = map,
        CurrentLocation = location
    };

    [Fact]
    public void EmptyTerrainUsesNoDynamicIndex()
    {
        var map = CreateMap(20, 10);

        Assert.True(map.GetTerrain(19, 9).Valid);
        Assert.Equal(-1, map.GetTerrain(19, 9).FishingAttribute);
        Assert.Equal(2, Marshal.SizeOf<CellTerrain>());
        Assert.Equal(0, map.GetObjectCount(new Point(1, 1)));
        Assert.Equal((0, 0, 0), (map.GetCellStatistics().Occupied,
            map.GetCellStatistics().Capacity, map.GetCellStatistics().Objects));
    }

    [Fact]
    public void ObjectCountTransitionsPreserveOrderBeyondPooledCapacity()
    {
        var map = CreateMap();
        var location = new Point(2, 3);
        var objects = Enumerable.Range(0, 17).Select(_ => CreateObject(map, location)).ToArray();

        foreach (var obj in objects) map.AddObjectAt(location, obj);

        using (var snapshot = map.RentObjectsSnapshot(location))
        {
            Assert.Equal(17, snapshot.Count);
            for (var i = 0; i < objects.Length; i++) Assert.Same(objects[i], snapshot[i]);
        }
        Assert.Equal(17, map.GetCellStatistics().Maximum);
        Assert.Equal(1, map.GetCellStatistics().Overflows);

        for (var i = 16; i > 0; i--) map.RemoveObjectAt(location, objects[i]);
        using (var snapshot = map.RentObjectsSnapshot(location))
        {
            Assert.Single(snapshot);
            Assert.Same(objects[0], snapshot[0]);
        }
        map.RemoveObjectAt(location, objects[0]);
        Assert.Equal((0, 0), (map.GetCellStatistics().Occupied, map.GetCellStatistics().Capacity));
    }

    [Fact]
    public void PoolHonoursGlobalContainerCountLimit()
    {
        CellObjectPool.Configure(2);
        var map = CreateMap();
        for (var cell = 0; cell < 8; cell++)
        {
            var location = new Point(cell, 0);
            var first = CreateObject(map, location);
            var second = CreateObject(map, location);
            map.AddObjectAt(location, first);
            map.AddObjectAt(location, second);
            map.RemoveObjectAt(location, second);
            map.RemoveObjectAt(location, first);
        }

        Assert.InRange(CellObjectPool.Statistics.Retained, 0, 2);
        CellObjectPool.Configure(0);
        Assert.Equal(0, CellObjectPool.Statistics.Retained);
    }

    [Fact]
    public void NegativePoolLimitIsClampedToZero()
    {
        CellObjectPool.Configure(-1);

        Assert.Equal(0, CellObjectPool.Statistics.Limit);
        Assert.Equal(0, CellObjectPool.Statistics.Retained);
    }

    [Fact]
    public void SnapshotCopiesCanOnlyReturnBufferOnce()
    {
        CellObjectPool.Configure(4);
        var map = CreateMap();
        var location = new Point(1, 1);
        map.AddObjectAt(location, CreateObject(map, location));
        map.AddObjectAt(location, CreateObject(map, location));

        var snapshot = map.RentObjectsSnapshot(location);
        var copy = snapshot;
        snapshot.Dispose();
        copy.Dispose();

        Assert.Equal(1, CellObjectPool.Statistics.Retained);
        Assert.Throws<ObjectDisposedException>(() => _ = copy.Count);
    }

    [Fact]
    public void NestedSnapshotsHaveIndependentLeases()
    {
        CellObjectPool.Configure(4);
        var map = CreateMap();
        var location = new Point(1, 1);
        var first = CreateObject(map, location);
        var second = CreateObject(map, location);
        map.AddObjectAt(location, first);
        map.AddObjectAt(location, second);

        var outer = map.RentObjectsSnapshot(location);
        var inner = map.RentObjectsSnapshot(location);
        outer.Dispose();

        Assert.Equal(2, inner.Count);
        Assert.Same(first, inner[0]);
        Assert.Same(second, inner[1]);
        inner.Dispose();
        Assert.Equal(2, CellObjectPool.Statistics.Retained);
    }

    [Fact]
    public void MoveUpdatesMembershipAndObjectCoordinatesTogether()
    {
        var source = CreateMap();
        var destination = CreateMap();
        var start = new Point(1, 1);
        var end = new Point(2, 2);
        var obj = CreateObject(source, start);
        source.AddObjectAt(start, obj);

        source.MoveObject(obj, destination, end);

        Assert.Equal(0, source.GetObjectCount(start));
        Assert.Equal(1, destination.GetObjectCount(end));
        Assert.Same(destination, obj.CurrentMap);
        Assert.Equal(end, obj.CurrentLocation);
    }

    [Fact]
    public void ActiveIndexEntriesFollowCurrentOccupancy()
    {
        var map = CreateMap(2048, 1);
        var objects = Enumerable.Range(0, 1024)
            .Select(x => CreateObject(map, new Point(x, 0))).ToArray();
        for (var x = 0; x < objects.Length; x++) map.AddObjectAt(new Point(x, 0), objects[x]);
        for (var x = 100; x < objects.Length; x++) map.RemoveObjectAt(new Point(x, 0), objects[x]);
        Assert.Equal(100, map.GetCellStatistics().Capacity);
        Assert.Equal(100, map.GetCellStatistics().Occupied);
    }

    [Fact]
    public async Task ConcurrentSnapshotsAndMovesDoNotLeakOrDuplicateMembership()
    {
        CellObjectPool.Configure(16);
        var map = CreateMap();
        var left = new Point(1, 1);
        var right = new Point(2, 1);
        var obj = CreateObject(map, left);
        var upperLeft = new Point(1, 2);
        var upperRight = new Point(2, 2);
        var secondObject = CreateObject(map, upperLeft);
        map.AddObjectAt(left, obj);
        map.AddObjectAt(upperLeft, secondObject);

        var mover = Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
                map.MoveObject(obj, i % 2 == 0 ? right : left);
        });
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                using var leftSnapshot = map.RentObjectsSnapshot(left);
                for (var j = 0; j < leftSnapshot.Count; j++)
                    _ = leftSnapshot.IsCurrent(leftSnapshot[j]);
                using var rightSnapshot = map.RentObjectsSnapshot(right);
                for (var j = 0; j < rightSnapshot.Count; j++)
                    _ = rightSnapshot.IsCurrent(rightSnapshot[j]);
            }
        });
        var secondMover = Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
                map.MoveObject(secondObject, i % 2 == 0 ? upperRight : upperLeft);
        });

        await Task.WhenAll(mover, reader, secondMover);

        Assert.Equal(2, map.GetCellStatistics().Objects);
        Assert.True(map.ContainsObjectAt(obj.CurrentLocation, obj));
        Assert.True(map.ContainsObjectAt(secondObject.CurrentLocation, secondObject));
        Assert.InRange(CellObjectPool.Statistics.Retained, 0, 16);
    }

    [Fact]
    public void MovingAcrossLargeHistoricalAreaKeepsOnlyCurrentEntry()
    {
        var map = CreateMap(250_000, 1);
        var obj = CreateObject(map, Point.Empty);
        map.AddObjectAt(Point.Empty, obj);

        for (var x = 1; x < map.Width; x++) map.MoveObject(obj, new Point(x, 0));

        var stats = map.GetCellStatistics();
        Assert.Equal(1, stats.Occupied);
        Assert.Equal(1, stats.Capacity);
        Assert.Equal(1, stats.Objects);
        Assert.True(map.ContainsObjectAt(new Point(map.Width - 1, 0), obj));
    }

    [Fact]
    public void RepeatedHighWaterMarksReleasePagesAndRespectPoolLimit()
    {
        CellObjectPool.Configure(32);
        var map = CreateMap(4096, 1);
        for (var round = 0; round < 10; round++)
        {
            var objects = new List<(Point Location, DecoObject First, DecoObject Second)>();
            for (var x = 0; x < map.Width; x += 4)
            {
                var location = new Point(x, 0);
                var first = CreateObject(map, location);
                var second = CreateObject(map, location);
                map.AddObjectAt(location, first);
                map.AddObjectAt(location, second);
                objects.Add((location, first, second));
            }
            foreach (var item in objects)
            {
                map.RemoveObjectAt(item.Location, item.Second);
                map.RemoveObjectAt(item.Location, item.First);
            }
            Assert.Equal((0, 0, 0), (map.GetCellStatistics().Occupied,
                map.GetCellStatistics().Capacity, map.GetCellStatistics().Objects));
        }
        Assert.InRange(CellObjectPool.Statistics.Retained, 0, 32);
    }

    [Fact]
    public void CrossMapMoveUpdatesSpecializedMapLists()
    {
        var source = CreateMap();
        var destination = CreateMap();
        var spell = new SpellObject { CurrentMap = source, CurrentLocation = new Point(1, 1) };
        source.AddObject(spell);

        source.MoveObject(spell, destination, new Point(2, 2));

        Assert.DoesNotContain(spell, source.Spells);
        Assert.Contains(spell, destination.Spells);
        Assert.True(destination.ContainsObjectAt(spell.CurrentLocation, spell));
    }

    [Fact]
    public async Task OppositeCrossMapMovesUseAStableLockOrder()
    {
        var firstMap = CreateMap();
        var secondMap = CreateMap();
        var firstLocation = new Point(1, 1);
        var secondLocation = new Point(2, 2);
        var firstObject = CreateObject(firstMap, firstLocation);
        var secondObject = CreateObject(secondMap, secondLocation);
        firstMap.AddObjectAt(firstLocation, firstObject);
        secondMap.AddObjectAt(secondLocation, secondObject);

        Task MoveRepeatedly(DecoObject obj, Point location) => Task.Run(() =>
        {
            for (var i = 0; i < 2_000; i++)
            {
                var source = obj.CurrentMap;
                var destination = ReferenceEquals(source, firstMap) ? secondMap : firstMap;
                source.MoveObject(obj, destination, location);
            }
        });

        await Task.WhenAll(MoveRepeatedly(firstObject, firstLocation),
            MoveRepeatedly(secondObject, secondLocation)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, firstMap.GetCellStatistics().Objects + secondMap.GetCellStatistics().Objects);
        Assert.True(firstObject.CurrentMap.ContainsObjectAt(firstObject.CurrentLocation, firstObject));
        Assert.True(secondObject.CurrentMap.ContainsObjectAt(secondObject.CurrentLocation, secondObject));
    }

    [Fact]
    public void ReturnedContainersDoNotRetainMapObjects()
    {
        CellObjectPool.Configure(1);
        var weak = CreateReturnedObjectReference();

        for (var i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weak.IsAlive);
        Assert.Equal(1, CellObjectPool.Statistics.Retained);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateReturnedObjectReference()
    {
        var map = CreateMap();
        var location = new Point(1, 1);
        var first = CreateObject(map, location);
        var second = CreateObject(map, location);
        var weak = new WeakReference(second);
        map.AddObjectAt(location, first);
        map.AddObjectAt(location, second);
        map.RemoveObjectAt(location, second);
        map.RemoveObjectAt(location, first);
        return weak;
    }
}
