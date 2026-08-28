using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using Server.MirEnvir;

namespace Server.Database
{
    public partial class ItemInfoFormNew : Form
    {
        public Envir Envir => SMain.EditEnvir;

        private readonly Array StatEnums = Enum.GetValues(typeof(Stat));
        private readonly Array BindEnums = Enum.GetValues(typeof(BindMode));
        private readonly Array SpecialEnums = Enum.GetValues(typeof(SpecialItemMode));

        private bool _isInGemContext = false;
        private Dictionary<int, string> _defaultItemHeaderMappings = new();
        private Dictionary<int, string> _gemItemHeaderMappings = new();

        private DataTable Table;
        private ItemLocalizationEditorStore _localizationStore;
        private MirLibraryPreviewProvider _previewProvider;
        private DataGridViewImageColumn _itemPreviewColumn;
        private DataGridViewTextBoxColumn _translatedNameColumn;
        private DataGridViewTextBoxColumn _translatedToolTipColumn;
        private Button _selectItemLibraryButton;
        private Label _translationCultureLabel;
        private TextBox _itemLibraryPathTextBox;
        private Bitmap _unavailablePreview;
        private bool _localizationDirty;

        public ItemInfoFormNew()
        {
            InitializeComponent();

            SetDoubleBuffered(itemInfoGridView);

            InitializeLocalizationEditor();

            InitializeItemInfoFilters();
            InitializeItemInfoGridView();

            CreateDynamicColumns();

            PopulateTable();

            MapHeaderText();

            LoadDropRecipeQuestFiles();

            // register after initializing data to prevent erroneous throws
            itemInfoGridView.CellValueChanged += CellValueChanged;
            itemInfoGridView.CellValidating += itemInfoGridView_CellValidating;
            itemInfoGridView.MouseClick += ItemInfoGridView_MouseClick;
            itemInfoGridView.SelectionChanged += ItemInfoGridView_SelectionChanged;
            itemInfoGridView.CellBeginEdit += ItemInfoGridView_CellBeginEdit;
            itemInfoGridView.CellEndEdit += ItemInfoGridView_CellEndEdit;
            itemInfoGridView.CellFormatting += ItemInfoGridView_CellFormatting;
        }

        private void InitializeLocalizationEditor()
        {
            string culture = ResolveEditorCulture();
            string localizationPath = Path.GetFullPath(Path.Combine(Settings.LocalizationDirectory, culture, "items.json"));
            _localizationStore = ItemLocalizationEditorStore.Load(culture, localizationPath);
            _unavailablePreview = MirLibraryPreviewProvider.CreateErrorImage();

            _itemPreviewColumn = new DataGridViewImageColumn
            {
                Name = "ItemPreview",
                HeaderText = "Image",
                ReadOnly = true,
                Frozen = true,
                Width = 52,
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { NullValue = _unavailablePreview }
            };
            itemInfoGridView.Columns.Insert(0, _itemPreviewColumn);

            _translatedNameColumn = new DataGridViewTextBoxColumn
            {
                Name = "ItemTranslatedName",
                DataPropertyName = "ItemTranslatedName",
                HeaderText = $"Translated Name [{culture}]",
                ValueType = typeof(string),
                Frozen = true,
                Width = 170,
                ReadOnly = !_localizationStore.CanEdit
            };
            itemInfoGridView.Columns.Insert(itemInfoGridView.Columns["ItemName"].Index + 1, _translatedNameColumn);

            _translatedToolTipColumn = new DataGridViewTextBoxColumn
            {
                Name = "ItemTranslatedToolTip",
                DataPropertyName = "ItemTranslatedToolTip",
                HeaderText = $"Translated ToolTip [{culture}]",
                ValueType = typeof(string),
                Width = 220,
                ReadOnly = !_localizationStore.CanEdit,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            };
            itemInfoGridView.Columns.Insert(itemInfoGridView.Columns["ItemToolTip"].Index + 1, _translatedToolTipColumn);
            itemInfoGridView.RowTemplate.Height = 48;

            panel1.Height = 84;
            _translationCultureLabel = new Label
            {
                AutoSize = true,
                Location = new Point(8, 62),
                Text = $"Translation: {culture}"
            };
            _selectItemLibraryButton = new Button
            {
                Location = new Point(150, 56),
                Size = new Size(125, 25),
                Text = "Select Item Lib...",
                UseVisualStyleBackColor = true
            };
            _selectItemLibraryButton.Click += SelectItemLibraryButton_Click;
            _itemLibraryPathTextBox = new TextBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(282, 57),
                ReadOnly = true,
                Size = new Size(Math.Max(100, panel3.ClientSize.Width - 290), 23),
                Text = Settings.ItemEditorLibraryPath
            };
            panel3.Controls.Add(_translationCultureLabel);
            panel3.Controls.Add(_selectItemLibraryButton);
            panel3.Controls.Add(_itemLibraryPathTextBox);

            if (!_localizationStore.CanEdit)
            {
                MessageBox.Show(
                    $"Unable to edit localization file:{Environment.NewLine}{localizationPath}{Environment.NewLine}{Environment.NewLine}{_localizationStore.Error}",
                    "Item Localization", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (!string.IsNullOrWhiteSpace(Settings.ItemEditorLibraryPath))
                TryLoadItemLibrary(Settings.ItemEditorLibraryPath, false);
        }

        private static string ResolveEditorCulture()
        {
            string culture = ItemLocalizationFormat.NormalizeCulture(Settings.Language);
            if (culture.Length == 0)
                culture = ItemLocalizationFormat.NormalizeCulture(Settings.LocalizationDefaultCulture);
            return culture.Length == 0 ? "en-US" : culture;
        }

        private void SelectItemLibraryButton_Click(object sender, EventArgs e)
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "Mir Library (*.Lib)|*.Lib|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(Settings.ItemEditorLibraryPath))
            {
                string fullPath = Path.GetFullPath(Settings.ItemEditorLibraryPath);
                dialog.InitialDirectory = Path.GetDirectoryName(fullPath);
                dialog.FileName = Path.GetFileName(fullPath);
            }

            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            TryLoadItemLibrary(dialog.FileName, true);
        }

        private bool TryLoadItemLibrary(string path, bool saveSetting)
        {
            try
            {
                MirLibraryPreviewProvider replacement = new(path);
                MirLibraryPreviewProvider previous = _previewProvider;
                _previewProvider = replacement;
                previous?.Dispose();

                _itemLibraryPathTextBox.Text = replacement.FilePath;
                itemInfoGridView.InvalidateColumn(_itemPreviewColumn.Index);

                if (saveSetting)
                {
                    Settings.ItemEditorLibraryPath = replacement.FilePath;
                    Settings.Save();
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
            {
                _itemLibraryPathTextBox.Text = path;
                MessageBox.Show($"Unable to load item library:{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    "Item Library", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }



        public static void SetDoubleBuffered(Control c)
        {
            System.Reflection.PropertyInfo aProp =
                  typeof(Control).GetProperty(
                        "DoubleBuffered",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

            aProp.SetValue(c, true, null);
        }

        private void InitializeItemInfoFilters()
        {
            Dictionary<String, String> filterItems = new()
            {
                { "-1", "" }
            };

            var types = Enum.GetValues(typeof(ItemType));
            foreach (ItemType type in types)
            {
                filterItems.Add(((byte)type).ToString(), type.ToString());
            }

            drpFilterType.DataSource = new BindingSource(filterItems, null);
            drpFilterType.DisplayMember = "Value";
            drpFilterType.ValueMember = "Key";
        }

        private void InitializeItemInfoGridView()
        {
            Modified.ValueType = typeof(bool);
            ItemIndex.ValueType = typeof(int);
            ItemName.ValueType = typeof(string);
            ItemRandomStatsId.ValueType = typeof(byte);
            ItemRequiredAmount.ValueType = typeof(byte);
            ItemImage.ValueType = typeof(ushort);
            ItemShape.ValueType = typeof(short);
            ItemEffect.ValueType = typeof(byte);
            ItemStackSize.ValueType = typeof(ushort);
            ItemSlots.ValueType = typeof(byte);
            ItemWeight.ValueType = typeof(byte);

            ItemLightIntensity.ValueType = typeof(byte);
            ItemLightRange.ValueType = typeof(byte);
            ItemDurability.ValueType = typeof(ushort);
            ItemPrice.ValueType = typeof(uint);
            ItemToolTip.ValueType = typeof(string);

            StartItem.ValueType = typeof(bool);
            NeedIdentify.ValueType = typeof(bool);
            ShowGroupPickup.ValueType = typeof(bool);
            GlobalDropNotify.ValueType = typeof(bool);
            ClassBased.ValueType = typeof(bool);
            LevelBased.ValueType = typeof(bool);
            CanMine.ValueType = typeof(bool);
            CanFastRun.ValueType = typeof(bool);
            CanAwakening.ValueType = typeof(bool);

            //Basic
            ItemType.ValueType = typeof(ItemType);
            ItemType.DataSource = Enum2DataTable<ItemType>();
            ItemType.ValueMember = "Value";
            ItemType.DisplayMember = "Display";

            ItemGrade.ValueType = typeof(ItemGrade);
            ItemGrade.DataSource = Enum2DataTable<ItemGrade>();
            ItemGrade.ValueMember = "Value";
            ItemGrade.DisplayMember = "Display";

            ItemRequiredType.ValueType = typeof(RequiredType);
            ItemRequiredType.DataSource = Enum2DataTable<RequiredType>();
            ItemRequiredType.ValueMember = "Value";
            ItemRequiredType.DisplayMember = "Display";

            ItemRequiredGender.ValueType = typeof(RequiredGender);
            ItemRequiredGender.DataSource = Enum2DataTable<RequiredGender>();
            ItemRequiredGender.ValueMember = "Value";
            ItemRequiredGender.DisplayMember = "Display";

            ItemRequiredClass.ValueType = typeof(RequiredClass);
            ItemRequiredClass.DataSource = Enum2DataTable<RequiredClass>();
            ItemRequiredClass.ValueMember = "Value";
            ItemRequiredClass.DisplayMember = "Display";

            ItemSet.ValueType = typeof(ItemSet);
            ItemSet.DataSource = Enum2DataTable<ItemSet>();
            ItemSet.ValueMember = "Value";
            ItemSet.DisplayMember = "Display";
        }

        public static DataTable Enum2DataTable<T>()
        {
            DataTable enumTable = new DataTable();
            enumTable.Columns.Add(new DataColumn("Value", Enum.GetUnderlyingType(typeof(T))));
            enumTable.Columns.Add(new DataColumn("Display", typeof(string)));
            DataRow EnumRow;

            foreach (T E in Enum.GetValues(typeof(T)))
            {
                EnumRow = enumTable.NewRow();
                EnumRow["Value"] = E;
                EnumRow["Display"] = E.ToString();
                enumTable.Rows.Add(EnumRow);
            }

            return enumTable;
        }

        private void CreateDynamicColumns()
        {
            foreach (Stat stat in StatEnums)
            {
                if (stat == Stat.Unknown) continue;

                var key = stat.ToString();
                var strKey = RegexFunctions.SeperateCamelCase(key.Replace("Rate", "").Replace("Multiplier", "").Replace("Percent", ""));

                var sign = "";

                if (key.Contains("Percent"))
                    sign = "%";
                else if (key.Contains("Multiplier"))
                    sign = "x";

                var col = new DataGridViewTextBoxColumn
                {
                    HeaderText = $"{strKey} {sign}",
                    Name = "Stat" + stat.ToString(),
                    ValueType = typeof(int),
                    DataPropertyName = "Stat" + stat.ToString()
                };

                itemInfoGridView.Columns.Add(col);
            }

            foreach (BindMode bind in BindEnums)
            {
                if (bind == BindMode.None) continue;

                var col = new DataGridViewCheckBoxColumn
                {
                    HeaderText = bind.ToString(),
                    Name = "Bind" + bind.ToString(),
                    ValueType = typeof(bool),
                    DataPropertyName = "Bind" + bind.ToString()
                };

                itemInfoGridView.Columns.Add(col);
            }

            foreach (SpecialItemMode special in SpecialEnums)
            {
                if (special == SpecialItemMode.None) continue;

                var col = new DataGridViewCheckBoxColumn
                {
                    HeaderText = special.ToString(),
                    Name = "Special" + special.ToString(),
                    ValueType = typeof(bool),
                    DataPropertyName = "Special" + special.ToString()
                };

                itemInfoGridView.Columns.Add(col);
            }
        }

        private void PopulateTable()
        {
            Table = new DataTable("itemInfo");

            foreach (DataGridViewColumn col in itemInfoGridView.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.DataPropertyName)) continue;
                Table.Columns.Add(col.DataPropertyName, col.ValueType);
            }

            foreach (ItemInfo item in Envir.ItemInfoList)
            {
                DataRow row = Table.NewRow();

                row["Modified"] = false;

                row["ItemIndex"] = item.Index;
                row["ItemName"] = item.Name;
                if (_localizationStore.TryGetEntry(item.Index, out string translatedName, out string translatedToolTip, out string localizedSourceName))
                {
                    row["ItemTranslatedName"] = translatedName;
                    row["ItemTranslatedToolTip"] = translatedToolTip;
                    if (!localizedSourceName.Equals(item.Name, StringComparison.Ordinal))
                        _localizationDirty = true;
                }
                else
                {
                    row["ItemTranslatedName"] = string.Empty;
                    row["ItemTranslatedToolTip"] = string.Empty;
                }

                row["ItemType"] = item.Type;
                row["ItemGrade"] = item.Grade;
                row["ItemRequiredType"] = item.RequiredType;
                row["ItemRequiredGender"] = item.RequiredGender;
                row["ItemRequiredClass"] = item.RequiredClass;
                row["ItemSet"] = item.Set;
                row["ItemRandomStatsId"] = item.RandomStatsId;
                row["ItemRequiredAmount"] = item.RequiredAmount;
                row["ItemImage"] = item.Image;
                row["ItemShape"] = item.Shape;
                row["ItemEffect"] = item.Effect;
                row["ItemStackSize"] = item.StackSize;
                row["ItemSlots"] = item.Slots;
                row["ItemWeight"] = item.Weight;
                row["ItemLightRange"] = (byte)(item.Light % 15);
                row["ItemLightIntensity"] = (byte)(item.Light / 15);
                row["ItemDurability"] = item.Durability;
                row["ItemPrice"] = item.Price;
                row["ItemToolTip"] = item.ToolTip;

                row["StartItem"] = item.StartItem;
                row["NeedIdentify"] = item.NeedIdentify;
                row["ShowGroupPickup"] = item.ShowGroupPickup;
                row["GlobalDropNotify"] = item.GlobalDropNotify;
                row["ClassBased"] = item.ClassBased;
                row["LevelBased"] = item.LevelBased;
                row["CanMine"] = item.CanMine;
                row["CanFastRun"] = item.CanFastRun;
                row["CanAwakening"] = item.CanAwakening;

                foreach (Stat stat in StatEnums)
                {
                    if (stat == Stat.Unknown) continue;

                    row["Stat" + stat.ToString()] = item.Stats[stat];
                }

                foreach (BindMode bind in BindEnums)
                {
                    if (bind == BindMode.None) continue;

                    row["Bind" + bind.ToString()] = item.Bind.HasFlag(bind);
                }

                foreach (SpecialItemMode special in SpecialEnums)
                {
                    if (special == SpecialItemMode.None) continue;

                    row["Special" + special.ToString()] = item.Unique.HasFlag(special);
                }

                Table.Rows.Add(row);
            }

            itemInfoGridView.DataSource = Table;

            itemInfoGridView.Columns["Modified"].ReadOnly = true;
        }

        private void UpdateFilter()
        {
            if (itemInfoGridView.DataSource == null)
            {
                return;
            }

            var filterText = txtSearch.Text;
            var filterType = ((KeyValuePair<string, string>)drpFilterType.SelectedItem).Key;

            if (string.IsNullOrEmpty(filterText) &&
                filterType == "-1")
            {
                (itemInfoGridView.DataSource as DataTable).DefaultView.RowFilter = string.Empty;
                return;
            }

            string rowFilter = string.Format("([ItemType] = '{0}' OR '{0}' = -1) AND [ItemName] LIKE '%{1}%'", filterType, filterText);

            (itemInfoGridView.DataSource as DataTable).DefaultView.RowFilter = rowFilter;
        }

        private void SaveForm()
        {
            int lastIndex = 0;
            if (Envir.ItemInfoList.Count > 0)
            {
                lastIndex = Envir.ItemInfoList.Max(x => x.Index);
            }

            foreach (DataGridViewRow row in itemInfoGridView.Rows)
            {
                var name = row.Cells["ItemName"].Value;

                if (name == null || name.GetType() == typeof(System.DBNull) || string.IsNullOrWhiteSpace((string)name))
                {
                    continue;
                }

                ItemInfo item;

                if (string.IsNullOrEmpty((string)row.Cells["ItemIndex"].FormattedValue))
                {
                    Envir.ItemInfoList.Add(item = new ItemInfo());

                    item.Index = ++lastIndex;
                    row.Cells["ItemIndex"].Value = item.Index;
                }
                else
                {
                    int index = (int)row.Cells["ItemIndex"].Value;

                    item = Envir.ItemInfoList.FirstOrDefault(x => x.Index == index);

                    if (row.Cells["Modified"].Value != null && (bool)row.Cells["Modified"].Value == false) continue;
                }

                item.Name = (string)row.Cells["ItemName"].Value;
                item.Type = (ItemType)row.Cells["ItemType"].Value;
                item.Grade = (ItemGrade)row.Cells["ItemGrade"].Value;
                item.RequiredType = (RequiredType)row.Cells["ItemRequiredType"].Value;
                item.RequiredGender = (RequiredGender)row.Cells["ItemRequiredGender"].Value;
                item.RequiredClass = (RequiredClass)row.Cells["ItemRequiredClass"].Value;
                item.Set = (ItemSet)row.Cells["ItemSet"].Value;
                item.RandomStatsId = (byte)row.Cells["ItemRandomStatsId"].Value;
                item.RequiredAmount = (byte)row.Cells["ItemRequiredAmount"].Value;
                item.Image = (ushort)row.Cells["ItemImage"].Value;
                item.Shape = (short)row.Cells["ItemShape"].Value;
                item.Effect = (byte)row.Cells["ItemEffect"].Value;
                item.StackSize = (ushort)row.Cells["ItemStackSize"].Value;
                item.Slots = (byte)row.Cells["ItemSlots"].Value;
                item.Weight = (byte)row.Cells["ItemWeight"].Value;

                item.StartItem = (bool)row.Cells["StartItem"].Value;
                item.NeedIdentify = (bool)row.Cells["NeedIdentify"].Value;
                item.ShowGroupPickup = (bool)row.Cells["ShowGroupPickup"].Value;
                item.GlobalDropNotify = (bool)row.Cells["GlobalDropNotify"].Value;
                item.ClassBased = (bool)row.Cells["ClassBased"].Value;
                item.LevelBased = (bool)row.Cells["LevelBased"].Value;
                item.CanMine = (bool)row.Cells["CanMine"].Value;
                item.CanFastRun = (bool)row.Cells["CanFastRun"].Value;
                item.CanAwakening = (bool)row.Cells["CanAwakening"].Value;

                var light = ((byte)row.Cells["ItemLightRange"].Value % 15) + ((byte)row.Cells["ItemLightIntensity"].Value * 15);
                item.Light = (byte)Math.Min(byte.MaxValue, light);
                item.Durability = (ushort)row.Cells["ItemDurability"].Value;
                item.Price = (uint)row.Cells["ItemPrice"].Value;
                item.ToolTip = row.Cells["ItemToolTip"].Value.ToString();

                item.Stats.Clear();
                item.Bind = BindMode.None;
                item.Unique = SpecialItemMode.None;

                foreach (DataGridViewColumn col in itemInfoGridView.Columns)
                {
                    if (col.Name.StartsWith("Stat"))
                    {
                        var stat = col.Name.Substring(4);

                        Stat enumStat = (Stat)Enum.Parse(typeof(Stat), stat);

                        item.Stats[enumStat] = (int)row.Cells[col.Name].Value;
                    }
                    else if (col.Name.StartsWith("Bind"))
                    {
                        var bind = col.Name.Substring(4);

                        BindMode enumBind = (BindMode)Enum.Parse(typeof(BindMode), bind);

                        if ((bool)row.Cells[col.Name].Value)
                        {
                            item.Bind |= enumBind;
                        }
                    }
                    else if (col.Name.StartsWith("Special"))
                    {
                        var special = col.Name.Substring(7);

                        SpecialItemMode enumSpecial = (SpecialItemMode)Enum.Parse(typeof(SpecialItemMode), special);

                        if ((bool)row.Cells[col.Name].Value)
                        {
                            item.Unique |= enumSpecial;
                        }
                    }
                }
            }
            SaveDropRecipeQuestFiles();
        }

        private DataRow FindRowByItemName(string value)
        {
            foreach (DataRow row in Table.Rows)
            {
                var val = row["ItemName"];

                if (val?.ToString().Equals(value) ?? false)
                {
                    return row;
                }
            }

            return null;
        }
        private DataRow FindRowByItemIndex(string value)
        {
            foreach (DataRow row in Table.Rows)
            {
                var val = row["ItemIndex"];

                if (val?.ToString().Equals(value) ?? false)
                {
                    return row;
                }
            }

            return null;
        }

        private void itemInfoGridView_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var col = itemInfoGridView.Columns[e.ColumnIndex];

            if (col.Name.Equals("ItemPreview", StringComparison.OrdinalIgnoreCase) ||
                col.Name.Equals("Modified", comparisonType: StringComparison.CurrentCultureIgnoreCase) ||
                col.Name.Equals("ItemIndex", comparisonType: StringComparison.CurrentCultureIgnoreCase))
            {
                return;
            }

            var cell = itemInfoGridView.Rows[e.RowIndex].Cells[col.Name];

            var val = e.FormattedValue?.ToString() ?? string.Empty;

            itemInfoGridView.Rows[e.RowIndex].ErrorText = "";

            if (col.Name == "ItemTranslatedName" || col.Name == "ItemTranslatedToolTip")
            {
                int maximumLength = col.Name == "ItemTranslatedName"
                    ? ItemLocalizationFormat.MaxNameLength
                    : ItemLocalizationFormat.MaxToolTipLength;
                if (val.Length > maximumLength)
                {
                    e.Cancel = true;
                    itemInfoGridView.Rows[e.RowIndex].ErrorText =
                        $"{col.HeaderText} cannot exceed {maximumLength} characters.";
                }
                else
                {
                    _localizationDirty = true;
                }
                return;
            }

            if (cell.OwningColumn.Name == "ItemName")
            {
                var existingRow = FindRowByItemName(val);
                if (existingRow != null)
                {
                    var existingIndex = existingRow["ItemIndex"].ToString();
                    var currentIndex = itemInfoGridView.Rows[e.RowIndex].Cells["ItemIndex"].Value?.ToString() ?? "";
                    if (existingIndex != currentIndex)
                    {
                        e.Cancel = true;
                        itemInfoGridView.Rows[e.RowIndex].ErrorText = "An item with this name already exists.";
                    }
                }
            }
            //Only AttackSpeed stat can be negative
            else if (col.ValueType == typeof(int) && col.Name != "StatAttackSpeed" && int.TryParse(val, out int val1) && val1 < 0)
            {
                e.Cancel = true;
                itemInfoGridView.Rows[e.RowIndex].ErrorText = "the value must be a positive integer";
            }
            else if (col.ValueType == typeof(int) && !int.TryParse(val, out _))
            {
                e.Cancel = true;
                itemInfoGridView.Rows[e.RowIndex].ErrorText = "the value must be an integer";
            }
            else if (col.ValueType == typeof(byte) && !byte.TryParse(val, out _))
            {
                e.Cancel = true;
                itemInfoGridView.Rows[e.RowIndex].ErrorText = "the value must be a byte";
            }
            else if (col.ValueType == typeof(short) && !short.TryParse(val, out _))
            {
                e.Cancel = true;
                itemInfoGridView.Rows[e.RowIndex].ErrorText = "the value must be a short";
            }
            else if (col.ValueType == typeof(ushort) && !ushort.TryParse(val, out _))
            {
                e.Cancel = true;
                itemInfoGridView.Rows[e.RowIndex].ErrorText = "the value must be a ushort";
            }
            else if (col.ValueType == typeof(long) && !long.TryParse(val, out _))
            {
                e.Cancel = true;
                itemInfoGridView.Rows[e.RowIndex].ErrorText = "the value must be a long";
            }

            if (!e.Cancel)
            {
                itemInfoGridView.Rows[e.RowIndex].Cells["Modified"].Value = true;
            }
        }

        private void rbtnViewAll_CheckedChanged(object sender, EventArgs e)
        {
            if (rbtnViewAll.Checked)
            {
                foreach (DataGridViewColumn col in itemInfoGridView.Columns)
                {
                    if (col.Name == "Modified") continue;

                    col.Visible = true;
                    continue;
                }
            }
        }

        private void rbtnViewBasic_CheckedChanged(object sender, EventArgs e)
        {
            if (rbtnViewBasic.Checked)
            {
                foreach (DataGridViewColumn col in itemInfoGridView.Columns)
                {
                    if (IsPinnedItemColumn(col))
                    {
                        col.Visible = true;
                        continue;
                    }

                    if (col.Name.StartsWith("Item"))
                    {
                        col.Visible = true;
                        continue;
                    }

                    col.Visible = false;
                }
            }
        }

        private void rbtnViewStats_CheckedChanged(object sender, EventArgs e)
        {
            if (rbtnViewStats.Checked)
            {
                foreach (DataGridViewColumn col in itemInfoGridView.Columns)
                {
                    if (IsPinnedItemColumn(col))
                    {
                        col.Visible = true;
                        continue;
                    }

                    if (col.Name.StartsWith("Stat"))
                    {
                        col.Visible = true;
                        continue;
                    }

                    col.Visible = false;
                }
            }
        }

        private void rbtnViewBinding_CheckedChanged(object sender, EventArgs e)
        {
            if (rbtnViewBinding.Checked)
            {
                foreach (DataGridViewColumn col in itemInfoGridView.Columns)
                {
                    if (IsPinnedItemColumn(col))
                    {
                        col.Visible = true;
                        continue;
                    }

                    if (col.Name.StartsWith("Bind"))
                    {
                        col.Visible = true;
                        continue;
                    }

                    col.Visible = false;
                }
            }
        }

        private void rBtnViewSpecial_CheckedChanged(object sender, EventArgs e)
        {
            var specialCols = new string[]
            {
                "StartItem", "NeedIdentify", "ShowGroupPickup", "GlobalDropNotify", "ClassBased", "LevelBased", "CanMine", "CanFastRun", "CanAwakening"
            };

            if (rBtnViewSpecial.Checked)
            {
                foreach (DataGridViewColumn col in itemInfoGridView.Columns)
                {
                    if (IsPinnedItemColumn(col))
                    {
                        col.Visible = true;
                        continue;
                    }

                    if (col.Name.StartsWith("Special"))
                    {
                        col.Visible = true;
                        continue;
                    }

                    if (specialCols.Contains(col.Name))
                    {
                        col.Visible = true;
                        continue;
                    }

                    col.Visible = false;
                }
            }
        }

        private static bool IsPinnedItemColumn(DataGridViewColumn column)
        {
            return column.Name is "ItemPreview" or "Modified" or "ItemIndex" or "ItemName" or "ItemTranslatedName";
        }

        private void drpFilterType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (itemInfoGridView.DataSource == null)
            {
                return;
            }

            UpdateFilter();

            var filterType = ((KeyValuePair<string, string>)drpFilterType.SelectedItem).Value;

            if (filterType == global::ItemType.Gem.ToString())
            {
                SwapGemContext(true);
            }
            else
            {
                SwapGemContext(false);
            }

        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                UpdateFilter();

                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private async void btnImport_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "CSV (*.csv)|*.csv";

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                var fileName = ofd.FileName;
                bool fileError = false;

                var rows = File.ReadAllLines(fileName);

                if (rows.Length > 1)
                {
                    var columns = rows[0].Split(',');

                    if (columns.Length < 2)
                    {
                        fileError = true;
                        MessageBox.Show("No columns to import.");
                    }

                    if (!fileError)
                    {
                        itemInfoGridView.EditMode = DataGridViewEditMode.EditProgrammatically;

                        int rowsEdited = 0;

                        this.itemInfoGridView.CurrentCell = this.itemInfoGridView.Rows[0].Cells["ItemName"];
                        var formTitle = this.Text;

                        await Task.Run(() =>
                        {
                            for (int i = 1; i < rows.Length; i++)
                            {
                                var row = rows[i];

                                var cells = row.Split(',');

                                if (string.IsNullOrWhiteSpace(cells[0]))
                                {
                                    continue;
                                }

                                if (cells.Length != columns.Length)
                                {
                                    fileError = true;
                                    MessageBox.Show($"Row {i} column count does not match the headers column count.");
                                    break;
                                }

                                var dataRow = FindRowByItemIndex(cells[0]);

                                try
                                {
                                    var progress = ((rowsEdited + 1) / (double)(rows.Length - 1)) * 100;
                                    Invoke(() => this.Text = $"{formTitle} - Importing Progress: {(int)progress}% ({rowsEdited + 1}/{rows.Length - 1})");
                                    Invoke(() => itemInfoGridView.BeginEdit(true));
                                    bool isNew = false;
                                    if (dataRow == null)
                                    {
                                        dataRow = Table.NewRow();
                                        isNew = true;
                                    }

                                    for (int j = 1; j < columns.Length; j++)
                                    {
                                        var column = columns[j];

                                        if (string.IsNullOrWhiteSpace(column))
                                        {
                                            continue;
                                        }

                                        var dataColumn = itemInfoGridView.Columns[column];

                                        if (dataColumn == null)
                                        {
                                            throw new Exception($"Column {column} was not found.");
                                        }
                                        if (dataColumn.Name == "ItemName")
                                        {
                                            var existingRow = FindRowByItemName(cells[j]);
                                            if (existingRow != null)
                                            {
                                                var existingIndex = existingRow["ItemIndex"].ToString();
                                                var currentIndex = dataRow["ItemIndex"].ToString() ?? "";
                                                if (existingIndex != currentIndex)
                                                {
                                                    throw new Exception($"An item named {cells[j]} already exists.");
                                                }
                                            }
                                            if (!isNew) ItemNameChange(dataRow[column].ToString(), cells[j]);
                                        }
                                        if (dataColumn.ValueType.IsEnum)
                                        {
                                            dataRow[column] = Enum.Parse(dataColumn.ValueType, cells[j]);
                                        }
                                        else
                                        {
                                            if (dataColumn.Name == "ItemToolTip")
                                            {
                                                dataRow[column] = cells[j].Trim('"').Replace("\\r\\n", "\r\n");
                                            }
                                            else
                                            {
                                                dataRow[column] = cells[j];
                                            }
                                        }
                                    }
                                    dataRow["Modified"] = true;
                                    if (isNew)
                                    {
                                        Table.Rows.Add(dataRow);
                                    }
                                    rowsEdited++;

                                }
                                catch (Exception ex)
                                {
                                    fileError = true;

                                    MessageBox.Show($"Error when importing item {cells[0]}. {ex.Message}");

                                    break;
                                }
                                finally
                                {
                                    Invoke(() => itemInfoGridView.EndEdit());
                                }
                            }
                        });



                        itemInfoGridView.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
                        if (!fileError)
                        {
                            MessageBox.Show($"{rowsEdited} items have been imported.");
                        }
                        else
                        {
                            itemInfoGridView.ClearSelection();
                            itemInfoGridView.Rows[rowsEdited].Selected = true;
                            itemInfoGridView.CurrentCell = itemInfoGridView.Rows[rowsEdited].Cells[0];
                        }
                        this.Text = formTitle;
                    }
                }
                else
                {
                    MessageBox.Show("No rows to import.");
                }
            }
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            if (itemInfoGridView.Rows.Count > 0)
            {
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Filter = "CSV (*.csv)|*.csv";
                sfd.FileName = "ItemInfo Output.csv";
                bool fileError = false;
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    if (File.Exists(sfd.FileName))
                    {
                        try
                        {
                            File.Delete(sfd.FileName);
                        }
                        catch (IOException ex)
                        {
                            fileError = true;
                            MessageBox.Show("It wasn't possible to write the data to the disk." + ex.Message);
                        }
                    }
                    if (!fileError)
                    {
                        try
                        {
                            List<DataGridViewColumn> exportColumns = itemInfoGridView.Columns
                                .Cast<DataGridViewColumn>()
                                .Where(IsDatabaseCsvColumn)
                                .OrderBy(column => column.Index)
                                .ToList();
                            var outputCsv = new List<string>(itemInfoGridView.Rows.Count + 1);
                            outputCsv.Add(string.Join(",", exportColumns.Select(column => column.Name)) + ",");

                            var line = new StringBuilder();
                            for (int i = 1; (i - 1) < itemInfoGridView.Rows.Count; i++)
                            {
                                line.Clear();

                                var row = itemInfoGridView.Rows[i - 1];

                                var name = row.Cells["ItemName"].Value;
                                if (name == null || name.GetType() == typeof(System.DBNull) || string.IsNullOrWhiteSpace((string)name))
                                {
                                    continue;
                                }

                                foreach (DataGridViewColumn col in exportColumns)
                                {
                                    var cell = row.Cells[col.Name];

                                    var valueType = col.ValueType;
                                    if (valueType.IsEnum)
                                    {
                                        line.Append(((Enum.ToObject(valueType, cell.Value ?? 0))?.ToString() ?? "") + ",");
                                    }
                                    else
                                    {
                                        var val = cell.Value?.ToString() ?? "";

                                        if (col.Name == "ItemToolTip")
                                        {
                                            line.Append($"\"{val.Replace("\r\n", "\\r\\n")}\",");
                                        }
                                        else
                                        {
                                            line.Append($"{val},");
                                        }
                                    }
                                }

                                if (line.Length > 0)
                                    outputCsv.Add(line.ToString());
                            }

                            File.WriteAllLines(sfd.FileName, outputCsv, Encoding.UTF8);
                            MessageBox.Show("Data Exported Successfully.", "Info");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error :" + ex.Message);
                        }
                    }
                }
            }
            else
            {
                MessageBox.Show("No Items To Export.", "Info");
            }
        }

        private static bool IsDatabaseCsvColumn(DataGridViewColumn column)
        {
            return column.Name is not ("Modified" or "ItemPreview" or "ItemTranslatedName" or "ItemTranslatedToolTip");
        }

        private void itemInfoGridView_DefaultValuesNeeded(object sender, DataGridViewRowEventArgs e)
        {
            var row = e.Row;

            row.Cells["Modified"].Value = (bool)true;
            row.Cells["ItemName"].Value = "";
            row.Cells["ItemTranslatedName"].Value = "";
            row.Cells["ItemTranslatedToolTip"].Value = "";

            row.Cells["ItemType"].Value = (ItemType)0;
            row.Cells["ItemGrade"].Value = (ItemGrade)0;
            row.Cells["ItemRequiredType"].Value = (RequiredType)0;
            row.Cells["ItemRequiredGender"].Value = RequiredGender.None;
            row.Cells["ItemRequiredClass"].Value = RequiredClass.None;
            row.Cells["ItemSet"].Value = (ItemSet)0;
            row.Cells["ItemRandomStatsId"].Value = (byte)0;
            row.Cells["ItemRequiredAmount"].Value = (byte)0;
            row.Cells["ItemImage"].Value = (ushort)0;
            row.Cells["ItemShape"].Value = (short)0;
            row.Cells["ItemEffect"].Value = (byte)0;
            row.Cells["ItemStackSize"].Value = (ushort)1;
            row.Cells["ItemSlots"].Value = (byte)0;
            row.Cells["ItemWeight"].Value = (byte)0;
            row.Cells["ItemLightRange"].Value = (byte)0;
            row.Cells["ItemLightIntensity"].Value = (byte)0;
            row.Cells["ItemDurability"].Value = (ushort)0;
            row.Cells["ItemPrice"].Value = (uint)0;
            row.Cells["ItemToolTip"].Value = (string)"";

            row.Cells["StartItem"].Value = false;
            row.Cells["NeedIdentify"].Value = false;
            row.Cells["ShowGroupPickup"].Value = false;
            row.Cells["GlobalDropNotify"].Value = false;
            row.Cells["ClassBased"].Value = false;
            row.Cells["LevelBased"].Value = false;
            row.Cells["CanMine"].Value = false;
            row.Cells["CanFastRun"].Value = false;
            row.Cells["CanAwakening"].Value = false;

            foreach (Stat stat in StatEnums)
            {
                if (stat == Stat.Unknown) continue;

                row.Cells["Stat" + stat.ToString()].Value = 0;
            }

            foreach (BindMode bind in BindEnums)
            {
                if (bind == BindMode.None) continue;

                row.Cells["Bind" + bind.ToString()].Value = false;
            }

            foreach (SpecialItemMode special in SpecialEnums)
            {
                if (special == SpecialItemMode.None) continue;

                row.Cells["Special" + special.ToString()].Value = false;
            }
        }

        private void ItemInfoGridView_MouseClick(object sender, MouseEventArgs e)
        {

            if (e.Button == MouseButtons.Right &&
                itemInfoGridView.SelectedRows.Count > 1)
            {
                var mouseOverRow = itemInfoGridView.HitTest(e.X, e.Y).RowIndex;
                var mouseOverCol = itemInfoGridView.HitTest(e.X, e.Y).ColumnIndex;

                if (mouseOverRow >= 0 &&
                    mouseOverCol >= 0)
                {
                    var colName = itemInfoGridView.Rows[mouseOverRow].Cells[mouseOverCol].OwningColumn.HeaderText;

                    if (colName == "Modified" ||
                        colName == "Index" ||
                        colName == "Name" ||
                        itemInfoGridView.Rows[mouseOverRow].Cells[mouseOverCol] is DataGridViewComboBoxCell
                        )
                    {
                        return;
                    }

                    String promptText = $"Enter new value for column [{colName}]:";
                    if (itemInfoGridView.Rows[mouseOverRow].Cells[mouseOverCol] is DataGridViewCheckBoxCell)
                    {
                        promptText += $"{Environment.NewLine}[[Enter 1 for tick or 0 for untick]]";
                    }

                    var updateValue = Interaction.InputBox(promptText,
                                                        "Bulk Update",
                                                        String.Empty);

                    if (!String.IsNullOrEmpty(updateValue))
                    {
                        foreach (DataGridViewRow selectedRow in itemInfoGridView.SelectedRows)
                        {
                            selectedRow.Cells[mouseOverCol].Value = updateValue;
                        }

                        // for some reason datagridview doesn't reflect selected cell value updating like this
                        // so re-assigning value fixes it. 
                        if (itemInfoGridView.Rows[mouseOverRow].Cells[mouseOverCol] is DataGridViewCheckBoxCell)
                        {
                            itemInfoGridView.Rows[mouseOverRow].Cells[mouseOverCol].Value = updateValue;
                        }
                    }
                }
            }
        }

        private void itemInfoGridView_UserDeletingRow(object sender, DataGridViewRowCancelEventArgs e)
        {
            var row = e.Row;

            if (row.Cells["ItemIndex"].Value != null)
            {
                int index = (int)row.Cells["ItemIndex"].Value;

                var item = Envir.ItemInfoList.FirstOrDefault(x => x.Index == index);

                Envir.ItemInfoList.Remove(item);
            }
        }

        private void itemInfoGridView_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {

        }
        private void Gameshop_button_Click(object sender, EventArgs e)
        {
            foreach (DataGridViewRow row in itemInfoGridView.Rows)
            {
                if (row.Selected && row.Cells["ItemIndex"].Value != null)
                {
                    int index = (int)row.Cells["ItemIndex"].Value;

                    var item = Envir.ItemInfoList.FirstOrDefault(x => x.Index == index);

                    Envir.AddToGameShop(item);
                }
            }

            Envir.SaveDB();
        }

        private void CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string columnName = itemInfoGridView.Columns[e.ColumnIndex].Name;
            if (columnName is "ItemTranslatedName" or "ItemTranslatedToolTip")
            {
                _localizationDirty = true;
                return;
            }

            if (columnName == "ItemImage")
                itemInfoGridView.InvalidateCell(_itemPreviewColumn.Index, e.RowIndex);

            if (itemInfoGridView.CurrentCell is DataGridViewComboBoxCell ||
                itemInfoGridView.CurrentCell is DataGridViewCheckBoxCell &&
                e.RowIndex != -1)
            {
                if (itemInfoGridView.Rows[e.RowIndex].DataBoundItem != null)
                {
                    itemInfoGridView.Rows[e.RowIndex].Cells["Modified"].Value = true;
                }
            }

        }

        private void ItemInfoGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _itemPreviewColumn.Index) return;

            DataGridViewCell previewCell = itemInfoGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
            object imageValue = itemInfoGridView.Rows[e.RowIndex].Cells["ItemImage"].Value;
            if (imageValue == null || imageValue == DBNull.Value || !int.TryParse(imageValue.ToString(), out int imageIndex))
            {
                e.Value = _unavailablePreview;
                previewCell.ToolTipText = "This row does not contain a valid image index.";
                e.FormattingApplied = true;
                return;
            }

            if (_previewProvider == null)
            {
                e.Value = _unavailablePreview;
                previewCell.ToolTipText = "No item library has been selected.";
                e.FormattingApplied = true;
                return;
            }

            e.Value = _previewProvider.GetPreview(imageIndex, out string error);
            previewCell.ToolTipText = error;
            e.FormattingApplied = true;
        }

        private void ItemInfoGridView_SelectionChanged(object sender, EventArgs e)
        {
            if (itemInfoGridView.CurrentRow != null &&
                itemInfoGridView.CurrentRow.Index != -1)
            {
                var itemType = itemInfoGridView.CurrentRow.Cells["ItemType"];
                bool isGemSelected = (global::ItemType)itemType.Value == global::ItemType.Gem;
                SwapGemContext(isGemSelected);
            }
        }

        private void CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (itemInfoGridView.IsCurrentCellDirty)
            {
                itemInfoGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void ItemInfoFormNew_FormClosing(object sender, FormClosingEventArgs e)
        {
            List<String> inError = new();
            int indexColumn = itemInfoGridView.Columns["ItemIndex"].Index;
            int nameColumn = itemInfoGridView.Columns["ItemName"].Index;

            for (int i = 0; i < itemInfoGridView.RowCount; i++)
            {
                if (!String.IsNullOrEmpty(itemInfoGridView.Rows[i].ErrorText))
                {
                    inError.Add($"Index: [{itemInfoGridView.Rows[i].Cells[indexColumn].Value}] Item: [{itemInfoGridView.Rows[i].Cells[nameColumn].Value}]");
                }
            }

            if (inError.Count > 0)
            {
                String msg = string.Join(Environment.NewLine, inError);
                if (MessageBox.Show($"The following items are invalid: {msg}", "Discard Invalid Items?", MessageBoxButtons.OKCancel) != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
            }

            try
            {
                itemInfoGridView.EndEdit();
                if (Table != null && BindingContext[Table] is CurrencyManager manager)
                    manager.EndCurrentEdit();

                SaveForm();
                SaveLocalization();
                Envir.SaveDB();

                _previewProvider?.Dispose();
                _previewProvider = null;
                _unavailablePreview?.Dispose();
                _unavailablePreview = null;
            }
            catch (Exception ex)
            {
                e.Cancel = true;
                MessageBox.Show($"Unable to save item information:{Environment.NewLine}{ex.Message}",
                    "Item Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveLocalization()
        {
            if (!_localizationStore.CanEdit || !_localizationDirty) return;

            foreach (DataGridViewRow row in itemInfoGridView.Rows)
            {
                if (row.IsNewRow ||
                    row.Cells["ItemIndex"].Value == null ||
                    !int.TryParse(row.Cells["ItemIndex"].Value.ToString(), out int index))
                {
                    continue;
                }

                string sourceName = Convert.ToString(row.Cells["ItemName"].Value) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceName)) continue;

                string displayName = Convert.ToString(row.Cells["ItemTranslatedName"].Value) ?? string.Empty;
                string toolTip = Convert.ToString(row.Cells["ItemTranslatedToolTip"].Value) ?? string.Empty;
                if (displayName.Length > ItemLocalizationFormat.MaxNameLength)
                    throw new InvalidDataException($"Item {index} translated name exceeds {ItemLocalizationFormat.MaxNameLength} characters.");
                if (toolTip.Length > ItemLocalizationFormat.MaxToolTipLength)
                    throw new InvalidDataException($"Item {index} translated tooltip exceeds {ItemLocalizationFormat.MaxToolTipLength} characters.");

                _localizationStore.UpdateEntry(index, sourceName, displayName, toolTip);
            }

            _localizationStore.Save();
            _localizationDirty = false;
        }

        private void MapHeaderText()
        {
            for (int i = 0; i < itemInfoGridView.ColumnCount; i++)
            {
                var col = itemInfoGridView.Columns[i];

                _defaultItemHeaderMappings.Add(i, col.HeaderText);
            }

            foreach (var entry in _defaultItemHeaderMappings)
            {
                switch (entry.Value.Trim())
                {
                    case nameof(SpecialItemMode.Paralize):
                        _gemItemHeaderMappings.Add(entry.Key, "Weapon");
                        break;
                    case nameof(SpecialItemMode.Teleport):
                        _gemItemHeaderMappings.Add(entry.Key, "Armour");
                        break;
                    case nameof(SpecialItemMode.ClearRing):
                        _gemItemHeaderMappings.Add(entry.Key, "Helmet");
                        break;
                    case nameof(SpecialItemMode.Protection):
                        _gemItemHeaderMappings.Add(entry.Key, "Necklace");
                        break;
                    case nameof(SpecialItemMode.Revival):
                        _gemItemHeaderMappings.Add(entry.Key, "Bracelet");
                        break;
                    case nameof(SpecialItemMode.Muscle):
                        _gemItemHeaderMappings.Add(entry.Key, "Ring");
                        break;
                    case nameof(SpecialItemMode.Flame):
                        _gemItemHeaderMappings.Add(entry.Key, "Amulet");
                        break;
                    case nameof(SpecialItemMode.Healing):
                        _gemItemHeaderMappings.Add(entry.Key, "Belt");
                        break;
                    case nameof(SpecialItemMode.Probe):
                        _gemItemHeaderMappings.Add(entry.Key, "Boots");
                        break;
                    case nameof(SpecialItemMode.Skill):
                        _gemItemHeaderMappings.Add(entry.Key, "Stone");
                        break;
                    case nameof(SpecialItemMode.NoDuraLoss):
                        _gemItemHeaderMappings.Add(entry.Key, "Torch");
                        break;
                    case "Critical Damage":
                        _gemItemHeaderMappings.Add(entry.Key, "Max Stats (All)");
                        break;
                    case "Critical":
                        _gemItemHeaderMappings.Add(entry.Key, "Base Rate %");
                        break;
                    case "Reflect":
                        _gemItemHeaderMappings.Add(entry.Key, "Success Drop");
                        break;
                    case "HP Drain %":
                        _gemItemHeaderMappings.Add(entry.Key, "Max Gem Stat");
                        break;
                }
            }
        }

        private void SwapGemContext(bool showGemInfo)
        {
            // are we already showing correct field names?
            if ((showGemInfo && _isInGemContext) ||
                (!showGemInfo && !_isInGemContext))
            {
                return;
            }

            foreach (var entry in _gemItemHeaderMappings)
            {
                var col = itemInfoGridView.Columns[entry.Key];

                col.HeaderText = showGemInfo ?
                                   _gemItemHeaderMappings[entry.Key] :
                                   _defaultItemHeaderMappings[entry.Key];

                col.DefaultCellStyle.BackColor = showGemInfo ?
                                                    Color.Yellow :
                                                    Color.Empty;
            }

            _isInGemContext = showGemInfo;
        }

        private Dictionary<string, string[]> dropFilesContent;
        private Dictionary<string, string[]> recipeFilesContent;
        private Dictionary<string, string[]> questScriptFilesContent;
        private Dictionary<string, string[]> npcScriptFilesContent;
        private bool dropFileChange, recipeFileChange, questFileChange, npcFileChange;
        private string ItemNameCellOldValue;
        private void ItemInfoGridView_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (itemInfoGridView.Columns[e.ColumnIndex].Name == "ItemName")
            {
                ItemNameCellOldValue = itemInfoGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            }
            else
            {
                ItemNameCellOldValue = string.Empty;
            }
        }
        private void ItemInfoGridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (itemInfoGridView.Columns[e.ColumnIndex].Name == "ItemName" &&
               e.RowIndex != -1)
            {
                var newName = itemInfoGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;
                ItemNameChange(ItemNameCellOldValue, newName);
                if (!string.Equals(ItemNameCellOldValue, newName, StringComparison.Ordinal))
                    _localizationDirty = true;
                ItemNameCellOldValue = string.Empty;
            }
        }
        private Dictionary<string, string[]> LoadScriptFiles(string path, string searchPattern)
        {
            var filesContent = new Dictionary<string, string[]>();
            foreach (var file in Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories))
            {
                filesContent.Add(file, File.ReadAllLines(file));
            }
            return filesContent;
        }
        private void SaveScriptFile(Dictionary<string, string[]> files)
        {
            foreach (var file in files)
            {
                File.WriteAllLines(file.Key, file.Value);
            }
        }
        private void LoadDropRecipeQuestFiles()
        {
            dropFilesContent = LoadScriptFiles(@"Envir\Drops", "*.txt");

            recipeFilesContent = LoadScriptFiles(@"Envir\Recipe", "*.txt");

            questScriptFilesContent = LoadScriptFiles(@"Envir\Quests", "*.txt");

            npcScriptFilesContent = LoadScriptFiles(@"Envir\NPCs", "*.txt");
        }

        private void SaveDropRecipeQuestFiles()
        {
            if (dropFileChange) SaveScriptFile(dropFilesContent);
            if (recipeFileChange) SaveScriptFile(recipeFilesContent);
            if (questFileChange) SaveScriptFile(questScriptFilesContent);
            if (npcFileChange) SaveScriptFile(npcScriptFilesContent);
        }

        private void RenameRecipeFile(string oldName, string newName)
        {
            var oldFilePath = recipeFilesContent.Keys.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (oldFilePath != null)
            {
                var newFilePath = Path.Combine(Path.GetDirectoryName(oldFilePath), newName + Path.GetExtension(oldFilePath));
                File.Move(oldFilePath, newFilePath);
                recipeFilesContent[newFilePath] = recipeFilesContent[oldFilePath];
                recipeFilesContent.Remove(oldFilePath);
            }
        }

        private void ItemNameChange(string oldName, string newName)
        {
            if (!string.IsNullOrWhiteSpace(oldName) && !string.IsNullOrWhiteSpace(newName) && !oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in dropFilesContent)
                {
                    var lines = file.Value;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var match = Regex.Match(line, @"^\d+/\d+\s+(\S+)");
                        if (match.Success && match.Groups[1].Value.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                        {
                            var itemName = match.Groups[1].Value;
                            if (itemName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                            {
                                lines[i] = line.Replace(itemName, newName);
                                dropFileChange = true;
                            }
                        }
                    }
                }

                foreach (var file in recipeFilesContent.Keys.ToList())
                {
                    if (!recipeFilesContent.ContainsKey(file)) continue;
                    var lines = recipeFilesContent[file];

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("["))
                        {
                            var match = Regex.Match(line, @"^([^\s|[]+)(?=\s?)");
                            if (match.Success)
                            {
                                string itemName = match.Groups[1].Value;
                                if (itemName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                                {
                                    lines[i] = line.Replace(itemName, newName);
                                    recipeFileChange = true;
                                }
                            }
                        }
                    }

                    if (Path.GetFileNameWithoutExtension(file).Equals(oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        RenameRecipeFile(oldName, newName);
                        recipeFileChange = true;
                    }
                }

                foreach (var file in questScriptFilesContent)
                {
                    var lines = file.Value;
                    bool isItem = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (line.StartsWith("[@ItemTasks]") || line.StartsWith("[@CarryItems]") || line.StartsWith("[@FixedRewards]") || line.StartsWith("[@SelectRewards]"))
                        {
                            isItem = true;
                        }
                        else if (string.IsNullOrWhiteSpace(line) || line.StartsWith("["))
                        {
                            isItem = false;
                        }
                        else if (isItem)
                        {
                            var match = Regex.Match(line, @"^([^\s|[]+)(?=\s?)");
                            if (match.Success)
                            {
                                string itemName = match.Groups[1].Value;
                                if (itemName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                                {
                                    lines[i] = line.Replace(itemName, newName);
                                    questFileChange = true;
                                }
                            }
                        }
                    }
                }

                foreach (var file in npcScriptFilesContent)
                {
                    var lines = file.Value;
                    bool isConversion = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }
                        else if (line.StartsWith("[RECIPE]") || line.StartsWith("[Trade]"))
                        {
                            isConversion = true;
                        }
                        else if (isConversion && line.StartsWith("["))
                        {
                            isConversion = false;
                        }
                        else if (isConversion)
                        {
                            var match = Regex.Match(line, @"^(\S+)(?=\s?)");
                            if (match.Success)
                            {
                                string itemName = match.Groups[1].Value;
                                if (itemName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                                {
                                    lines[i] = line.Replace(itemName, newName);
                                    npcFileChange = true;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
