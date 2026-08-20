namespace PatchDownloader.Models;

public sealed record DownloadBatchResult(
    int SuccessfulFiles,
    int FailedFiles,
    int SkippedFiles);
