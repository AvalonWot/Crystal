using System.IO.Compression;
using PatchDownloader.Models;

namespace PatchDownloader.Services;

public sealed class LocalFileService
{
    public LocalFileState Inspect(string clientRoot, PatchManifestEntry entry)
    {
        var path = ResolvePath(clientRoot, entry.FileName);
        if (!File.Exists(path))
        {
            return new LocalFileState(false, false);
        }

        var info = new FileInfo(path);
        var matches = info.Length == entry.Length && info.LastWriteTime == entry.CreationTime;
        return new LocalFileState(true, matches);
    }

    public string ResolvePath(string clientRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(clientRoot))
        {
            throw new InvalidOperationException("客户端目录不能为空。");
        }

        var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedRelativePath))
        {
            throw new InvalidDataException($"清单包含绝对路径：{relativePath}");
        }

        if (normalizedRelativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == ".."))
        {
            throw new InvalidDataException($"清单包含目录穿越路径：{relativePath}");
        }

        var root = Path.GetFullPath(clientRoot);
        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(rootWithSeparator, normalizedRelativePath));
        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"清单路径超出客户端目录：{relativePath}");
        }

        return destination;
    }

    public string CreateTransferPath(string clientRoot, PatchManifestEntry entry)
    {
        var destination = ResolvePath(clientRoot, entry.FileName);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("无法确定目标文件目录。");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.download");
    }

    public async Task CommitAsync(
        string clientRoot,
        PatchManifestEntry entry,
        string transferPath,
        CancellationToken cancellationToken)
    {
        var destination = ResolvePath(clientRoot, entry.FileName);
        var finalTempPath = destination + $".{Guid.NewGuid():N}.patching";

        try
        {
            if (entry.IsCompressed)
            {
                await using var input = new FileStream(
                    transferPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false);
                await using var output = new FileStream(
                    finalTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await gzip.CopyToAsync(output, cancellationToken);
            }
            else
            {
                File.Move(transferPath, finalTempPath);
            }

            var outputInfo = new FileInfo(finalTempPath);
            if (outputInfo.Length != entry.Length)
            {
                throw new InvalidDataException(
                    $"文件长度不匹配，期望 {entry.Length}，实际 {outputInfo.Length}。");
            }

            File.SetLastWriteTime(finalTempPath, entry.CreationTime);
            File.Move(finalTempPath, destination, overwrite: true);
        }
        finally
        {
            TryDelete(transferPath);
            TryDelete(finalTempPath);
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
            // A stale temporary file is preferable to masking the original error.
        }
    }
}

public readonly record struct LocalFileState(bool Exists, bool Matches);
