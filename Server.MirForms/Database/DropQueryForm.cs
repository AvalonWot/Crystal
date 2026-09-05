using Server.MirDatabase;
using Server.MirEnvir;
using System.ComponentModel;
using System.Diagnostics;

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

    private void ResultsGrid_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
    {
        if (e.Column != DropRateColumn) return;

        decimal first = (decimal)ResultsGrid.Rows[e.RowIndex1].Cells[DropRateColumn.Index].Tag;
        decimal second = (decimal)ResultsGrid.Rows[e.RowIndex2].Cells[DropRateColumn.Index].Tag;
        e.SortResult = first.CompareTo(second);
        if (e.SortResult == 0)
        {
            e.SortResult = StringComparer.OrdinalIgnoreCase.Compare(
                ResultsGrid.Rows[e.RowIndex1].Cells[MonsterNameColumn.Index].Value as string,
                ResultsGrid.Rows[e.RowIndex2].Cells[MonsterNameColumn.Index].Value as string);
        }
        e.Handled = true;
    }

    private void ResultsGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        string path = ResultsGrid.Rows[e.RowIndex].Cells[DropFilePathColumn.Index].Value as string;
        if (string.IsNullOrWhiteSpace(path)) return;

        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"Drops file not found:\n{path}", "Drop Query",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Unable to open drops file:\n{path}\n\n{ex.Message}", "Drop Query",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
        HashSet<int> matchingItemIndexes = matchingItems.Select(item => item.Index).ToHashSet();
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
            int rowIndex = ResultsGrid.Rows.Add(
                result.Monster.Name,
                GetMonsterTranslation(result.Monster),
                GetDropRates(result.Monster, matchingItemIndexes),
                result.DropFilePath);
            // A row can contain several direct drop entries; sort by its highest chance.
            ResultsGrid.Rows[rowIndex].Cells[DropRateColumn.Index].Tag = result.Monster.Drops
                .Where(drop => drop?.Item != null && matchingItemIndexes.Contains(drop.Item.Index))
                .Select(drop => 1m / Math.Max(1, drop.Chance))
                .DefaultIfEmpty(0m)
                .Max();
        }

        if (ResultsGrid.SortedColumn != null)
            ResultsGrid.Sort(ResultsGrid.SortedColumn, ResultsGrid.SortOrder == SortOrder.Descending
                ? ListSortDirection.Descending : ListSortDirection.Ascending);

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

    private static string GetDropRates(MonsterInfo monster, HashSet<int> itemIndexes)
    {
        return string.Join(Environment.NewLine, monster.Drops
            .Where(drop => drop?.Item != null && itemIndexes.Contains(drop.Item.Index))
            .Select(drop =>
            {
                int denominator = Math.Max(1, drop.Chance);
                string itemName = itemIndexes.Count > 1 ? $"{drop.Item.Name}: " : string.Empty;
                string quest = drop.QuestRequired ? " [Quest]" : string.Empty;
                return $"{itemName}1/{denominator} ({100m / denominator:0.########}%){quest}";
            }));
    }

    private string GetMonsterTranslation(MonsterInfo monster)
    {
        return _monsterLocalization.TryGetEntry(monster.Index, out string displayName, out string sourceName) &&
               sourceName.Equals(monster.Name, StringComparison.Ordinal)
            ? displayName
            : string.Empty;
    }
}
