using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TerraWikiOverlay;

internal static class BrowserSmoke
{
    private sealed class QuietHost : Form
    {
        protected override bool ShowWithoutActivation => true;
        public QuietHost()
        {
            ShowInTaskbar = false; Opacity = 0; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(-16000, -16000, 1000, 760);
        }
    }

    internal static void Run(string directory)
    {
        using QuietHost host = new();
        using FullEncyclopediaPanel panel = new(directory);
        host.Controls.Add(panel);
        Exception? failure = null;
        bool exited = false;
        host.Shown += async (_, _) =>
        {
            try
            {
                MethodInfo initialize = panel.GetType().GetMethod("InitializeBrowserAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Task<bool> startup = (Task<bool>)initialize.Invoke(panel, null)!;
                if (!await startup.WaitAsync(TimeSpan.FromSeconds(20))) throw new InvalidOperationException("Wiki 浏览器初始化失败。");
                WebView2 browser = (WebView2)panel.GetType().GetField("_browser", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
                CoreWebView2 core = browser.CoreWebView2!;
                core.Environment.BrowserProcessExited += (_, _) => exited = true;
                if (core.Settings.AreHostObjectsAllowed || core.Settings.IsWebMessageEnabled)
                    throw new InvalidOperationException("Wiki 页面不应获得本地宿主或 API Key 桥接权限。");
                Console.WriteLine("OK webview-runtime initialized profile=isolated host-bridge=disabled");
                TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                core.NavigationCompleted += (_, args) => completed.TrySetResult(args);
                core.Navigate(WikiBrowserNavigation.Page("药水", false));
                CoreWebView2NavigationCompletedEventArgs navigation = await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
                if (!navigation.IsSuccess) throw new InvalidOperationException("Wiki 实时加载失败：" + navigation.WebErrorStatus);
                string dom = await core.ExecuteScriptAsync("JSON.stringify({title:document.title,images:document.images.length,tables:document.querySelectorAll('table').length,chars:document.body.innerText.length,url:location.href})");
                Console.WriteLine("Wiki browser HTTP=" + navigation.HttpStatusCode + " document=" + JsonSerializer.Deserialize<string>(dom));
                if (navigation.HttpStatusCode >= 400) throw new InvalidOperationException("Wiki 返回 HTTP " + navigation.HttpStatusCode + "，不能确认完整正文渲染。");
                string png = Path.Combine("F:\\TerraMod\\Overlay\\obj\\ui-review", "wiki-live.png");
                Directory.CreateDirectory(Path.GetDirectoryName(png)!);
                using FileStream stream = File.Create(png);
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                Console.WriteLine("OK wiki-live original-html=rendered preview=" + png);
            }
            catch (Exception exception) { failure = exception; }
            finally { host.Close(); }
        };
        Application.Run(host);
        panel.Dispose();
        Stopwatch timer = Stopwatch.StartNew();
        while (!exited && timer.ElapsedMilliseconds < 2000) { Application.DoEvents(); Thread.Sleep(10); }
        if (failure is not null) throw failure;
    }
}
