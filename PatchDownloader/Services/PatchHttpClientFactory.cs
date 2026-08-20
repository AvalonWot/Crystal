using System.Net;
using PatchDownloader.Models;

namespace PatchDownloader.Services;

public static class PatchHttpClientFactory
{
    public static HttpClient Create(DownloaderSettings settings)
    {
        var proxy = ProxyFactory.Create(settings.Proxy);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
            Proxy = proxy,
            UseProxy = proxy is not null,
            MaxConnectionsPerServer = settings.Concurrency
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }
}
