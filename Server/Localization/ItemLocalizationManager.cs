using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Server.MirEnvir;

namespace Server.Library.Localization;

public sealed class ItemLocalizationSnapshot
{
    public string Culture { get; init; }
    public string Hash { get; init; }
    public byte[] Content { get; init; }
    public IReadOnlyDictionary<int, ItemLocalizationEntry> Items { get; init; }
    public long FileLength { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
}

public static class ItemLocalizationManager
{
    private static readonly ConcurrentDictionary<string, ItemLocalizationSnapshot> Snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, DateTime> LastErrors =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, TextMap> ServerTextMaps =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, (long Length, DateTime LastWrite)> ServerTextFiles =
        new(StringComparer.OrdinalIgnoreCase);

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
        try
        {
            _monitorTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        finally
        {
            cancellation.Dispose();
            _monitorTask = null;
        }
    }

    public static string ResolveCulture(string requestedCulture)
    {
        string normalized = ItemLocalizationFormat.NormalizeCulture(requestedCulture);
        if (normalized.Length > 0 && Snapshots.ContainsKey(normalized)) return normalized;

        string fallback = ItemLocalizationFormat.NormalizeCulture(Settings.LocalizationDefaultCulture);
        if (fallback.Length > 0 && Snapshots.ContainsKey(fallback)) return fallback;

        return fallback.Length > 0 ? fallback : "en-US";
    }

    public static bool TryGetSnapshot(string culture, out ItemLocalizationSnapshot snapshot)
    {
        string normalized = ItemLocalizationFormat.NormalizeCulture(culture);
        return Snapshots.TryGetValue(normalized, out snapshot);
    }

    public static string GetDisplayName(string culture, ItemInfo info)
    {
        if (info == null) return string.Empty;

        if (TryGetSnapshot(culture, out ItemLocalizationSnapshot snapshot) &&
            snapshot.Items.TryGetValue(info.Index, out ItemLocalizationEntry entry) &&
            entry.SourceName.Equals(info.Name, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return entry.DisplayName;
        }

        return info.FriendlyName;
    }

    public static string GetServerText(string culture, ServerTextKeys key, params object[] arguments)
    {
        string normalized = ItemLocalizationFormat.NormalizeCulture(culture);
        if (ServerTextMaps.TryGetValue(normalized, out TextMap map) &&
            map.Text != null && map.Text.TryGetValue(key.ToString(), out string value))
        {
            return arguments == null || arguments.Length == 0 ? value : string.Format(value, arguments);
        }

        return GameLanguage.ServerTextMap.GetLocalization(key, arguments ?? Array.Empty<object>());
    }

    private static async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                Scan();
        }
        catch (OperationCanceledException)
        {
        }
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

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception ex)
        {
            ReportError(root, ex.Message);
            return;
        }

        LoadServerTextMap(root, "en-US", "English.json");
        LoadServerTextMap(root, "zh-CN", "Chinese.json");

        foreach (string directory in directories)
        {
            string culture = ItemLocalizationFormat.NormalizeCulture(Path.GetFileName(directory));
            if (culture.Length == 0) continue;

            string path = Path.Combine(directory, "items.json");
            if (!File.Exists(path)) continue;

            try
            {
                FileInfo before = new(path);
                if (Snapshots.TryGetValue(culture, out ItemLocalizationSnapshot current) &&
                    current.FileLength == before.Length &&
                    current.LastWriteTimeUtc == before.LastWriteTimeUtc)
                {
                    continue;
                }

                byte[] content = File.ReadAllBytes(path);
                FileInfo after = new(path);
                if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
                    continue;

                if (!ItemLocalizationFormat.TryParse(content, culture, out ItemLocalizationDocument document, out string error))
                {
                    ReportError(path, error);
                    continue;
                }

                ValidateSourceNames(path, document);

                ItemLocalizationSnapshot snapshot = new()
                {
                    Culture = culture,
                    Hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                    Content = content,
                    Items = new Dictionary<int, ItemLocalizationEntry>(document.Items),
                    FileLength = after.Length,
                    LastWriteTimeUtc = after.LastWriteTimeUtc
                };

                Snapshots[culture] = snapshot;
                LastErrors.TryRemove(path, out _);
                MessageQueue.Instance.Enqueue($"Loaded item localization '{culture}' ({document.Items.Count} entries, {snapshot.Hash}).");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ReportError(path, ex.Message);
            }
        }
    }

    private static void LoadServerTextMap(string root, string culture, string fileName)
    {
        string path = Path.Combine(root, fileName);
        if (!File.Exists(path)) return;

        try
        {
            FileInfo info = new(path);
            if (ServerTextFiles.TryGetValue(culture, out var current) &&
                current.Length == info.Length && current.LastWrite == info.LastWriteTimeUtc)
            {
                return;
            }

            TextMap map = JsonSerializer.Deserialize<TextMap>(File.ReadAllBytes(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                MaxDepth = 32
            });
            if (map?.Text == null) throw new InvalidDataException("Server text map has no Text dictionary.");

            ServerTextMaps[culture] = map;
            ServerTextFiles[culture] = (info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            ReportError(path, ex.Message);
        }
    }

    private static void ValidateSourceNames(string path, ItemLocalizationDocument document)
    {
        if (Envir.Main == null) return;

        Dictionary<int, ItemInfo> items = Envir.Main.ItemInfoList.ToDictionary(x => x.Index);
        foreach ((int index, ItemLocalizationEntry entry) in document.Items)
        {
            if (!items.TryGetValue(index, out ItemInfo info) ||
                !entry.SourceName.Equals(info.Name, StringComparison.Ordinal))
            {
                ReportError(path + ":" + index, $"sourceName '{entry.SourceName}' does not match the database item.");
            }
        }
    }

    private static void ReportError(string key, string message)
    {
        DateTime now = DateTime.UtcNow;
        if (LastErrors.TryGetValue(key, out DateTime last) && now - last < TimeSpan.FromMinutes(1)) return;

        LastErrors[key] = now;
        MessageQueue.Instance.Enqueue($"Item localization error [{key}]: {message}");
    }
}
