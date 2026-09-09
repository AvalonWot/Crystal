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
        public static IReadOnlyList<AutoPotionRule> HPRules { get; private set; } = [];
        public static IReadOnlyList<AutoPotionRule> MPRules { get; private set; } = [];
        public static bool AutoMagicShield { get; private set; }

        public static void Load(string filePath)
        {
            InIReader reader = new InIReader(filePath);
            HPRules = ParseRules(reader.ReadString("AutoPotion", "HP", string.Empty, false), "HP");
            MPRules = ParseRules(reader.ReadString("AutoPotion", "MP", string.Empty, false), "MP");
            AutoMagicShield = reader.ReadBoolean("AutoMagic", "MagicShield", false);
        }

        public static void Clear()
        {
            HPRules = [];
            MPRules = [];
            AutoMagicShield = false;
        }

        private static IReadOnlyList<AutoPotionRule> ParseRules(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value)) return [];

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
