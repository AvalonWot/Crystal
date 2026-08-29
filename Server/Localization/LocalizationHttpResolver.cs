using System.Net;
using System.Text.RegularExpressions;

namespace Server.Library.Localization;

public readonly record struct LocalizationHttpResult(HttpStatusCode StatusCode, LocalizationSnapshot Snapshot);

public static class LocalizationHttpResolver
{
    private static readonly Regex HashPattern = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    public static LocalizationHttpResult Resolve(string absolutePath, string hash)
    {
        string[] segments = (absolutePath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 ||
            !segments[0].Equals("localization", StringComparison.OrdinalIgnoreCase) ||
            !LocalizationManager.IsKnownResource(segments[2]))
            return new LocalizationHttpResult(HttpStatusCode.NotFound, null);

        string language = LocalizationManager.ResolveLanguage(Uri.UnescapeDataString(segments[1]));
        hash ??= string.Empty;
        if (language.Length == 0)
            return new LocalizationHttpResult(HttpStatusCode.NotFound, null);
        if (hash.Length > 0 && !HashPattern.IsMatch(hash))
            return new LocalizationHttpResult(HttpStatusCode.BadRequest, null);
        if (!LocalizationManager.TryGetSnapshot(language, segments[2], out LocalizationSnapshot snapshot))
            return new LocalizationHttpResult(HttpStatusCode.NotFound, null);

        return new LocalizationHttpResult(
            hash.Equals(snapshot.Hash, StringComparison.OrdinalIgnoreCase) ? HttpStatusCode.NotModified : HttpStatusCode.OK,
            snapshot);
    }
}
