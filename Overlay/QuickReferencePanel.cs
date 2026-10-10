namespace TerraWikiOverlay;

internal sealed class QuickReferencePanel : UserControl
{
    private static readonly Color Background = Color.FromArgb(18, 19, 23);
    private static readonly Color Surface = Color.FromArgb(27, 29, 34);
    private static readonly Color SurfaceRaised = Color.FromArgb(33, 35, 42);
    private static readonly Color Accent = Color.FromArgb(102, 183, 255);
    private static readonly Color Muted = Color.FromArgb(151, 156, 168);

    private readonly TextBox _search = new();
    private readonly DataGridView _table = new();
    private readonly Label _count = new();
    private readonly Label _detailTitle = new();
    private readonly TextBox _detail = new();
    private readonly Button _ask = new();
    private readonly Dictionary<string, Button> _categoryButtons = new();
    private string _category = "全部";
    private bool _refreshing;

    internal event Action? BackRequested;
    internal event Action<string>? AskRequested;

    internal QuickReferencePanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Background;
        BuildLayout();
        RefreshRows();
    }

    internal void FocusSearch() => _search.Focus();

    private void BuildLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 178f));
        Controls.Add(layout);

        Panel heading = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 22, 8), BackColor = Background };
        Label title = new()
        {
            Text = "实用速查",
            Dock = DockStyle.Top,
            Height = 31,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold)
        };
        Label subtitle = new()
        {
            Text = "离线可用 · 常用原版配方与关键掉落，不是完整图鉴",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Muted,
            Font = new Font("Microsoft YaHei UI", 8.4f)
        };
        Button back = MakeButton("返回对话", 102);
        back.Dock = DockStyle.Right;
        back.Click += (_, _) => BackRequested?.Invoke();
        heading.Controls.Add(subtitle);
        heading.Controls.Add(title);
        heading.Controls.Add(back);
        back.BringToFront();
        layout.Controls.Add(heading, 0, 0);

        TableLayoutPanel filters = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(22, 4, 22, 8),
            BackColor = Background
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 312f));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        FlowLayoutPanel categories = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background
        };
        foreach (string category in new[] { "全部", "药水", "掉落" })
        {
            Button button = MakeButton(category, 92);
            button.Margin = new Padding(0, 0, 7, 0);
            button.Click += (_, _) => SelectCategory(category);
            _categoryButtons.Add(category, button);
            categories.Controls.Add(button);
        }
        filters.Controls.Add(categories, 0, 0);
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(4, 2, 0, 3);
        _search.PlaceholderText = "搜索药水、材料、怪物或掉落物……";
        _search.BackColor = SurfaceRaised;
        _search.ForeColor = Color.White;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.Font = new Font("Microsoft YaHei UI", 10f);
        _search.TextChanged += (_, _) => RefreshRows();
        filters.Controls.Add(_search, 1, 0);
        layout.Controls.Add(filters, 0, 1);

        Panel tableHost = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 0, 22, 10), BackColor = Background };
        _table.Dock = DockStyle.Fill;
        _table.AllowUserToAddRows = false;
        _table.AllowUserToDeleteRows = false;
        _table.AllowUserToResizeRows = false;
        _table.ReadOnly = true;
        _table.MultiSelect = false;
        _table.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _table.RowHeadersVisible = false;
        _table.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _table.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _table.BackgroundColor = Surface;
        _table.BorderStyle = BorderStyle.None;
        _table.GridColor = Color.FromArgb(48, 54, 65);
        _table.EnableHeadersVisualStyles = false;
        _table.ColumnHeadersHeight = 38;
        _table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _table.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(38, 43, 54);
        _table.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(202, 218, 235);
        _table.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.6f, FontStyle.Bold);
        _table.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
        _table.DefaultCellStyle.BackColor = Surface;
        _table.DefaultCellStyle.ForeColor = Color.FromArgb(224, 229, 237);
        _table.DefaultCellStyle.SelectionBackColor = Color.FromArgb(48, 67, 92);
        _table.DefaultCellStyle.SelectionForeColor = Color.White;
        _table.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _table.DefaultCellStyle.Padding = new Padding(8, 8, 8, 8);
        _table.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.7f);
        _table.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(30, 33, 39);
        _table.RowTemplate.MinimumHeight = 53;
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "条目", FillWeight = 20 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "材料 / 出现条件", FillWeight = 38 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "效果 / 主要掉落", FillWeight = 32 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "阶段", FillWeight = 18 });
        _table.SelectionChanged += (_, _) => UpdateDetail();
        _table.CellDoubleClick += (_, _) => AskSelected();
        tableHost.Controls.Add(_table);
        layout.Controls.Add(tableHost, 0, 2);

        TableLayoutPanel detailHost = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(22, 10, 22, 14),
            BackColor = SurfaceRaised
        };
        detailHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        detailHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 29f));
        detailHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        detailHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        _detailTitle.Dock = DockStyle.Fill;
        _detailTitle.ForeColor = Color.White;
        _detailTitle.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        detailHost.Controls.Add(_detailTitle, 0, 0);
        _detail.Dock = DockStyle.Fill;
        _detail.ReadOnly = true;
        _detail.Multiline = true;
        _detail.ScrollBars = ScrollBars.Vertical;
        _detail.BorderStyle = BorderStyle.None;
        _detail.BackColor = SurfaceRaised;
        _detail.ForeColor = Color.FromArgb(202, 210, 222);
        _detail.Font = new Font("Microsoft YaHei UI", 8.7f);
        _detail.TabStop = false;
        detailHost.Controls.Add(_detail, 0, 1);
        Panel footer = new() { Dock = DockStyle.Fill, BackColor = SurfaceRaised };
        _count.Dock = DockStyle.Fill;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.ForeColor = Muted;
        _count.Font = new Font("Microsoft YaHei UI", 8f);
        footer.Controls.Add(_count);
        _ask.Text = "继续问 Agent  →";
        _ask.Dock = DockStyle.Right;
        _ask.Width = 154;
        _ask.FlatStyle = FlatStyle.Flat;
        _ask.FlatAppearance.BorderSize = 0;
        _ask.BackColor = Accent;
        _ask.ForeColor = Color.FromArgb(5, 35, 48);
        _ask.Font = new Font("Microsoft YaHei UI", 8.7f, FontStyle.Bold);
        _ask.Cursor = Cursors.Hand;
        _ask.Click += (_, _) => AskSelected();
        footer.Controls.Add(_ask);
        _ask.BringToFront();
        detailHost.Controls.Add(footer, 0, 2);
        layout.Controls.Add(detailHost, 0, 3);

        UpdateCategoryButtons();
    }

    private void SelectCategory(string category)
    {
        _category = category;
        UpdateCategoryButtons();
        RefreshRows();
    }

    private void UpdateCategoryButtons()
    {
        foreach ((string category, Button button) in _categoryButtons)
        {
            bool selected = category == _category;
            button.BackColor = selected ? Accent : SurfaceRaised;
            button.ForeColor = selected ? Color.FromArgb(5, 35, 48) : Color.FromArgb(210, 220, 234);
        }
    }

    private void RefreshRows()
    {
        _refreshing = true;
        _table.Rows.Clear();
        IReadOnlyList<QuickReferenceEntry> entries = QuickReferenceCatalog.Search(_category, _search.Text);
        foreach (QuickReferenceEntry entry in entries)
        {
            int rowIndex = _table.Rows.Add(entry.Name, entry.Requirement, entry.Result, entry.Stage);
            _table.Rows[rowIndex].Tag = entry;
        }
        _refreshing = false;
        if (_table.Rows.Count > 0)
        {
            _table.ClearSelection();
            _table.Rows[0].Selected = true;
            _table.CurrentCell = _table.Rows[0].Cells[0];
        }
        UpdateDetail();
        _count.Text = $"显示 {entries.Count} / {QuickReferenceCatalog.Entries.Count} 条 · 双击条目可追问";
    }

    private void UpdateDetail()
    {
        if (_refreshing) return;
        QuickReferenceEntry? entry = SelectedEntry();
        _detailTitle.Text = entry is null ? "没有匹配条目" : entry.Name + " · " + entry.Category;
        _detail.Text = entry is null
            ? "试试其他关键词；常用速查未收录的内容，可以返回对话让 Agent 检索。"
            : entry.Category == "药水"
                ? $"材料：{entry.Requirement}\r\n效果：{entry.Result}\r\n制作站：放置的瓶子 / 炼药桌 / 炼金瓶\r\n说明：{entry.Note}"
                : $"出现条件：{entry.Requirement}\r\n主要掉落：{entry.Result}\r\n说明：{entry.Note}";
        _ask.Enabled = entry is not null;
    }

    private QuickReferenceEntry? SelectedEntry() =>
        _table.SelectedRows.Count > 0 ? _table.SelectedRows[0].Tag as QuickReferenceEntry : null;

    private void AskSelected()
    {
        QuickReferenceEntry? entry = SelectedEntry();
        if (entry is null) return;
        string question = entry.Category == "药水"
            ? $"请详细说明{entry.Name}的制作方法、材料获取方式，以及什么时候值得使用。"
            : $"请详细说明{entry.Name}的主要掉落、生成条件和高效获取方法。";
        AskRequested?.Invoke(question);
    }

    private static Button MakeButton(string text, int width)
    {
        Button button = new()
        {
            Text = text,
            Width = width,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceRaised,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 8.8f),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(56, 69, 91);
        return button;
    }
}
