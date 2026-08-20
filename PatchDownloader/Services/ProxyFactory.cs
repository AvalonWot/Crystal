using System.Net;

namespace PatchDownloader.Services;

public static class ProxyFactory
{
    public static IWebProxy? Create(string proxyText)
    {
        if (string.IsNullOrWhiteSpace(proxyText))
        {
            return null;
        }

        if (!Uri.TryCreate(proxyText.Trim(), UriKind.Absolute, out var proxyUri) ||
            (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new FormatException("代理地址必须是有效的 HTTP 或 HTTPS URI。");
        }

        NetworkCredential? credentials = null;
        if (!string.IsNullOrEmpty(proxyUri.UserInfo))
        {
            var parts = proxyUri.UserInfo.Split(':', 2);
            var userName = Uri.UnescapeDataString(parts[0]);
            var password = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            credentials = new NetworkCredential(userName, password);
        }

        var addressBuilder = new UriBuilder(proxyUri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };
        var proxy = new WebProxy(addressBuilder.Uri);
        if (credentials is not null)
        {
            proxy.Credentials = credentials;
        }

        return proxy;
    }
}
