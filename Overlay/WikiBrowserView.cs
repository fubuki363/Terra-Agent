using System.Text.Json;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TerraWikiOverlay;

// Only the external document is native. Navigation controls live in the shared local UI.
internal sealed class WikiBrowserView : UserControl
{
    private readonly string _profile;
    private WebView2 _browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    private Task<bool>? _initializing;
    private bool _needsReset;
    private bool _dark;
    private bool _loading;
    private bool _canAsk;
    private bool _disposed;
    private bool _english;
    private long _generation;
    private string? _themeScript;
    private long _themeGeneration;
    private string _url = WikiBrowserNavigation.Home(false);
    private string _status = "需要联网 · 官方 Wiki 原网页";
    internal event Action<object>? StateChanged;
    internal event Action<string>? AskRequested;
    internal WebView2 Browser => _browser;

    internal WikiBrowserView(string directory)
    {
        _profile = Path.Combine(directory, "wiki-browser");
        Visible = false; Controls.Add(_browser);
    }

    internal void Start() { if (_browser.CoreWebView2 is null || _needsReset) _ = NavigateAsync(_url); else Publish(); }

    internal bool StopLoading()
    {
        if (!_loading || _browser.CoreWebView2 is null) return false;
        _browser.CoreWebView2.Stop(); return true;
    }

    internal async Task ExecuteAsync(string command, string value)
    {
        switch (command)
        {
            case "wikiNavigate":
                try { await NavigateAsync(WikiBrowserNavigation.Resolve(value, _english)); }
                catch (Exception exception) { _status = exception.GetBaseException().Message; Publish(); }
                break;
            case "wikiBack": if (_browser.CoreWebView2?.CanGoBack == true) _browser.CoreWebView2.GoBack(); break;
            case "wikiForward": if (_browser.CoreWebView2?.CanGoForward == true) _browser.CoreWebView2.GoForward(); break;
            case "wikiReload":
                if (!StopLoading()) { if (_needsReset || _browser.CoreWebView2 is null) await NavigateAsync(_url); else _browser.Reload(); }
                break;
            case "wikiHome": await NavigateAsync(WikiBrowserNavigation.Home(_english)); break;
            case "wikiLanguage": _english = value == "en"; await NavigateAsync(WikiBrowserNavigation.Home(_english)); break;
            case "wikiAsk":
                if (!_canAsk) break;
                string title = _browser.CoreWebView2?.DocumentTitle?.Split(" - ")[0] ?? "当前条目";
                AskRequested?.Invoke($"请详细说明 Terraria 官方 Wiki 的“{title}”条目，并给出适合游戏内使用的建议。");
                break;
        }
    }

    internal async Task ApplyThemeAsync(bool dark)
    {
        _dark = dark;
        BackColor = _browser.DefaultBackgroundColor = dark ? Color.FromArgb(21, 21, 23) : Color.White;
        CoreWebView2? core = _browser.CoreWebView2;
        if (core is null || _disposed) return;
        long generation = ++_themeGeneration;
        try
        {
            core.Profile.PreferredColorScheme = dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            if (_themeScript is not null) core.RemoveScriptToExecuteOnDocumentCreated(_themeScript);
            _themeScript = null;
            string script = ThemeScript(dark);
            string id = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            if (_disposed) return;
            if (generation != _themeGeneration) { core.RemoveScriptToExecuteOnDocumentCreated(id); return; }
            _themeScript = id;
            await core.ExecuteScriptAsync(script);
        }
        catch (Exception exception) { if (!_disposed) AppDiagnostics.Record(exception); }
    }

    private static readonly string DocumentThemeCss = LoadDocumentTheme();

    private static string LoadDocumentTheme()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Harness/design-platform.css")
            ?? throw new InvalidOperationException("缺少内嵌主题资源。");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd() + """
            body, #content, .mw-body, .mw-body-content, .mw-page-container, .vector-header-container, .vector-page-toolbar {
                background-color: var(--dsw-alias-bg-base) !important;
                color: var(--dsw-alias-label-primary) !important;
            }
            .wikitable, .wikitable th, .wikitable td, .terraria {
                background-color: var(--dsw-alias-bg-layer-1) !important;
                color: var(--dsw-alias-label-primary) !important;
                border-color: var(--dsw-alias-border-l2) !important;
            }
            input, select, textarea {
                background-color: var(--dsw-specific-input-major) !important;
                color: var(--dsw-alias-label-primary) !important;
            }
            a { color: var(--dsw-alias-link); }
            """;
    }

    private static string ThemeScript(bool dark) => "(()=>{const dark=" + (dark ? "true" : "false") + ";const css="
        + JsonSerializer.Serialize(DocumentThemeCss) + ";"
        + "function apply(){if(!document.documentElement)return;document.documentElement.style.colorScheme=dark?'dark':'light';"
        + "document.documentElement.classList.toggle('view-dark',dark);document.documentElement.classList.toggle('view-light',!dark);"
        + "if(document.body)document.body.toggleAttribute('data-ds-dark-theme',dark);"
        + "let style=document.getElementById('terra-wiki-theme');if(!style){style=document.createElement('style');style.id='terra-wiki-theme';document.documentElement.append(style);}style.textContent=css;}"
        + "apply();if(!document.body&&document.documentElement){const observer=new MutationObserver(()=>{if(document.body){document.body.toggleAttribute('data-ds-dark-theme',dark);observer.disconnect();}});observer.observe(document.documentElement,{childList:true,subtree:true});}"
        + "if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',apply,{once:true});})()";

    private async Task NavigateAsync(string address)
    {
        if (!WikiBrowserNavigation.IsWikiPage(address)) { _status = "仅允许打开 Terraria 官方 Wiki。"; Publish(); return; }
        long generation = ++_generation;
        _url = address;
        _initializing ??= InitializeBrowserAsync();
        Task<bool> initialization = _initializing;
        bool ready = await initialization;
        if (ReferenceEquals(_initializing, initialization)) _initializing = null;
        if (!ready || _disposed || generation != _generation) return;
        _browser.CoreWebView2.Navigate(_url);
    }

    private async Task<bool> InitializeBrowserAsync()
    {
        if (_browser.CoreWebView2 is not null && !_needsReset) return true;
        try
        {
            if (_needsReset)
            {
                Controls.Remove(_browser); _browser.Dispose();
                _themeScript = null;
                _browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
                Controls.Add(_browser); _needsReset = false;
            }
            _status = "正在启动在线 Wiki 浏览器…"; Publish();
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _profile);
            if (_disposed) return false;
            await _browser.EnsureCoreWebView2Async(environment);
            if (_disposed) return false;
            CoreWebView2 core = _browser.CoreWebView2 ?? throw new InvalidOperationException("WebView2 未完成初始化。");
            core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => { args.Cancel = true; _status = "图鉴不自动下载文件。"; Publish(); };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (WikiBrowserNavigation.IsWikiPage(args.Uri)) core.Navigate(args.Uri);
                else { _status = "已阻止站外弹窗。"; Publish(); }
            };
            core.NavigationStarting += (_, args) =>
            {
                if (!WikiBrowserNavigation.IsWikiPage(args.Uri)) { args.Cancel = true; _status = "已阻止站外或本地文件导航。"; Publish(); return; }
                _loading = true; _canAsk = false; _status = "正在加载官方 Wiki 原网页…"; Publish();
            };
            core.SourceChanged += (_, _) =>
            {
                if (!WikiBrowserNavigation.IsWikiPage(core.Source)) return;
                _url = core.Source; _english = !new Uri(_url).AbsolutePath.StartsWith("/zh/", StringComparison.Ordinal); Publish();
            };
            core.HistoryChanged += (_, _) => Publish();
            core.DocumentTitleChanged += (_, _) => Publish();
            core.NavigationCompleted += async (_, args) =>
            {
                _loading = false;
                _canAsk = args.IsSuccess && args.HttpStatusCode < 400;
                _status = !args.IsSuccess ? $"加载失败：{args.WebErrorStatus}，请检查网络后刷新。"
                    : args.HttpStatusCode >= 400 ? $"Wiki 返回 HTTP {args.HttpStatusCode}，如需验证请在网页中完成。"
                    : "在线原网页 · Ctrl+F 页内查找 · Ctrl+滚轮缩放";
                if (args.IsSuccess) await ApplyThemeAsync(_dark);
                Publish();
            };
            core.ProcessFailed += (_, _) => { _needsReset = true; _loading = false; _canAsk = false; _status = "浏览器进程已中断，请刷新重试。"; Publish(); };
            await ApplyThemeAsync(_dark);
            Publish(); return true;
        }
        catch (Exception exception)
        {
            _needsReset = true;
            if (!_disposed) { _status = "浏览器暂时无法启动，请安装或修复 WebView2 后刷新。" + exception.GetBaseException().Message; Publish(); }
            return false;
        }
    }

    private void Publish()
    {
        if (_disposed) return;
        CoreWebView2? core = _browser.CoreWebView2;
        StateChanged?.Invoke(new { type = "wikiState", url = _url, title = core?.DocumentTitle ?? "Terraria 在线图鉴", status = _status,
            loading = _loading, canBack = core?.CanGoBack ?? false, canForward = core?.CanGoForward ?? false, english = _english,
            canAsk = _canAsk,
            ready = core is not null && !_needsReset, failed = _needsReset });
    }

    protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
}
