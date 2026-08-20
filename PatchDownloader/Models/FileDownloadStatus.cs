namespace PatchDownloader.Models;

public sealed record FileDownloadStatus(
    string FileName,
    string Status,
    string? ErrorMessage = null);
