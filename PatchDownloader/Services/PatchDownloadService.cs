using System.Diagnostics;
using PatchDownloader.Models;

namespace PatchDownloader.Services;

public sealed class PatchDownloadService
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly HttpClient _httpClient;
    private readonly LocalFileService _localFileService;

    public PatchDownloadService(HttpClient httpClient, LocalFileService localFileService)
    {
        _httpClient = httpClient;
        _localFileService = localFileService;
    }

    public async Task<DownloadBatchResult> DownloadAsync(
        IReadOnlyList<PatchManifestEntry> entries,
        int skippedFiles,
        DownloaderSettings settings,
        IProgress<DownloadProgressSnapshot> progress,
        IProgress<FileDownloadStatus> fileStatus,
        CancellationToken cancellationToken)
    {
        var host = PatchUriBuilder.NormalizeHost(settings.Host);
        var totalBytes = entries.Aggregate(0L, (total, entry) => checked(total + entry.ExpectedTransferLength));
        long transferredBytes = 0;
        var completedFiles = 0;
        var failedFiles = 0;
        var activeDownloads = 0;

        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorTask = MonitorProgressAsync(
            () => Interlocked.Read(ref transferredBytes),
            () => Volatile.Read(ref completedFiles),
            () => Volatile.Read(ref failedFiles),
            () => Volatile.Read(ref activeDownloads),
            totalBytes,
            entries.Count,
            progress,
            monitorCancellation.Token);

        try
        {
            await Parallel.ForEachAsync(
                entries,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = settings.Concurrency
                },
                async (entry, token) =>
                {
                    Interlocked.Increment(ref activeDownloads);
                    try
                    {
                        var error = await DownloadWithRetryAsync(
                            host,
                            settings.ClientRoot,
                            entry,
                            bytes => Interlocked.Add(ref transferredBytes, bytes),
                            fileStatus,
                            token);

                        if (error is null)
                        {
                            Interlocked.Increment(ref completedFiles);
                            fileStatus.Report(new FileDownloadStatus(entry.FileName, "已完成"));
                        }
                        else
                        {
                            Interlocked.Increment(ref failedFiles);
                            fileStatus.Report(new FileDownloadStatus(entry.FileName, "失败", error));
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeDownloads);
                    }
                });
        }
        finally
        {
            monitorCancellation.Cancel();
            try
            {
                await monitorTask;
            }
            catch (OperationCanceledException) when (monitorCancellation.IsCancellationRequested)
            {
            }

            progress.Report(new DownloadProgressSnapshot(
                Interlocked.Read(ref transferredBytes),
                totalBytes,
                0,
                Volatile.Read(ref completedFiles),
                Volatile.Read(ref failedFiles),
                entries.Count,
                Volatile.Read(ref activeDownloads)));
        }

        return new DownloadBatchResult(completedFiles, failedFiles, skippedFiles);
    }

    private async Task<string?> DownloadWithRetryAsync(
        Uri host,
        string clientRoot,
        PatchManifestEntry entry,
        Action<int> reportBytes,
        IProgress<FileDownloadStatus> fileStatus,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transferPath = _localFileService.CreateTransferPath(clientRoot, entry);
            fileStatus.Report(new FileDownloadStatus(
                entry.FileName,
                attempt == 1 ? "下载中" : $"重试 {attempt}/{MaximumAttempts}"));

            try
            {
                await DownloadToFileAsync(host, entry, transferPath, reportBytes, cancellationToken);
                await _localFileService.CommitAsync(clientRoot, entry, transferPath, cancellationToken);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                lastException = exception;
                TryDelete(transferPath);

                if (attempt < MaximumAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                }
            }
        }

        return lastException?.Message ?? "未知下载错误。";
    }

    private async Task DownloadToFileAsync(
        Uri host,
        PatchManifestEntry entry,
        string transferPath,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        var remoteName = entry.FileName.Replace('\\', '/');
        if (!remoteName.Equals("PList.gz", StringComparison.OrdinalIgnoreCase) &&
            (entry.CompressedLength != entry.Length || entry.CompressedLength == 0))
        {
            remoteName += ".gz";
        }

        var remoteUri = PatchUriBuilder.Build(host, remoteName);
        using var response = await _httpClient.GetAsync(
            remoteUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            transferPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            reportBytes(read);
        }
    }

    private static async Task MonitorProgressAsync(
        Func<long> readTransferredBytes,
        Func<int> readCompletedFiles,
        Func<int> readFailedFiles,
        Func<int> readActiveDownloads,
        long totalBytes,
        int totalFiles,
        IProgress<DownloadProgressSnapshot> progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastTimestamp = stopwatch.Elapsed;
        var lastBytes = readTransferredBytes();

        while (true)
        {
            await Task.Delay(ProgressInterval, cancellationToken);
            var timestamp = stopwatch.Elapsed;
            var bytes = readTransferredBytes();
            var elapsedSeconds = (timestamp - lastTimestamp).TotalSeconds;
            var speed = elapsedSeconds > 0 ? (bytes - lastBytes) / elapsedSeconds : 0;

            progress.Report(new DownloadProgressSnapshot(
                bytes,
                totalBytes,
                Math.Max(0, speed),
                readCompletedFiles(),
                readFailedFiles(),
                totalFiles,
                readActiveDownloads()));

            lastTimestamp = timestamp;
            lastBytes = bytes;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
