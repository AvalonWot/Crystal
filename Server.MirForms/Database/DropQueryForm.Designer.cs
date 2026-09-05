using System.ComponentModel;

namespace Server.Database;

partial class DropQueryForm
{
    private IContainer components;
    private Panel SearchPanel;
    private Label SearchLabel;
    private TextBox SearchTextBox;
    private Button QueryButton;
    private DataGridView ResultsGrid;
    private DataGridViewTextBoxColumn MonsterNameColumn;
    private DataGridViewTextBoxColumn MonsterTranslatedNameColumn;
    private DataGridViewTextBoxColumn DropRateColumn;
    private DataGridViewTextBoxColumn DropFilePathColumn;
    private StatusStrip StatusStrip;
    private ToolStripStatusLabel StatusLabel;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        SearchPanel = new Panel();
        SearchLabel = new Label();
        SearchTextBox = new TextBox();
        QueryButton = new Button();
        ResultsGrid = new DataGridView();
        MonsterNameColumn = new DataGridViewTextBoxColumn();
        MonsterTranslatedNameColumn = new DataGridViewTextBoxColumn();
        DropRateColumn = new DataGridViewTextBoxColumn();
        DropFilePathColumn = new DataGridViewTextBoxColumn();
        StatusStrip = new StatusStrip();
        StatusLabel = new ToolStripStatusLabel();
        SearchPanel.SuspendLayout();
        ((ISupportInitialize)ResultsGrid).BeginInit();
        StatusStrip.SuspendLayout();
        SuspendLayout();
        //
        // SearchPanel
        //
        SearchPanel.Controls.Add(SearchLabel);
        SearchPanel.Controls.Add(SearchTextBox);
        SearchPanel.Controls.Add(QueryButton);
        SearchPanel.Dock = DockStyle.Top;
        SearchPanel.Location = new Point(0, 0);
        SearchPanel.Name = "SearchPanel";
        SearchPanel.Padding = new Padding(12, 11, 12, 10);
        SearchPanel.Size = new Size(884, 48);
        SearchPanel.TabIndex = 0;
        //
        // SearchLabel
        //
        SearchLabel.AutoSize = true;
        SearchLabel.Location = new Point(12, 15);
        SearchLabel.Name = "SearchLabel";
        SearchLabel.Size = new Size(67, 15);
        SearchLabel.TabIndex = 0;
        SearchLabel.Text = "Item name:";
        //
        // SearchTextBox
        //
        SearchTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        SearchTextBox.Location = new Point(85, 11);
        SearchTextBox.Name = "SearchTextBox";
        SearchTextBox.Size = new Size(690, 23);
        SearchTextBox.TabIndex = 1;
        //
        // QueryButton
        //
        QueryButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        QueryButton.Location = new Point(789, 10);
        QueryButton.Name = "QueryButton";
        QueryButton.Size = new Size(83, 25);
        QueryButton.TabIndex = 2;
        QueryButton.Text = "Query";
        QueryButton.UseVisualStyleBackColor = true;
        QueryButton.Click += QueryButton_Click;
        //
        // ResultsGrid
        //
        ResultsGrid.AllowUserToAddRows = false;
        ResultsGrid.AllowUserToDeleteRows = false;
        ResultsGrid.AllowUserToResizeRows = false;
        ResultsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        ResultsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        ResultsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        ResultsGrid.Columns.AddRange(new DataGridViewColumn[] { MonsterNameColumn, MonsterTranslatedNameColumn, DropRateColumn, DropFilePathColumn });
        ResultsGrid.Dock = DockStyle.Fill;
        ResultsGrid.Location = new Point(0, 48);
        ResultsGrid.MultiSelect = false;
        ResultsGrid.Name = "ResultsGrid";
        ResultsGrid.ReadOnly = true;
        ResultsGrid.RowHeadersVisible = false;
        ResultsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        ResultsGrid.Size = new Size(884, 469);
        ResultsGrid.TabIndex = 1;
        ResultsGrid.CellDoubleClick += ResultsGrid_CellDoubleClick;
        ResultsGrid.SortCompare += ResultsGrid_SortCompare;
        //
        // MonsterNameColumn
        //
        MonsterNameColumn.FillWeight = 25F;
        MonsterNameColumn.HeaderText = "Monster Name";
        MonsterNameColumn.Name = "MonsterNameColumn";
        MonsterNameColumn.ReadOnly = true;
        //
        // MonsterTranslatedNameColumn
        //
        MonsterTranslatedNameColumn.FillWeight = 25F;
        MonsterTranslatedNameColumn.HeaderText = "Translated Name";
        MonsterTranslatedNameColumn.Name = "MonsterTranslatedNameColumn";
        MonsterTranslatedNameColumn.ReadOnly = true;
        //
        // DropRateColumn
        //
        DropRateColumn.FillWeight = 30F;
        DropRateColumn.HeaderText = "Base Drop Rate";
        DropRateColumn.Name = "DropRateColumn";
        DropRateColumn.ReadOnly = true;
        DropRateColumn.SortMode = DataGridViewColumnSortMode.Automatic;
        DropRateColumn.MinimumWidth = 150;
        DropRateColumn.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        DropRateColumn.ToolTipText = "Configured chance per direct drop entry, before server, player and rarity bonuses. Quest drops require an eligible quest. Click to sort numerically by the highest chance in each row.";
        //
        // DropFilePathColumn
        //
        DropFilePathColumn.FillWeight = 50F;
        DropFilePathColumn.HeaderText = "Drops File Path";
        DropFilePathColumn.Name = "DropFilePathColumn";
        DropFilePathColumn.ReadOnly = true;
        //
        // StatusStrip
        //
        StatusStrip.Items.AddRange(new ToolStripItem[] { StatusLabel });
        StatusStrip.Location = new Point(0, 517);
        StatusStrip.Name = "StatusStrip";
        StatusStrip.Size = new Size(884, 22);
        StatusStrip.TabIndex = 2;
        //
        // StatusLabel
        //
        StatusLabel.Name = "StatusLabel";
        StatusLabel.Size = new Size(126, 17);
        StatusLabel.Text = "Enter an item name.";
        //
        // DropQueryForm
        //
        AcceptButton = QueryButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(884, 539);
        Controls.Add(ResultsGrid);
        Controls.Add(StatusStrip);
        Controls.Add(SearchPanel);
        MinimumSize = new Size(650, 350);
        Name = "DropQueryForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Drop Query";
        SearchPanel.ResumeLayout(false);
        SearchPanel.PerformLayout();
        ((ISupportInitialize)ResultsGrid).EndInit();
        StatusStrip.ResumeLayout(false);
        StatusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
