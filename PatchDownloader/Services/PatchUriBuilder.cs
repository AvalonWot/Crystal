namespace PatchDownloader.Services;

public static class PatchUriBuilder
{
    public static Uri NormalizeHost(string host)
    {
        var value = host.Trim();
        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            value = $"http://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new FormatException("Host 必须是有效的 HTTP 或 HTTPS 地址。");
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    public static Uri Build(Uri host, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var escaped = string.Join('/', normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        return new Uri(host, escaped);
    }
}
