using System.Net;
using System.Text.RegularExpressions;

namespace Server.Library.Localization;

public readonly record struct ItemLocalizationHttpResult(
    HttpStatusCode StatusCode,
    ItemLocalizationSnapshot Snapshot);

public static class ItemLocalizationHttpResolver
{
    private static readonly Regex HashPattern = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    public static ItemLocalizationHttpResult Resolve(string absolutePath, string hash)
    {
        string[] segments = (absolutePath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 ||
            !segments[0].Equals("localization", StringComparison.OrdinalIgnoreCase) ||
            !segments[2].Equals("items.json", StringComparison.OrdinalIgnoreCase))
        {
            return new ItemLocalizationHttpResult(HttpStatusCode.NotFound, null);
        }

        string culture = ItemLocalizationFormat.NormalizeCulture(segments[1]);
        hash ??= string.Empty;
        if (culture.Length == 0 || (hash.Length > 0 && !HashPattern.IsMatch(hash)))
            return new ItemLocalizationHttpResult(HttpStatusCode.BadRequest, null);

        if (!ItemLocalizationManager.TryGetSnapshot(culture, out ItemLocalizationSnapshot snapshot))
            return new ItemLocalizationHttpResult(HttpStatusCode.NotFound, null);

        HttpStatusCode status = hash.Equals(snapshot.Hash, StringComparison.OrdinalIgnoreCase)
            ? HttpStatusCode.NotModified
            : HttpStatusCode.OK;
        return new ItemLocalizationHttpResult(status, snapshot);
    }
}
