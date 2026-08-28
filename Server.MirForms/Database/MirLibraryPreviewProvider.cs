using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Server.Database;

internal sealed class MirLibraryPreviewProvider : IDisposable
{
    private const int PreviewSize = 44;
    private const int ImageHeaderSize = 17;
    private const int MaximumImageCount = 1_000_000;

    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly int[] _offsets;
    private readonly Dictionary<int, Bitmap> _cache = new();
    private readonly Dictionary<int, string> _errors = new();

    public string FilePath { get; }
    public Bitmap ErrorImage { get; }

    public MirLibraryPreviewProvider(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        ErrorImage = CreateErrorImage();

        try
        {
            _stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            _reader = new BinaryReader(_stream);

            if (_stream.Length < 8) throw new InvalidDataException("The library header is incomplete.");

            int version = _reader.ReadInt32();
            if (version is < 2 or > 3)
                throw new InvalidDataException($"Unsupported library version {version}; expected version 2 or 3.");

            int count = _reader.ReadInt32();
            if (count < 0 || count > MaximumImageCount)
                throw new InvalidDataException($"Invalid library image count {count}.");

            if (version == 3)
            {
                if (_stream.Position + sizeof(int) > _stream.Length)
                    throw new InvalidDataException("The library frame header is incomplete.");
                _reader.ReadInt32();
            }

            long indexBytes = (long)count * sizeof(int);
            if (_stream.Position + indexBytes > _stream.Length)
                throw new InvalidDataException("The library image index is incomplete.");

            _offsets = new int[count];
            for (int index = 0; index < count; index++) _offsets[index] = _reader.ReadInt32();
        }
        catch
        {
            _reader?.Dispose();
            _stream?.Dispose();
            ErrorImage.Dispose();
            throw;
        }
    }

    public Bitmap GetPreview(int index, out string error)
    {
        if (_cache.TryGetValue(index, out Bitmap cached))
        {
            error = string.Empty;
            return cached;
        }

        if (_errors.TryGetValue(index, out error)) return ErrorImage;

        try
        {
            Bitmap preview = ReadPreview(index);
            _cache[index] = preview;
            error = string.Empty;
            return preview;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or ExternalException or OverflowException)
        {
            error = ex.Message;
            _errors[index] = error;
            return ErrorImage;
        }
    }

    public void Dispose()
    {
        foreach (Bitmap bitmap in _cache.Values) bitmap.Dispose();
        _cache.Clear();
        _errors.Clear();
        _reader.Dispose();
        _stream.Dispose();
        ErrorImage.Dispose();
    }

    private Bitmap ReadPreview(int index)
    {
        if (index < 0 || index >= _offsets.Length)
            throw new InvalidDataException($"Image index {index} is outside the library range 0-{Math.Max(0, _offsets.Length - 1)}.");

        int offset = _offsets[index];
        if (offset <= 0 || (long)offset + ImageHeaderSize > _stream.Length)
            throw new InvalidDataException($"Image index {index} has no readable image data.");

        _stream.Position = offset;
        short width = _reader.ReadInt16();
        short height = _reader.ReadInt16();
        _reader.ReadInt16(); // X
        _reader.ReadInt16(); // Y
        _reader.ReadInt16(); // ShadowX
        _reader.ReadInt16(); // ShadowY
        _reader.ReadByte();  // Shadow and mask flag
        int compressedLength = _reader.ReadInt32();

        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Image index {index} is empty.");
        if (compressedLength <= 0 || _stream.Position + compressedLength > _stream.Length)
            throw new InvalidDataException($"Image index {index} contains an invalid compressed payload.");

        int expectedLength = checked(width * height * 4);
        byte[] compressed = _reader.ReadBytes(compressedLength);
        byte[] pixels = DecompressExact(compressed, expectedLength);

        using Bitmap source = new(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = source.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = width * 4;
            for (int row = 0; row < height; row++)
                Marshal.Copy(pixels, row * rowBytes, data.Scan0 + row * data.Stride, rowBytes);
        }
        finally
        {
            source.UnlockBits(data);
        }

        Bitmap preview = new(PreviewSize, PreviewSize, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(preview);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        float scale = Math.Min((float)PreviewSize / width, (float)PreviewSize / height);
        int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        graphics.DrawImage(source,
            new Rectangle((PreviewSize - targetWidth) / 2, (PreviewSize - targetHeight) / 2, targetWidth, targetHeight),
            new Rectangle(0, 0, width, height), GraphicsUnit.Pixel);
        return preview;
    }

    private static byte[] DecompressExact(byte[] compressed, int expectedLength)
    {
        byte[] output = new byte[expectedLength];
        using GZipStream gzip = new(new MemoryStream(compressed, false), CompressionMode.Decompress);

        int total = 0;
        while (total < output.Length)
        {
            int read = gzip.Read(output, total, output.Length - total);
            if (read == 0) break;
            total += read;
        }

        if (total != expectedLength || gzip.ReadByte() != -1)
            throw new InvalidDataException("The decompressed image size does not match its dimensions.");
        return output;
    }

    internal static Bitmap CreateErrorImage()
    {
        Bitmap bitmap = new(PreviewSize, PreviewSize, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen pen = new(Color.Red, 4);
        graphics.DrawRectangle(pen, 3, 3, PreviewSize - 7, PreviewSize - 7);
        graphics.DrawLine(pen, 10, 10, PreviewSize - 11, PreviewSize - 11);
        graphics.DrawLine(pen, PreviewSize - 11, 10, 10, PreviewSize - 11);
        return bitmap;
    }
}
