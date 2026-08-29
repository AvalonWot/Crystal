using System.Net;
using System.Security.Cryptography;

namespace Client.Localization;

public static class LocalizationService
{
    private delegate bool CatalogParser(byte[] content, out object catalog, out string error);
    private delegate void CatalogSetter(object catalog);
    private sealed record ResourceDefinition(string ResourceName, int MaxFileBytes, CatalogParser Parser, CatalogSetter Setter);

    private static readonly ResourceDefinition[] Resources =
    {
        new("items.json", ItemLocalizationFormat.MaxFileBytes, ParseItems, SetItems),
        new("monsters.json", MonsterLocalizationFormat.MaxFileBytes, ParseMonsters, SetMonsters)
    };
    private static IReadOnlyDictionary<int, ItemLocalizationEntry> _items = new Dictionary<int, ItemLocalizationEntry>();
    private static IReadOnlyDictionary<int, MonsterLocalizationEntry> _monsters = new Dictionary<int, MonsterLocalizationEntry>();

    public static string Language { get; private set; } = string.Empty;

    public static async Task SynchronizeAsync(string effectiveLanguage, string baseUrl)
    {
        string language = NormalizeLanguageKey(effectiveLanguage);
        Language = language;
        if (language.Length == 0)
        {
            ClearCatalogs();
            return;
        }

        await Task.WhenAll(Resources.Select(resource => SynchronizeResourceAsync(language, baseUrl, resource)));
    }

    public static void Apply(ItemInfo info)
    {
        if (info == null) return;
        IReadOnlyDictionary<int, ItemLocalizationEntry> items = Volatile.Read(ref _items);
        if (items.TryGetValue(info.Index, out ItemLocalizationEntry entry) &&
            entry.SourceName.Equals(info.Name, StringComparison.Ordinal))
        {
            info.DisplayName = entry.DisplayName;
            info.DisplayToolTip = entry.ToolTip;
            return;
        }
        info.DisplayName = string.Empty;
        info.DisplayToolTip = string.Empty;
    }

    public static void Apply(UserItem item)
    {
        if (item == null) return;
        Apply(item.Info);
        if (item.Slots == null) return;
        foreach (UserItem slot in item.Slots) Apply(slot);
    }

    public static void Apply(ClientMonsterInfo info)
    {
        if (info == null) return;
        IReadOnlyDictionary<int, MonsterLocalizationEntry> monsters = Volatile.Read(ref _monsters);
        if (monsters.TryGetValue(info.Index, out MonsterLocalizationEntry entry) &&
            entry.SourceName.Equals(info.Name, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            info.DisplayName = entry.DisplayName;
            return;
        }
        info.DisplayName = string.Empty;
    }

    public static string GetItemDisplayName(int index, string sourceName)
    {
        IReadOnlyDictionary<int, ItemLocalizationEntry> items = Volatile.Read(ref _items);
        if (items.TryGetValue(index, out ItemLocalizationEntry entry) &&
            entry.SourceName.Equals(sourceName ?? string.Empty, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.DisplayName)) return entry.DisplayName;
        return sourceName ?? string.Empty;
    }

    public static string GetMonsterDisplayName(int index, string sourceName)
    {
        IReadOnlyDictionary<int, MonsterLocalizationEntry> monsters = Volatile.Read(ref _monsters);
        if (monsters.TryGetValue(index, out MonsterLocalizationEntry entry) &&
            entry.SourceName.Equals(sourceName ?? string.Empty, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.DisplayName)) return entry.DisplayName;
        return sourceName ?? string.Empty;
    }

    public static string GetMonsterObjectDisplayName(int index, string objectName, byte ai)
    {
        objectName ??= string.Empty;
        if (index <= 0 || ai == 64) return objectName;
        IReadOnlyDictionary<int, MonsterLocalizationEntry> monsters = Volatile.Read(ref _monsters);
        return monsters.TryGetValue(index, out MonsterLocalizationEntry entry)
            ? MonsterLocalizationNames.GetObjectDisplayName(entry, objectName, ai)
            : objectName;
    }

    private static async Task SynchronizeResourceAsync(string language, string baseUrl, ResourceDefinition resource)
    {
        string resourceName = resource.ResourceName;
        string cachePath = GetCachePath(language, resourceName);
        object catalog = null;
        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(EnsureTrailingSlash(baseUrl), UriKind.Absolute, out Uri localizationBase) &&
            (localizationBase.Scheme == Uri.UriSchemeHttp || localizationBase.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                string localHash = File.Exists(cachePath) ? ComputeHash(await File.ReadAllBytesAsync(cachePath)) : string.Empty;
                catalog = await DownloadOrLoadAsync(localizationBase, language, resource, cachePath, localHash, true);
            }
            catch (Exception ex)
            {
                CMain.SaveError($"Localization download failed for {language}/{resourceName}: {ex}");
            }
        }

        catalog ??= TryLoadCache(cachePath, resource);
        resource.Setter(catalog);
    }

    private static async Task<object> DownloadOrLoadAsync(Uri localizationBase, string language, ResourceDefinition resource,
        string cachePath, string hash, bool allowRetry)
    {
        string resourceName = resource.ResourceName;
        using HttpClientHandler handler = new() { AllowAutoRedirect = true, MaxAutomaticRedirections = 3 };
        using HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(10) };
        Uri requestUri = new(localizationBase,
            $"{Uri.EscapeDataString(language)}/{resourceName}?hash={Uri.EscapeDataString(hash)}");
        using HttpResponseMessage response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            object cached = TryLoadCache(cachePath, resource);
            if (cached != null) return cached;
            return allowRetry
                ? await DownloadOrLoadAsync(localizationBase, language, resource, cachePath, string.Empty, false)
                : null;
        }
        if (response.StatusCode != HttpStatusCode.OK) return null;

        if (response.Content.Headers.ContentLength > resource.MaxFileBytes) return null;
        byte[] content = await response.Content.ReadAsByteArrayAsync();
        if (!resource.Parser(content, out object catalog, out string error))
        {
            CMain.SaveError($"Invalid localization response for {resourceName}: {error}");
            return null;
        }

        string actualHash = ComputeHash(content);
        if (!response.Headers.TryGetValues("X-Content-SHA256", out IEnumerable<string> values))
        {
            CMain.SaveError($"Localization response for {resourceName} did not include X-Content-SHA256.");
            return null;
        }
        string expectedHash = values.FirstOrDefault();
        if (expectedHash?.Length != 64 || expectedHash.Any(character => !Uri.IsHexDigit(character)) ||
            !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            CMain.SaveError($"Localization response hash mismatch for {resourceName}.");
            return null;
        }

        return WriteCacheAtomically(cachePath, resourceName, content) ? catalog : null;
    }

    private static object TryLoadCache(string path, ResourceDefinition resource)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (resource.Parser(File.ReadAllBytes(path), out object catalog, out string error)) return catalog;
            CMain.SaveError($"Invalid cached localization for {resource.ResourceName}: {error}");
            return null;
        }
        catch (Exception ex)
        {
            CMain.SaveError($"Unable to load cached localization for {resource.ResourceName}: {ex}");
            return null;
        }
    }

    private static bool ParseItems(byte[] content, out object catalog, out string error)
    {
        if (ItemLocalizationFormat.TryParse(content, out ItemLocalizationDocument document, out error))
        {
            catalog = document.Items;
            return true;
        }
        catalog = null;
        return false;
    }

    private static bool ParseMonsters(byte[] content, out object catalog, out string error)
    {
        if (MonsterLocalizationFormat.TryParse(content, out MonsterLocalizationDocument document, out error))
        {
            catalog = document.Monsters;
            return true;
        }
        catalog = null;
        return false;
    }

    private static bool WriteCacheAtomically(string path, string resourceName, byte[] content)
    {
        string tempPath = string.Empty;
        try
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(resourceName)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(tempPath, content);
            File.Move(tempPath, path, true);
            return true;
        }
        catch (Exception ex)
        {
            CMain.SaveError($"Unable to cache localization for {resourceName}: {ex}");
            return false;
        }
        finally
        {
            if (tempPath.Length > 0 && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static void SetItems(object catalog)
    {
        Dictionary<int, ItemLocalizationEntry> items = catalog as Dictionary<int, ItemLocalizationEntry> ?? new();
        Volatile.Write(ref _items, new Dictionary<int, ItemLocalizationEntry>(items));
    }

    private static void SetMonsters(object catalog)
    {
        Dictionary<int, MonsterLocalizationEntry> monsters = catalog as Dictionary<int, MonsterLocalizationEntry> ?? new();
        Volatile.Write(ref _monsters, new Dictionary<int, MonsterLocalizationEntry>(monsters));
    }

    private static void ClearCatalogs()
    {
        Volatile.Write(ref _items, new Dictionary<int, ItemLocalizationEntry>());
        Volatile.Write(ref _monsters, new Dictionary<int, MonsterLocalizationEntry>());
    }

    private static string NormalizeLanguageKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string language = value.Trim();
        return language is "." or ".." || language.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
               language.Contains('/') || language.Contains('\\')
            ? string.Empty
            : language;
    }

    private static string GetCachePath(string language, string resourceName) =>
        Path.Combine(".", "Localization", "Cache", language, resourceName);
    private static string EnsureTrailingSlash(string value) => value.Trim().EndsWith('/') ? value.Trim() : value.Trim() + "/";
    private static string ComputeHash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
