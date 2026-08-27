using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class ItemLocalizationDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Culture { get; set; } = string.Empty;
    public Dictionary<int, ItemLocalizationEntry> Items { get; set; } = new();
}

public sealed class ItemLocalizationEntry
{
    public string SourceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ToolTip { get; set; } = string.Empty;
}

public static class ItemLocalizationFormat
{
    public const int SchemaVersion = 1;
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxItems = 100_000;
    public const int MaxNameLength = 256;
    public const int MaxToolTipLength = 8_192;

    private static readonly Regex CulturePattern = new(
        @"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32
    };

    public static string NormalizeCulture(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return string.Empty;

        culture = culture.Trim();
        if (culture.Equals("Chinese", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (culture.Equals("English", StringComparison.OrdinalIgnoreCase)) return "en-US";
        if (!CulturePattern.IsMatch(culture)) return string.Empty;

        try
        {
            return CultureInfo.GetCultureInfo(culture).Name;
        }
        catch (CultureNotFoundException)
        {
            return string.Empty;
        }
    }

    public static bool TryParse(byte[] bytes, string expectedCulture, out ItemLocalizationDocument document, out string error)
    {
        document = null;
        error = string.Empty;

        if (bytes == null || bytes.Length == 0)
        {
            error = "The localization file is empty.";
            return false;
        }

        if (bytes.Length > MaxFileBytes)
        {
            error = $"The localization file exceeds {MaxFileBytes} bytes.";
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<ItemLocalizationDocument>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }

        if (document == null)
        {
            error = "The localization document is null.";
            return false;
        }

        if (document.SchemaVersion != SchemaVersion)
        {
            error = $"Unsupported schema version {document.SchemaVersion}.";
            return false;
        }

        string normalizedCulture = NormalizeCulture(document.Culture);
        string normalizedExpectedCulture = NormalizeCulture(expectedCulture);
        if (normalizedCulture.Length == 0 ||
            normalizedExpectedCulture.Length == 0 ||
            !normalizedCulture.Equals(normalizedExpectedCulture, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Culture '{document.Culture}' does not match '{expectedCulture}'.";
            return false;
        }

        document.Culture = normalizedCulture;
        document.Items ??= new Dictionary<int, ItemLocalizationEntry>();

        if (document.Items.Count > MaxItems)
        {
            error = $"The localization document contains more than {MaxItems} items.";
            return false;
        }

        foreach ((int index, ItemLocalizationEntry entry) in document.Items)
        {
            if (index <= 0 || entry == null)
            {
                error = $"Invalid item entry at index {index}.";
                return false;
            }

            entry.SourceName ??= string.Empty;
            entry.DisplayName ??= string.Empty;
            entry.ToolTip ??= string.Empty;

            if (entry.SourceName.Length > MaxNameLength || entry.DisplayName.Length > MaxNameLength)
            {
                error = $"Item {index} contains a name longer than {MaxNameLength} characters.";
                return false;
            }

            if (entry.ToolTip.Length > MaxToolTipLength)
            {
                error = $"Item {index} contains a tooltip longer than {MaxToolTipLength} characters.";
                return false;
            }
        }

        return true;
    }
}
