using System.Text;
using PatchDownloader.Models;

namespace PatchDownloader.Services;

public sealed class PatchManifestService
{
    private const int MaximumManifestBytes = 64 * 1024 * 1024;
    private const int MaximumEntryCount = 1_000_000;
    private readonly HttpClient _httpClient;

    public PatchManifestService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<PatchManifestEntry>> LoadAsync(
        Uri host,
        CancellationToken cancellationToken)
    {
        var manifestUri = PatchUriBuilder.Build(host, "PList.gz");
        using var response = await _httpClient.GetAsync(
            manifestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaximumManifestBytes)
        {
            throw new InvalidDataException("补丁清单超过允许的最大大小。");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumManifestBytes)
            {
                throw new InvalidDataException("补丁清单超过允许的最大大小。");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return Parse(buffer.ToArray());
    }

    internal static IReadOnlyList<PatchManifestEntry> Parse(byte[] data)
    {
        if (data.Length == 0)
        {
            throw new InvalidDataException("补丁清单为空。");
        }

        if (data[0] == (byte)'<')
        {
            throw new InvalidDataException("服务器返回了 HTML 页面，而不是补丁清单。");
        }

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumEntryCount)
            {
                throw new InvalidDataException("补丁清单中的文件数量无效。");
            }

            var entries = new List<PatchManifestEntry>(count);
            var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < count; index++)
            {
                var fileName = reader.ReadString();
                var length = reader.ReadInt32();
                var compressedLength = reader.ReadInt32();
                var creationTime = DateTime.FromBinary(reader.ReadInt64());

                if (string.IsNullOrWhiteSpace(fileName) || length < 0 || compressedLength < 0)
                {
                    throw new InvalidDataException($"补丁清单第 {index + 1} 项无效。");
                }

                if (!fileNames.Add(fileName.Replace('\\', '/')))
                {
                    throw new InvalidDataException($"补丁清单包含重复文件：{fileName}");
                }

                entries.Add(new PatchManifestEntry(fileName, length, compressedLength, creationTime));
            }

            return entries;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("补丁清单不完整或版本不兼容。", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("补丁清单包含无效数据。", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("补丁清单包含无效时间。", exception);
        }
    }
}
