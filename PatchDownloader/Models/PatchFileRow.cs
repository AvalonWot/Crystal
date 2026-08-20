using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PatchDownloader.Models;

public sealed class PatchFileRow : INotifyPropertyChanged
{
    private bool _localExists;
    private bool _needsUpdate;
    private string _status = "等待检查";
    private string? _errorMessage;

    public PatchFileRow(PatchManifestEntry entry)
    {
        Entry = entry;
    }

    public PatchManifestEntry Entry { get; }

    public string FileName => Entry.FileName;

    public string DisplaySize => SizeFormatter.Format(Entry.Length);

    public string CreationTime => Entry.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");

    public bool LocalExists
    {
        get => _localExists;
        set => SetField(ref _localExists, value);
    }

    [Browsable(false)]
    public bool NeedsUpdate
    {
        get => _needsUpdate;
        set => SetField(ref _needsUpdate, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    [Browsable(false)]
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(double bytes)
    {
        var value = Math.Max(0, bytes);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{value:0} {Units[unitIndex]}" : $"{value:0.##} {Units[unitIndex]}";
    }
}
