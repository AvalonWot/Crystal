using System.Text;
using Client.MirObjects;
using Client.MirScenes;

namespace Client;

internal static class GroundItemFilter
{
    private static readonly string FilePath = Path.Combine(Application.StartupPath, "GroundItemFilter.txt");
    private static HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private static string _content;
    private static long _nextCheck;

    public static void Load()
    {
        _nextCheck = Environment.TickCount64 + 3000;
        try
        {
            string content;
            try
            {
                content = File.ReadAllText(FilePath, new UTF8Encoding(false, true));
            }
            catch (FileNotFoundException) { content = string.Empty; }
            catch (DirectoryNotFoundException) { content = string.Empty; }

            if (content == _content) return;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = new StringReader(content);
            while (reader.ReadLine() is string line)
            {
                line = line.Trim();
                if (line.Length != 0 && !line.StartsWith('#')) names.Add(line);
            }
            _names = names;
            _content = content;
            if (MapObject.MouseObject is ItemObject item && item.IsFiltered)
                MapObject.MouseObjectID = 0;
            if (GameScene.Scene?.MapControl != null)
                GameScene.Scene.MapControl.TextureValid = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            CMain.SaveError($"Ground item filter reload failed: {ex.Message}");
        }
    }

    public static void Process()
    {
        if (Environment.TickCount64 >= _nextCheck) Load();
    }

    public static bool Contains(string name) => _names.Contains(WithoutCount(name));

    public static string WithoutCount(string name)
    {
        name = (name ?? string.Empty).Trim();
        int suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (suffix >= 0 && name.EndsWith(')') &&
            uint.TryParse(name.AsSpan(suffix + 2, name.Length - suffix - 3), out _))
            return name[..suffix];
        return name;
    }
}
