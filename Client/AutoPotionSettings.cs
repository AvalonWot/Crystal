namespace Client
{
    internal sealed class AutoPotionRule
    {
        public int Percent { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public int Order { get; init; }
    }

    internal static class AutoPotionSettings
    {
        private const string FileName = @".\AutoTool.ini";

        public static IReadOnlyList<AutoPotionRule> HPRules { get; private set; } = Array.Empty<AutoPotionRule>();
        public static IReadOnlyList<AutoPotionRule> MPRules { get; private set; } = Array.Empty<AutoPotionRule>();

        public static void Load()
        {
            InIReader reader = new InIReader(FileName);
            HPRules = ParseRules(reader.ReadString("AutoPotion", "HP", string.Empty, false), "HP");
            MPRules = ParseRules(reader.ReadString("AutoPotion", "MP", string.Empty, false), "MP");
        }

        private static IReadOnlyList<AutoPotionRule> ParseRules(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<AutoPotionRule>();

            List<AutoPotionRule> rules = new();
            string[] entries = value.Split(';');

            for (int index = 0; index < entries.Length; index++)
            {
                string entry = entries[index].Trim();
                if (entry.Length == 0) continue;

                int separator = entry.IndexOf(':');
                if (separator <= 0 || separator == entry.Length - 1 ||
                    !int.TryParse(entry.Substring(0, separator).Trim(), out int percent) ||
                    percent < 1 || percent > 100)
                {
                    CMain.SaveError($"Invalid AutoTool.ini AutoPotion {key} entry: {entry}");
                    continue;
                }

                string itemName = entry.Substring(separator + 1).Trim();
                if (itemName.Length == 0)
                {
                    CMain.SaveError($"Invalid AutoTool.ini AutoPotion {key} entry: {entry}");
                    continue;
                }

                rules.Add(new AutoPotionRule { Percent = percent, ItemName = itemName, Order = index });
            }

            return rules.OrderBy(rule => rule.Percent).ThenBy(rule => rule.Order).ToArray();
        }
    }
}
