using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using TerraWikiOverlay;

internal static class WikiThemeChecks
{
    internal static async Task RunAsync(HarnessChatView view, HarnessFeaturesController controller, Form host,
        string directory, string[] args, WikiViewport initialBounds)
    {
        using WikiBrowserView wiki = new(directory);
        void Bounds(WikiViewport bounds)
        {
            wiki.Bounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            wiki.Visible = bounds.Visible && controller.Page == "wiki";
            if (wiki.Visible) wiki.BringToFront();
        }
        void Page(string page) { if (page != "wiki") wiki.Visible = false; }
        host.Controls.Add(wiki); controller.WikiBoundsChanged += Bounds; controller.PageChanged += Page;
        wiki.StateChanged += view.Post;
        Bounds(initialBounds);
        try
        {
            MethodInfo initialize = typeof(WikiBrowserView).GetMethod("InitializeBrowserAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            if (!await ((Task<bool>)initialize.Invoke(wiki, null)!).WaitAsync(TimeSpan.FromSeconds(20))) throw new Exception("Wiki theme fixture could not initialize.");
            CoreWebView2 core = wiki.Browser.CoreWebView2;
            if (core.Settings.AreHostObjectsAllowed || core.Settings.IsWebMessageEnabled) throw new Exception("External Wiki obtained the local bridge.");
            core.AddWebResourceRequestedFilter("https://terraria.wiki.gg/zh/wiki/Terra_UI_Test", CoreWebView2WebResourceContext.Document);
            core.WebResourceRequested += (_, request) => request.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(Encoding.UTF8.GetBytes("<!doctype html><html><head><meta charset=utf-8><title>Wiki 页面主题验证</title><style>body{margin:0;padding:24px;background:white;color:#111;font:16px/1.8 'Microsoft YaHei',sans-serif}#content{background:white;padding:20px}.wikitable{background:white;border-collapse:collapse}td,th{border:1px solid #ccc;padding:12px}</style></head><body><main id=content><h1>Wiki 页面主题验证</h1><p>网页视口沿用独立浏览器，工具栏与其他页面统一。</p><table class=wikitable><tr><th>内容</th><th>展示</th></tr><tr><td>表格与正文</td><td>随浅色 / 夜间主题切换</td></tr></table></main></body></html>")),
                200, "OK", "Content-Type: text/html; charset=utf-8");
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, _) => completion.TrySetResult();
            core.Navigate("https://terraria.wiki.gg/zh/wiki/Terra_UI_Test");
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await wiki.ApplyThemeAsync(true);
            await Task.Delay(100);
            if (!wiki.Visible) throw new Exception("Ready Wiki browser remained hidden behind its placeholder.");
            await view.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#more').click()"); await Task.Delay(100);
            if (wiki.Visible) throw new Exception("Native Wiki obscured a local popup.");
            await view.Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#more').click()"); await Task.Delay(100);
            if (!wiki.Visible) throw new Exception("Wiki browser did not return after closing a popup.");
            string dark = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("getComputedStyle(document.body).backgroundColor"))!;
            if (dark != "rgb(21, 21, 23)" || core.Profile.PreferredColorScheme != CoreWebView2PreferredColorScheme.Dark) throw new Exception("Wiki night styling did not reach the document.");
            await wiki.ApplyThemeAsync(false);
            string light = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("getComputedStyle(document.body).backgroundColor"))!;
            if (light != "rgb(255, 255, 255)" || core.Profile.PreferredColorScheme != CoreWebView2PreferredColorScheme.Light) throw new Exception("Wiki retained forced dark styling in light mode.");
            await Task.WhenAll(wiki.ApplyThemeAsync(false), wiki.ApplyThemeAsync(true), wiki.ApplyThemeAsync(false), wiki.ApplyThemeAsync(true));
            string latest = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("getComputedStyle(document.body).backgroundColor"))!;
            if (latest != "rgb(21, 21, 23)") throw new Exception("Rapid theme changes restored an outdated Wiki theme.");
            int flag = Array.IndexOf(args, "--screenshots");
            if (flag >= 0 && flag + 1 < args.Length)
            {
                string folder = Path.GetFullPath(args[flag + 1]); Directory.CreateDirectory(folder);
                using MemoryStream shellStream = new(), wikiStream = new();
                await view.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shellStream);
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, wikiStream);
                shellStream.Position = wikiStream.Position = 0;
                using Bitmap shell = new(shellStream), pageImage = new(wikiStream);
                using (Graphics graphics = Graphics.FromImage(shell)) graphics.DrawImage(pageImage, wiki.Bounds);
                shell.Save(Path.Combine(folder, "wiki-unified-night.png"), ImageFormat.Png);
            }
            TaskCompletionSource<bool> blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationStarting += (_, navigation) => { if (navigation.Uri.StartsWith("https://example.invalid", StringComparison.Ordinal)) blocked.TrySetResult(navigation.Cancel); };
            core.Navigate("https://example.invalid/");
            if (!await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5))) throw new Exception("Wiki allowed navigation outside its official domain.");
            Console.WriteLine("OK wiki-theme light=restored dark=document+tables rapid-toggle=latest bridge=disabled navigation=restricted");
        }
        finally { controller.WikiBoundsChanged -= Bounds; controller.PageChanged -= Page; host.Controls.Remove(wiki); }
    }
}
