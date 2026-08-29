using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class MonsterLocalizationDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<int, MonsterLocalizationEntry> Monsters { get; set; } = new();
}

public sealed class MonsterLocalizationEntry
{
    public string SourceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public static class MonsterLocalizationFormat
{
    public const int SchemaVersion = 1;
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxMonsters = 100_000;
    public const int MaxNameLength = 256;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32
    };

    public static bool TryParse(byte[] bytes, out MonsterLocalizationDocument document, out string error)
    {
        document = null;
        error = string.Empty;

        if (bytes == null || bytes.Length == 0)
        {
            error = "The localization file is empty.";
            return false;
        }

        if (bytes.Length > MaxFileBytes)
        {
            error = $"The localization file exceeds {MaxFileBytes} bytes.";
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<MonsterLocalizationDocument>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }

        if (document == null)
        {
            error = "The localization document is null.";
            return false;
        }

        if (document.SchemaVersion != SchemaVersion)
        {
            error = $"Unsupported schema version {document.SchemaVersion}.";
            return false;
        }

        document.Monsters ??= new Dictionary<int, MonsterLocalizationEntry>();
        if (document.Monsters.Count > MaxMonsters)
        {
            error = $"The localization document contains more than {MaxMonsters} monsters.";
            return false;
        }

        foreach ((int index, MonsterLocalizationEntry entry) in document.Monsters)
        {
            if (index <= 0 || entry == null)
            {
                error = $"Invalid monster entry at index {index}.";
                return false;
            }

            entry.SourceName ??= string.Empty;
            entry.DisplayName ??= string.Empty;
            if (entry.SourceName.Length > MaxNameLength || entry.DisplayName.Length > MaxNameLength)
            {
                error = $"Monster {index} contains a name longer than {MaxNameLength} characters.";
                return false;
            }
        }

        return true;
    }
}

public static class MonsterLocalizationNames
{
    public static string GetGameName(string sourceName)
    {
        return Regex.Replace(sourceName ?? string.Empty, @"[\d-]", string.Empty);
    }

    public static string GetObjectDisplayName(MonsterLocalizationEntry entry, string objectName, byte ai)
    {
        objectName ??= string.Empty;
        if (entry == null || ai == 64 || string.IsNullOrWhiteSpace(entry.DisplayName)) return objectName;

        string sourceGameName = GetGameName(entry.SourceName);
        if (sourceGameName.Length == 0) return objectName;
        if (objectName.Equals(sourceGameName, StringComparison.Ordinal)) return entry.DisplayName;
        if (objectName.StartsWith(sourceGameName + "(", StringComparison.Ordinal))
            return entry.DisplayName + objectName[sourceGameName.Length..];
        return objectName;
    }
}
