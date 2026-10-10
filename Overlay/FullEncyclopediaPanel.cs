namespace TerraWikiOverlay;

internal sealed class FullEncyclopediaPanel : UserControl
{
    private static readonly Color Background = Color.FromArgb(18, 19, 23);
    private static readonly Color Surface = Color.FromArgb(27, 29, 34);
    private static readonly Color SurfaceRaised = Color.FromArgb(33, 35, 42);
    private static readonly Color Accent = Color.FromArgb(102, 183, 255);
    private static readonly Color Muted = Color.FromArgb(151, 156, 168);

    private readonly WikiEncyclopediaService _service;
    private readonly TextBox _search = new();
    private readonly DataGridView _results = new();
    private readonly RichTextBox _article = new();
    private readonly Label _status = new();
    private readonly Label _title = new();
    private readonly Button _open = new();
    private readonly Button _ask = new();
    private readonly Button _sync = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 430 };
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _articleCancellation;
    private CancellationTokenSource? _indexCancellation;
    private bool _started;
    private bool _loadingRows;

    internal event Action? BackRequested;
    internal event Action? CuratedRequested;
    internal event Action<string>? AskRequested;

    internal FullEncyclopediaPanel(string dataDirectory)
    {
        Dock = DockStyle.Fill;
        BackColor = Background;
        _service = new WikiEncyclopediaService(dataDirectory);
        BuildLayout();
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await SearchAsync();
        };
        _status.Text = _service.IndexedPageCount > 0
            ? $"已缓存 {_service.IndexedPageCount:N0} 个中英文页面标题 · 输入关键词搜索"
            : "输入关键词可立即在线检索；首次打开会后台同步完整中英文页面标题索引";
    }

    internal void Start()
    {
        _search.Focus();
        if (_started) return;
        _started = true;
        if (!_service.IndexIsFresh)
            _ = SyncIndexAsync(force: false);
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 264f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(layout);

        Panel heading = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 22, 8), BackColor = Background };
        Label title = new()
        {
            Text = "Terraria 全图鉴",
            Dock = DockStyle.Top,
            Height = 31,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold)
        };
        Label subtitle = new()
        {
            Text = "官方中英文 Wiki 全页面检索 · 配方、掉落与机制按需读取 · 不消耗 AI 用量",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Muted,
            Font = new Font("Microsoft YaHei UI", 8.4f)
        };
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background
        };
        Button curated = MakeButton("常用速查", 96);
        curated.Click += (_, _) => CuratedRequested?.Invoke();
        Button back = MakeButton("返回对话", 96);
        back.Click += (_, _) => BackRequested?.Invoke();
        actions.Controls.AddRange(new Control[] { curated, back });
        heading.Controls.Add(subtitle);
        heading.Controls.Add(title);
        heading.Controls.Add(actions);
        actions.BringToFront();
        layout.Controls.Add(heading, 0, 0);

        TableLayoutPanel searchBar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(22, 4, 22, 8),
            BackColor = Background
        };
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 2, 10, 3);
        _search.PlaceholderText = "搜索全部物品、怪物、药水、配方或掉落物……";
        _search.BackColor = SurfaceRaised;
        _search.ForeColor = Color.White;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.Font = new Font("Microsoft YaHei UI", 10f);
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _debounce.Stop();
            await SearchAsync();
        };
        searchBar.Controls.Add(_search, 0, 0);
        Button searchNow = MakeButton("搜索", 104);
        searchNow.Dock = DockStyle.Fill;
        searchNow.Margin = new Padding(0, 2, 8, 3);
        searchNow.BackColor = Accent;
        searchNow.ForeColor = Color.FromArgb(5, 35, 48);
        searchNow.Click += async (_, _) => { _debounce.Stop(); await SearchAsync(); };
        searchBar.Controls.Add(searchNow, 1, 0);
        _sync.Text = "同步索引";
        _sync.Dock = DockStyle.Fill;
        _sync.Margin = new Padding(0, 2, 0, 3);
        _sync.FlatStyle = FlatStyle.Flat;
        _sync.FlatAppearance.BorderSize = 0;
        _sync.BackColor = SurfaceRaised;
        _sync.ForeColor = Color.FromArgb(210, 220, 234);
        _sync.Font = new Font("Microsoft YaHei UI", 8.7f);
        _sync.Cursor = Cursors.Hand;
        _sync.Click += async (_, _) => await SyncIndexAsync(force: true);
        searchBar.Controls.Add(_sync, 2, 0);
        layout.Controls.Add(searchBar, 0, 1);

        Panel resultsHost = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 0, 22, 8), BackColor = Background };
        _results.Dock = DockStyle.Fill;
        _results.AllowUserToAddRows = false;
        _results.AllowUserToDeleteRows = false;
        _results.AllowUserToResizeRows = false;
        _results.ReadOnly = true;
        _results.MultiSelect = false;
        _results.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _results.RowHeadersVisible = false;
        _results.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _results.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
        _results.BackgroundColor = Surface;
        _results.BorderStyle = BorderStyle.None;
        _results.GridColor = Color.FromArgb(48, 54, 65);
        _results.EnableHeadersVisualStyles = false;
        _results.ColumnHeadersHeight = 36;
        _results.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _results.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(38, 43, 54);
        _results.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(202, 218, 235);
        _results.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.6f, FontStyle.Bold);
        _results.DefaultCellStyle.BackColor = Surface;
        _results.DefaultCellStyle.ForeColor = Color.FromArgb(224, 229, 237);
        _results.DefaultCellStyle.SelectionBackColor = Color.FromArgb(48, 67, 92);
        _results.DefaultCellStyle.SelectionForeColor = Color.White;
        _results.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _results.DefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
        _results.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.7f);
        _results.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(30, 33, 39);
        _results.RowTemplate.MinimumHeight = 40;
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "条目", FillWeight = 23 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "匹配说明", FillWeight = 63 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "来源", FillWeight = 14 });
        _results.SelectionChanged += (_, _) => ShowSelectedSummary();
        _results.CellDoubleClick += async (_, _) => await OpenSelectedAsync();
        resultsHost.Controls.Add(_results);
        layout.Controls.Add(resultsHost, 0, 2);

        TableLayoutPanel articleHost = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(22, 10, 22, 14),
            BackColor = SurfaceRaised
        };
        articleHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        articleHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        articleHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        articleHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        _title.Dock = DockStyle.Fill;
        _title.ForeColor = Color.White;
        _title.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        _title.Text = "搜索官方全图鉴";
        articleHost.Controls.Add(_title, 0, 0);
        _article.Dock = DockStyle.Fill;
        _article.ReadOnly = true;
        _article.BorderStyle = BorderStyle.None;
        _article.BackColor = SurfaceRaised;
        _article.ForeColor = Color.FromArgb(202, 210, 222);
        _article.Font = new Font("Microsoft YaHei UI", 8.8f);
        _article.ScrollBars = RichTextBoxScrollBars.Vertical;
        _article.DetectUrls = false;
        _article.Text = "输入关键词后选择条目；双击结果或点击“打开条目”读取完整说明。\n配方、掉落与版本差异会以官方页面实际内容为准。";
        articleHost.Controls.Add(_article, 0, 1);
        Panel footer = new() { Dock = DockStyle.Fill, BackColor = SurfaceRaised };
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Muted;
        _status.Font = new Font("Microsoft YaHei UI", 8f);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        footer.Controls.Add(_status);
        _ask.Text = "向 Agent 追问";
        _ask.Dock = DockStyle.Right;
        _ask.Width = 132;
        _ask.Click += (_, _) => AskSelected();
        StyleFooterButton(_ask, false);
        footer.Controls.Add(_ask);
        _open.Text = "打开条目";
        _open.Dock = DockStyle.Right;
        _open.Width = 116;
        _open.Click += async (_, _) => await OpenSelectedAsync();
        StyleFooterButton(_open, true);
        footer.Controls.Add(_open);
        _ask.BringToFront();
        _open.BringToFront();
        articleHost.Controls.Add(footer, 0, 2);
        layout.Controls.Add(articleHost, 0, 3);
        _open.Enabled = false;
        _ask.Enabled = false;
    }

    private async Task SearchAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        CancellationToken token = _searchCancellation.Token;
        string query = _search.Text.Trim();
        _status.Text = query.Length == 0 ? "请输入名称开始搜索。" : "正在搜索官方全图鉴……";
        try
        {
            WikiCatalogSearch found = await _service.SearchAsync(query, token);
            if (token.IsCancellationRequested || IsDisposed) return;
            _loadingRows = true;
            _results.Rows.Clear();
            foreach (WikiCatalogHit hit in found.Hits)
            {
                string source = hit.Language switch { "zh" => "中文 Wiki", "en" => "英文 Wiki", _ => "本机物品" };
                int rowIndex = _results.Rows.Add(hit.Title, hit.Snippet, source);
                _results.Rows[rowIndex].Tag = hit;
            }
            _loadingRows = false;
            if (_results.Rows.Count > 0)
            {
                _results.ClearSelection();
                _results.Rows[0].Selected = true;
                _results.CurrentCell = _results.Rows[0].Cells[0];
            }
            ShowSelectedSummary();
            _status.Text = found.Notice.Length > 0
                ? found.Notice
                : found.TotalMatches > found.Hits.Count
                    ? $"显示前 {found.Hits.Count} / {found.TotalMatches} 条，请缩小关键词 · 双击打开"
                    : $"显示 {found.Hits.Count} 条 · 中英文标题索引 {_service.IndexedPageCount:N0} 条 · 双击打开";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsDisposed) _status.Text = "搜索失败：" + exception.GetBaseException().Message;
        }
        finally
        {
            _loadingRows = false;
        }
    }

    private void ShowSelectedSummary()
    {
        if (_loadingRows) return;
        WikiCatalogHit? hit = SelectedHit();
        _open.Enabled = hit is not null;
        _ask.Enabled = hit is not null;
        if (hit is null)
        {
            _title.Text = "搜索官方全图鉴";
            _article.Text = "没有匹配条目。试试物品名、怪物名、材料名或英文名。";
            return;
        }
        _title.Text = hit.Title;
        _article.Text = hit.Snippet + "\n\n双击结果或点击“打开条目”读取完整页面内容。";
    }

    private async Task OpenSelectedAsync()
    {
        WikiCatalogHit? hit = SelectedHit();
        if (hit is null) return;
        _articleCancellation?.Cancel();
        _articleCancellation?.Dispose();
        _articleCancellation = new CancellationTokenSource();
        CancellationToken token = _articleCancellation.Token;
        _title.Text = hit.Title;
        _article.Text = "正在读取官方 Wiki 页面……";
        _open.Enabled = false;
        try
        {
            WikiCatalogArticle article = await _service.GetArticleAsync(hit, token);
            if (token.IsCancellationRequested || IsDisposed) return;
            _title.Text = article.Title;
            _article.Text = article.Text;
            _article.Select(0, 0);
            _article.ScrollToCaret();
            _status.Text = (article.FromCache ? "已从本地缓存读取" : "已从官方 Wiki 读取并缓存")
                + (article.Language == "en" ? " · 英文页面" : " · 中文页面");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                _article.Text = "暂时无法读取此页面。你仍可向 Agent 追问，或稍后重试。\n\n" + exception.GetBaseException().Message;
                _status.Text = "页面读取失败";
            }
        }
        finally
        {
            if (!IsDisposed) _open.Enabled = SelectedHit() is not null;
        }
    }

    private async Task SyncIndexAsync(bool force)
    {
        _indexCancellation?.Cancel();
        _indexCancellation?.Dispose();
        _indexCancellation = new CancellationTokenSource();
        CancellationToken token = _indexCancellation.Token;
        _sync.Enabled = false;
        _status.Text = "正在后台同步中英文页面标题……搜索仍可使用";
        try
        {
            await _service.SyncTitleIndexesAsync(count =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(() => _status.Text = $"已同步 {count:N0} 个标题……搜索仍可使用");
            }, token, force);
            if (token.IsCancellationRequested || IsDisposed) return;
            _status.Text = $"中英文页面标题已同步：{_service.IndexedPageCount:N0} 条；正文按需缓存。";
            if (_search.TextLength > 0)
                await SearchAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsDisposed) _status.Text = "标题同步未完成；在线搜索仍可用：" + exception.GetBaseException().Message;
        }
        finally
        {
            if (!IsDisposed) _sync.Enabled = true;
        }
    }

    private WikiCatalogHit? SelectedHit() =>
        _results.SelectedRows.Count > 0 ? _results.SelectedRows[0].Tag as WikiCatalogHit : null;

    private void AskSelected()
    {
        WikiCatalogHit? hit = SelectedHit();
        if (hit is null) return;
        AskRequested?.Invoke($"请结合官方 Terraria Wiki 详细说明“{hit.Title}”：如果涉及配方，列出制作材料与工作站；如果涉及怪物，列出主要掉落、生成条件和版本差异。");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Stop();
            _debounce.Dispose();
            _searchCancellation?.Cancel();
            _articleCancellation?.Cancel();
            _indexCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _articleCancellation?.Dispose();
            _indexCancellation?.Dispose();
            _service.Dispose();
        }
        base.Dispose(disposing);
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

    private static void StyleFooterButton(Button button, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = accent ? Accent : Color.FromArgb(50, 61, 78);
        button.ForeColor = accent ? Color.FromArgb(5, 35, 48) : Color.White;
        button.Font = new Font("Microsoft YaHei UI", 8.7f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }
}
