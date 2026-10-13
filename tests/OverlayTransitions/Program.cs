using System.Diagnostics;
using System.Reflection;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string directory = Path.Combine(Path.GetTempPath(), "TerraAgent-transitions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Form? form = null;
        try
        {
            if (args.Contains("--browser-live")) { BrowserSmoke.Run(directory); return; }
            Assembly assembly = Assembly.Load("TerraWikiOverlay");
            Type type = assembly.GetType("TerraWikiOverlay.FloatingAssistantForm", throwOnError: true)!;
            form = (Form)Activator.CreateInstance(type, new object?[] { directory })!;
            RunResponsiveLayoutChecks(form, type);
            UiFeatureChecks.Run(form, args);
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
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { Console.WriteLine("Temporary browser profile is still being released: " + directory); }
            }
        }
    }

    private static void RunResponsiveLayoutChecks(Form form, Type type)
    {
        _ = form.Handle;
        Control Field(string name) => (Control)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        Control content = Field("_contentHost"), input = Field("_input"), send = Field("_sendButton");
        Control composer = Field("_composerLayout"), world = Field("_worldSearch"), encyclopedia = Field("_encyclopedia");
        void Layout(Control control)
        {
            control.PerformLayout();
            foreach (Control child in control.Controls) Layout(child);
        }
        foreach (Size size in new[] { new Size(860, 600), new Size(1180, 820), new Size(1920, 1080) })
        {
            form.ClientSize = size;
            Layout(form);
            Application.DoEvents();
            int BottomInForm(Control control)
            {
                int bottom = control.Height;
                for (Control? ancestor = control; ancestor != form && ancestor is not null; ancestor = ancestor.Parent)
                    bottom += ancestor.Top;
                return bottom;
            }
            int contentBottom = BottomInForm(content);
            int sendBottom = BottomInForm(send);
            if (size.Height - contentBottom > 24 || size.Height - sendBottom > 35
                || content.Width < size.Width - 250 || input.Width < content.Width - 40
                || input.Height < 35 || composer.Width < content.Width - 10)
                throw new InvalidOperationException($"布局没有铺满窗口：size={size}, content={content.Bounds}, input={input.Bounds}, composer={composer.Bounds}, sendBottom={sendBottom}。");
            foreach (Control control in new[] { input, send, Field("_inputCounter"), Field("_attachScreenshot") })
                if (!control.Parent!.ClientRectangle.Contains(control.Bounds))
                    throw new InvalidOperationException($"输入组件越界：{control.GetType().Name} bounds={control.Bounds}, parent={control.Parent.ClientRectangle}。");
            foreach (Control view in new[] { world, encyclopedia })
            {
                view.Visible = true;
                view.BringToFront();
                Layout(form);
                if (view.Height < content.Height - 8 || view.Width < content.Width - 8)
                    throw new InvalidOperationException("定位或浏览器视图没有自适应窗口。");
                view.Visible = false;
            }
            Console.WriteLine($"OK responsive size={size.Width}x{size.Height} inputWidth={input.Width} sendBottom={sendBottom} contentHeight={content.Height}");
        }
        Rectangle before = form.Bounds;
        MethodInfo toggle = type.GetMethod("ToggleMaximize", BindingFlags.Instance | BindingFlags.NonPublic)!;
        toggle.Invoke(form, null);
        Layout(form);
        if (form.Bounds != Screen.FromControl(form).WorkingArea) throw new InvalidOperationException("放大没有覆盖工作区。");
        toggle.Invoke(form, null);
        if (form.Bounds != before) throw new InvalidOperationException("窗口还原没有恢复原尺寸。");
        MethodInfo create = type.GetMethod("CreateNewSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object assistant = type.GetField("_assistant", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        PropertyInfo active = assistant.GetType().GetProperty("ActiveSessionId")!;
        string original = (string)active.GetValue(assistant)!;
        ((TextBox)input).Text = "尚未发送的草稿";
        create.Invoke(form, null); create.Invoke(form, null);
        if ((string)active.GetValue(assistant)! != original || ((TextBox)input).Text != "尚未发送的草稿")
            throw new InvalidOperationException("复用空对话时丢失了会话或草稿。");
        Console.WriteLine("OK empty-dialog repeated=new-session-reused draft=retained maximize=restore");
    }
}
