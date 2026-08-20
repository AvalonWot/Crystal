using System.Text.Json;
using PatchDownloader.Models;

namespace PatchDownloader.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public SettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public async Task<DownloaderSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return new DownloaderSettings();
        }

        try
        {
            await using var stream = new FileStream(
                _settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var settings = await JsonSerializer.DeserializeAsync<DownloaderSettings>(
                stream, JsonOptions, cancellationToken);
            return (settings ?? new DownloaderSettings()).Normalize();
        }
        catch (JsonException)
        {
            return new DownloaderSettings();
        }
    }

    public async Task SaveAsync(DownloaderSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            _settingsPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, settings.Normalize(), JsonOptions, cancellationToken);
    }
}
