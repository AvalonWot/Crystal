using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Server.Database;

internal delegate bool LocalizationDocumentValidator(byte[] content, out string error);

internal sealed class LocalizationEditorStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private readonly string _rootProperty;
    private readonly LocalizationDocumentValidator _validator;
    private JsonObject _root;
    private JsonObject _entries;
    private string _actualRootProperty;

    public string Language { get; }
    public string FilePath { get; }
    public bool CanEdit { get; private set; }
    public bool IsDirty { get; private set; }
    public string Error { get; private set; } = string.Empty;

    private LocalizationEditorStore(string language, string filePath, string rootProperty,
        LocalizationDocumentValidator validator)
    {
        Language = language;
        FilePath = filePath;
        _rootProperty = rootProperty;
        _validator = validator;
    }

    public static LocalizationEditorStore LoadItems(string language, string filePath)
    {
        return Load(language, filePath, "items", ValidateItems);
    }

    public static LocalizationEditorStore LoadMonsters(string language, string filePath)
    {
        return Load(language, filePath, "monsters", ValidateMonsters);
    }

    public static string ResolveEditorLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "English";
        language = language.Trim();
        return language is "." or ".." || language.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
               language.Contains('/') || language.Contains('\\')
            ? "English"
            : language;
    }

    public bool TryGetEntry(int index, out string displayName, out string toolTip, out string sourceName)
    {
        bool found = TryGetEntryCore(index, out JsonObject entry);
        displayName = found ? GetString(entry, "displayName") : string.Empty;
        toolTip = found ? GetString(entry, "tooltip") : string.Empty;
        sourceName = found ? GetString(entry, "sourceName") : string.Empty;
        return found;
    }

    public bool TryGetEntry(int index, out string displayName, out string sourceName)
    {
        bool found = TryGetEntryCore(index, out JsonObject entry);
        displayName = found ? GetString(entry, "displayName") : string.Empty;
        sourceName = found ? GetString(entry, "sourceName") : string.Empty;
        return found;
    }

    public void UpdateEntry(int index, string sourceName, string displayName, string toolTip)
    {
        UpdateEntryCore(index, sourceName, displayName, new Dictionary<string, string> { ["tooltip"] = toolTip ?? string.Empty });
    }

    public void UpdateEntry(int index, string sourceName, string displayName)
    {
        UpdateEntryCore(index, sourceName, displayName, null);
    }

    public void Save()
    {
        if (!CanEdit || !IsDirty) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
        string resourceName = Path.GetFileNameWithoutExtension(FilePath);
        string temporaryPath = Path.Combine(Path.GetDirectoryName(FilePath) ?? ".", $"{resourceName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, _root.ToJsonString(WriteOptions) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporaryPath, FilePath, true);
            IsDirty = false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private static LocalizationEditorStore Load(string language, string filePath, string rootProperty,
        LocalizationDocumentValidator validator)
    {
        LocalizationEditorStore store = new(language, filePath, rootProperty, validator);
        store.LoadCore();
        return store;
    }

    private void LoadCore()
    {
        if (!File.Exists(FilePath))
        {
            CreateEmptyDocument();
            CanEdit = true;
            return;
        }
        try
        {
            byte[] content = File.ReadAllBytes(FilePath);
            if (!_validator(content, out string validationError)) throw new InvalidDataException(validationError);
            _root = JsonNode.Parse(content) as JsonObject ?? throw new InvalidDataException("The localization root must be a JSON object.");
            _actualRootProperty = FindPropertyName(_root, _rootProperty) ?? _rootProperty;
            _entries = _root[_actualRootProperty] as JsonObject ??
                throw new InvalidDataException($"The localization {_rootProperty} property must be a JSON object.");
            CanEdit = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            CanEdit = false;
            Error = ex.Message;
        }
    }

    private void CreateEmptyDocument()
    {
        _actualRootProperty = _rootProperty;
        _entries = new JsonObject();
        _root = new JsonObject
        {
            ["schemaVersion"] = 1,
            [_actualRootProperty] = _entries
        };
    }

    private bool TryGetEntryCore(int index, out JsonObject entry)
    {
        entry = _entries?[index.ToString()] as JsonObject;
        return entry != null;
    }

    private void UpdateEntryCore(int index, string sourceName, string displayName,
        IReadOnlyDictionary<string, string> additionalValues)
    {
        if (!CanEdit || index <= 0) return;
        sourceName ??= string.Empty;
        displayName ??= string.Empty;
        string key = index.ToString();
        bool existed = _entries[key] is JsonObject;
        bool hasAdditionalValue = additionalValues?.Values.Any(value => !string.IsNullOrEmpty(value)) == true;
        if (!existed && displayName.Length == 0 && !hasAdditionalValue) return;

        JsonObject entry = _entries[key] as JsonObject ?? new JsonObject();
        bool changed = SetString(entry, "sourceName", sourceName);
        changed |= SetString(entry, "displayName", displayName);
        if (additionalValues != null)
            foreach ((string name, string value) in additionalValues) changed |= SetString(entry, name, value ?? string.Empty);
        if (!existed)
        {
            _entries[key] = entry;
            changed = true;
        }
        IsDirty |= changed;
    }

    private static bool ValidateItems(byte[] content, out string error) =>
        ItemLocalizationFormat.TryParse(content, out _, out error);
    private static bool ValidateMonsters(byte[] content, out string error) =>
        MonsterLocalizationFormat.TryParse(content, out _, out error);

    private static string GetString(JsonObject owner, string propertyName)
    {
        string actualName = FindPropertyName(owner, propertyName);
        return actualName == null ? string.Empty : owner[actualName]?.GetValue<string>() ?? string.Empty;
    }

    private static bool SetString(JsonObject owner, string propertyName, string value)
    {
        string actualName = FindPropertyName(owner, propertyName) ?? propertyName;
        string current = owner[actualName]?.GetValue<string>() ?? string.Empty;
        if (current.Equals(value, StringComparison.Ordinal)) return false;
        owner[actualName] = value;
        return true;
    }

    private static string FindPropertyName(JsonObject owner, string propertyName)
    {
        foreach ((string name, _) in owner)
            if (name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) return name;
        return null;
    }
}
