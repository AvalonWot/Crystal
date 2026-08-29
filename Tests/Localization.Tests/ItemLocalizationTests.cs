using System.Net;
using System.Reflection;
using System.Text;
using Server;
using Server.Library.Localization;
using Server.MirDatabase;
using Xunit;
using S = ServerPackets;

namespace Localization.Tests;

public sealed class ItemLocalizationTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "crystal-localization-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ParsesDocumentsWithoutLanguageMetadata()
    {
        Assert.True(ItemLocalizationFormat.TryParse(CreateItemDocument("铁剑"), out ItemLocalizationDocument items, out string itemError), itemError);
        Assert.Equal("铁剑", items.Items[123].DisplayName);
        Assert.True(MonsterLocalizationFormat.TryParse(CreateMonsterDocument("沃玛教主"), out MonsterLocalizationDocument monsters, out string monsterError), monsterError);
        Assert.Equal("沃玛教主", monsters.Monsters[321].DisplayName);
    }

    [Fact]
    public void DisplayFieldsDoNotChangeBinarySerialization()
    {
        ItemInfo item = new() { Index = 123, Name = "IronSword", ToolTip = "English tooltip" };
        Assert.Equal(item.FriendlyName, item.DisplayName);
        byte[] itemBefore = Save(item);
        item.DisplayName = "铁剑";
        item.DisplayToolTip = "中文说明";
        Assert.Equal(itemBefore, Save(item));

        ClientMonsterInfo monster = new() { Index = 321, Name = "OmaKing-1", GameName = "OmaKing" };
        Assert.Equal("OmaKing", monster.DisplayName);
        byte[] monsterBefore = Save(monster);
        monster.DisplayName = "沃玛教主";
        Assert.Equal(monsterBefore, Save(monster));
    }

    [Fact]
    public void DecoratesOnlyMatchingMonsterNames()
    {
        MonsterLocalizationEntry entry = new() { SourceName = "OmaKing-1", DisplayName = "沃玛教主" };
        Assert.Equal("沃玛教主", MonsterLocalizationNames.GetObjectDisplayName(entry, "OmaKing", 0));
        Assert.Equal("沃玛教主(Player)", MonsterLocalizationNames.GetObjectDisplayName(entry, "OmaKing(Player)", 0));
        Assert.Equal("Custom", MonsterLocalizationNames.GetObjectDisplayName(entry, "Custom", 64));
        Assert.Equal("Renamed", MonsterLocalizationNames.GetObjectDisplayName(entry, "Renamed", 0));
    }

    [Fact]
    public void RejectsInvalidMonsterEntries()
    {
        byte[] invalidIndex = Encoding.UTF8.GetBytes("""
            { "schemaVersion": 1, "monsters": {
              "0": { "sourceName": "OmaKing", "displayName": "Oma King" }
            } }
            """);
        Assert.False(MonsterLocalizationFormat.TryParse(invalidIndex, out _, out _));

        string longName = new('x', MonsterLocalizationFormat.MaxNameLength + 1);
        byte[] invalidName = Encoding.UTF8.GetBytes($$"""
            { "schemaVersion": 1, "monsters": {
              "1": { "sourceName": "OmaKing", "displayName": "{{longName}}" }
            } }
            """);
        Assert.False(MonsterLocalizationFormat.TryParse(invalidName, out _, out _));
    }

    [Fact]
    public void ObjectMonsterReadsPacketsWithoutOptionalMonsterIndex()
    {
        S.ObjectMonster packet = new() { ObjectID = 1, Name = "OmaKing", MonsterIndex = 321 };
        MethodInfo write = typeof(S.ObjectMonster).GetMethod("WritePacket", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo read = typeof(S.ObjectMonster).GetMethod("ReadPacket", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, true)) write.Invoke(packet, new object[] { writer });
        byte[] current = stream.ToArray();

        S.ObjectMonster oldPacket = new();
        using MemoryStream oldStream = new(current, 0, current.Length - sizeof(int));
        using BinaryReader reader = new(oldStream);
        read.Invoke(oldPacket, new object[] { reader });
        Assert.Equal(0, oldPacket.MonsterIndex);
        Assert.Equal("OmaKing", oldPacket.Name);
    }

    [Fact]
    public async Task UnifiedManagerPublishesBothResourcesAndRetainsLastValidSnapshots()
    {
        string languageDirectory = Path.Combine(_tempDirectory, "Chinese");
        Directory.CreateDirectory(languageDirectory);
        string itemPath = Path.Combine(languageDirectory, "items.json");
        string monsterPath = Path.Combine(languageDirectory, "monsters.json");
        await File.WriteAllBytesAsync(itemPath, CreateItemDocument("铁剑"));
        await File.WriteAllBytesAsync(monsterPath, CreateMonsterDocument("沃玛教主"));

        string oldDirectory = Settings.LocalizationDirectory;
        try
        {
            Settings.LocalizationDirectory = _tempDirectory;
            LocalizationManager.Start();
            LocalizationSnapshot items = await WaitForSnapshot("Chinese", "items.json", null);
            LocalizationSnapshot monsters = await WaitForSnapshot("Chinese", "monsters.json", null);

            Assert.Equal("Chinese", LocalizationManager.ResolveLanguage("chinese"));
            Assert.Equal(string.Empty, LocalizationManager.ResolveLanguage("Unknown"));
            Assert.Equal(string.Empty, LocalizationManager.ResolveLanguage(string.Empty));
            Assert.Equal(HttpStatusCode.OK, LocalizationHttpResolver.Resolve("/localization/Chinese/items.json", string.Empty).StatusCode);
            Assert.Equal(HttpStatusCode.OK, LocalizationHttpResolver.Resolve("/localization/Chinese/monsters.json", string.Empty).StatusCode);
            Assert.Equal(HttpStatusCode.NotModified, LocalizationHttpResolver.Resolve("/localization/Chinese/items.json", items.Hash).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, LocalizationHttpResolver.Resolve("/localization/Unknown/items.json", string.Empty).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, LocalizationHttpResolver.Resolve("/localization/Chinese/items.json", "invalid").StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, LocalizationHttpResolver.Resolve("/localization/Chinese/quests.json", string.Empty).StatusCode);

            await File.WriteAllBytesAsync(itemPath, CreateItemDocument("新铁剑"));
            File.SetLastWriteTimeUtc(itemPath, DateTime.UtcNow.AddSeconds(1));
            LocalizationSnapshot updatedItems = await WaitForSnapshot("Chinese", "items.json", items.Hash);
            IReadOnlyDictionary<int, ItemLocalizationEntry> updatedCatalog =
                Assert.IsAssignableFrom<IReadOnlyDictionary<int, ItemLocalizationEntry>>(updatedItems.Catalog);
            Assert.Equal("新铁剑", updatedCatalog[123].DisplayName);

            await File.WriteAllTextAsync(monsterPath, "{ invalid json");
            File.SetLastWriteTimeUtc(monsterPath, DateTime.UtcNow.AddSeconds(2));
            await Task.Delay(1_300);
            Assert.True(LocalizationManager.TryGetSnapshot("Chinese", "monsters.json", out LocalizationSnapshot retained));
            Assert.Equal(monsters.Hash, retained.Hash);
        }
        finally
        {
            LocalizationManager.Stop();
            Settings.LocalizationDirectory = oldDirectory;
        }
    }

    [Fact]
    public void UnknownLanguageUsesDatabaseNames()
    {
        ItemInfo item = new() { Index = 123, Name = "IronSword" };
        MonsterInfo monster = new() { Index = 321, Name = "OmaKing-1" };
        Assert.Equal(item.FriendlyName, LocalizationManager.GetItemDisplayName("Unknown", item));
        Assert.Equal("OmaKing", LocalizationManager.GetMonsterDisplayName("Unknown", monster));
    }

    private static async Task<LocalizationSnapshot> WaitForSnapshot(string language, string resourceName, string previousHash)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (LocalizationManager.TryGetSnapshot(language, resourceName, out LocalizationSnapshot snapshot) &&
                (previousHash == null || !snapshot.Hash.Equals(previousHash, StringComparison.Ordinal))) return snapshot;
            await Task.Delay(100);
        }
        throw new TimeoutException($"The localization snapshot was not published for {language}/{resourceName}.");
    }

    private static byte[] CreateItemDocument(string displayName)
    {
        string json = $$"""
        { "schemaVersion": 1, "items": {
          "123": { "sourceName": "IronSword", "displayName": "{{displayName}}", "tooltip": "" }
        } }
        """;
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] CreateMonsterDocument(string displayName)
    {
        string json = $$"""
        { "schemaVersion": 1, "monsters": {
          "321": { "sourceName": "OmaKing-1", "displayName": "{{displayName}}" }
        } }
        """;
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] Save(ItemInfo item)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        item.Save(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Save(ClientMonsterInfo info)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        info.Save(writer);
        writer.Flush();
        return stream.ToArray();
    }

    public void Dispose()
    {
        LocalizationManager.Stop();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
    }
}
