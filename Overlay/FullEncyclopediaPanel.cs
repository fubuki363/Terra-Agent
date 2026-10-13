using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TerraWikiOverlay;

internal sealed class FullEncyclopediaPanel : UserControl
{
    private static readonly Color Background = Color.FromArgb(19, 23, 30);
    private static readonly Color Surface = Color.FromArgb(29, 37, 49);
    private readonly string _browserDirectory;
    private WebView2 _browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _notice = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly TextBox _address = new();
    private readonly ComboBox _language = new();
    private readonly Label _status = new();
    private readonly Label _title = new();
    private readonly Button _back = new();
    private readonly Button _forward = new();
    private readonly Button _reload = new();
    private readonly Button _ask = new();
    private readonly LinkLabel _install = new();
    private readonly ToolTip _tips = new();
    private Task<bool>? _initializing;
    private string _currentPage = WikiBrowserNavigation.Home(false);
    private bool _needsReset;
    private bool _loading;
    private bool _updatingLanguage;
    private long _navigationGeneration;

    internal event Action? BackRequested;
    internal event Action<string>? AskRequested;
    private bool English => _language.SelectedIndex == 1;

    internal FullEncyclopediaPanel(string dataDirectory)
    {
        _browserDirectory = Path.Combine(dataDirectory, "wiki-browser");
        Dock = DockStyle.Fill;
        BackColor = Background;
        BuildLayout();
    }

    internal void Start()
    {
        FocusSearch();
        if (_browser.CoreWebView2 is null) _ = NavigateAsync(_currentPage);
    }

    internal void FocusSearch() { _address.Focus(); _address.SelectAll(); }

    internal bool StopLoading()
    {
        if (!_loading || _browser.CoreWebView2 is null) return false;
        _browser.CoreWebView2.Stop();
        return true;
    }

    private void BuildLayout()
    {
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(layout);
        TableLayoutPanel heading = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16, 10, 16, 6), Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198));
        TableLayoutPanel brand = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        _title.Text = "Terraria 在线全图鉴";
        _title.ForeColor = Color.White;
        _title.Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold);
        _title.Dock = DockStyle.Fill;
        _title.AutoEllipsis = true;
        brand.Controls.Add(_title, 0, 0);
        brand.Controls.Add(new Label { Text = "官方 Wiki 原网页 · 图片 / 表格 / 链接完整显示", Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(151, 166, 187), Font = new Font("Microsoft YaHei UI", 8f), AutoEllipsis = true }, 0, 1);
        heading.Controls.Add(brand, 0, 0);
        FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0) };
        StyleButton(_ask, "问 Agent", 92);
        _ask.Enabled = false;
        _ask.Click += (_, _) =>
        {
            string title = _browser.CoreWebView2?.DocumentTitle ?? "当前 Wiki 条目";
            AskRequested?.Invoke($"请结合官方 Terraria Wiki 详细解释“{title}”的机制、配方、掉落或使用方法。页面：{_currentPage}");
        };
        Button backToChat = new();
        StyleButton(backToChat, "返回对话", 92);
        backToChat.Click += (_, _) => BackRequested?.Invoke();
        actions.Controls.AddRange(new Control[] { _ask, backToChat });
        heading.Controls.Add(actions, 1, 0);
        layout.Controls.Add(heading, 0, 0);

        TableLayoutPanel bar = new() { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Padding = new Padding(16, 3, 16, 5), Margin = Padding.Empty };
        foreach (float width in new[] { 36f, 36f, 36f, 52f }) bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        StyleButton(_back, "‹", 32);
        StyleButton(_forward, "›", 32);
        StyleButton(_reload, "↻", 32);
        _back.Enabled = _forward.Enabled = false;
        _back.Click += (_, _) => { if (_browser.CoreWebView2?.CanGoBack == true) _browser.CoreWebView2.GoBack(); };
        _forward.Click += (_, _) => { if (_browser.CoreWebView2?.CanGoForward == true) _browser.CoreWebView2.GoForward(); };
        _reload.Click += async (_, _) =>
        {
            if (_loading && _browser.CoreWebView2 is not null) { _browser.CoreWebView2.Stop(); return; }
            if (_browser.CoreWebView2 is null || _needsReset) await NavigateAsync(_currentPage);
            else _browser.CoreWebView2.Reload();
        };
        _tips.SetToolTip(_back, "后退 · Alt+←");
        _tips.SetToolTip(_forward, "前进 · Alt+→");
        _tips.SetToolTip(_reload, "刷新 / 停止加载");
        Button home = new();
        StyleButton(home, "首页", 48);
        home.Click += async (_, _) => await NavigateAsync(WikiBrowserNavigation.Home(English));
        foreach (Control button in new Control[] { _back, _forward, _reload, home }) button.Dock = DockStyle.Fill;
        bar.Controls.Add(_back, 0, 0); bar.Controls.Add(_forward, 1, 0); bar.Controls.Add(_reload, 2, 0); bar.Controls.Add(home, 3, 0);
        _address.Dock = DockStyle.Fill;
        _address.Margin = new Padding(6, 5, 8, 3);
        _address.Font = new Font("Microsoft YaHei UI", 10f);
        _address.BackColor = Surface;
        _address.ForeColor = Color.White;
        _address.BorderStyle = BorderStyle.FixedSingle;
        _address.PlaceholderText = "搜索 Wiki，或粘贴官方页面地址…";
        _address.KeyDown += async (_, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            await OpenAddressAsync();
        };
        bar.Controls.Add(_address, 4, 0);
        _language.Items.AddRange(new object[] { "简体中文", "English" });
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.Dock = DockStyle.Fill;
        _language.Margin = new Padding(0, 5, 6, 3);
        _language.Font = new Font("Microsoft YaHei UI", 8.5f);
        _language.SelectedIndex = 0;
        _language.SelectedIndexChanged += async (_, _) => { if (!_updatingLanguage) await NavigateAsync(WikiBrowserNavigation.Home(English)); };
        bar.Controls.Add(_language, 5, 0);
        Button go = new();
        StyleButton(go, "搜索", 64);
        go.Dock = DockStyle.Fill;
        go.Click += async (_, _) => await OpenAddressAsync();
        bar.Controls.Add(go, 6, 0);
        layout.Controls.Add(bar, 0, 1);

        FlowLayoutPanel links = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(16, 0, 16, 6), Margin = Padding.Empty };
        foreach ((string zh, string en) in new[] { ("物品", "Items"), ("药水", "Potions"), ("敌怪", "Enemies"), ("NPC", "NPCs"), ("Boss", "Bosses"), ("制作", "Crafting") })
        {
            Button link = new();
            StyleButton(link, zh, 64);
            link.Height = 28;
            link.Click += async (_, _) => await NavigateAsync(WikiBrowserNavigation.Page(English ? en : zh, English));
            links.Controls.Add(link);
        }
        layout.Controls.Add(links, 0, 2);
        _notice.Text = "在线图鉴\r\n直接浏览官方 Wiki 原网页，不使用本地文本快照。";
        _notice.ForeColor = Color.FromArgb(165, 188, 215);
        _notice.Font = new Font("Microsoft YaHei UI", 11f);
        _notice.Padding = new Padding(28);
        _pageHost.Controls.Add(_browser);
        _pageHost.Controls.Add(_notice);
        _notice.BringToFront();
        layout.Controls.Add(_pageHost, 0, 3);
        Panel footer = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 3, 16, 3), Margin = Padding.Empty };
        _status.Text = "需要联网 · Ctrl+L 地址栏 · Ctrl+F 页内查找 · Ctrl+滚轮缩放 · 不消耗 AI 用量";
        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = true;
        _status.ForeColor = Color.FromArgb(145, 165, 190);
        _status.Font = new Font("Microsoft YaHei UI", 8f);
        _install.Text = "安装 WebView2";
        _install.Dock = DockStyle.Right;
        _install.Width = 130;
        _install.LinkColor = Color.FromArgb(102, 183, 255);
        _install.Visible = false;
        _install.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true }); }
            catch (Exception exception) { _status.Text = "无法打开安装页面：" + exception.GetBaseException().Message; }
        };
        footer.Controls.Add(_status); footer.Controls.Add(_install);
        layout.Controls.Add(footer, 0, 4);
    }

    private async Task OpenAddressAsync()
    {
        try
        {
            await NavigateAsync(WikiBrowserNavigation.Resolve(_address.Text, English));
            if (!IsDisposed && _browser.CoreWebView2 is not null) _browser.Focus();
        }
        catch (Exception exception) { if (!IsDisposed) _status.Text = exception.GetBaseException().Message; }
    }

    private async Task NavigateAsync(string address)
    {
        if (!WikiBrowserNavigation.IsWikiPage(address)) { _status.Text = "仅允许打开 Terraria 官方 Wiki 页面。"; return; }
        long generation = ++_navigationGeneration;
        _currentPage = address;
        if (_initializing is null) _initializing = InitializeBrowserAsync();
        Task<bool> initialization = _initializing;
        bool ready = await initialization;
        if (ReferenceEquals(_initializing, initialization)) _initializing = null;
        if (!ready || IsDisposed || Disposing || generation != _navigationGeneration) return;
        _browser.CoreWebView2.Navigate(_currentPage);
    }

    private async Task<bool> InitializeBrowserAsync()
    {
        if (_browser.CoreWebView2 is not null && !_needsReset) return true;
        try
        {
            if (_needsReset)
            {
                _pageHost.Controls.Remove(_browser);
                _browser.Dispose();
                _browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
                _pageHost.Controls.Add(_browser);
                _notice.BringToFront();
                _needsReset = false;
            }
            _notice.Visible = true;
            _notice.Text = "正在启动在线 Wiki 浏览器…";
            _status.Text = "初始化 WebView2…";
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _browserDirectory);
            if (IsDisposed || Disposing) return false;
            await _browser.EnsureCoreWebView2Async(environment);
            if (IsDisposed || Disposing) return false;
            CoreWebView2 core = _browser.CoreWebView2 ?? throw new InvalidOperationException("WebView2 未完成初始化。");
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => { args.Cancel = true; _status.Text = "图鉴不自动下载文件。"; };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (WikiBrowserNavigation.IsWikiPage(args.Uri)) core.Navigate(args.Uri);
                else _status.Text = "已阻止站外弹窗；图鉴只浏览 Terraria 官方 Wiki。";
            };
            core.NavigationStarting += (_, args) =>
            {
                if (!WikiBrowserNavigation.IsWikiPage(args.Uri)) { args.Cancel = true; _status.Text = "已阻止站外或本地文件导航。"; return; }
                _loading = true;
                _ask.Enabled = false;
                _reload.Text = "×";
                _status.Text = "正在加载官方 Wiki 原网页…";
            };
            core.SourceChanged += (_, _) =>
            {
                if (!WikiBrowserNavigation.IsWikiPage(core.Source)) return;
                _currentPage = core.Source;
                if (!_address.Focused) _address.Text = _currentPage;
                _updatingLanguage = true;
                _language.SelectedIndex = new Uri(core.Source).AbsolutePath.StartsWith("/zh/", StringComparison.Ordinal) ? 0 : 1;
                _updatingLanguage = false;
            };
            core.HistoryChanged += (_, _) => { _back.Enabled = core.CanGoBack; _forward.Enabled = core.CanGoForward; };
            core.DocumentTitleChanged += (_, _) => _title.Text = core.DocumentTitle;
            core.NavigationCompleted += (_, args) =>
            {
                _loading = false;
                _reload.Text = "↻";
                _ask.Enabled = args.IsSuccess && args.HttpStatusCode < 400 && WikiBrowserNavigation.IsWikiPage(core.Source);
                _status.Text = !args.IsSuccess ? $"加载失败：{args.WebErrorStatus}，请检查网络后刷新。不会切换离线内容。"
                    : args.HttpStatusCode >= 400 ? $"Wiki 返回 HTTP {args.HttpStatusCode}；如页面要求验证，请在网页中完成后刷新。"
                    : "在线原网页 · Ctrl+F 页内查找 · Ctrl+滚轮缩放 · 图片、表格和站内链接可直接使用";
            };
            core.ProcessFailed += (_, _) =>
            {
                _needsReset = true;
                _loading = false;
                _ask.Enabled = false;
                _reload.Text = "↻";
                _notice.Text = "浏览器进程已中断，点击刷新重新加载。";
                _notice.Visible = true;
                _notice.BringToFront();
            };
            _notice.Visible = false;
            _install.Visible = false;
            return true;
        }
        catch (Exception exception)
        {
            if (!IsDisposed && !Disposing)
            {
                _needsReset = true;
                _notice.Visible = true;
                _notice.Text = "在线浏览器暂时无法启动。\r\n请安装或修复 Microsoft Edge WebView2 Runtime，然后点击刷新。\r\n\r\n" + exception.GetBaseException().Message;
                _status.Text = "浏览器未启动 · 不会使用离线图鉴替代";
                _install.Visible = true;
            }
            return false;
        }
    }

    private static void StyleButton(Button button, string text, int width)
    {
        button.Text = text; button.Width = width; button.Height = 34;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(46, 64, 86);
        button.BackColor = Surface;
        button.ForeColor = Color.FromArgb(219, 234, 251);
        button.Font = new Font("Microsoft YaHei UI", 9f);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 2, 5, 2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
