using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Server.Database;

internal sealed class ItemLocalizationEditorStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private JsonObject _root;
    private JsonObject _items;
    private string _itemsPropertyName;

    public string Culture { get; }
    public string FilePath { get; }
    public bool CanEdit { get; private set; }
    public bool IsDirty { get; private set; }
    public string Error { get; private set; } = string.Empty;

    private ItemLocalizationEditorStore(string culture, string filePath)
    {
        Culture = culture;
        FilePath = filePath;
    }

    public static ItemLocalizationEditorStore Load(string culture, string filePath)
    {
        ItemLocalizationEditorStore store = new(culture, filePath);
        store.LoadCore();
        return store;
    }

    public bool TryGetEntry(int index, out string displayName, out string toolTip, out string sourceName)
    {
        displayName = string.Empty;
        toolTip = string.Empty;
        sourceName = string.Empty;

        if (_items?[index.ToString()] is not JsonObject entry) return false;

        displayName = GetString(entry, "displayName");
        toolTip = GetString(entry, "tooltip");
        sourceName = GetString(entry, "sourceName");
        return true;
    }

    public void UpdateEntry(int index, string sourceName, string displayName, string toolTip)
    {
        if (!CanEdit || index <= 0) return;

        sourceName ??= string.Empty;
        displayName ??= string.Empty;
        toolTip ??= string.Empty;

        string key = index.ToString();
        bool existed = _items[key] is JsonObject;
        if (!existed && displayName.Length == 0 && toolTip.Length == 0) return;

        JsonObject entry = _items[key] as JsonObject ?? new JsonObject();
        bool changed = SetString(entry, "sourceName", sourceName);
        changed |= SetString(entry, "displayName", displayName);
        changed |= SetString(entry, "tooltip", toolTip);

        if (!existed)
        {
            _items[key] = entry;
            changed = true;
        }

        IsDirty |= changed;
    }

    public void Save()
    {
        if (!CanEdit || !IsDirty) return;

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(FilePath) ?? ".",
            $"items.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = _root.ToJsonString(WriteOptions) + Environment.NewLine;
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
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
            if (!ItemLocalizationFormat.TryParse(content, Culture, out _, out string validationError))
                throw new InvalidDataException(validationError);

            _root = JsonNode.Parse(content) as JsonObject
                ?? throw new InvalidDataException("The localization root must be a JSON object.");

            _itemsPropertyName = FindPropertyName(_root, "items") ?? "items";
            _items = _root[_itemsPropertyName] as JsonObject
                ?? throw new InvalidDataException("The localization items property must be a JSON object.");
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
        _itemsPropertyName = "items";
        _items = new JsonObject();
        _root = new JsonObject
        {
            ["schemaVersion"] = ItemLocalizationFormat.SchemaVersion,
            ["culture"] = Culture,
            [_itemsPropertyName] = _items
        };
    }

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
        {
            if (name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) return name;
        }

        return null;
    }
}
