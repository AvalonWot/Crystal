using System.Net;
using System.Security.Cryptography;

namespace Client.Localization;

public static class ItemLocalizationService
{
    private static IReadOnlyDictionary<int, ItemLocalizationEntry> _items =
        new Dictionary<int, ItemLocalizationEntry>();

    public static string Culture { get; private set; } = "en-US";

    public static async Task SynchronizeAsync(string effectiveLanguage, string baseUrl)
    {
        string culture = ItemLocalizationFormat.NormalizeCulture(effectiveLanguage);
        if (culture.Length == 0) culture = ItemLocalizationFormat.NormalizeCulture(Settings.Language);
        if (culture.Length == 0) culture = "en-US";

        string cachePath = GetCachePath(culture);
        bool loaded = false;

        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(EnsureTrailingSlash(baseUrl), UriKind.Absolute, out Uri localizationBase) &&
            (localizationBase.Scheme == Uri.UriSchemeHttp || localizationBase.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                string localHash = File.Exists(cachePath) ? ComputeHash(await File.ReadAllBytesAsync(cachePath)) : string.Empty;
                loaded = await DownloadOrLoadAsync(localizationBase, culture, cachePath, localHash, true);
            }
            catch (Exception ex)
            {
                CMain.SaveError($"Item localization download failed: {ex}");
            }
        }

        if (!loaded) loaded = TryLoadCache(cachePath, culture);
        if (!loaded) SetCatalog(culture, new Dictionary<int, ItemLocalizationEntry>());
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

    public static string GetDisplayName(int index, string sourceName)
    {
        IReadOnlyDictionary<int, ItemLocalizationEntry> items = Volatile.Read(ref _items);
        if (items.TryGetValue(index, out ItemLocalizationEntry entry) &&
            entry.SourceName.Equals(sourceName, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return entry.DisplayName;
        }

        return sourceName;
    }

    public static void Apply(UserItem item)
    {
        if (item == null) return;
        Apply(item.Info);
        if (item.Slots == null) return;
        foreach (UserItem slot in item.Slots) Apply(slot);
    }

    private static async Task<bool> DownloadOrLoadAsync(
        Uri localizationBase,
        string culture,
        string cachePath,
        string hash,
        bool allowRetry)
    {
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3
        };

        using HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(10) };
        Uri requestUri = new(localizationBase, $"{Uri.EscapeDataString(culture)}/items.json?hash={Uri.EscapeDataString(hash)}");
        using HttpResponseMessage response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (TryLoadCache(cachePath, culture)) return true;
            return allowRetry && await DownloadOrLoadAsync(localizationBase, culture, cachePath, string.Empty, false);
        }

        if (response.StatusCode != HttpStatusCode.OK) return false;
        if (response.Content.Headers.ContentLength > ItemLocalizationFormat.MaxFileBytes) return false;

        byte[] content = await response.Content.ReadAsByteArrayAsync();
        if (!ItemLocalizationFormat.TryParse(content, culture, out ItemLocalizationDocument document, out string error))
        {
            CMain.SaveError($"Invalid item localization response: {error}");
            return false;
        }

        string actualHash = ComputeHash(content);
        if (!response.Headers.TryGetValues("X-Content-SHA256", out IEnumerable<string> values))
        {
            CMain.SaveError("Item localization response did not include X-Content-SHA256.");
            return false;
        }

        string expectedHash = values.FirstOrDefault();
        if (expectedHash?.Length != 64 ||
            expectedHash.Any(character => !Uri.IsHexDigit(character)) ||
            !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            CMain.SaveError("Item localization response hash mismatch.");
            return false;
        }

        if (!WriteCacheAtomically(cachePath, content)) return false;
        SetCatalog(culture, document.Items);
        return true;
    }

    private static bool TryLoadCache(string path, string culture)
    {
        try
        {
            if (!File.Exists(path)) return false;
            byte[] content = File.ReadAllBytes(path);
            if (!ItemLocalizationFormat.TryParse(content, culture, out ItemLocalizationDocument document, out string error))
            {
                CMain.SaveError($"Invalid cached item localization: {error}");
                return false;
            }

            SetCatalog(culture, document.Items);
            return true;
        }
        catch (Exception ex)
        {
            CMain.SaveError($"Unable to load cached item localization: {ex}");
            return false;
        }
    }

    private static bool WriteCacheAtomically(string path, byte[] content)
    {
        string tempPath = string.Empty;
        try
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $"items.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(tempPath, content);
            File.Move(tempPath, path, true);
            return true;
        }
        catch (Exception ex)
        {
            CMain.SaveError($"Unable to cache item localization: {ex}");
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

    private static void SetCatalog(string culture, Dictionary<int, ItemLocalizationEntry> items)
    {
        Culture = culture;
        Volatile.Write(ref _items, new Dictionary<int, ItemLocalizationEntry>(items));
    }

    private static string GetCachePath(string culture)
    {
        return Path.Combine(".", "Localization", "Cache", culture, "items.json");
    }

    private static string EnsureTrailingSlash(string value)
    {
        value = value.Trim();
        return value.EndsWith('/') ? value : value + "/";
    }

    private static string ComputeHash(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }
}
