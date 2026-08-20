namespace PatchDownloader.Models;

public sealed record DownloadProgressSnapshot(
    long TransferredBytes,
    long TotalBytes,
    double BytesPerSecond,
    int CompletedFiles,
    int FailedFiles,
    int TotalFiles,
    int ActiveDownloads);
