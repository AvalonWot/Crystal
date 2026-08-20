using System.ComponentModel;
using PatchDownloader.Models;
using PatchDownloader.Services;

namespace PatchDownloader.Forms;

public partial class MainForm : Form
{
    private readonly SettingsStore _settingsStore;
    private readonly LocalFileService _localFileService;
    private readonly BindingList<PatchFileRow> _rows = [];
    private readonly Dictionary<string, PatchFileRow> _rowsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private DownloaderSettings _settings = new();
    private HttpClient? _httpClient;
    private bool _busy;

    public MainForm(SettingsStore settingsStore, LocalFileService localFileService)
    {
        _settingsStore = settingsStore;
        _localFileService = localFileService;

        InitializeComponent();
        _filesGrid.DataSource = _rows;

        Shown += MainForm_Shown;
        FormClosed += MainForm_FormClosed;
        _browseButton.Click += BrowseButton_Click;
        _refreshButton.Click += RefreshButton_Click;
        _downloadButton.Click += DownloadButton_Click;
        _filesGrid.CellToolTipTextNeeded += FilesGrid_CellToolTipTextNeeded;
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            _settings = await _settingsStore.LoadAsync(_lifetimeCancellation.Token);
            WriteSettingsToControls();
            await RefreshManifestAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private async void RefreshButton_Click(object? sender, EventArgs e)
    {
        await RefreshManifestAsync();
    }

    private async void DownloadButton_Click(object? sender, EventArgs e)
    {
        await StartDownloadAsync();
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择客户端根目录",
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_clientRootTextBox.Text)
                ? _clientRootTextBox.Text
                : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _clientRootTextBox.Text = dialog.SelectedPath;
        }
    }

    private async Task RefreshManifestAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, "正在加载补丁清单...");
        try
        {
            _settings = ReadSettingsFromControls();
            Directory.CreateDirectory(_settings.ClientRoot);
            var host = PatchUriBuilder.NormalizeHost(_settings.Host);
            _ = ProxyFactory.Create(_settings.Proxy);
            await _settingsStore.SaveAsync(_settings, _lifetimeCancellation.Token);

            _httpClient?.Dispose();
            _httpClient = PatchHttpClientFactory.Create(_settings);
            var manifestService = new PatchManifestService(_httpClient);
            var entries = await manifestService.LoadAsync(host, _lifetimeCancellation.Token);

            _rows.RaiseListChangedEvents = false;
            _rows.Clear();
            _rowsByName.Clear();
            foreach (var entry in entries)
            {
                var state = _localFileService.Inspect(_settings.ClientRoot, entry);
                var row = new PatchFileRow(entry)
                {
                    LocalExists = state.Exists,
                    NeedsUpdate = !state.Matches,
                    Status = state.Matches ? "已是最新" : state.Exists ? "需要更新" : "缺失"
                };
                _rows.Add(row);
                _rowsByName[entry.FileName] = row;
            }
            _rows.RaiseListChangedEvents = true;
            _rows.ResetBindings();

            var updateCount = _rows.Count(row => row.NeedsUpdate);
            _downloadButton.Enabled = updateCount > 0;
            _totalProgressBar.Value = updateCount == 0 ? _totalProgressBar.Maximum : 0;
            _progressLabel.Text = $"进度：{(updateCount == 0 ? 100 : 0)}%　已完成：0 / {updateCount}";
            _summaryLabel.Text = updateCount == 0
                ? $"共 {entries.Count} 个文件，已是最新版本。"
                : $"共 {entries.Count} 个文件，{updateCount} 个需要下载或更新。";
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _summaryLabel.Text = "加载补丁清单失败。";
            MessageBox.Show(this, exception.Message, "加载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StartDownloadAsync()
    {
        if (_busy || _httpClient is null)
        {
            return;
        }

        var pendingRows = _rows.Where(row => row.NeedsUpdate).ToArray();
        if (pendingRows.Length == 0)
        {
            _totalProgressBar.Value = _totalProgressBar.Maximum;
            _summaryLabel.Text = "已是最新版本。";
            return;
        }

        SetBusy(true, "正在下载补丁文件...");
        foreach (var row in pendingRows)
        {
            row.Status = "等待下载";
            row.ErrorMessage = null;
            UpdateRowAppearance(row);
        }

        try
        {
            var progress = new Progress<DownloadProgressSnapshot>(UpdateProgress);
            var fileStatus = new Progress<FileDownloadStatus>(UpdateFileStatus);
            var service = new PatchDownloadService(_httpClient, _localFileService);
            var result = await service.DownloadAsync(
                pendingRows.Select(row => row.Entry).ToArray(),
                _rows.Count - pendingRows.Length,
                _settings,
                progress,
                fileStatus,
                _lifetimeCancellation.Token);

            foreach (var row in pendingRows.Where(row => row.ErrorMessage is null))
            {
                var state = _localFileService.Inspect(_settings.ClientRoot, row.Entry);
                row.LocalExists = state.Exists;
                row.NeedsUpdate = !state.Matches;
                if (state.Matches)
                {
                    row.Status = "已完成";
                }
            }

            _summaryLabel.Text = result.FailedFiles == 0
                ? $"下载完成：成功 {result.SuccessfulFiles} 个文件。"
                : $"下载完成：成功 {result.SuccessfulFiles} 个，失败 {result.FailedFiles} 个；失败项已标红。";
            if (result.FailedFiles == 0)
            {
                _totalProgressBar.Value = _totalProgressBar.Maximum;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _summaryLabel.Text = "下载任务异常终止。";
            MessageBox.Show(this, exception.Message, "下载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _downloadButton.Enabled = _rows.Any(row => row.NeedsUpdate);
        }
    }

    private DownloaderSettings ReadSettingsFromControls()
    {
        return new DownloaderSettings
        {
            Host = _hostTextBox.Text,
            Proxy = _proxyTextBox.Text,
            Concurrency = decimal.ToInt32(_concurrencyNumeric.Value),
            ClientRoot = _clientRootTextBox.Text
        }.Normalize();
    }

    private void WriteSettingsToControls()
    {
        _hostTextBox.Text = _settings.Host;
        _proxyTextBox.Text = _settings.Proxy;
        _clientRootTextBox.Text = _settings.ClientRoot;
        _concurrencyNumeric.Value = Math.Clamp(_settings.Concurrency, 1, 100);
    }

    private void UpdateProgress(DownloadProgressSnapshot snapshot)
    {
        var ratio = snapshot.TotalBytes == 0
            ? (snapshot.TotalFiles == 0 ? 1d : 0d)
            : Math.Clamp((double)snapshot.TransferredBytes / snapshot.TotalBytes, 0d, 1d);
        _totalProgressBar.Value = (int)Math.Round(ratio * _totalProgressBar.Maximum);
        _speedLabel.Text = $"速度：{SizeFormatter.Format(snapshot.BytesPerSecond)}/s";
        _progressLabel.Text = $"进度：{ratio:P0}　已完成：{snapshot.CompletedFiles} / {snapshot.TotalFiles}（失败 {snapshot.FailedFiles}）";
        _activeLabel.Text = $"活动下载：{snapshot.ActiveDownloads}";
    }

    private void UpdateFileStatus(FileDownloadStatus update)
    {
        if (!_rowsByName.TryGetValue(update.FileName, out var row))
        {
            return;
        }

        row.Status = update.Status;
        row.ErrorMessage = update.ErrorMessage;
        if (update.Status == "已完成")
        {
            var state = _localFileService.Inspect(_settings.ClientRoot, row.Entry);
            row.LocalExists = state.Exists;
            row.NeedsUpdate = !state.Matches;
        }

        UpdateRowAppearance(row);
    }

    private void UpdateRowAppearance(PatchFileRow item)
    {
        foreach (DataGridViewRow gridRow in _filesGrid.Rows)
        {
            if (!ReferenceEquals(gridRow.DataBoundItem, item))
            {
                continue;
            }

            gridRow.DefaultCellStyle.BackColor = item.ErrorMessage is null
                ? _filesGrid.DefaultCellStyle.BackColor
                : Color.MistyRose;
            gridRow.DefaultCellStyle.ForeColor = item.ErrorMessage is null
                ? _filesGrid.DefaultCellStyle.ForeColor
                : Color.DarkRed;
            break;
        }
    }

    private void FilesGrid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex >= 0 && _filesGrid.Rows[e.RowIndex].DataBoundItem is PatchFileRow row)
        {
            e.ToolTipText = row.ErrorMessage;
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        _hostTextBox.Enabled = !busy;
        _proxyTextBox.Enabled = !busy;
        _clientRootTextBox.Enabled = !busy;
        _concurrencyNumeric.Enabled = !busy;
        _browseButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _downloadButton.Enabled = !busy && _rows.Any(row => row.NeedsUpdate);
        // UseWaitCursor = busy;
        if (message is not null)
        {
            _summaryLabel.Text = message;
        }
    }

    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _httpClient?.Dispose();
    }
}
