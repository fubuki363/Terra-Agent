namespace TerraWikiOverlay;

internal sealed class WorldSearchPanel : UserControl
{
    private readonly OverlayAskService _assistant;
    private readonly ComboBox _query = new();
    private readonly Button _search = new();
    private readonly Label _status = new();
    private readonly DataGridView _results = new();
    private readonly Button _navigate = new();
    private readonly CheckBox _personal = new() { Text = "包含猪猪储蓄罐 / 保险箱 / 护卫熔炉 / 虚空保险库", Checked = true, AutoSize = true };
    private readonly TextBox _detail = new();
    private readonly FlowLayoutPanel _shortcuts = new();
    private readonly Dictionary<WorldSearchKind, Button> _modeButtons = new();
    private readonly Dictionary<WorldSearchKind, string> _queries = new();
    private CancellationTokenSource? _active;
    private WorldSearchResult? _lastResult;
    private WorldSearchKind _kind;
    public event Action? BackRequested;

    public WorldSearchPanel(OverlayAskService assistant)
    {
        _assistant = assistant;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(19, 23, 30);
        Padding = new Padding(18, 12, 18, 12);
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 44f, 40f, 46f, 36f, 28f, 46f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(layout);
        Label heading = new() { Text = "世界定位", Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(heading, 0, 0);
        FlowLayoutPanel modes = new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach ((WorldSearchKind kind, string title) in new[]
            { (WorldSearchKind.Objects, "世界物体"), (WorldSearchKind.Biomes, "群系 / 结构"), (WorldSearchKind.Storage, "箱内物品") })
        {
            Button mode = new() { Text = title, Width = 116 };
            StyleButton(mode);
            mode.Click += (_, _) => SelectKind(kind);
            _modeButtons.Add(kind, mode);
            modes.Controls.Add(mode);
        }
        layout.Controls.Add(modes, 0, 1);
        TableLayoutPanel searchRow = new() { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _query.Font = new Font("Microsoft YaHei UI", 10.5f);
        _query.BackColor = Color.FromArgb(29, 37, 49);
        _query.ForeColor = Color.White;
        _query.Dock = DockStyle.Fill;
        _query.Margin = new Padding(0, 7, 12, 7);
        _query.DropDownStyle = ComboBoxStyle.DropDown;
        _query.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _query.AutoCompleteSource = AutoCompleteSource.ListItems;
        _query.MaxDropDownItems = 15;
        _query.KeyDown += async (_, args) => { if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; if (_active is null) await SearchAsync(); } };
        _search.Text = "查找";
        StyleButton(_search);
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 3, 0, 7);
        _search.Click += async (_, _) => { if (_active is not null) _active.Cancel(); else await SearchAsync(); };
        searchRow.Controls.Add(_query, 0, 0);
        searchRow.Controls.Add(_search, 1, 0);
        layout.Controls.Add(searchRow, 0, 2);
        _shortcuts.Dock = DockStyle.Fill;
        _shortcuts.WrapContents = false;
        _shortcuts.Margin = Padding.Empty;
        layout.Controls.Add(_shortcuts, 0, 3);
        _personal.ForeColor = Color.FromArgb(172, 192, 213);
        _personal.Font = new Font("Microsoft YaHei UI", 8.6f);
        layout.Controls.Add(_personal, 0, 4);
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(160, 184, 206);
        _status.Font = new Font("Microsoft YaHei UI", 9f);
        _status.Padding = new Padding(0, 3, 0, 4);
        _status.AutoEllipsis = true;
        layout.Controls.Add(_status, 0, 5);
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
        _results.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(29, 37, 49);
        _results.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(200, 217, 236);
        _results.ColumnHeadersHeight = 36;
        _results.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _results.DefaultCellStyle.BackColor = BackColor;
        _results.DefaultCellStyle.ForeColor = Color.FromArgb(221, 233, 246);
        _results.DefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 66, 90);
        _results.DefaultCellStyle.SelectionForeColor = Color.White;
        _results.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f);
        _results.RowTemplate.Height = 36;
        foreach (string title in new[] { "目标 / 数量", "位置", "方位", "距离（格）", "图格坐标" }) _results.Columns.Add(title, title);
        _results.Columns[0].FillWeight = 150;
        _results.Columns[1].FillWeight = 140;
        _results.Columns[2].FillWeight = 65;
        _results.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) NavigateSelected(); };
        _results.SelectionChanged += (_, _) => UpdateSelection();
        layout.Controls.Add(_results, 0, 6);
        _detail.Multiline = true;
        _detail.ReadOnly = true;
        _detail.Dock = DockStyle.Fill;
        _detail.BackColor = BackColor;
        _detail.ForeColor = Color.FromArgb(182, 199, 219);
        _detail.Font = new Font("Microsoft YaHei UI", 8.8f);
        _detail.BorderStyle = BorderStyle.None;
        _detail.Margin = new Padding(0, 8, 0, 2);
        _detail.ScrollBars = ScrollBars.Vertical;
        layout.Controls.Add(_detail, 0, 7);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), WrapContents = false, Margin = Padding.Empty };
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
        layout.Controls.Add(footer, 0, 8);
        SelectKind(WorldSearchKind.Objects);
    }

    public void FocusSearch() => _query.Focus();

    public bool CancelSearch()
    {
        if (_active is null) return false;
        _active.Cancel();
        return true;
    }

    private void SelectKind(WorldSearchKind kind)
    {
        if (_active is not null) return;
        _queries[_kind] = _query.Text;
        _kind = kind;
        foreach ((WorldSearchKind mode, Button button) in _modeButtons)
        {
            button.BackColor = mode == kind ? Color.FromArgb(102, 183, 255) : Color.FromArgb(35, 48, 65);
            button.ForeColor = mode == kind ? Color.FromArgb(5, 35, 48) : Color.FromArgb(214, 232, 248);
        }
        _query.Items.Clear();
        if (kind == WorldSearchKind.Biomes) _query.Items.AddRange(TerrariaWorldLocator.SupportedBiomeNames.Cast<object>().ToArray());
        _query.Text = _queries.GetValueOrDefault(kind) ?? "";
        _personal.Visible = kind == WorldSearchKind.Storage;
        while (_shortcuts.Controls.Count > 0) { Control old = _shortcuts.Controls[0]; _shortcuts.Controls.RemoveAt(0); old.Dispose(); }
        string[] examples = kind switch
        {
            WorldSearchKind.Biomes => new[] { "微光湖", "地下丛林", "猩红之地", "附魔剑冢", "浮空岛" },
            WorldSearchKind.Storage => new[] { "魔镜", "回忆药水", "铁锭", "木材", "生命水晶" },
            _ => new[] { "生命果", "矿物", "金箱", "家具", "石中剑" }
        };
        foreach (string name in examples)
        {
            Button shortcut = new() { Text = name, AutoSize = true, Padding = new Padding(6, 1, 6, 1) };
            StyleButton(shortcut);
            shortcut.Click += (_, _) => { _query.Text = name; _query.Focus(); };
            _shortcuts.Controls.Add(shortcut);
        }
        _lastResult = null;
        _results.Rows.Clear();
        _detail.Clear();
        _navigate.Enabled = false;
        _status.Text = kind == WorldSearchKind.Biomes
            ? $"下拉列表提供全部 {TerrariaWorldLocator.SupportedBiomeNames.Count} 种可检测群系 / 世界层 / 结构，也可输入别名。"
            : kind == WorldSearchKind.Storage ? "搜索世界箱子及个人储物，显示数量、槽位和箱子排列提示。不消耗 AI 用量。"
            : "输入矿物、指定宝箱、家具或自然物体名称；直接读取实时地图，不消耗 AI 用量。";
    }

    private async Task SearchAsync()
    {
        string query = _query.Text.Trim();
        if (query.Length == 0) { _status.Text = "请输入要查找的名称，或点击下方示例。"; return; }
        string? denied = WorldSearchService.PermissionError(_kind, _assistant.Settings);
        if (denied is not null) { _status.Text = denied; return; }
        using CancellationTokenSource request = new();
        _active = request;
        _search.Text = "取消";
        _status.Text = "正在读取实时游戏数据…";
        _navigate.Enabled = false;
        _lastResult = null;
        _results.Rows.Clear();
        _detail.Clear();
        WorldSearchKind kind = _kind;
        bool personal = _personal.Checked;
        _query.Enabled = _personal.Enabled = _shortcuts.Enabled = false;
        foreach (Button mode in _modeButtons.Values) mode.Enabled = false;
        try
        {
            WorldSearchResult result = await Task.Run(() => WorldSearchService.Search(kind, query, personal, _assistant.Settings, request.Token), request.Token);
            if (IsDisposed || Disposing || request.IsCancellationRequested) return;
            ShowResults(result);
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "查找已取消，原有导航未受影响。"; }
        catch (Exception exception) { if (!IsDisposed) _status.Text = exception.GetBaseException().Message; }
        finally
        {
            _active = null;
            if (!IsDisposed)
            {
                _search.Text = "查找";
                _query.Enabled = _personal.Enabled = _shortcuts.Enabled = true;
                foreach (Button mode in _modeButtons.Values) mode.Enabled = true;
                UpdateSelection();
            }
        }
    }

    private void ShowResults(WorldSearchResult result)
    {
        _lastResult = result;
        _results.Rows.Clear();
        foreach (WorldSearchRow hit in result.Rows)
        {
            int index = _results.Rows.Add(hit.Name, hit.Place, hit.Direction, hit.DistanceTiles?.ToString("0") ?? "—",
                hit.TileX.HasValue && hit.TileY.HasValue ? $"{hit.TileX}, {hit.TileY}" : "个人储物");
            _results.Rows[index].Tag = hit;
        }
        _status.Text = result.Summary;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        WorldSearchRow? selected = _results.SelectedRows.Count > 0 ? _results.SelectedRows[0].Tag as WorldSearchRow : null;
        _navigate.Enabled = _active is null && selected?.Target is not null;
        _detail.Text = selected?.Detail ?? "选中结果查看位置细节；双击有世界坐标的目标即可导航。";
    }

    private void NavigateSelected()
    {
        if (_active is not null || _lastResult is null || _results.SelectedRows.Count == 0
            || _results.SelectedRows[0].Tag is not WorldSearchRow { Target: not null } selected) return;
        _assistant.NavigateToSearchResult(_lastResult, selected.Target);
        _status.Text = "已指向 " + selected.Target.Label + "。收起窗口跟随悬浮球箭头；右键可切换结果或停止。";
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
        button.Margin = new Padding(0, 0, 8, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _active?.Cancel();
        base.Dispose(disposing);
    }
}
