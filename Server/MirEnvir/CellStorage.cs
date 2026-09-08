using System.Collections;
using System.Drawing;
using System.Runtime.CompilerServices;
using Server.MirObjects;

namespace Server.MirEnvir;

public struct CellTerrain
{
    public CellAttribute Attribute;
    public sbyte FishingAttribute;
    public readonly bool Valid => Attribute == CellAttribute.Walk;
}

internal sealed class CellObjectBuffer
{
    internal readonly MapObject[] Items;
    internal int Count;
    internal CellObjectBuffer(int capacity) => Items = new MapObject[capacity];
}

public static class CellObjectPool
{
    private static readonly object Sync = new();
    private static readonly Stack<CellObjectBuffer>[] Buckets = { new(), new(), new() };
    private static int retained, limit = 4096;
    private static long rents, hits;
    public static (int Retained, int Limit, long Rents, long Hits) Statistics
    { get { lock (Sync) return (retained, limit, rents, hits); } }

    public static void Configure(int maximum)
    {
        lock (Sync)
        {
            limit = Math.Max(0, maximum);
            foreach (var bucket in Buckets)
                while (retained > limit && bucket.Count > 0) { bucket.Pop(); retained--; }
        }
    }

    internal static CellObjectBuffer Rent(int count)
    {
        int capacity = 4;
        while (capacity < count) capacity = checked(capacity * 2);
        lock (Sync)
        {
            rents++;
            if (capacity <= 16)
            {
                int bin = capacity == 4 ? 0 : capacity == 8 ? 1 : 2;
                if (Buckets[bin].TryPop(out var buffer)) { retained--; hits++; return buffer; }
            }
        }
        return new CellObjectBuffer(capacity);
    }

    internal static void Return(CellObjectBuffer buffer)
    {
        Array.Clear(buffer.Items);
        buffer.Count = 0;
        if (buffer.Items.Length > 16) return;
        lock (Sync)
        {
            if (retained >= limit) return;
            Buckets[buffer.Items.Length == 4 ? 0 : buffer.Items.Length == 8 ? 1 : 2].Push(buffer);
            retained++;
        }
    }
}

// A lease is not pooled: stale copies can never dispose a later renter's buffer.
internal sealed class CellSnapshotLease : IDisposable
{
    internal CellObjectBuffer Buffer;
    internal CellSnapshotLease(CellObjectBuffer buffer) => Buffer = buffer;
    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref Buffer, null);
        if (buffer != null) CellObjectPool.Return(buffer);
    }
}

public readonly struct CellSnapshot : IReadOnlyList<MapObject>, IDisposable
{
    private readonly CellSnapshotLease lease;
    private readonly MapObject single;
    private readonly int count;
    private readonly Map map;
    private readonly Point location;
    private readonly CellTerrain terrain;
    internal CellSnapshot(Map map, Point location, CellTerrain terrain, MapObject single, CellObjectBuffer buffer, int count)
    {
        this.map = map; this.location = location; this.terrain = terrain;
        this.single = single; this.count = count;
        lease = buffer == null ? null : new CellSnapshotLease(buffer);
    }
    public bool Valid => terrain.Valid;
    public CellAttribute Attribute => terrain.Attribute;
    public sbyte FishingAttribute => terrain.FishingAttribute;
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { CheckLease(); return count; }
    }
    public MapObject this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            CheckLease();
            if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
            return lease == null ? single : lease.Buffer.Items[index];
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsCurrent(MapObject obj) => obj != null && ReferenceEquals(obj.CurrentMap, map) &&
        obj.CurrentLocation.X == location.X && obj.CurrentLocation.Y == location.Y;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckLease()
    {
        if (lease != null && lease.Buffer == null) throw new ObjectDisposedException(nameof(CellSnapshot));
    }
    public void Dispose() => lease?.Dispose();
    public IEnumerator<MapObject> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public partial class Map
{
    private CellTerrain[] terrain;
    private object[] cellObjects;
    private const int CellLockCount = 64;
    private readonly object[] cellSyncs = CreateCellLocks();
    private readonly object mapListSync = new();
    private static long nextLockId;
    private readonly long cellLockId = Interlocked.Increment(ref nextLockId);
    private int occupiedCellCount;
    private int maximumCellObjects;
    private long overflowEvents;

    private static object[] CreateCellLocks()
    {
        var locks = new object[CellLockCount];
        for (var i = 0; i < locks.Length; i++) locks[i] = new object();
        return locks;
    }

    private CellLockScope LockCell(int index) => new(cellSyncs[index & (CellLockCount - 1)]);
    private CellPairLockScope LockCellPair(int firstIndex, int secondIndex) =>
        new(cellSyncs, firstIndex & (CellLockCount - 1), secondIndex & (CellLockCount - 1));
    private AllCellLocksScope LockAllCells() => new(cellSyncs);

    private ref struct CellLockScope
    {
        private readonly object gate;
        internal CellLockScope(object gate) { this.gate = gate; Monitor.Enter(gate); }
        public void Dispose() => Monitor.Exit(gate);
    }

    private ref struct CellPairLockScope
    {
        private readonly object first, second;
        internal CellPairLockScope(object[] gates, int one, int two)
        {
            if (one == two) { first = gates[one]; second = null; }
            else if (one < two) { first = gates[one]; second = gates[two]; }
            else { first = gates[two]; second = gates[one]; }
            Monitor.Enter(first);
            if (second != null) Monitor.Enter(second);
        }
        public void Dispose()
        {
            if (second != null) Monitor.Exit(second);
            Monitor.Exit(first);
        }
    }

    private ref struct AllCellLocksScope
    {
        private readonly object[] gates;
        internal AllCellLocksScope(object[] gates)
        {
            this.gates = gates;
            for (var i = 0; i < gates.Length; i++) Monitor.Enter(gates[i]);
        }
        public void Dispose()
        {
            for (var i = gates.Length - 1; i >= 0; i--) Monitor.Exit(gates[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CellIndex(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(x));
        return y * Width + x;
    }

    public void InitializeTerrain(int width, int height)
    {
        ClearCellObjects();
        Width = width;
        Height = height;
        terrain = new CellTerrain[checked(width * height)];
        cellObjects = new object[terrain.Length];
        Array.Fill(terrain, new CellTerrain { Attribute = CellAttribute.Walk, FishingAttribute = -1 });
    }

    public bool HasTerrain => terrain != null;
    public CellTerrain GetTerrain(Point p) => GetTerrain(p.X, p.Y);
    public CellTerrain GetTerrain(int x, int y) => terrain[CellIndex(x, y)];
    private void SetTerrainAttribute(int x, int y, CellAttribute attribute) => terrain[CellIndex(x, y)].Attribute = attribute;
    private void SetFishingAttribute(int x, int y, sbyte value) => terrain[CellIndex(x, y)].FishingAttribute = value;

    public ulong GetTerrainChecksum()
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        for (var i = 0; i < terrain.Length; i++)
        {
            hash = (hash ^ (byte)terrain[i].Attribute) * prime;
            hash = (hash ^ (byte)terrain[i].FishingAttribute) * prime;
        }
        return hash;
    }

    public Point[] GetOccupiedCellPositions()
    {
        using var cellLock = LockAllCells();
        if (occupiedCellCount == 0) return Array.Empty<Point>();
        var result = new List<Point>(occupiedCellCount);
        for (var index = 0; index < cellObjects.Length; index++)
            if (cellObjects[index] != null) result.Add(new Point(index % Width, index / Width));
        return result.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetObjectCount(Point p)
    {
        var slot = Volatile.Read(ref cellObjects[CellIndex(p.X, p.Y)]);
        return slot switch { null => 0, CellObjectBuffer buffer => buffer.Count, _ => 1 };
    }

    public bool ContainsObjectAt(Point p, MapObject obj)
    {
        var index = CellIndex(p.X, p.Y);
        var slot = Volatile.Read(ref cellObjects[index]);
        if (slot == null) return false;
        if (slot is MapObject single) return ReferenceEquals(single, obj);
        using var cellLock = LockCell(index);
        slot = cellObjects[index];
        if (slot is MapObject currentSingle) return ReferenceEquals(currentSingle, obj);
        if (slot is not CellObjectBuffer buffer) return false;
        for (var i = 0; i < buffer.Count; i++)
            if (ReferenceEquals(buffer.Items[i], obj)) return true;
        return false;
    }

    public (int Occupied, int Capacity, int Objects, int Maximum, long Overflows) GetCellStatistics()
    {
        using var cellLock = LockAllCells();
        var objects = 0;
        for (var index = 0; index < cellObjects.Length; index++)
            objects += cellObjects[index] switch { null => 0, CellObjectBuffer buffer => buffer.Count, _ => 1 };
        return (occupiedCellCount, occupiedCellCount, objects, maximumCellObjects, overflowEvents);
    }

    public CellSnapshot RentObjectsSnapshot(Point p) => RentObjectsSnapshot(p.X, p.Y);
    public CellSnapshot RentObjectsSnapshot(int x, int y)
    {
        var index = CellIndex(x, y);
        var location = new Point(x, y);
        var slot = Volatile.Read(ref cellObjects[index]);
        if (slot == null) return new CellSnapshot(this, location, terrain[index], null, null, 0);
        if (slot is MapObject single) return new CellSnapshot(this, location, terrain[index], single, null, 1);

        using var cellLock = LockCell(index);
        slot = cellObjects[index];
        if (slot == null) return new CellSnapshot(this, location, terrain[index], null, null, 0);
        if (slot is MapObject currentSingle) return new CellSnapshot(this, location, terrain[index], currentSingle, null, 1);
        var source = (CellObjectBuffer)slot;
        var snapshot = CellObjectPool.Rent(source.Count);
        Array.Copy(source.Items, snapshot.Items, source.Count);
        snapshot.Count = source.Count;
        return new CellSnapshot(this, location, terrain[index], null, snapshot, snapshot.Count);
    }

    public void AddObjectAt(Point p, MapObject obj)
    {
        bool added, overflow;
        var index = CellIndex(p.X, p.Y);
        using (LockCell(index)) added = AddCellCore(index, obj, out overflow);
        if (!added) ReportCellIssue("Null or duplicate object added to map cell.");
        if (overflow) MessageQueue.Enqueue($"Map {Info?.Index}: cell exceeded 16 objects; using unpooled capacity.");
    }

    private bool AddCellCore(int index, MapObject obj, out bool firstOverflow)
    {
        firstOverflow = false;
        if (obj == null) return false;
        var slot = cellObjects[index];
        if (slot == null)
        {
            cellObjects[index] = obj;
            Interlocked.Increment(ref occupiedCellCount);
            RecordMaximum(1);
            return true;
        }
        if (slot is MapObject single)
        {
            if (ReferenceEquals(single, obj)) return false;
            var created = CellObjectPool.Rent(2);
            created.Items[0] = single;
            created.Items[1] = obj;
            created.Count = 2;
            cellObjects[index] = created;
            RecordMaximum(2);
            return true;
        }

        var buffer = (CellObjectBuffer)slot;
        for (var i = 0; i < buffer.Count; i++)
            if (ReferenceEquals(buffer.Items[i], obj)) return false;
        if (buffer.Count == buffer.Items.Length)
        {
            var expanded = CellObjectPool.Rent(buffer.Count + 1);
            Array.Copy(buffer.Items, expanded.Items, buffer.Count);
            expanded.Count = buffer.Count;
            CellObjectPool.Return(buffer);
            buffer = expanded;
            cellObjects[index] = buffer;
        }
        buffer.Items[buffer.Count++] = obj;
        RecordMaximum(buffer.Count);
        if (buffer.Count > 16) firstOverflow = Interlocked.Increment(ref overflowEvents) == 1;
        return true;
    }

    public void RemoveObjectAt(Point p, MapObject obj)
    {
        bool removed;
        var index = CellIndex(p.X, p.Y);
        using (LockCell(index)) removed = RemoveCellCore(index, obj);
        if (!removed) ReportCellIssue("Null or missing object removed from map cell.");
    }

    private bool RemoveCellCore(int index, MapObject obj)
    {
        if (obj == null) return false;
        var slot = cellObjects[index];
        if (slot == null) return false;
        if (slot is MapObject single)
        {
            if (!ReferenceEquals(single, obj)) return false;
            cellObjects[index] = null;
            Interlocked.Decrement(ref occupiedCellCount);
            return true;
        }

        var buffer = (CellObjectBuffer)slot;
        var found = -1;
        for (var i = 0; i < buffer.Count; i++)
            if (ReferenceEquals(buffer.Items[i], obj)) { found = i; break; }
        if (found < 0) return false;
        Array.Copy(buffer.Items, found + 1, buffer.Items, found, buffer.Count - found - 1);
        buffer.Items[--buffer.Count] = null;
        if (buffer.Count == 1)
        {
            cellObjects[index] = buffer.Items[0];
            CellObjectPool.Return(buffer);
        }
        else if (buffer.Count <= buffer.Items.Length / 4)
        {
            var smaller = CellObjectPool.Rent(buffer.Count);
            Array.Copy(buffer.Items, smaller.Items, buffer.Count);
            smaller.Count = buffer.Count;
            cellObjects[index] = smaller;
            CellObjectPool.Return(buffer);
        }
        return true;
    }

    public void MoveObject(MapObject obj, Point destination) => MoveObject(obj, this, destination);
    public void MoveObject(MapObject obj, Map destinationMap, Point destination)
    {
        if (ReferenceEquals(this, destinationMap))
        {
            bool removed = false, added = false, overflow = false;
            var from = CellIndex(obj.CurrentLocation.X, obj.CurrentLocation.Y);
            var to = CellIndex(destination.X, destination.Y);
            using (LockCellPair(from, to))
            {
                if (from == to) return;
                removed = RemoveCellCore(from, obj);
                if (removed)
                {
                    added = AddCellCore(to, obj, out overflow);
                    if (!added) AddCellCore(from, obj, out _);
                    else obj.CurrentLocation = destination;
                }
            }
            if (!removed || !added) ReportCellIssue("Map cell move failed; membership was not changed.");
            if (overflow) MessageQueue.Enqueue($"Map {Info?.Index}: cell exceeded 16 objects.");
            return;
        }

        var fromIndex = CellIndex(obj.CurrentLocation.X, obj.CurrentLocation.Y);
        var toIndex = destinationMap.CellIndex(destination.X, destination.Y);
        var first = cellLockId <= destinationMap.cellLockId ? this : destinationMap;
        var second = ReferenceEquals(first, this) ? destinationMap : this;
        var firstIndex = ReferenceEquals(first, this) ? fromIndex : toIndex;
        var secondIndex = ReferenceEquals(first, this) ? toIndex : fromIndex;
        bool crossRemoved = false, crossAdded = false, crossOverflow = false;
        using (first.LockCell(firstIndex)) using (second.LockCell(secondIndex))
        {
            crossRemoved = RemoveCellCore(fromIndex, obj);
            if (crossRemoved)
            {
                crossAdded = destinationMap.AddCellCore(toIndex, obj, out crossOverflow);
                if (!crossAdded) AddCellCore(fromIndex, obj, out _);
                else
                {
                    lock (first.mapListSync) lock (second.mapListSync)
                    {
                        RemoveObjectFromMapLists(obj);
                        destinationMap.AddObjectToMapLists(obj);
                    }
                    obj.CurrentMap = destinationMap;
                    obj.CurrentLocation = destination;
                }
            }
        }
        if (!crossRemoved || !crossAdded) ReportCellIssue("Map cell move failed; membership was not changed.");
        if (crossOverflow) MessageQueue.Enqueue($"Map {destinationMap.Info?.Index}: cell exceeded 16 objects.");
    }

    public void ClearCellObjects()
    {
        using var cellLock = LockAllCells();
        if (cellObjects != null)
        {
            for (var index = 0; index < cellObjects.Length; index++)
                if (cellObjects[index] is CellObjectBuffer buffer) CellObjectPool.Return(buffer);
            Array.Clear(cellObjects);
        }
        occupiedCellCount = 0;
    }

    private void RecordMaximum(int count)
    {
        var current = Volatile.Read(ref maximumCellObjects);
        while (count > current)
        {
            var observed = Interlocked.CompareExchange(ref maximumCellObjects, count, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private static void ReportCellIssue(string message) => MessageQueue.Enqueue(new InvalidOperationException(message));
}
