using Server.MirDatabase;

namespace Server.MirEnvir;

public sealed class MonsterDropSearchIndex
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<MonsterInfo>> _monstersByItemIndex;

    private MonsterDropSearchIndex(IReadOnlyDictionary<int, IReadOnlyList<MonsterInfo>> monstersByItemIndex)
    {
        _monstersByItemIndex = monstersByItemIndex;
    }

    public static MonsterDropSearchIndex Build(IEnumerable<MonsterInfo> monsters)
    {
        Dictionary<int, List<MonsterInfo>> mutableIndex = new();

        foreach (MonsterInfo monster in monsters ?? Array.Empty<MonsterInfo>())
        {
            if (monster == null) continue;

            HashSet<int> seenItemIndexes = new();
            foreach (DropInfo drop in monster.Drops)
            {
                if (drop?.Item == null || !seenItemIndexes.Add(drop.Item.Index)) continue;

                if (!mutableIndex.TryGetValue(drop.Item.Index, out List<MonsterInfo> itemMonsters))
                {
                    itemMonsters = new List<MonsterInfo>();
                    mutableIndex.Add(drop.Item.Index, itemMonsters);
                }

                itemMonsters.Add(monster);
            }
        }

        Dictionary<int, IReadOnlyList<MonsterInfo>> index = mutableIndex.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<MonsterInfo>)pair.Value.ToArray());

        return new MonsterDropSearchIndex(index);
    }

    public IReadOnlyList<MonsterInfo> FindMonsters(int itemIndex)
    {
        return _monstersByItemIndex.TryGetValue(itemIndex, out IReadOnlyList<MonsterInfo> monsters)
            ? monsters
            : Array.Empty<MonsterInfo>();
    }
}
