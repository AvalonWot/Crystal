using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Xunit;

namespace Localization.Tests;

public sealed class MonsterDropSearchIndexTests
{
    [Fact]
    public void BuildIndexesOnlyDirectItemDropsAndDeduplicatesPerMonster()
    {
        ItemInfo directItem = new() { Index = 10, Name = "DirectItem" };
        ItemInfo groupedItem = new() { Index = 20, Name = "GroupedItem" };

        MonsterInfo firstMonster = new() { Index = 1, Name = "FirstMonster" };
        firstMonster.Drops.Add(new DropInfo { Item = directItem });
        firstMonster.Drops.Add(new DropInfo { Item = directItem });
        firstMonster.Drops.Add(new DropInfo { Gold = 100 });
        firstMonster.Drops.Add(new DropInfo
        {
            GroupedDrop = new GroupDropInfo
            {
                new DropInfo { Item = groupedItem }
            }
        });

        MonsterInfo secondMonster = new() { Index = 2, Name = "SecondMonster" };
        secondMonster.Drops.Add(new DropInfo { Item = directItem });

        MonsterDropSearchIndex index = MonsterDropSearchIndex.Build(new[] { firstMonster, secondMonster });

        Assert.Equal(new[] { firstMonster, secondMonster }, index.FindMonsters(directItem.Index));
        Assert.Empty(index.FindMonsters(groupedItem.Index));
        Assert.Empty(index.FindMonsters(999));
    }

    [Fact]
    public void RebuildingProducesAReplacementWithoutOldRelationships()
    {
        ItemInfo oldItem = new() { Index = 10, Name = "OldItem" };
        ItemInfo newItem = new() { Index = 20, Name = "NewItem" };
        MonsterInfo monster = new() { Index = 1, Name = "Monster" };
        monster.Drops.Add(new DropInfo { Item = oldItem });

        MonsterDropSearchIndex oldIndex = MonsterDropSearchIndex.Build(new[] { monster });

        monster.Drops.Clear();
        monster.Drops.Add(new DropInfo { Item = newItem });
        MonsterDropSearchIndex newIndex = MonsterDropSearchIndex.Build(new[] { monster });

        Assert.Single(oldIndex.FindMonsters(oldItem.Index));
        Assert.Empty(newIndex.FindMonsters(oldItem.Index));
        Assert.Single(newIndex.FindMonsters(newItem.Index));
    }

    [Fact]
    public void DropFilePathUsesOverrideOrMonsterName()
    {
        MonsterInfo defaultPathMonster = new() { Name = "Oma" };
        MonsterInfo overridePathMonster = new() { Name = "Oma", DropPath = Path.Combine("Bosses", "OmaDrops") };

        Assert.Equal(Path.Combine(Settings.DropPath, "Oma.txt"), Envir.GetMonsterDropFilePath(defaultPathMonster));
        Assert.Equal(Path.Combine(Settings.DropPath, "Bosses", "OmaDrops.txt"), Envir.GetMonsterDropFilePath(overridePathMonster));
    }
}
