namespace PatchDownloader.Models;

public sealed class DownloaderSettings
{
    public string Host { get; set; } = "http://mirfiles.com/mir2/cmir/patch/";

    public string Proxy { get; set; } = string.Empty;

    public int Concurrency { get; set; } = 4;

    public string ClientRoot { get; set; } = AppContext.BaseDirectory;

    public DownloaderSettings Normalize()
    {
        Host = Host.Trim();
        Proxy = Proxy.Trim();
        ClientRoot = string.IsNullOrWhiteSpace(ClientRoot)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(ClientRoot.Trim());
        Concurrency = Math.Clamp(Concurrency, 1, 100);
        return this;
    }
}
