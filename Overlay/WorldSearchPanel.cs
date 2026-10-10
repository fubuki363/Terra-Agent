namespace TerraWikiOverlay;

internal sealed class WorldSearchPanel : UserControl
{
    private readonly OverlayAskService _assistant;
    private readonly TextBox _query = new();
    private readonly Button _search = new();
    private readonly Label _status = new();
    private readonly DataGridView _results = new();
    private readonly Button _navigate = new();
    private CancellationTokenSource? _active;
    private WorldObjectSearchResult? _lastResult;
    public event Action? BackRequested;

    public WorldSearchPanel(OverlayAskService assistant)
    {
        _assistant = assistant;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(19, 23, 30);
        Padding = new Padding(24);
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 58f, 48f, 42f, 66f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(layout);
        Label heading = new() { Text = "查找世界物体", Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(heading, 0, 0);
        TableLayoutPanel searchRow = new() { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _query.Text = "生命果";
        _query.PlaceholderText = "输入矿物、金箱、木床、工作台、生命果或其他物体…";
        _query.Font = new Font("Microsoft YaHei UI", 10.5f);
        _query.BackColor = Color.FromArgb(29, 37, 49);
        _query.ForeColor = Color.White;
        _query.BorderStyle = BorderStyle.FixedSingle;
        _query.Dock = DockStyle.Fill;
        _query.Margin = new Padding(0, 7, 12, 7);
        _query.KeyDown += async (_, args) => { if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; if (_active is null) await SearchAsync(); } };
        _search.Text = "查找";
        StyleButton(_search);
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 3, 0, 7);
        _search.Click += async (_, _) => { if (_active is not null) _active.Cancel(); else await SearchAsync(); };
        searchRow.Controls.Add(_query, 0, 0);
        searchRow.Controls.Add(_search, 1, 0);
        layout.Controls.Add(searchRow, 0, 1);
        FlowLayoutPanel shortcuts = new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (string name in new[] { "生命果", "矿物", "金箱", "家具", "石中剑" })
        {
            Button shortcut = new() { Text = name, AutoSize = true, Height = 30, Padding = new Padding(7, 2, 7, 2) };
            StyleButton(shortcut);
            shortcut.Click += (_, _) => { _query.Text = name; _query.Focus(); };
            shortcuts.Controls.Add(shortcut);
        }
        layout.Controls.Add(shortcuts, 0, 2);
        _status.Text = "直接读取当前游戏地图，不消耗 AI 用量。结果按距离排序，双击即可导航。";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(160, 184, 206);
        _status.Font = new Font("Microsoft YaHei UI", 9f);
        _status.Padding = new Padding(0, 8, 0, 4);
        layout.Controls.Add(_status, 0, 3);
        _results.Dock = DockStyle.Fill;
        _results.ReadOnly = true;
        _results.AllowUserToAddRows = false;
        _results.AllowUserToDeleteRows = false;
        _results.AllowUserToResizeRows = false;
        _results.RowHeadersVisible = false;
        _results.MultiSelect = false;
        _results.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _results.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _results.BackgroundColor = BackColor;
        _results.BorderStyle = BorderStyle.None;
        _results.GridColor = Color.FromArgb(42, 52, 65);
        _results.EnableHeadersVisualStyles = false;
        _results.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(29, 37, 49);
        _results.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 217, 236);
        _results.ColumnHeadersHeight = 38;
        _results.DefaultCellStyle.BackColor = BackColor;
        _results.DefaultCellStyle.ForeColor = Color.FromArgb(221, 233, 246);
        _results.DefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 66, 90);
        _results.DefaultCellStyle.SelectionForeColor = Color.White;
        _results.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5f);
        _results.RowTemplate.Height = 38;
        _results.Columns.Add("name", "物体");
        _results.Columns.Add("direction", "方位");
        _results.Columns.Add("distance", "距离（格）");
        _results.Columns.Add("coordinates", "图格坐标");
        _results.Columns[0].FillWeight = 150;
        _results.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) NavigateSelected(); };
        _results.SelectionChanged += (_, _) => _navigate.Enabled = _results.SelectedRows.Count > 0;
        layout.Controls.Add(_results, 0, 4);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
        _navigate.Text = "导航到所选";
        _navigate.Width = 130;
        _navigate.Enabled = false;
        StyleButton(_navigate);
        _navigate.Click += (_, _) => NavigateSelected();
        Button stop = new() { Text = "停止导航", Width = 100 };
        StyleButton(stop);
        stop.Click += (_, _) => { _assistant.StopNavigation(); _status.Text = "导航已停止。"; };
        Button back = new() { Text = "返回对话", Width = 100 };
        StyleButton(back);
        back.Click += (_, _) => BackRequested?.Invoke();
        footer.Controls.AddRange(new Control[] { _navigate, stop, back });
        layout.Controls.Add(footer, 0, 5);
    }

    public void FocusSearch() => _query.Focus();

    private async Task SearchAsync()
    {
        string query = _query.Text.Trim();
        if (query.Length == 0) return;
        if (!_assistant.Settings.IncludeMemoryContext || !_assistant.Settings.EnableObjectLocator)
        {
            _status.Text = "请在“设置 → 游戏数据”启用实时读取和世界物体定位权限。";
            return;
        }
        using CancellationTokenSource request = new();
        _active = request;
        _search.Text = "取消";
        _status.Text = "正在逐格扫描当前世界…";
        _navigate.Enabled = false;
        _lastResult = null;
        _results.Rows.Clear();
        try
        {
            WorldObjectSearchResult result = await Task.Run(() =>
            {
                if (!TerrariaWorldLocator.TryFindWorldObject(query, 24, out WorldObjectSearchResult? found,
                    out string failure, request.Token) || found is null) throw new InvalidOperationException(failure);
                return found;
            }, request.Token);
            if (IsDisposed || Disposing) return;
            _lastResult = result;
            _results.Rows.Clear();
            foreach (WorldObjectHit hit in result.Hits)
            {
                int index = _results.Rows.Add(hit.Name, hit.Direction, hit.DistanceTiles.ToString("0"), $"{hit.TileX}, {hit.TileY}");
                _results.Rows[index].Tag = hit;
            }
            _status.Text = result.Hits.Count > 0
                ? $"确认 {result.TotalObjects} {result.CountUnit}{result.ObjectName}，显示最近 {result.Hits.Count} 个方向点。选择目标后导航。"
                : $"本次未确认{result.ObjectName}。不能据此推断它从未生成或已被开采。";
            if (!result.Complete || result.MultiplayerClient) _status.Text += " 扫描未覆盖完整世界数据。";
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "扫描已取消。"; }
        catch (Exception exception) { if (!IsDisposed) _status.Text = exception.GetBaseException().Message; }
        finally
        {
            _active = null;
            if (!IsDisposed) { _search.Text = "查找"; _navigate.Enabled = _results.SelectedRows.Count > 0; }
        }
    }

    private void NavigateSelected()
    {
        if (_lastResult is null || _results.SelectedRows.Count == 0 || _results.SelectedRows[0].Tag is not WorldObjectHit selected) return;
        _assistant.NavigateToObjects(_lastResult, selected);
        _status.Text = "已指向 " + selected.Name + "。收起窗口跟随悬浮球箭头；右键可切换其他结果。";
    }

    private static void StyleButton(Button button)
    {
        button.Height = 32;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(66, 88, 112);
        button.BackColor = Color.FromArgb(35, 48, 65);
        button.ForeColor = Color.FromArgb(214, 232, 248);
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Microsoft YaHei UI", 9f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _active?.Cancel();
        base.Dispose(disposing);
    }
}
