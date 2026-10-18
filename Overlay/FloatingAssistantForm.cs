using System.Diagnostics;
using System.Drawing.Drawing2D;
using TerraWikiAI.Services;

namespace TerraWikiOverlay;

internal sealed class FloatingAssistantForm : Form
{
    private static readonly Color Background = Color.White;
    private static readonly Color Accent = Color.FromArgb(65, 118, 230);
    private const int BallSize = 104;
    private const int ExpandedWidth = 1180;
    private const int ExpandedHeight = 820;
    private readonly OverlayAskService _assistant;
    private readonly FloatingBallForm _ball;
    private readonly Panel _expandedPanel = new();
    private readonly Panel _contentHost = new();
    private readonly HarnessChatView _conversation;
    private readonly HarnessFeaturesController _features;
    private readonly WikiBrowserView _wiki;
    private bool _darkMode;
    private readonly ContextMenuStrip _ballMenu = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 40 };
    private Rectangle _expandedBounds;
    private Rectangle _beforeMaximize;
    private int _visibleHistoryTurns = 20;
    private string _draft = "";
    private readonly Dictionary<string, string> _drafts = new();
    private bool _attachGameScreenshot;
    private string _lastNavigationUi = "";
    private bool _collapsed;
    private bool _busy;
    private float _animationPhase;
    private DateTime _lastNavigationRefreshUtc;

    public FloatingAssistantForm(string? dataDirectory = null)
    {
        _assistant = new OverlayAskService(dataDirectory);
        _conversation = new HarnessChatView(_assistant.DataDirectory, _assistant.Settings.ThemeMode);
        _features = new HarnessFeaturesController(_assistant, _conversation);
        _wiki = new WikiBrowserView(_assistant.DataDirectory);
        _darkMode = OverlayAppearance.IsDark(_assistant.Settings.ThemeMode);
        _ball = new FloatingBallForm(BallSize, DrawBall, Expand);
        Text = "Terra Agent";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        BackColor = Background;
        Padding = new Padding(4);
        Font = new Font("Microsoft YaHei UI", 9f);
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        KeyPreview = true;
        BuildExpandedPanel();
        BuildBallMenu();
        BuildTrayIcon();
        ApplyAppearance(_darkMode);
        ClientSize = new Size(ExpandedWidth, ExpandedHeight);
        _expandedPanel.Visible = true;

        _animationTimer.Tick += (_, _) =>
        {
            _animationPhase += _busy ? 0.18f : 0.065f;
            if (_animationPhase > MathF.Tau)
                _animationPhase -= MathF.Tau;
            NavigationSnapshot navigation = _assistant.Navigation;
            if (navigation.Active && DateTime.UtcNow - _lastNavigationRefreshUtc > TimeSpan.FromSeconds(1.25))
            {
                _lastNavigationRefreshUtc = DateTime.UtcNow;
                _assistant.RequestNavigationRefresh();
            }
            string navigationText = navigation.Active ? NavigationStatusText(navigation) : "";
            if (navigationText != _lastNavigationUi)
            {
                _lastNavigationUi = navigationText;
                _conversation.Post(new { type = "navigation", active = navigation.Active, value = navigationText, count = navigation.TargetCount });
            }
            if (_collapsed)
                _ball.Invalidate();
        };
        _animationTimer.Start();

        Shown += (_, _) =>
        {
            Rectangle? saved = WindowPlacement.Load(_assistant.DataDirectory);
            Rectangle area = saved.HasValue ? Screen.FromRectangle(saved.Value).WorkingArea
                : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Size desired = saved?.Size ?? new Size(ExpandedWidth, ExpandedHeight);
            ClientSize = new Size(Math.Min(desired.Width, area.Width - 24), Math.Min(desired.Height, area.Height - 24));
            MinimumSize = new Size(Math.Min(860, area.Width - 24), Math.Min(600, area.Height - 24));
            ApplyExpandedRegion();
            Location = saved.HasValue ? ClampLocation(saved.Value.Location, area, Size)
                : new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            _expandedBounds = Bounds;
            RefreshSessions();
            RefreshStatus();
            _ = _ball.Handle; // Prepare the small window before the first collapse.
            Activate();
            BringToFront();
        };
        Resize += (_, _) =>
        {
            if (_collapsed || !IsHandleCreated) return;
            ApplyExpandedRegion();
        };
        KeyDown += (_, args) =>
        {
            if (args.Control && args.KeyCode == Keys.N) { CreateNewSession(); args.SuppressKeyPress = true; }
            else if (args.Control && args.KeyCode == Keys.L)
            {
                _conversation.FocusInput();
                args.SuppressKeyPress = true;
            }
            else if (args.KeyCode == Keys.Escape)
            {
                if (_busy) _assistant.CancelCurrentRequest();
                else if (!_features.CancelSearch() && !_wiki.StopLoading()) Collapse();
                args.SuppressKeyPress = true;
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!NativeMethods.RegisterHotKey(Handle, NativeMethods.HotkeyId, NativeMethods.ModControl | NativeMethods.ModShift, NativeMethods.VkT))
            _conversation.Post(new { type = "notice", value = "全局快捷键注册失败，可直接点击悬浮球。" });
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        NativeMethods.UnregisterHotKey(Handle, NativeMethods.HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey && message.WParam.ToInt32() == NativeMethods.HotkeyId)
        {
            if (_collapsed) Expand(); else Collapse();
            return;
        }
        if (message.Msg == 0x0112 && (message.WParam.ToInt64() & 0xFFF0) == 0xF020)
        {
            Collapse(); // Taskbar/system minimize uses the same immediate ball transition.
            return;
        }
        if (message.Msg == 0x0084 && !_collapsed) // WM_NCHITTEST: resize the frameless panel.
        {
            Point point = PointToClient(new Point((short)(message.LParam.ToInt64() & 0xFFFF),
                (short)((message.LParam.ToInt64() >> 16) & 0xFFFF)));
            int edge = Math.Max(6, DeviceDpi / 16);
            bool left = point.X < edge, right = point.X >= Width - edge;
            bool top = point.Y < edge, bottom = point.Y >= Height - edge;
            int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17
                : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
            if (hit != 0) { message.Result = (IntPtr)hit; return; }
        }
        base.WndProc(ref message);
    }

    private void DrawBall(Graphics graphics, Rectangle bounds)
    {
        int width = bounds.Width, height = bounds.Height;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float pulse = (MathF.Sin(_animationPhase) + 1f) / 2f;
        NavigationSnapshot navigation = _assistant.Navigation;
        bool navigating = navigation.Active;
        bool arrived = navigating && navigation.DistanceTiles <= 8f;
        Color navigationColor = arrived ? Color.FromArgb(82, 232, 142) : Color.FromArgb(255, 190, 62);
        Rectangle orb = new(10, 10, width - 21, height - 21);
        using SolidBrush shadow = new(Color.FromArgb(_ball.Hovered ? 42 : 24, Color.Black));
        graphics.FillEllipse(shadow, 9, 12, width - 18, height - 18);
        using SolidBrush fill = new(navigating ? (arrived ? Color.FromArgb(44, 143, 89) : Color.FromArgb(184, 125, 30)) : (_darkMode ? Color.FromArgb(31, 33, 37) : Color.FromArgb(250, 251, 253)));
        graphics.FillEllipse(fill, orb);
        using Pen ring = new(navigating ? navigationColor : Color.FromArgb(217, 223, 233), _ball.Hovered ? 2f : 1f);
        graphics.DrawEllipse(ring, orb);
        if (_busy)
        {
            using Pen progress = new(Accent, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(progress, orb, _animationPhase * 70f, 110f);
        }

        if (navigating && !_busy)
        {
            DrawNavigationArrow(graphics, navigation, pulse, arrived);
            string distanceText = arrived ? "到达" : navigation.DistanceTiles >= 1000
                ? $"{navigation.DistanceTiles / 1000f:0.#}k格"
                : $"{navigation.DistanceTiles:0}格";
            Rectangle distanceBounds = new(14, 70, width - 28, 20);
            using Font distanceFont = new("Microsoft YaHei UI", 8.2f, FontStyle.Bold);
            TextRenderer.DrawText(graphics, distanceText, distanceFont, distanceBounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            using Font font = new("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Point);
            TextRenderer.DrawText(graphics, _busy ? "···" : "T", font, bounds, _darkMode ? Color.FromArgb(122, 170, 255) : Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _ball.Dispose();
        WindowPlacement.Save(_assistant.DataDirectory, _collapsed ? _expandedBounds : Bounds);
        _features.Dispose();
        _animationTimer.Stop();
        _animationTimer.Dispose();
        _assistant.Dispose();
        _ballMenu.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        base.OnFormClosed(e);
    }

    private void BuildBallMenu()
    {
        _ballMenu.Items.Add("展开对话", null, (_, _) => Expand());
        _ballMenu.Items.Add("全图鉴", null, (_, _) => ShowEncyclopedia());
        _ballMenu.Items.Add("查找世界物体", null, (_, _) => ShowWorldSearch());
        _ballMenu.Items.Add("常用速查", null, (_, _) => ShowQuickReference());
        _ballMenu.Items.Add("新建会话", null, (_, _) => { Expand(); CreateNewSession(); });
        _ballMenu.Items.Add("下一个导航目标", null, (_, _) => NextNavigationTarget());
        _ballMenu.Items.Add("停止导航", null, (_, _) => StopNavigation());
        _ballMenu.Items.Add("设置", null, (_, _) => { Expand(); OpenSettings(); });
        _ballMenu.Items.Add(new ToolStripSeparator());
        _ballMenu.Items.Add("退出", null, (_, _) => Close());
        ContextMenuStrip = _ballMenu;
        _ball.ContextMenuStrip = _ballMenu;
    }

    private void BuildTrayIcon()
    {
        ContextMenuStrip trayMenu = new();
        trayMenu.Items.Add("显示对话窗口", null, (_, _) => Expand());
        trayMenu.Items.Add("全图鉴", null, (_, _) => ShowEncyclopedia());
        trayMenu.Items.Add("查找世界物体", null, (_, _) => ShowWorldSearch());
        trayMenu.Items.Add("常用速查", null, (_, _) => ShowQuickReference());
        trayMenu.Items.Add("收起为悬浮球", null, (_, _) => Collapse());
        trayMenu.Items.Add("新建会话", null, (_, _) => { Expand(); CreateNewSession(); });
        trayMenu.Items.Add("下一个导航目标", null, (_, _) => NextNavigationTarget());
        trayMenu.Items.Add("停止导航", null, (_, _) => StopNavigation());
        trayMenu.Items.Add("设置", null, (_, _) => { Expand(); OpenSettings(); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => Close());
        _trayIcon.Icon = SystemIcons.Information;
        _trayIcon.Text = "Terra Agent 悬浮助手";
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => { if (_collapsed) Expand(); else Collapse(); };
    }

    private void BuildExpandedPanel()
    {
        _expandedPanel.Dock = DockStyle.Fill;
        _expandedPanel.BackColor = Background;
        Controls.Add(_expandedPanel);
        _contentHost.Dock = DockStyle.Fill;
        _expandedPanel.Controls.Add(_contentHost);
        _contentHost.Controls.Add(_conversation);
        _contentHost.Controls.Add(_wiki);
        _features.PageChanged += page =>
        {
            _wiki.Visible = false;
            if (page == "wiki") _wiki.Start();
        };
        _features.ThemeChanged += (mode, dark) => ApplyAppearance(dark);
        _features.SettingsChanged += RefreshStatus;
        _features.WikiBoundsChanged += bounds =>
        {
            Rectangle requested = new(bounds.X, bounds.Y, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height));
            Rectangle clipped = Rectangle.Intersect(_contentHost.ClientRectangle, requested);
            _wiki.Bounds = clipped;
            _wiki.Visible = bounds.Visible && _features.Page == "wiki" && clipped.Width > 40 && clipped.Height > 40;
            if (_wiki.Visible) _wiki.BringToFront();
        };
        _features.WikiCommand += async (action, value) => await _wiki.ExecuteAsync(action, value);
        _wiki.StateChanged += state => _conversation.Post(state);
        void Ask(string question) { ShowConversation(); _draft = question; _conversation.SetDraft(question); _conversation.FocusInput(); }
        _features.AskRequested += Ask;
        _wiki.AskRequested += Ask;
        _conversation.Command += (action, data) =>
        {
            if (_features.Handle(action, data)) return;
            string value = data.TryGetProperty("value", out var text) && text.ValueKind == System.Text.Json.JsonValueKind.String
                ? text.GetString() ?? "" : "";
            switch (action)
            {
                case "send": _draft = value; _ = SendQuestionAsync(); break;
                case "draft": _draft = value; break;
                case "screenshot": _attachGameScreenshot = value == "true"; break;
                case "cancel": _assistant.CancelCurrentRequest(); break;
                case "newSession": CreateNewSession(); break;
                case "resume": SwitchSession(value); break;
                case "deleteSession":
                    if (!_busy)
                    {
                        _assistant.DeleteSession(value); _drafts.Remove(value);
                        _draft = _drafts.GetValueOrDefault(_assistant.ActiveSessionId, ""); _conversation.SetDraft(_draft);
                        RefreshSessions(); RefreshStatus();
                    }
                    break;
                case "clearHistory":
                    if (!_busy)
                    {
                        _assistant.ClearAllHistory(); _drafts.Clear(); _draft = ""; _conversation.SetDraft("");
                        RefreshSessions(); RefreshStatus();
                    }
                    break;
                case "clearCache":
                    _conversation.Post(new { type = "notice", value = $"已清理 {_assistant.ClearWikiCache()} 个检索缓存页面。" });
                    break;
                case "older":
                    if (!_busy) { _visibleHistoryTurns = Math.Min(100, _visibleHistoryTurns + 20); LoadCurrentConversation(); }
                    break;
                case "wiki": ShowEncyclopedia(); break;
                case "locator": ShowWorldSearch(); break;
                case "settings": OpenSettings(); break;
                case "usage": OpenSettings(2); break;
                case "collapse": Collapse(); break;
                case "maximize": ToggleMaximize(); break;
                case "close": Close(); break;
                case "stopNavigation": StopNavigation(); break;
                case "nextTarget": NextNavigationTarget(); break;
                case "drag":
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
                    break;
            }
        };
    }

    private async Task SendQuestionAsync()
    {
        if (_busy || IsDisposed) return;
        string question = _draft.Trim();
        if (question.Length is < 1 or > 2000) return;
        _draft = "";
        string? previousTurnId = _assistant.GetCurrentTurns().LastOrDefault()?.Id;
        ConversationTurn pending = new() { Question = question, Status = "running" };
        _conversation.Post(new { type = "begin", turn = pending });
        SetBusy(true);
        string? notice = null;
        Progress<OverlayMessage> progress = new(message =>
        {
            if (IsDisposed || Disposing) return;
            switch (message.Kind)
            {
                case OverlayMessageKind.Status:
                    _conversation.Post(new { type = "status", value = message.Text }); break;
                case OverlayMessageKind.Stream:
                    _conversation.Post(new { type = "stream", stream = message.Stream }); break;
                case OverlayMessageKind.Reasoning:
                case OverlayMessageKind.AssistantMessage:
                case OverlayMessageKind.ToolCall:
                case OverlayMessageKind.ToolResult:
                case OverlayMessageKind.ToolError:
                    _conversation.Post(new { type = "trace", trace = message.Trace }); break;
                case OverlayMessageKind.Notice:
                case OverlayMessageKind.Error:
                    notice = message.Text; break;
            }
        });
        try { await _assistant.AskAsync(question, _attachGameScreenshot, progress); }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                // Progress<T> posts onto the UI queue; settle after those queued deltas.
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    ConversationTurn? final = _assistant.GetCurrentTurns().LastOrDefault();
                    if (final is null || final.Id == previousTurnId)
                    {
                        pending.Status = "error"; pending.Notice = notice ?? "请求未完成"; final = pending;
                    }
                    _conversation.Post(new { type = "finish", turn = final });
                    SetBusy(false); RefreshSessions(preserveTranscript: true); RefreshStatus();
                    _conversation.FocusInput();
                });
            }
        }
    }

    private void RefreshSessions(bool preserveTranscript = false)
    {
        _conversation.Post(new { type = "sessions", sessions = _assistant.ListSessions(), active = _assistant.ActiveSessionId });
        if (!preserveTranscript) LoadCurrentConversation();
    }

    private void LoadCurrentConversation()
    {
        _conversation.Post(new { type = "history", turns = _assistant.GetCurrentTurns().TakeLast(_visibleHistoryTurns).ToList() });
    }

    private void SwitchSession(string id)
    {
        if (_busy) return;
        _drafts[_assistant.ActiveSessionId] = _draft;
        if (!_assistant.ResumeSession(id)) return;
        _draft = _drafts.GetValueOrDefault(_assistant.ActiveSessionId, "");
        _conversation.SetDraft(_draft);
        ShowConversation(); RefreshSessions(); RefreshStatus();
    }

    private void CreateNewSession()
    {
        if (_busy) return;
        string previous = _assistant.ActiveSessionId;
        _drafts[previous] = _draft;
        string next = _assistant.NewSession();
        if (previous != next) { _draft = _drafts.GetValueOrDefault(next, ""); _conversation.SetDraft(_draft); }
        ShowConversation(); RefreshSessions(); RefreshStatus(); _conversation.FocusInput();
    }

    private void OpenSettings(int tab = 0)
    {
        Expand(); _features.Show("settings", tab); RefreshStatus();
    }

    private void ApplyAppearance(bool dark)
    {
        _darkMode = dark;
        Color background = dark ? Color.FromArgb(21, 21, 23) : Color.White;
        BackColor = _expandedPanel.BackColor = _contentHost.BackColor = background;
        _ballMenu.Renderer = new OverlayMenuRenderer(dark);
        if (_trayIcon.ContextMenuStrip is { } menu) menu.Renderer = new OverlayMenuRenderer(dark);
        _ = _wiki.ApplyThemeAsync(dark);
        _ball.Invalidate();
    }

    private void RefreshStatus()
    {
        _conversation.Post(new { type = "info", hasKey = _assistant.HasKey(out _),
            status = DeepSeekClient.ModelName + " · Ctrl+Shift+T", usage = ShortUsage(_assistant.UsageSummary) });
    }

    private void NextNavigationTarget() { _assistant.NextNavigationTarget(); _ball.Invalidate(); }
    private void StopNavigation() { _assistant.StopNavigation(); _ball.Invalidate(); }

    private static string NavigationStatusText(NavigationSnapshot navigation)
    {
        if (!navigation.Active)
            return "● Agent 已就绪";
        string direction = DirectionText(navigation.DeltaX, navigation.DeltaY);
        string index = navigation.TargetCount > 1 ? $" · 目标 {navigation.TargetIndex}/{navigation.TargetCount}" : "";
        return navigation.DistanceTiles <= 8
            ? $"● 已到达目标附近 · {navigation.Label}{index}"
            : $"● 正在导航 · {navigation.Label} · {direction} {navigation.DistanceTiles:0} 格{index}";
    }

    private static string DirectionText(float dx, float dy)
    {
        string horizontal = MathF.Abs(dx) < 8 ? "" : dx > 0 ? "右" : "左";
        string vertical = MathF.Abs(dy) < 8 ? "" : dy > 0 ? "下" : "上";
        return (horizontal + vertical).Length == 0 ? "附近" : horizontal + vertical;
    }

    private static void DrawNavigationArrow(Graphics graphics, NavigationSnapshot navigation, float pulse, bool arrived)
    {
        if (arrived)
        {
            float radius = 17f + pulse * 4f;
            using Pen arrivedRing = new(Color.White, 5f);
            graphics.DrawEllipse(arrivedRing, 52f - radius, 45f - radius, radius * 2f, radius * 2f);
            using SolidBrush center = new(Color.FromArgb(230, 255, 255, 255));
            graphics.FillEllipse(center, 47f, 40f, 10f, 10f);
            return;
        }

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(52f, 44f);
        graphics.RotateTransform(navigation.AngleRadians * 180f / MathF.PI);
        float scale = 1f + pulse * 0.08f;
        graphics.ScaleTransform(scale, scale);
        PointF[] arrow =
        {
            new(29f, 0f), new(5f, -18f), new(7f, -7f), new(-24f, -7f),
            new(-24f, 7f), new(7f, 7f), new(5f, 18f)
        };
        using SolidBrush brush = new(Color.FromArgb(242, Color.White));
        using Pen border = new(Color.FromArgb(130, 80, 40), 2f);
        graphics.FillPolygon(brush, arrow);
        graphics.DrawPolygon(border, arrow);
        graphics.Restore(state);
    }

    private static string ShortUsage(string summary)
    {
        var match = System.Text.RegularExpressions.Regex.Match(summary, @"今日：([\d,]+) 个问题 / ([\d,]+) 次 API / ([\d,]+) Token");
        return match.Success ? $"今日 {match.Groups[1].Value} 问 · {match.Groups[3].Value} Token" : "用量统计";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _conversation.Post(new { type = "busy", value = busy });
        _ball.Invalidate();
    }

    private void Expand()
    {
        if (!_collapsed)
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            return;
        }
        bool wasVisible = _ball.Visible;
        Point anchor = _ball.Location;
        _collapsed = false;
        Size desired = _expandedBounds.Size.Width > 0 ? _expandedBounds.Size : new Size(ExpandedWidth, ExpandedHeight);
        Rectangle area = Screen.FromPoint(anchor).WorkingArea;
        Size restoredSize = new(Math.Min(desired.Width, area.Width - 24), Math.Min(desired.Height, area.Height - 24));
        Point restoredLocation = ClampLocation(new Point(anchor.X - restoredSize.Width + BallSize, anchor.Y), area, restoredSize);
        SuspendLayout();
        try
        {
            MinimumSize = new Size(Math.Min(860, area.Width - 24), Math.Min(600, area.Height - 24));
            Bounds = new Rectangle(restoredLocation, restoredSize);
        }
        finally { ResumeLayout(performLayout: true); }
        _ball.Hide();
        if (wasVisible)
        {
            Show();
            Activate();
            BringToFront();
        }
        _conversation.FocusInput();
        Invalidate();
    }

    private void ShowEncyclopedia() { Expand(); _features.Show("wiki"); }
    private void ShowQuickReference() { Expand(); _features.Show("reference"); }
    private void ShowConversation() { _features.Show("chat"); if (!_collapsed) _conversation.FocusInput(); }
    private void ShowWorldSearch() { Expand(); _features.Show("locator"); }

    private void ApplyExpandedRegion()
    {
        const int radius = 18;
        Rectangle bounds = new(0, 0, Width, Height);
        using GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
        path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
        path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private void ToggleMaximize()
    {
        Rectangle area = Screen.FromControl(this).WorkingArea;
        bool restore = Bounds == area && _beforeMaximize.Width > 0;
        SuspendLayout();
        try
        {
            if (restore) Bounds = _beforeMaximize;
            else { _beforeMaximize = Bounds; Bounds = area; }
        }
        finally { ResumeLayout(true); }
        _expandedPanel.PerformLayout();
        _contentHost.PerformLayout();
    }

    private void Collapse()
    {
        if (_collapsed) return;
        Point anchor = Location;
        _expandedBounds = Bounds;
        bool wasVisible = Visible;
        _collapsed = true;
        Rectangle area = Screen.FromPoint(anchor).WorkingArea;
        _ball.Location = ClampLocation(new Point(anchor.X + Math.Max(0, _expandedBounds.Width - BallSize), anchor.Y), area, _ball.Size);
        Hide();
        if (wasVisible) _ball.Show();
        _ball.Invalidate();
        WindowPlacement.SaveInBackground(_assistant.DataDirectory, _expandedBounds);
    }


    private static Point ClampLocation(Point point, Rectangle area, Size size) => new(
        Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));

}
