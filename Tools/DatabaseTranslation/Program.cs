using System.Text.Encodings.Web;
using System.Text.Json;
using Server.MirEnvir;

if (args.Length < 2 || args[0] is not ("export" or "apply"))
{
    Console.Error.WriteLine("Usage: DatabaseTranslation <export|apply> <server-directory> [translation-json]");
    return 2;
}

string serverDirectory = Path.GetFullPath(args[1]);
string? documentPath = args.Length >= 3 ? Path.GetFullPath(args[2]) : null;
if (!Directory.Exists(serverDirectory))
{
    Console.Error.WriteLine($"Server directory does not exist: {serverDirectory}");
    return 2;
}

Environment.CurrentDirectory = serverDirectory;
Envir environment = new();
if (!environment.LoadDB())
{
    Console.Error.WriteLine($"Unable to load {Path.Combine(serverDirectory, "Server.MirDB")}");
    return 1;
}

JsonSerializerOptions jsonOptions = new()
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

if (args[0] == "export")
{
    string outputPath = documentPath is not null
        ? documentPath
        : Path.Combine(serverDirectory, "Exports", "DatabaseTranslation.json");
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

    TranslationDocument export = new()
    {
        Maps = environment.MapInfoList.ToDictionary(
            x => x.Index,
            x => new MapTranslation(x.FileName, x.Title, x.Title)),
        Npcs = environment.NPCInfoList.ToDictionary(
            x => x.Index,
            x => new NpcTranslation(x.FileName, x.Name, x.Name)),
        Quests = environment.QuestInfoList.ToDictionary(
            x => x.Index,
            x => new QuestTranslation(
                x.FileName, x.Name, x.Name,
                x.Group, x.Group,
                x.GotoMessage, x.GotoMessage,
                x.KillMessage, x.KillMessage,
                x.ItemMessage, x.ItemMessage,
                x.FlagMessage, x.FlagMessage)),
        QuestScripts = environment.QuestInfoList.ToDictionary(
            x => x.Index,
            x =>
            {
                string path = GetQuestScriptPath(serverDirectory, x.FileName);
                string[] lines = File.ReadAllLines(path);
                string[] description = GetSection(lines, "[@Description]");
                string[] taskDescription = GetSection(lines, "[@TaskDescription]");
                return new QuestScriptTranslation(x.FileName, description, description, taskDescription, taskDescription);
            })
    };

    File.WriteAllText(outputPath, JsonSerializer.Serialize(export, jsonOptions) + Environment.NewLine);
    Console.WriteLine($"Exported {export.Maps.Count} maps, {export.Npcs.Count} NPCs and {export.Quests.Count} quests to {outputPath}");
    return 0;
}

if (args.Length < 3)
{
    Console.Error.WriteLine("The apply command requires a translation JSON path.");
    return 2;
}

string translationPath = documentPath!;
TranslationDocument translations = JsonSerializer.Deserialize<TranslationDocument>(
    File.ReadAllText(translationPath), jsonOptions) ?? throw new InvalidDataException("Translation document is empty.");

List<string> errors = [];
foreach (var map in environment.MapInfoList)
{
    if (!translations.Maps.TryGetValue(map.Index, out MapTranslation? entry))
    {
        errors.Add($"Missing map {map.Index} ({map.FileName}, {map.Title}).");
        continue;
    }
    if (!entry.FileName.Equals(map.FileName, StringComparison.Ordinal) ||
        !MatchesSourceOrTranslation(map.Title, entry.SourceTitle, entry.Title))
        errors.Add($"Map {map.Index} source mismatch.");
}

foreach (var npc in environment.NPCInfoList)
{
    if (!translations.Npcs.TryGetValue(npc.Index, out NpcTranslation? entry))
    {
        errors.Add($"Missing NPC {npc.Index} ({npc.Name}).");
        continue;
    }
    if (!entry.FileName.Equals(npc.FileName, StringComparison.Ordinal) ||
        !MatchesSourceOrTranslation(npc.Name, entry.SourceName, entry.Name))
        errors.Add($"NPC {npc.Index} source mismatch.");
}

foreach (var quest in environment.QuestInfoList)
{
    if (!translations.Quests.TryGetValue(quest.Index, out QuestTranslation? entry))
    {
        errors.Add($"Missing quest {quest.Index} ({quest.Name}).");
        continue;
    }
    if (!entry.FileName.Equals(quest.FileName, StringComparison.Ordinal) ||
        !MatchesSourceOrTranslation(quest.Name, entry.SourceName, entry.Name) ||
        !MatchesSourceOrTranslation(quest.Group, entry.SourceGroup, entry.Group) ||
        !MatchesSourceOrTranslation(quest.GotoMessage, entry.SourceGotoMessage, entry.GotoMessage) ||
        !MatchesSourceOrTranslation(quest.KillMessage, entry.SourceKillMessage, entry.KillMessage) ||
        !MatchesSourceOrTranslation(quest.ItemMessage, entry.SourceItemMessage, entry.ItemMessage) ||
        !MatchesSourceOrTranslation(quest.FlagMessage, entry.SourceFlagMessage, entry.FlagMessage))
        errors.Add($"Quest {quest.Index} source mismatch.");
}

foreach (var quest in environment.QuestInfoList)
{
    if (!translations.QuestScripts.TryGetValue(quest.Index, out QuestScriptTranslation? entry))
    {
        errors.Add($"Missing quest script {quest.Index} ({quest.FileName}).");
        continue;
    }
    string path = GetQuestScriptPath(serverDirectory, quest.FileName);
    if (!entry.FileName.Equals(quest.FileName, StringComparison.Ordinal) || !File.Exists(path))
    {
        errors.Add($"Quest script {quest.Index} path mismatch.");
        continue;
    }
    string[] lines = File.ReadAllLines(path);
    if (!MatchesSourceOrTranslationLines(GetSection(lines, "[@Description]"), entry.SourceDescription, entry.Description) ||
        !MatchesSourceOrTranslationLines(GetSection(lines, "[@TaskDescription]"), entry.SourceTaskDescription, entry.TaskDescription))
        errors.Add($"Quest script {quest.Index} source text mismatch.");
}

if (errors.Count > 0)
{
    foreach (string error in errors.Take(50)) Console.Error.WriteLine(error);
    Console.Error.WriteLine($"Refusing to modify the database: {errors.Count} validation error(s).");
    return 1;
}

foreach (var map in environment.MapInfoList)
    map.Title = RequireTranslation(translations.Maps[map.Index].Title, "map", map.Index);
foreach (var npc in environment.NPCInfoList)
    npc.Name = RequireTranslation(translations.Npcs[npc.Index].Name, "NPC", npc.Index);
foreach (var quest in environment.QuestInfoList)
{
    QuestTranslation entry = translations.Quests[quest.Index];
    quest.Name = RequireTranslation(entry.Name, "quest", quest.Index);
    quest.Group = RequireTranslation(entry.Group, "quest group", quest.Index);
    quest.GotoMessage = entry.GotoMessage;
    quest.KillMessage = entry.KillMessage;
    quest.ItemMessage = entry.ItemMessage;
    quest.FlagMessage = entry.FlagMessage;
}

environment.SaveDB();
string scriptBackupRoot = Path.Combine(serverDirectory, "Back Up", $"Translation Scripts {DateTime.Now:yyyy-MM-dd HH-mm-ss}");
foreach (var quest in environment.QuestInfoList)
{
    QuestScriptTranslation entry = translations.QuestScripts[quest.Index];
    string path = GetQuestScriptPath(serverDirectory, quest.FileName);
    string relativePath = Path.GetRelativePath(serverDirectory, path);
    string backupPath = Path.Combine(scriptBackupRoot, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
    File.Copy(path, backupPath, true);
    string[] lines = File.ReadAllLines(path);
    lines = ReplaceSection(lines, "[@Description]", entry.Description);
    lines = ReplaceSection(lines, "[@TaskDescription]", entry.TaskDescription);
    File.WriteAllLines(path, lines);
}
Console.WriteLine($"Updated {translations.Maps.Count} maps, {translations.Npcs.Count} NPCs, {translations.Quests.Count} quests and {translations.QuestScripts.Count} quest scripts. Backups were created by the server library and under {scriptBackupRoot}.");
return 0;

static bool MatchesSourceOrTranslation(string value, string source, string translation) =>
    value.Equals(source, StringComparison.Ordinal) || value.Equals(translation, StringComparison.Ordinal);

static bool MatchesSourceOrTranslationLines(string[] value, string[] source, string[] translation) =>
    value.SequenceEqual(source) || value.SequenceEqual(translation);

static string RequireTranslation(string value, string kind, int index)
{
    if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"Empty {kind} translation at index {index}.");
    return value;
}

static string GetQuestScriptPath(string serverDirectory, string fileName) =>
    Path.Combine(serverDirectory, "Envir", "Quests", fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar) + ".txt");

static string[] GetSection(string[] lines, string header)
{
    int start = Array.FindIndex(lines, x => x.Equals(header, StringComparison.OrdinalIgnoreCase));
    if (start < 0) throw new InvalidDataException($"Missing script section {header}.");
    int end = Array.FindIndex(lines, start + 1, x => x.StartsWith("[@", StringComparison.Ordinal));
    if (end < 0) end = lines.Length;
    return lines[(start + 1)..end];
}

static string[] ReplaceSection(string[] lines, string header, string[] replacement)
{
    int start = Array.FindIndex(lines, x => x.Equals(header, StringComparison.OrdinalIgnoreCase));
    if (start < 0) throw new InvalidDataException($"Missing script section {header}.");
    int end = Array.FindIndex(lines, start + 1, x => x.StartsWith("[@", StringComparison.Ordinal));
    if (end < 0) end = lines.Length;
    return lines[..(start + 1)].Concat(replacement).Concat(lines[end..]).ToArray();
}

internal sealed class TranslationDocument
{
    public Dictionary<int, MapTranslation> Maps { get; set; } = [];
    public Dictionary<int, NpcTranslation> Npcs { get; set; } = [];
    public Dictionary<int, QuestTranslation> Quests { get; set; } = [];
    public Dictionary<int, QuestScriptTranslation> QuestScripts { get; set; } = [];
}

internal sealed record MapTranslation(string FileName, string SourceTitle, string Title);
internal sealed record NpcTranslation(string FileName, string SourceName, string Name);
internal sealed record QuestTranslation(
    string FileName,
    string SourceName,
    string Name,
    string SourceGroup,
    string Group,
    string SourceGotoMessage,
    string GotoMessage,
    string SourceKillMessage,
    string KillMessage,
    string SourceItemMessage,
    string ItemMessage,
    string SourceFlagMessage,
    string FlagMessage);
internal sealed record QuestScriptTranslation(
    string FileName,
    string[] SourceDescription,
    string[] Description,
    string[] SourceTaskDescription,
    string[] TaskDescription);
