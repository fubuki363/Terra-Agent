using System.Reflection;
using TerraWikiOverlay;

internal static class UiFeatureChecks
{
    internal static void Run(Form form, string[] args)
    {
        Type type = form.GetType();
        object Field(string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        WorldSearchPanel panel = (WorldSearchPanel)Field("_worldSearch");
        ComboBox query = (ComboBox)panel.GetType().GetField("_query", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        MethodInfo select = panel.GetType().GetMethod("SelectKind", BindingFlags.NonPublic | BindingFlags.Instance)!;
        MethodInfo show = panel.GetType().GetMethod("ShowResults", BindingFlags.NonPublic | BindingFlags.Instance)!;
        MethodInfo navigate = panel.GetType().GetMethod("NavigateSelected", BindingFlags.NonPublic | BindingFlags.Instance)!;
        DataGridView rows = (DataGridView)panel.GetType().GetField("_results", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        OverlayAskService assistant = (OverlayAskService)Field("_assistant");
        select.Invoke(panel, new object[] { WorldSearchKind.Biomes });
        if (query.Items.Count != TerrariaWorldLocator.SupportedBiomeNames.Count) throw new InvalidOperationException("直接群系定位未展示全部检测范围。");
        query.Text = "地下丛林";
        select.Invoke(panel, new object[] { WorldSearchKind.Storage });
        query.Text = "魔镜";
        NavigationTarget first = new("item", "魔镜 · 材料箱", 100, 100, "上排第 2 个");
        NavigationTarget second = new("item", "魔镜 · 药水箱", 120, 100, "上排第 3 个");
        WorldSearchResult result = new("测试库存示例", new[]
        {
            new WorldSearchRow("魔镜 ×1", "材料箱", "上排第 2 个 · 槽4", "右", 100, 100, 100, first),
            new WorldSearchRow("魔镜 ×2", "药水箱", "上排第 3 个 · 槽2、槽7", "右", 120, 120, 100, second),
            new WorldSearchRow("魔镜 ×1", "猪猪储蓄罐", "随身个人储物 · 槽1", "无需导航", null, null, null, null)
        }, new[] { first, second }, 0, 0);
        panel.Visible = true;
        Control conversationArea = (Control)Field("_conversationArea");
        conversationArea.Visible = false;
        panel.BringToFront();
        show.Invoke(panel, new object[] { result });
        rows.ClearSelection(); rows.Rows[1].Selected = true;
        navigate.Invoke(panel, null);
        if (!assistant.Navigation.Active || assistant.Navigation.TargetTileX != 120)
            throw new InvalidOperationException("点击指定箱子没有导航到所选结果。");
        rows.ClearSelection(); rows.Rows[2].Selected = true;
        navigate.Invoke(panel, null);
        if (assistant.Navigation.TargetTileX != 120) throw new InvalidOperationException("个人储物错误地产生了世界坐标。");
        assistant.StopNavigation();
        int flag = Array.IndexOf(args, "--screenshots");
        if (flag >= 0 && args.Length > flag + 1)
        {
            string destination = Path.GetFullPath(args[flag + 1]);
            Directory.CreateDirectory(destination);
            Save(form, Path.Combine(destination, "locator.png"));
            panel.Visible = false;
            conversationArea.Visible = true;
            conversationArea.BringToFront();
            form.ClientSize = new Size(1180, 820);
            form.PerformLayout();
            Save(form, Path.Combine(destination, "chat.png"));
            form.ClientSize = new Size(1920, 1080);
            form.PerformLayout();
            Save(form, Path.Combine(destination, "chat-expanded.png"));
            form.ClientSize = new Size(1180, 820);
            Console.WriteLine("UI previews: " + destination);
        }
        panel.Visible = false;
        conversationArea.Visible = true;
        conversationArea.BringToFront();
        select.Invoke(panel, new object[] { WorldSearchKind.Biomes });
        if (query.Text != "地下丛林") throw new InvalidOperationException("切换定位类型后丢失了搜索内容。");
        panel.Visible = false;
        Console.WriteLine("OK locator-ui biomeOptions=all navigation=selected-chest personal=no-world-target queries=retained");
    }

    private static void Save(Form form, string path)
    {
        Application.DoEvents();
        // Render only this test's controls in an invisible, non-activating host.
        Control root = (Control)form.GetType().GetField("_expandedPanel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        Size size = root.Size;
        form.Controls.Remove(root);
        using QuietPreviewHost host = new() { ClientSize = size };
        try
        {
            host.Controls.Add(root);
            host.Show();
            Application.DoEvents();
            root.PerformLayout();
            foreach (Control container in root.Controls) container.PerformLayout();
            using Bitmap bitmap = new(size.Width, size.Height);
            root.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            bitmap.Save(path);
        }
        finally { host.Controls.Remove(root); host.Close(); form.Controls.Add(root); root.Dock = DockStyle.Fill; }
    }

    private sealed class QuietPreviewHost : Form
    {
        protected override bool ShowWithoutActivation => true;
        internal QuietPreviewHost()
        {
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            Opacity = 0;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-16000, -16000);
        }
    }
}
