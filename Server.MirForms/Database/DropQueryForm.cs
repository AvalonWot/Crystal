using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Database;

public partial class DropQueryForm : Form
{
    private Envir Envir => SMain.Envir;

    private readonly LocalizationEditorStore _itemLocalization;
    private readonly LocalizationEditorStore _monsterLocalization;

    public DropQueryForm()
    {
        InitializeComponent();

        string language = LocalizationEditorStore.ResolveEditorLanguage(Settings.Language);
        string languageDirectory = Path.GetFullPath(Path.Combine(Settings.LocalizationDirectory, language));
        _itemLocalization = LocalizationEditorStore.LoadItems(language, Path.Combine(languageDirectory, "items.json"));
        _monsterLocalization = LocalizationEditorStore.LoadMonsters(language, Path.Combine(languageDirectory, "monsters.json"));

        Text = $"Drop Query [{language}]";
        SearchTextBox.Select();
    }

    private void QueryButton_Click(object sender, EventArgs e)
    {
        RunQuery();
    }

    private void RunQuery()
    {
        ResultsGrid.Rows.Clear();

        if (!Envir.Running)
        {
            StatusLabel.Text = "The server is not running.";
            return;
        }

        if (!Envir.MonsterDropSearchReady)
        {
            StatusLabel.Text = "Monster drops have not finished loading.";
            return;
        }

        string query = SearchTextBox.Text.Trim();
        if (query.Length == 0)
        {
            StatusLabel.Text = "Enter an item name.";
            return;
        }

        List<ItemInfo> matchingItems = Envir.ItemInfoList
            .Where(item => ItemNameMatches(item, query))
            .ToList();

        if (matchingItems.Count == 0)
        {
            StatusLabel.Text = "No matching item was found.";
            return;
        }

        Dictionary<int, MonsterInfo> monsters = new();
        foreach (ItemInfo item in matchingItems)
        {
            foreach (MonsterInfo monster in Envir.FindMonstersDroppingItem(item.Index))
                monsters.TryAdd(monster.Index, monster);
        }

        var results = monsters.Values
            .Select(monster => new
            {
                Monster = monster,
                DropFilePath = Path.GetFullPath(Envir.GetMonsterDropFilePath(monster))
            })
            .OrderBy(result => result.Monster.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.DropFilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var result in results)
        {
            ResultsGrid.Rows.Add(
                result.Monster.Name,
                GetMonsterTranslation(result.Monster),
                result.DropFilePath);
        }

        StatusLabel.Text = results.Count == 0
            ? "No monsters have this item as a direct drop."
            : $"Results: {results.Count}";
    }

    private bool ItemNameMatches(ItemInfo item, string query)
    {
        if (item.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return true;

        return _itemLocalization.TryGetEntry(item.Index, out string displayName, out _, out string sourceName) &&
               sourceName.Equals(item.Name, StringComparison.Ordinal) &&
               displayName.Equals(query, StringComparison.OrdinalIgnoreCase);
    }

    private string GetMonsterTranslation(MonsterInfo monster)
    {
        return _monsterLocalization.TryGetEntry(monster.Index, out string displayName, out string sourceName) &&
               sourceName.Equals(monster.Name, StringComparison.Ordinal)
            ? displayName
            : string.Empty;
    }
}
