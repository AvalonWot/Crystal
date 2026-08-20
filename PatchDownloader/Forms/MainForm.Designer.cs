namespace PatchDownloader.Forms;

partial class MainForm
{
    private readonly TextBox _hostTextBox = new();
    private readonly TextBox _proxyTextBox = new();
    private readonly TextBox _clientRootTextBox = new();
    private readonly NumericUpDown _concurrencyNumeric = new();
    private readonly Button _browseButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _downloadButton = new();
    private readonly DataGridView _filesGrid = new();
    private readonly ProgressBar _totalProgressBar = new();
    private readonly Label _speedLabel = new();
    private readonly Label _progressLabel = new();
    private readonly Label _activeLabel = new();
    private readonly Label _summaryLabel = new();

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = ".NET 10 补丁下载器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(1120, 720);
        Font = new Font("Microsoft YaHei UI", 9F);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        rootLayout.Controls.Add(CreateSettingsGroup(), 0, 0);
        ConfigureFilesGrid();
        rootLayout.Controls.Add(_filesGrid, 0, 1);
        rootLayout.Controls.Add(CreateStatusPanel(), 0, 2);

        Controls.Add(rootLayout);
        ResumeLayout(performLayout: true);
    }

    private Control CreateSettingsGroup()
    {
        var group = new GroupBox
        {
            Text = "下载设置",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _hostTextBox.Dock = DockStyle.Fill;
        _proxyTextBox.Dock = DockStyle.Fill;
        _clientRootTextBox.Dock = DockStyle.Fill;

        _concurrencyNumeric.Minimum = 1;
        _concurrencyNumeric.Maximum = 100;
        _concurrencyNumeric.Value = 4;
        _concurrencyNumeric.Width = 80;

        _browseButton.Text = "选择目录...";
        _browseButton.AutoSize = true;
        _refreshButton.Text = "应用并刷新";
        _refreshButton.AutoSize = true;
        _downloadButton.Text = "开始下载";
        _downloadButton.AutoSize = true;
        _downloadButton.Enabled = false;

        layout.Controls.Add(CreateFieldLabel("Host："), 0, 0);
        layout.Controls.Add(_hostTextBox, 1, 0);
        layout.SetColumnSpan(_hostTextBox, 3);
        layout.Controls.Add(CreateFieldLabel("代理："), 0, 1);
        layout.Controls.Add(_proxyTextBox, 1, 1);
        layout.SetColumnSpan(_proxyTextBox, 3);
        layout.Controls.Add(CreateFieldLabel("客户端目录："), 0, 2);
        layout.Controls.Add(_clientRootTextBox, 1, 2);
        layout.SetColumnSpan(_clientRootTextBox, 2);
        layout.Controls.Add(_browseButton, 3, 2);
        layout.Controls.Add(CreateFieldLabel("并发量："), 0, 3);
        layout.Controls.Add(_concurrencyNumeric, 1, 3);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right
        };
        buttons.Controls.Add(_refreshButton);
        buttons.Controls.Add(_downloadButton);
        layout.Controls.Add(buttons, 2, 3);
        layout.SetColumnSpan(buttons, 2);

        group.Controls.Add(layout);
        return group;
    }

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 7, 6, 3)
    };

    private void ConfigureFilesGrid()
    {
        _filesGrid.Dock = DockStyle.Fill;
        _filesGrid.Margin = new Padding(0, 10, 0, 10);
        _filesGrid.AutoGenerateColumns = false;
        _filesGrid.AllowUserToAddRows = false;
        _filesGrid.AllowUserToDeleteRows = false;
        _filesGrid.AllowUserToResizeRows = false;
        _filesGrid.ReadOnly = true;
        _filesGrid.MultiSelect = false;
        _filesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _filesGrid.RowHeadersVisible = false;
        _filesGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "文件名",
            DataPropertyName = "FileName",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 280
        });
        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "大小",
            DataPropertyName = "DisplaySize",
            Width = 110
        });
        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "创建时间",
            DataPropertyName = "CreationTime",
            Width = 165
        });
        _filesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "本地存在",
            DataPropertyName = "LocalExists",
            Width = 85
        });
        _filesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "状态",
            DataPropertyName = "Status",
            Width = 130
        });
    }

    private Control CreateStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _totalProgressBar.Dock = DockStyle.Fill;
        _totalProgressBar.Height = 24;
        _totalProgressBar.Maximum = 1000;
        _speedLabel.Text = "速度：0 B/s";
        _speedLabel.AutoSize = true;
        _progressLabel.Text = "已完成：0 / 0";
        _progressLabel.AutoSize = true;
        _activeLabel.Text = "活动下载：0";
        _activeLabel.AutoSize = true;
        _summaryLabel.Text = "准备就绪";
        _summaryLabel.AutoSize = true;
        _summaryLabel.ForeColor = SystemColors.GrayText;

        panel.Controls.Add(_totalProgressBar, 0, 0);
        panel.Controls.Add(_speedLabel, 1, 0);
        panel.Controls.Add(_progressLabel, 2, 0);
        panel.Controls.Add(_activeLabel, 3, 0);
        panel.Controls.Add(_summaryLabel, 0, 1);
        panel.SetColumnSpan(_summaryLabel, 4);
        return panel;
    }
}
