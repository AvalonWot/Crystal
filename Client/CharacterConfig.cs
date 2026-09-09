using System.Text;

namespace Client;

internal static class CharacterConfig
{
    private const string AutoToolFileName = "AutoTool.ini";
    private const string GroundItemFilterFileName = "GroundItemFilter.txt";
    private const string DefaultAutoToolContent = "[AutoPotion]\r\nHP=\r\nMP=\r\n\r\n[AutoMagic]\r\nMagicShield=False\r\n";

    public static void Load(string characterName)
    {
        AutoPotionSettings.Clear();
        GroundItemFilter.Clear();

        try
        {
            string characterDirectory = GetCharacterDirectory(characterName);
            Directory.CreateDirectory(characterDirectory);

            string autoToolPath = Path.Combine(characterDirectory, AutoToolFileName);
            string groundItemFilterPath = Path.Combine(characterDirectory, GroundItemFilterFileName);

            CreateIfMissing(autoToolPath, DefaultAutoToolContent);
            CreateIfMissing(groundItemFilterPath, string.Empty);

            AutoPotionSettings.Load(autoToolPath);
            GroundItemFilter.Load(groundItemFilterPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AutoPotionSettings.Clear();
            GroundItemFilter.Clear();
            CMain.SaveError($"Character configuration load failed for '{characterName}': {ex.Message}");
        }
    }

    private static string GetCharacterDirectory(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName) ||
            characterName is "." or ".." ||
            characterName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The character name cannot be used as a directory name.", nameof(characterName));

        string configDirectory = Path.GetFullPath(Path.Combine(Application.StartupPath, "Config"));
        string characterDirectory = Path.GetFullPath(Path.Combine(configDirectory, characterName));
        string relativePath = Path.GetRelativePath(configDirectory, characterDirectory);

        if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new ArgumentException("The character configuration directory is outside Config.", nameof(characterName));

        return characterDirectory;
    }

    private static void CreateIfMissing(string filePath, string content)
    {
        if (!File.Exists(filePath))
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
    }
}
