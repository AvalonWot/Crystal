namespace PatchDownloader.Models;

public sealed record PatchManifestEntry(
    string FileName,
    int Length,
    int CompressedLength,
    DateTime CreationTime)
{
    public bool IsCompressed => CompressedLength > 0 && CompressedLength != Length;

    public long ExpectedTransferLength => CompressedLength > 0 ? CompressedLength : Length;
}
