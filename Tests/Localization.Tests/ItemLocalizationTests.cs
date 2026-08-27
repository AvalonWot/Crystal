using System.Text;
using System.Net;
using Server;
using Server.Library.Localization;
using Xunit;

namespace Localization.Tests;

public sealed class ItemLocalizationTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "crystal-localization-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ParsesValidDocumentAndNormalizesLegacyCultures()
    {
        byte[] content = CreateDocument("zh-CN", "铁剑");
        Assert.True(ItemLocalizationFormat.TryParse(content, "Chinese", out ItemLocalizationDocument document, out string error), error);
        Assert.Equal("zh-CN", document.Culture);
        Assert.Equal("铁剑", document.Items[123].DisplayName);
        Assert.Equal("en-US", ItemLocalizationFormat.NormalizeCulture("English"));
    }

    [Fact]
    public void DisplayFieldsDoNotChangeItemBinarySerialization()
    {
        ItemInfo item = new() { Index = 123, Name = "IronSword", ToolTip = "English tooltip" };
        byte[] before = Save(item);
        item.DisplayName = "铁剑";
        item.DisplayToolTip = "中文说明";
        byte[] after = Save(item);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task MonitorPublishesValidChangesAndRetainsLastValidSnapshot()
    {
        string cultureDirectory = Path.Combine(_tempDirectory, "fr-FR");
        Directory.CreateDirectory(cultureDirectory);
        string path = Path.Combine(cultureDirectory, "items.json");
        await File.WriteAllBytesAsync(path, CreateDocument("fr-FR", "Épée de fer"));

        string oldDirectory = Settings.LocalizationDirectory;
        string oldDefaultCulture = Settings.LocalizationDefaultCulture;
        try
        {
            Settings.LocalizationDirectory = _tempDirectory;
            Settings.LocalizationDefaultCulture = "fr-FR";
            ItemLocalizationManager.Start();

            ItemLocalizationSnapshot first = await WaitForSnapshot("fr-FR", null);
            Assert.Equal(HttpStatusCode.OK,
                ItemLocalizationHttpResolver.Resolve("/localization/fr-FR/items.json", string.Empty).StatusCode);
            Assert.Equal(HttpStatusCode.NotModified,
                ItemLocalizationHttpResolver.Resolve("/localization/fr-FR/items.json", first.Hash).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                ItemLocalizationHttpResolver.Resolve("/localization/fr-FR/items.json", "invalid").StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                ItemLocalizationHttpResolver.Resolve("/localization/de-DE/items.json", string.Empty).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                ItemLocalizationHttpResolver.Resolve("/localization/fr-FR/quests.json", string.Empty).StatusCode);

            await File.WriteAllBytesAsync(path, CreateDocument("fr-FR", "Épée nouvelle"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
            ItemLocalizationSnapshot second = await WaitForSnapshot("fr-FR", first.Hash);
            Assert.NotEqual(first.Hash, second.Hash);
            Assert.Equal("Épée nouvelle", second.Items[123].DisplayName);
            ItemLocalizationHttpResult updated =
                ItemLocalizationHttpResolver.Resolve("/localization/fr-FR/items.json", first.Hash);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(second.Hash, updated.Snapshot.Hash);

            await File.WriteAllTextAsync(path, "{ invalid json");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
            await Task.Delay(1_300);
            Assert.True(ItemLocalizationManager.TryGetSnapshot("fr-FR", out ItemLocalizationSnapshot retained));
            Assert.Equal(second.Hash, retained.Hash);
        }
        finally
        {
            ItemLocalizationManager.Stop();
            Settings.LocalizationDirectory = oldDirectory;
            Settings.LocalizationDefaultCulture = oldDefaultCulture;
        }
    }

    private static async Task<ItemLocalizationSnapshot> WaitForSnapshot(string culture, string previousHash)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (ItemLocalizationManager.TryGetSnapshot(culture, out ItemLocalizationSnapshot snapshot) &&
                (previousHash == null || !snapshot.Hash.Equals(previousHash, StringComparison.Ordinal)))
                return snapshot;
            await Task.Delay(100);
        }
        throw new TimeoutException("The localization snapshot was not published.");
    }

    private static byte[] CreateDocument(string culture, string displayName)
    {
        string json = $$"""
        { "schemaVersion": 1, "culture": "{{culture}}", "items": {
          "123": { "sourceName": "IronSword", "displayName": "{{displayName}}", "tooltip": "" }
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

    public void Dispose()
    {
        ItemLocalizationManager.Stop();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
    }
}
