namespace Server.Database;

internal static class NameSearchFilter
{
    // Treat search text as a literal substring in DataView LIKE expressions.
    public static string Escape(string text)
    {
        var result = new System.Text.StringBuilder();
        foreach (char character in text)
        {
            result.Append(character switch
            {
                '\'' => "''",
                '[' => "[[]",
                ']' => "[]]",
                '%' => "[%]",
                '*' => "[*]",
                _ => character.ToString()
            });
        }
        return result.ToString();
    }
}
