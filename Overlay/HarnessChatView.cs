using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TerraWikiOverlay;

// Local, embedded UI only. Wiki pages use a separate WebView without this bridge.
internal sealed class HarnessChatView : UserControl
{
    internal const string Origin = "https://terra-agent.local/";
    private readonly WebView2 _browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    private readonly string _profile;
    private readonly Queue<string> _pending = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private bool _ready;
    private bool _initializing;
    internal event Action<string, JsonElement>? Command;
    internal bool Ready => _ready;
    internal WebView2 Browser => _browser;

    internal HarnessChatView(string dataDirectory, string themeMode = "system")
    {
        _profile = Path.Combine(dataDirectory, "chat-browser");
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        ApplyAppearance(themeMode, OverlayAppearance.IsDark(themeMode));
        Controls.Add(_browser);
        HandleCreated += async (_, _) => await InitializeAsync();
        Post(new { type = "theme", mode = themeMode });
    }

    private async Task InitializeAsync()
    {
        if (_initializing || IsDisposed) return;
        _initializing = true;
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, _profile);
            if (IsDisposed) return;
            await _browser.EnsureCoreWebView2Async(environment);
            if (IsDisposed) return;
            CoreWebView2 core = _browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                Uri uri = new(args.Request.Uri);
                string name = uri.GetLeftPart(UriPartial.Authority) == Origin.TrimEnd('/')
                    ? Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) : "";
                Stream? resource = name.Length > 0 && !name.Contains("..", StringComparison.Ordinal)
                    ? Assembly.GetExecutingAssembly().GetManifestResourceStream("Harness/" + Path.GetFileName(name)) : null;
                string mime = Path.GetExtension(name) switch
                {
                    ".html" => "text/html; charset=utf-8", ".js" or ".mjs" => "text/javascript; charset=utf-8",
                    ".css" => "text/css; charset=utf-8", _ => "text/plain; charset=utf-8"
                };
                args.Response = environment.CreateWebResourceResponse(resource ?? new MemoryStream(), resource is null ? 404 : 200,
                    resource is null ? "Not Found" : "OK", "Content-Type: " + mime + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
            };
            core.NavigationStarting += (_, args) =>
            {
                if (args.Uri != Origin + "index.html") args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => { args.Handled = true; OpenLink(args.Uri); };
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.WebMessageReceived += (_, args) =>
            {
                if (args.Source != Origin + "index.html") return;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(args.WebMessageAsJson);
                    JsonElement root = document.RootElement;
                    string action = root.GetProperty("action").GetString() ?? "";
                    if (action == "ready")
                    {
                        _ready = true;
                        while (_pending.TryDequeue(out string? message)) core.PostWebMessageAsJson(message);
                    }
                    else if (action == "openLink") OpenLink(root.GetProperty("value").GetString() ?? "");
                    else if (action == "copy") OverlayClipboard.TryCopy(root.GetProperty("value").GetString() ?? "");
                    else Command?.Invoke(action, root.Clone());
                }
                catch (JsonException exception) { AppDiagnostics.Record(exception); }
            };
            core.Navigate(Origin + "index.html");
        }
        catch (Exception exception)
        {
            if (IsDisposed) return;
            AppDiagnostics.Record(exception);
            Label notice = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Text = "聊天界面需要 Microsoft Edge WebView2 Runtime。\n安装或修复后重启助手。\n" + exception.GetBaseException().Message };
            LinkLabel install = new() { Dock = DockStyle.Bottom, Height = 48, Text = "安装 WebView2 Runtime", TextAlign = ContentAlignment.MiddleCenter };
            install.LinkClicked += (_, _) => OpenLink("https://developer.microsoft.com/microsoft-edge/webview2/");
            Controls.Add(notice); Controls.Add(install); notice.BringToFront(); install.BringToFront();
        }
    }

    internal void Post(object message)
    {
        if (IsDisposed) return;
        string json = JsonSerializer.Serialize(message, _json);
        if (_ready) _browser.CoreWebView2.PostWebMessageAsJson(json);
        else _pending.Enqueue(json);
    }

    internal void FocusInput() { if (Visible) _browser.Focus(); Post(new { type = "focus" }); }
    internal void SetDraft(string value) => Post(new { type = "draft", value });

    internal void ApplyAppearance(string mode, bool dark)
    {
        BackColor = _browser.DefaultBackgroundColor = dark ? Color.FromArgb(21, 21, 23) : Color.White;
        if (_browser.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = mode == "system" ? CoreWebView2PreferredColorScheme.Auto
                : dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }

    private static void OpenLink(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception exception) { AppDiagnostics.Record(exception); }
    }
}
