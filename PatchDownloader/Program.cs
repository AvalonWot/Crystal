using PatchDownloader.Forms;
using PatchDownloader.Services;

namespace PatchDownloader;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var settingsStore = new SettingsStore(
            Path.Combine(Application.StartupPath, "patchdownloader.settings.json"));
        var localFileService = new LocalFileService();

        Application.Run(new MainForm(settingsStore, localFileService));
    }
}
