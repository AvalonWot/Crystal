using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Library.Localization;

public sealed class LocalizationSnapshot
{
    public string Language { get; init; }
    public string ResourceName { get; init; }
    public string Hash { get; init; }
    public byte[] Content { get; init; }
    public object Catalog { get; init; }
    public long FileLength { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
}

public static class LocalizationManager
{
    public const string ItemsResourceName = "items.json";
    public const string MonstersResourceName = "monsters.json";

    private delegate bool ResourceParser(byte[] content, out object catalog, out string error);
    private delegate void ResourceValidator(string path, object catalog);

    private sealed record ResourceDefinition(
        string ResourceName,
        ResourceParser Parser,
        ResourceValidator Validator);

    private static readonly IReadOnlyDictionary<string, ResourceDefinition> Resources =
        new Dictionary<string, ResourceDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [ItemsResourceName] = new(ItemsResourceName, ParseItems, ValidateItemSourceNames),
            [MonstersResourceName] = new(MonstersResourceName, ParseMonsters, ValidateMonsterSourceNames)
        };
    private static readonly ConcurrentDictionary<string, LocalizationSnapshot> Snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> LastErrors =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, TextMap> ServerTextMaps =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (long Length, DateTime LastWrite)> ServerTextFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> _languages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static CancellationTokenSource _cancellation;
    private static Task _monitorTask;

    public static void Start()
    {
        Stop();
        _cancellation = new CancellationTokenSource();
        Scan();
        _monitorTask = Task.Run(() => MonitorAsync(_cancellation.Token));
    }

    public static void Stop()
    {
        CancellationTokenSource cancellation = Interlocked.Exchange(ref _cancellation, null);
        if (cancellation == null) return;
        cancellation.Cancel();
        try { _monitorTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        finally
        {
            cancellation.Dispose();
            _monitorTask = null;
        }
    }

    public static string ResolveLanguage(string requestedLanguage)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguage)) return string.Empty;
        IReadOnlyDictionary<string, string> languages = Volatile.Read(ref _languages);
        return languages.TryGetValue(requestedLanguage.Trim(), out string language) ? language : string.Empty;
    }

    public static bool IsKnownResource(string resourceName)
    {
        return resourceName != null && Resources.ContainsKey(resourceName);
    }

    public static bool TryGetSnapshot(string language, string resourceName, out LocalizationSnapshot snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(language) || !IsKnownResource(resourceName)) return false;
        return Snapshots.TryGetValue(GetSnapshotKey(language.Trim(), resourceName), out snapshot);
    }

    public static string GetItemDisplayName(string language, ItemInfo info)
    {
        if (info == null) return string.Empty;
        if (TryGetSnapshot(language, ItemsResourceName, out LocalizationSnapshot snapshot) &&
            snapshot.Catalog is IReadOnlyDictionary<int, ItemLocalizationEntry> items &&
            items.TryGetValue(info.Index, out ItemLocalizationEntry entry) &&
            entry.SourceName.Equals(info.Name, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.DisplayName))
            return entry.DisplayName;
        return info.FriendlyName;
    }

    public static string GetMonsterDisplayName(string language, MonsterInfo info)
    {
        if (info == null) return string.Empty;
        if (TryGetSnapshot(language, MonstersResourceName, out LocalizationSnapshot snapshot) &&
            snapshot.Catalog is IReadOnlyDictionary<int, MonsterLocalizationEntry> monsters &&
            monsters.TryGetValue(info.Index, out MonsterLocalizationEntry entry) &&
            entry.SourceName.Equals(info.Name, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.DisplayName))
            return entry.DisplayName;
        return info.GameName;
    }

    public static string GetServerText(string language, ServerTextKeys key, params object[] arguments)
    {
        if (!string.IsNullOrWhiteSpace(language) &&
            ServerTextMaps.TryGetValue(language.Trim(), out TextMap map) &&
            map.Text != null && map.Text.TryGetValue(key.ToString(), out string value))
            return arguments == null || arguments.Length == 0 ? value : string.Format(value, arguments);
        return GameLanguage.ServerTextMap.GetLocalization(key, arguments ?? Array.Empty<object>());
    }

    private static async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        try { while (await timer.WaitForNextTickAsync(cancellationToken)) Scan(); }
        catch (OperationCanceledException) { }
    }

    private static void Scan()
    {
        string root;
        try
        {
            root = Path.GetFullPath(Settings.LocalizationDirectory);
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            ReportError(Settings.LocalizationDirectory, ex.Message);
            return;
        }

        LoadServerTextMaps(root);
        string[] directories;
        try { directories = Directory.GetDirectories(root); }
        catch (Exception ex)
        {
            ReportError(root, ex.Message);
            return;
        }

        Dictionary<string, string> languages = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
        {
            string language = Path.GetFileName(directory)?.Trim() ?? string.Empty;
            if (language.Length == 0 || !Resources.Values.Any(resource =>
                    File.Exists(Path.Combine(directory, resource.ResourceName)))) continue;
            languages[language] = language;
            foreach (ResourceDefinition resource in Resources.Values) LoadResource(directory, language, resource);
        }
        Volatile.Write(ref _languages, languages);
        foreach ((string key, LocalizationSnapshot snapshot) in Snapshots)
        {
            if (!languages.ContainsKey(snapshot.Language) ||
                !File.Exists(Path.Combine(root, snapshot.Language, snapshot.ResourceName)))
                Snapshots.TryRemove(key, out _);
        }
    }

    private static void LoadResource(string directory, string language, ResourceDefinition resource)
    {
        string resourceName = resource.ResourceName;
        string path = Path.Combine(directory, resourceName);
        if (!File.Exists(path)) return;
        string key = GetSnapshotKey(language, resourceName);
        try
        {
            FileInfo before = new(path);
            if (Snapshots.TryGetValue(key, out LocalizationSnapshot current) &&
                current.FileLength == before.Length && current.LastWriteTimeUtc == before.LastWriteTimeUtc) return;

            byte[] content = File.ReadAllBytes(path);
            FileInfo after = new(path);
            if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc) return;

            if (!resource.Parser(content, out object catalog, out string error))
            {
                ReportError(path, error);
                return;
            }
            resource.Validator(path, catalog);

            LocalizationSnapshot snapshot = new()
            {
                Language = language,
                ResourceName = resourceName,
                Hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                Content = content,
                Catalog = catalog,
                FileLength = after.Length,
                LastWriteTimeUtc = after.LastWriteTimeUtc
            };
            Snapshots[key] = snapshot;
            LastErrors.TryRemove(path, out _);
            int count = catalog switch
            {
                IReadOnlyDictionary<int, ItemLocalizationEntry> items => items.Count,
                IReadOnlyDictionary<int, MonsterLocalizationEntry> monsters => monsters.Count,
                _ => 0
            };
            MessageQueue.Instance.Enqueue($"Loaded localization '{language}/{resourceName}' ({count} entries, {snapshot.Hash}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError(path, ex.Message);
        }
    }

    private static void LoadServerTextMaps(string root)
    {
        foreach (string path in Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            string language = Path.GetFileNameWithoutExtension(path);
            try
            {
                FileInfo info = new(path);
                if (ServerTextFiles.TryGetValue(language, out var current) &&
                    current.Length == info.Length && current.LastWrite == info.LastWriteTimeUtc) continue;
                TextMap map = JsonSerializer.Deserialize<TextMap>(File.ReadAllBytes(path), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    MaxDepth = 32
                });
                if (map?.Text == null) throw new InvalidDataException("Server text map has no Text dictionary.");
                ServerTextMaps[language] = map;
                ServerTextFiles[language] = (info.Length, info.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                ReportError(path, ex.Message);
            }
        }
    }

    private static bool ParseItems(byte[] content, out object catalog, out string error)
    {
        if (ItemLocalizationFormat.TryParse(content, out ItemLocalizationDocument document, out error))
        {
            catalog = new Dictionary<int, ItemLocalizationEntry>(document.Items);
            return true;
        }
        catalog = null;
        return false;
    }

    private static bool ParseMonsters(byte[] content, out object catalog, out string error)
    {
        if (MonsterLocalizationFormat.TryParse(content, out MonsterLocalizationDocument document, out error))
        {
            catalog = new Dictionary<int, MonsterLocalizationEntry>(document.Monsters);
            return true;
        }
        catalog = null;
        return false;
    }

    private static void ValidateItemSourceNames(string path, object catalog)
    {
        if (Envir.Main == null) return;
        Dictionary<int, ItemInfo> items = Envir.Main.ItemInfoList.ToDictionary(x => x.Index);
        foreach ((int index, ItemLocalizationEntry entry) in (IReadOnlyDictionary<int, ItemLocalizationEntry>)catalog)
            if (!items.TryGetValue(index, out ItemInfo info) || !entry.SourceName.Equals(info.Name, StringComparison.Ordinal))
                ReportError(path + ":" + index, $"sourceName '{entry.SourceName}' does not match the database item.");
    }

    private static void ValidateMonsterSourceNames(string path, object catalog)
    {
        if (Envir.Main == null) return;
        Dictionary<int, MonsterInfo> monsters = Envir.Main.MonsterInfoList.ToDictionary(x => x.Index);
        foreach ((int index, MonsterLocalizationEntry entry) in (IReadOnlyDictionary<int, MonsterLocalizationEntry>)catalog)
            if (!monsters.TryGetValue(index, out MonsterInfo info) || !entry.SourceName.Equals(info.Name, StringComparison.Ordinal))
                ReportError(path + ":" + index, $"sourceName '{entry.SourceName}' does not match the database monster.");
    }

    private static string GetSnapshotKey(string language, string resourceName) => $"{language}\0{resourceName}";

    private static void ReportError(string key, string message)
    {
        DateTime now = DateTime.UtcNow;
        if (LastErrors.TryGetValue(key, out DateTime last) && now - last < TimeSpan.FromMinutes(1)) return;
        LastErrors[key] = now;
        MessageQueue.Instance.Enqueue($"Localization error [{key}]: {message}");
    }
}
