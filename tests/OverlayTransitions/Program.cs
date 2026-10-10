using System.Diagnostics;
using System.Reflection;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        string directory = Path.Combine(Path.GetTempPath(), "TerraAgent-transitions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Form? form = null;
        try
        {
            Assembly assembly = Assembly.Load("TerraWikiOverlay");
            Type type = assembly.GetType("TerraWikiOverlay.FloatingAssistantForm", throwOnError: true)!;
            form = (Form)Activator.CreateInstance(type, new object?[] { directory })!;
            Panel panel = (Panel)type.GetField("_expandedPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Form ball = (Form)type.GetField("_ball", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Control conversation = (Control)type.GetField("_conversation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            MethodInfo append = conversation.GetType().GetMethod("AppendMessage")!;
            for (int index = 0; index < 30; index++)
                append.Invoke(conversation, new object?[] { "Terra Agent", "# 长消息\n\n" + string.Join('\n', Enumerable.Repeat(new string('测', 100), 10)), Color.CornflowerBlue, null });
            IntPtr mainHandle = form.Handle, ballHandle = ball.Handle;
            Rectangle area = Screen.PrimaryScreen!.WorkingArea;
            form.Bounds = new Rectangle(area.Left + 12, area.Top + 12, Math.Min(1180, area.Width - 24), Math.Min(820, area.Height - 24));
            form.PerformLayout();
            Application.DoEvents();
            MethodInfo collapse = type.GetMethod("Collapse", BindingFlags.Instance | BindingFlags.NonPublic)!;
            MethodInfo expand = type.GetMethod("Expand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            collapse.Invoke(form, null);
            expand.Invoke(form, null);
            Application.DoEvents();
            Size expandedSize = form.Size, chatSize = panel.Size;
            int destroyed = 0, chatResizes = 0;
            form.HandleDestroyed += (_, _) => destroyed++;
            ball.HandleDestroyed += (_, _) => destroyed++;
            panel.Resize += (_, _) => chatResizes++;
            conversation.Resize += (_, _) => chatResizes++;
            long collapseTicks = 0;
            for (int cycle = 0; cycle < 30; cycle++)
            {
                long start = Stopwatch.GetTimestamp();
                collapse.Invoke(form, null);
                collapseTicks += Stopwatch.GetTimestamp() - start;
                if (form.Size != expandedSize || panel.Size != chatSize || ball.Size != new Size(104, 104))
                    throw new InvalidOperationException("收起时不应缩放聊天控件；仅悬浮球应为 104x104。");
                expand.Invoke(form, null);
                Application.DoEvents();
                if (form.Handle != mainHandle || ball.Handle != ballHandle || form.Size != expandedSize)
                    throw new InvalidOperationException("切换时窗口句柄或展开尺寸发生变化。");
                if (form.Visible || ball.Visible)
                    throw new InvalidOperationException("隐藏窗口回归测试不应显示窗口或抢占用户焦点。");
            }
            if (destroyed != 0 || chatResizes != 0)
                throw new InvalidOperationException($"切换仍触发重建/聊天排版：destroyed={destroyed}, resizes={chatResizes}");
            Console.WriteLine($"OK transitions=30 longMessages=30 hwndRecreations=0 chatResizes=0 collapseAverageMs={collapseTicks * 1000d / Stopwatch.Frequency / 30:0.###}");
        }
        finally
        {
            form?.Close();
            form?.Dispose();
            if (Directory.Exists(directory) && Path.GetFullPath(directory).StartsWith(Path.Combine(Path.GetTempPath(), "TerraAgent-transitions-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, recursive: true);
        }
    }
}
