using System.Diagnostics;
using System.Drawing.Drawing2D;
using TerraWikiAI.Services;

namespace TerraWikiOverlay;

internal sealed class FloatingAssistantForm : Form
{
    private static readonly Color Background = Color.FromArgb(19, 23, 30);
    private static readonly Color Surface = Color.FromArgb(25, 31, 41);
    private static readonly Color SurfaceRaised = Color.FromArgb(29, 37, 49);
    private static readonly Color Accent = Color.FromArgb(102, 183, 255);
    private static readonly Color AccentPurple = Color.FromArgb(145, 128, 255);
    private static readonly Color Muted = Color.FromArgb(151, 156, 168);
    private const int BallSize = 104;
    private const int ExpandedWidth = 1180;
    private const int ExpandedHeight = 820;

    private readonly OverlayAskService _assistant = new();
    private readonly Panel _expandedPanel = new();
    private readonly Panel _contentHost = new();
    private readonly ConversationTimeline _conversation = new();
    private readonly QuickReferencePanel _quickReference = new();
    private readonly FullEncyclopediaPanel _encyclopedia;
    private readonly WorldSearchPanel _worldSearch;
    private readonly TextBox _input = new();
    private readonly Label _inputCounter = new();
    private readonly CheckBox _attachScreenshot = new();
    private readonly Button _sendButton = new();
    private readonly Label _status = new();
    private readonly Label _usage = new();
    private readonly Label _agentActivity = new();
    private readonly Button _stopNavigationButton = new();
    private readonly Button _nextNavigationButton = new();
    private readonly Button _cancelButton = new();
    private readonly TableLayoutPanel _bodyLayout = new();
    private Control? _sidebar;
    private bool _sidebarVisible = true;
    private Rectangle _expandedBounds;
    private Rectangle _beforeMaximize;
    private int _visibleHistoryTurns = 20;
    private readonly ListBox _sessionList = new();
    private readonly Button _newSessionButton = new();
    private readonly Button _deleteSessionButton = new();
    private readonly ContextMenuStrip _ballMenu = new();
    private Button? _quickReferenceButton;
    private readonly NotifyIcon _trayIcon = new();
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 40 };
    private readonly ToolTip _toolTip = new();
    private AssistantSettingsForm? _settingsWindow;

    private bool _collapsed;
    private bool _busy;
    private bool _hoveringBall;
    private bool _loadingSessions;
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragStart;
    private Point _formStart;
    private float _animationPhase;
    private DateTime _lastNavigationRefreshUtc;

    public FloatingAssistantForm()
    {
        _encyclopedia = new FullEncyclopediaPanel(_assistant.DataDirectory);
        _worldSearch = new WorldSearchPanel(_assistant);
        Text = "Terra Agent";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        BackColor = Background;
        Padding = new Padding(6);
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        KeyPreview = true;
        BuildExpandedPanel();
        BuildBallMenu();
        BuildTrayIcon();
        ClientSize = new Size(ExpandedWidth, ExpandedHeight);
        _expandedPanel.Visible = true;

        _animationTimer.Tick += (_, _) =>
        {
            _animationPhase += _busy ? 0.18f : 0.065f;
            if (_animationPhase > MathF.Tau)
                _animationPhase -= MathF.Tau;
            NavigationSnapshot navigation = _assistant.Navigation;
            _stopNavigationButton.Visible = navigation.Active;
            _nextNavigationButton.Visible = navigation.Active && navigation.TargetCount > 1;
            if (navigation.Active && DateTime.UtcNow - _lastNavigationRefreshUtc > TimeSpan.FromSeconds(1.25))
            {
                _lastNavigationRefreshUtc = DateTime.UtcNow;
                _assistant.RequestNavigationRefresh();
            }
            if (!_busy && !_collapsed && navigation.Active)
            {
                _agentActivity.Text = NavigationStatusText(navigation);
                _agentActivity.ForeColor = navigation.DistanceTiles <= 8
                    ? Color.FromArgb(126, 235, 158)
                    : Color.FromArgb(255, 205, 96);
            }
            else if (!_busy && !navigation.Active && (_agentActivity.Text.StartsWith("● 正在导航", StringComparison.Ordinal)
                || _agentActivity.Text.StartsWith("● 已到达", StringComparison.Ordinal)))
            {
                _agentActivity.Text = "● 导航已停止 · Agent 已就绪";
                _agentActivity.ForeColor = Color.FromArgb(126, 218, 190);
            }
            if (_collapsed)
                Invalidate();
        };
        _animationTimer.Start();
        MouseEnter += (_, _) => { _hoveringBall = true; Invalidate(); };
        MouseLeave += (_, _) => { _hoveringBall = false; Invalidate(); };

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
            Activate();
            BringToFront();
        };
        Resize += (_, _) => { if (!_collapsed && IsHandleCreated) ApplyExpandedRegion(); };
        KeyDown += (_, args) =>
        {
            if (args.Control && args.KeyCode == Keys.N) { CreateNewSession(); args.SuppressKeyPress = true; }
            else if (args.Control && args.KeyCode == Keys.L) { _input.Focus(); args.SuppressKeyPress = true; }
            else if (args.KeyCode == Keys.Escape)
            {
                if (_busy) _assistant.CancelCurrentRequest(); else Collapse();
                args.SuppressKeyPress = true;
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!NativeMethods.RegisterHotKey(Handle, NativeMethods.HotkeyId, NativeMethods.ModControl | NativeMethods.ModShift, NativeMethods.VkT))
            _status.Text = "全局快捷键注册失败，可直接点击悬浮球。";
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_collapsed)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float pulse = (MathF.Sin(_animationPhase) + 1f) / 2f;
        NavigationSnapshot navigation = _assistant.Navigation;
        bool navigating = navigation.Active;
        bool arrived = navigating && navigation.DistanceTiles <= 8f;
        Color navigationColor = arrived ? Color.FromArgb(82, 232, 142) : Color.FromArgb(255, 190, 62);
        int inset = 7 - (int)MathF.Round(pulse * 3f);
        int glowAlpha = 42 + (int)(pulse * 38f) + (_hoveringBall ? 30 : 0);

        using Pen outerGlow = new(Color.FromArgb(Math.Min(170, glowAlpha + (navigating ? 30 : 0)), navigating ? navigationColor : Accent), 4f + pulse * 3f);
        e.Graphics.DrawEllipse(outerGlow, inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
        Rectangle orb = new(10, 10, Width - 21, Height - 21);
        using LinearGradientBrush fill = new(
            orb,
            navigating ? Color.FromArgb(255, 215, 92) : Color.FromArgb(46, 224, 255),
            navigating ? Color.FromArgb(239, 106, 68) : AccentPurple,
            35f + pulse * 35f);
        e.Graphics.FillEllipse(fill, orb);
        using Pen ring = new(Color.FromArgb(225, 240, 255), 2f);
        e.Graphics.DrawEllipse(ring, orb);

        if (navigating && !_busy)
        {
            DrawNavigationArrow(e.Graphics, navigation, pulse, arrived);
            string distanceText = arrived ? "到达" : navigation.DistanceTiles >= 1000
                ? $"{navigation.DistanceTiles / 1000f:0.#}k格"
                : $"{navigation.DistanceTiles:0}格";
            Rectangle distanceBounds = new(14, 70, Width - 28, 20);
            using Font distanceFont = new("Microsoft YaHei UI", 8.2f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, distanceText, distanceFont, distanceBounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            float angle = _animationPhase * (_busy ? 2.1f : 1f);
            int dots = _busy ? 3 : 1;
            for (int i = 0; i < dots; i++)
            {
                float dotAngle = angle + i * MathF.Tau / dots;
                float x = Width / 2f + MathF.Cos(dotAngle) * 39f;
                float y = Height / 2f + MathF.Sin(dotAngle) * 39f;
                float dotSize = _busy ? 7f : 5f;
                using SolidBrush dot = new(Color.FromArgb(235, Color.White));
                e.Graphics.FillEllipse(dot, x - dotSize / 2f, y - dotSize / 2f, dotSize, dotSize);
            }

            using Font font = new("Segoe UI", 17f + pulse, FontStyle.Bold, GraphicsUnit.Point);
            TextRenderer.DrawText(e.Graphics, _busy ? "•••" : "AI", font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_collapsed && e.Button == MouseButtons.Left)
            BeginDrag();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ContinueDrag();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        bool click = EndDrag();
        if (_collapsed && click) Expand();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) _dragging = false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        WindowPlacement.Save(_assistant.DataDirectory, _collapsed ? _expandedBounds : Bounds);
        if (_settingsWindow is { IsDisposed: false })
            _settingsWindow.Close();
        _animationTimer.Stop();
        _animationTimer.Dispose();
        _toolTip.Dispose();
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
        _expandedPanel.Visible = false;
        Controls.Add(_expandedPanel);

        TableLayoutPanel shell = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _expandedPanel.Controls.Add(shell);
        shell.Controls.Add(BuildHeader(), 0, 0);

        Panel statusPanel = new() { Dock = DockStyle.Fill, BackColor = Background };
        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(22, 0, 10, 0);
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Muted;
        statusPanel.Controls.Add(_status);
        _usage.Dock = DockStyle.Right;
        _usage.Width = 220;
        _usage.TextAlign = ContentAlignment.MiddleRight;
        _usage.Padding = new Padding(0, 0, 16, 0);
        _usage.ForeColor = Color.FromArgb(110, 189, 221);
        _usage.Cursor = Cursors.Hand;
        _usage.Click += (_, _) => OpenSettings(2);
        statusPanel.Controls.Add(_usage);
        _usage.BringToFront();
        shell.Controls.Add(statusPanel, 0, 1);

        TableLayoutPanel body = _bodyLayout;
        body.Dock = DockStyle.Fill;
        body.ColumnCount = 2;
        body.RowCount = 1;
        body.Margin = Padding.Empty;
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _sidebar = BuildSessionSidebar();
        body.Controls.Add(_sidebar, 0, 0);
        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = Background;
        Control conversationArea = BuildConversationArea();
        conversationArea.Dock = DockStyle.Fill;
        _contentHost.Controls.Add(conversationArea);
        _quickReference.Visible = false;
        _quickReference.BackRequested += ShowConversation;
        _quickReference.AskRequested += question =>
        {
            ShowConversation();
            _input.Text = question;
            _input.SelectionStart = _input.TextLength;
            _input.Focus();
        };
        _contentHost.Controls.Add(_quickReference);
        _encyclopedia.Visible = false;
        _encyclopedia.BackRequested += ShowConversation;
        _encyclopedia.CuratedRequested += ShowQuickReference;
        _encyclopedia.AskRequested += question =>
        {
            ShowConversation();
            _input.Text = question;
            _input.SelectionStart = _input.TextLength;
            _input.Focus();
        };
        _contentHost.Controls.Add(_encyclopedia);
        _worldSearch.Visible = false;
        _worldSearch.BackRequested += ShowConversation;
        _contentHost.Controls.Add(_worldSearch);
        body.Controls.Add(_contentHost, 1, 0);
        shell.Controls.Add(body, 0, 2);
    }

    private Control BuildHeader()
    {
        Panel header = new() { Dock = DockStyle.Fill, BackColor = Surface, Cursor = Cursors.SizeAll };
        header.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginDrag(); };
        header.MouseMove += (_, _) => ContinueDrag();
        header.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) EndDrag(); };

        Panel brand = new() { Dock = DockStyle.Fill, BackColor = Surface };
        Label title = new()
        {
            Text = "✦  Terra Agent",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Bounds = new Rectangle(22, 9, 260, 31),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginDrag(); };
        title.MouseMove += (_, _) => ContinueDrag();
        title.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) EndDrag(); };
        Label subtitle = new()
        {
            Text = "随时提问 · 查找物体 · 跟随方向",
            ForeColor = Color.FromArgb(133, 151, 177),
            Font = new Font("Microsoft YaHei UI", 7.8f),
            Bounds = new Rectangle(22, 45, 320, 21),
            BackColor = Color.Transparent
        };
        brand.Controls.Add(title);
        brand.Controls.Add(subtitle);
        brand.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginDrag(); };
        brand.MouseMove += (_, _) => ContinueDrag();
        brand.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) EndDrag(); };
        header.Controls.Add(brand);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 14, 8, 14),
            BackColor = Surface
        };
        _quickReferenceButton = MakeHeaderButton("全图鉴", 82);
        _quickReferenceButton.Click += (_, _) =>
        {
            if (_quickReference.Visible || _encyclopedia.Visible) ShowConversation();
            else ShowEncyclopedia();
        };
        Button settings = MakeHeaderButton("设置", 72);
        settings.Click += (_, _) => OpenSettings();
        Button collapse = MakeHeaderButton("—", 44);
        collapse.Click += (_, _) => Collapse();
        Button close = MakeHeaderButton("×", 44);
        close.Click += (_, _) => Close();
        Button history = MakeHeaderButton("历史", 64);
        history.Click += (_, _) =>
        {
            _sidebarVisible = !_sidebarVisible;
            if (_sidebar is not null) _sidebar.Visible = _sidebarVisible;
            _bodyLayout.ColumnStyles[0].Width = _sidebarVisible ? 220 : 0;
        };
        Button maximize = MakeHeaderButton("□", 44);
        maximize.Click += (_, _) =>
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Bounds == area && _beforeMaximize.Width > 0) Bounds = _beforeMaximize;
            else { _beforeMaximize = Bounds; Bounds = area; }
        };
        Button locate = MakeHeaderButton("定位", 64);
        locate.Click += (_, _) => ShowWorldSearch();
        actions.Controls.AddRange(new Control[] { history, locate, _quickReferenceButton, settings, collapse, maximize, close });
        header.Controls.Add(actions);
        actions.BringToFront();
        return header;
    }

    private Control BuildSessionSidebar()
    {
        Panel sidebar = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 14, 14), BackColor = Surface };
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140f));
        sidebar.Controls.Add(layout);
        Label title = new()
        {
            Text = "对话历史",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
        };
        layout.Controls.Add(title, 0, 0);

        FlowLayoutPanel topActions = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, BackColor = Surface };
        _newSessionButton.Text = "+ 新对话";
        StyleSidebarButton(_newSessionButton, 102, Accent, Color.FromArgb(5, 35, 48));
        _newSessionButton.Click += (_, _) => CreateNewSession();
        _deleteSessionButton.Text = "删除";
        StyleSidebarButton(_deleteSessionButton, 60, SurfaceRaised, Color.White);
        _deleteSessionButton.Click += (_, _) => DeleteSelectedSession();
        topActions.Controls.Add(_newSessionButton);
        topActions.Controls.Add(_deleteSessionButton);
        layout.Controls.Add(topActions, 0, 1);

        Panel bottom = new() { Dock = DockStyle.Fill, BackColor = Surface };
        Button clearAll = MakeSidebarWideButton("清空全部历史");
        clearAll.SetBounds(0, 4, 190, 34);
        clearAll.Click += (_, _) => ClearAllHistory();
        Button clearCache = MakeSidebarWideButton("清理 Wiki 缓存");
        clearCache.SetBounds(0, 46, 190, 34);
        clearCache.Click += (_, _) =>
        {
            int count = _assistant.ClearWikiCache();
            _status.Text = $"已清理 {count} 个 Wiki 缓存页面。";
        };
        bottom.Controls.Add(clearAll);
        bottom.Controls.Add(clearCache);
        Button older = MakeSidebarWideButton("加载更早的对话");
        older.SetBounds(0, 88, 190, 34);
        older.Click += (_, _) => { _visibleHistoryTurns = Math.Min(100, _visibleHistoryTurns + 20); LoadCurrentConversation(); };
        bottom.Controls.Add(older);
        layout.Controls.Add(bottom, 0, 3);

        _sessionList.Dock = DockStyle.Fill;
        _sessionList.BackColor = Surface;
        _sessionList.ForeColor = Color.FromArgb(220, 228, 240);
        _sessionList.BorderStyle = BorderStyle.None;
        _sessionList.Font = new Font("Microsoft YaHei UI", 9f);
        _sessionList.DrawMode = DrawMode.OwnerDrawFixed;
        _sessionList.ItemHeight = 52;
        _sessionList.IntegralHeight = false;
        _sessionList.DrawItem += DrawSessionItem;
        _sessionList.SelectedIndexChanged += (_, _) => SwitchSession();
        layout.Controls.Add(_sessionList, 0, 2);
        return sidebar;
    }

    private void DrawSessionItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _sessionList.Items.Count || _sessionList.Items[e.Index] is not SessionListItem item)
            return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using SolidBrush background = new(selected ? Color.FromArgb(39, 50, 68) : Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (selected)
        {
            using SolidBrush accent = new(Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + 7, 3, e.Bounds.Height - 14);
        }
        Rectangle titleBounds = new(e.Bounds.X + 8, e.Bounds.Y + 6, e.Bounds.Width - 12, 22);
        Rectangle metaBounds = new(e.Bounds.X + 8, e.Bounds.Y + 29, e.Bounds.Width - 12, 18);
        string title = item.Session.Title.Length <= 17 ? item.Session.Title : item.Session.Title[..17] + "…";
        TextRenderer.DrawText(e.Graphics, title, _sessionList.Font, titleBounds, Color.White,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using Font metaFont = new("Microsoft YaHei UI", 8f);
        TextRenderer.DrawText(e.Graphics, $"{item.Session.TurnCount} 轮 · {item.Session.UpdatedUtc.ToLocalTime():MM-dd HH:mm}",
            metaFont, metaBounds, Muted, TextFormatFlags.NoPadding);
        using Pen divider = new(Color.FromArgb(43, 52, 69));
        e.Graphics.DrawLine(divider, e.Bounds.Left + 8, e.Bounds.Bottom - 1, e.Bounds.Right - 8, e.Bounds.Bottom - 1);
    }

    private Control BuildConversationArea()
    {
        TableLayoutPanel area = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 168f));

        _conversation.Dock = DockStyle.Fill;
        _conversation.BackColor = Background;
        area.Controls.Add(_conversation, 0, 0);

        Panel agentBar = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 7, 18, 7), BackColor = Color.FromArgb(24, 25, 30) };
        _stopNavigationButton.Text = "停止导航";
        _stopNavigationButton.Dock = DockStyle.Right;
        _stopNavigationButton.Width = 92;
        _stopNavigationButton.Visible = false;
        _stopNavigationButton.FlatStyle = FlatStyle.Flat;
        _stopNavigationButton.FlatAppearance.BorderColor = Color.FromArgb(195, 102, 86);
        _stopNavigationButton.BackColor = Color.FromArgb(70, 42, 48);
        _stopNavigationButton.ForeColor = Color.FromArgb(255, 205, 190);
        _stopNavigationButton.Font = new Font("Microsoft YaHei UI", 8.6f, FontStyle.Bold);
        _stopNavigationButton.Cursor = Cursors.Hand;
        _stopNavigationButton.Click += (_, _) => StopNavigation();
        agentBar.Controls.Add(_stopNavigationButton);
        _cancelButton.Text = "停止生成";
        _cancelButton.Dock = DockStyle.Right;
        _cancelButton.Width = 88;
        _cancelButton.FlatStyle = FlatStyle.Flat;
        _cancelButton.FlatAppearance.BorderSize = 0;
        _cancelButton.BackColor = Color.FromArgb(68, 47, 53);
        _cancelButton.ForeColor = Color.FromArgb(255, 205, 190);
        _cancelButton.Visible = false;
        _cancelButton.Click += (_, _) => _assistant.CancelCurrentRequest();
        agentBar.Controls.Add(_cancelButton);
        _nextNavigationButton.Text = "下一个";
        _nextNavigationButton.Dock = DockStyle.Right;
        _nextNavigationButton.Width = 72;
        _nextNavigationButton.FlatStyle = FlatStyle.Flat;
        _nextNavigationButton.FlatAppearance.BorderSize = 0;
        _nextNavigationButton.BackColor = SurfaceRaised;
        _nextNavigationButton.ForeColor = Color.White;
        _nextNavigationButton.Visible = false;
        _nextNavigationButton.Click += (_, _) => NextNavigationTarget();
        agentBar.Controls.Add(_nextNavigationButton);
        _agentActivity.Dock = DockStyle.Fill;
        _agentActivity.Text = "● 就绪 · 状态分析 / 箱子查找 / 物体定位 / 群系导航";
        _agentActivity.TextAlign = ContentAlignment.MiddleLeft;
        _agentActivity.AutoEllipsis = true;
        _agentActivity.ForeColor = Color.FromArgb(126, 218, 190);
        _agentActivity.Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold);
        agentBar.Controls.Add(_agentActivity);
        _stopNavigationButton.BringToFront();
        area.Controls.Add(agentBar, 0, 1);

        Panel composer = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), BackColor = SurfaceRaised };
        FlowLayoutPanel suggestions = new()
        {
            BackColor = SurfaceRaised,
            Bounds = new Rectangle(14, 7, 718, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        suggestions.Controls.Add(MakePromptChip("下一步做什么", "结合我当前的世界进度和装备，我下一步最适合做什么？"));
        suggestions.Controls.Add(MakePromptChip("分析当前装备", "读取我当前的装备并分析配装问题，给出最优先的改进建议。"));
        suggestions.Controls.Add(MakePromptChip("查找箱中物品", "帮我查找一件物品放在哪个箱子。"));
        suggestions.Controls.Add(MakePromptChip("查找世界物体", "帮我查找当前世界里的矿物、特定箱子、家具或其他物体并开始导航。"));
        composer.Controls.Add(suggestions);

        _input.Multiline = true;
        _input.AcceptsReturn = true;
        _input.ScrollBars = ScrollBars.Vertical;
        _input.BorderStyle = BorderStyle.FixedSingle;
        _input.BackColor = Color.FromArgb(19, 23, 32);
        _input.ForeColor = Color.White;
        _input.Font = new Font("Microsoft YaHei UI", 10.5f);
        _input.PlaceholderText = "向 Terra Agent 提问……  Enter 发送，Shift+Enter 换行";
        _input.SetBounds(14, 42, 718, 82);
        _input.KeyDown += InputOnKeyDown;
        _input.TextChanged += (_, _) => UpdateInputCounter();
        composer.Controls.Add(_input);

        _attachScreenshot.Text = "▣ 附带游戏截图";
        _attachScreenshot.ForeColor = Muted;
        _attachScreenshot.AutoSize = true;
        _attachScreenshot.SetBounds(14, 139, 180, 28);
        composer.Controls.Add(_attachScreenshot);
        _toolTip.SetToolTip(_attachScreenshot, "仅勾选时捕获游戏窗口，并发送压缩截图给 DeepSeek。");

        _inputCounter.Text = "0 / 2000";
        _inputCounter.ForeColor = Color.FromArgb(125, 140, 164);
        _inputCounter.Font = new Font("Microsoft YaHei UI", 8f);
        _inputCounter.TextAlign = ContentAlignment.MiddleRight;
        _inputCounter.SetBounds(548, 139, 76, 28);
        composer.Controls.Add(_inputCounter);

        _sendButton.Text = "发送  ↑";
        _sendButton.FlatStyle = FlatStyle.Flat;
        _sendButton.FlatAppearance.BorderSize = 0;
        _sendButton.BackColor = Accent;
        _sendButton.ForeColor = Color.FromArgb(5, 35, 48);
        _sendButton.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        _sendButton.SetBounds(634, 134, 98, 36);
        _sendButton.Click += async (_, _) => await SendQuestionAsync();
        composer.Controls.Add(_sendButton);
        composer.Resize += (_, _) => LayoutComposer(composer, suggestions);
        LayoutComposer(composer, suggestions);
        UpdateInputCounter();
        area.Controls.Add(composer, 0, 2);
        return area;
    }

    private void InputOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || e.Shift)
            return;
        e.SuppressKeyPress = true;
        _ = SendQuestionAsync();
    }

    private async Task SendQuestionAsync()
    {
        if (_busy || IsDisposed) return;
        string question = _input.Text.Trim();
        if (question.Length == 0)
            return;
        if (question.Length > 2000)
        {
            AppendMessage("错误", "问题请控制在 2000 个字符以内。", Color.Salmon);
            return;
        }

        _input.Clear();
        AppendMessage("你", question, Accent);
        SetBusy(true);
        Progress<OverlayMessage> progress = new(message =>
        {
            if (IsDisposed || Disposing) return;
            switch (message.Kind)
            {
                case OverlayMessageKind.Status:
                    _status.Text = message.Text;
                    _agentActivity.Text = "● Agent 正在规划  ·  " + message.Text;
                    _agentActivity.ForeColor = Color.FromArgb(118, 195, 255);
                    break;
                case OverlayMessageKind.Notice:
                    AppendMessage("提示", message.Text, Color.FromArgb(255, 205, 128));
                    break;
                case OverlayMessageKind.Reasoning:
                    AppendMessage("模型思考", message.Text, Color.FromArgb(176, 159, 255));
                    _agentActivity.Text = "● Agent 深度思考中";
                    _agentActivity.ForeColor = Color.FromArgb(176, 159, 255);
                    break;
                case OverlayMessageKind.ToolCall:
                    AppendMessage("Agent 技能", "🔧 " + message.Text, Color.FromArgb(92, 214, 190));
                    _agentActivity.Text = "● 技能执行中  ·  " + message.Text;
                    _agentActivity.ForeColor = Color.FromArgb(92, 214, 190);
                    break;
                case OverlayMessageKind.ToolResult:
                    AppendMessage("技能结果", "✓ " + message.Text, Color.FromArgb(126, 218, 190));
                    _agentActivity.Text = "● 技能已返回  ·  " + message.Text;
                    _agentActivity.ForeColor = Color.FromArgb(126, 218, 190);
                    break;
                case OverlayMessageKind.ToolError:
                    AppendMessage("技能未完成", "⚠ " + message.Text, Color.FromArgb(255, 178, 112));
                    _agentActivity.Text = "● 技能未完成  ·  " + message.Text;
                    _agentActivity.ForeColor = Color.FromArgb(255, 178, 112);
                    break;
                case OverlayMessageKind.Answer:
                    _conversation.CompleteActivities();
                    AppendMessage("Terra Agent", RemoveReferenceMarkers(message.Text), AccentPurple);
                    _agentActivity.Text = "● Agent 已完成  ·  等待下一个问题";
                    _agentActivity.ForeColor = Color.FromArgb(126, 218, 190);
                    break;
                case OverlayMessageKind.Source:
                    break;
                case OverlayMessageKind.Error:
                    AppendMessage("错误", message.Text, Color.Salmon);
                    _agentActivity.Text = "● Agent 执行失败  ·  " + message.Text;
                    _agentActivity.ForeColor = Color.Salmon;
                    break;
            }
        });

        try
        {
            await _assistant.AskAsync(question, _attachScreenshot.Checked, progress);
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
            SetBusy(false);
            RefreshSessions(preserveTranscript: true);
            RefreshStatus();
            _input.Focus();
            }
        }
    }

    private void RefreshSessions(bool preserveTranscript = false)
    {
        _loadingSessions = true;
        try
        {
            string active = _assistant.ActiveSessionId;
            _sessionList.Items.Clear();
            foreach (OverlaySessionInfo session in _assistant.ListSessions())
            {
                SessionListItem item = new(session);
                _sessionList.Items.Add(item);
                if (session.Id.Equals(active, StringComparison.OrdinalIgnoreCase))
                    _sessionList.SelectedItem = item;
            }
        }
        finally
        {
            _loadingSessions = false;
        }
        if (!preserveTranscript)
            LoadCurrentConversation();
    }

    private void LoadCurrentConversation()
    {
        _conversation.ClearMessages();
        IReadOnlyList<ConversationTurn> turns = _assistant.GetCurrentTurns();
        if (turns.Count == 0)
        {
            AppendMessage("Terra Agent", "# 新对话\n我是一个带本地技能的 Terraria Agent。你可以问进度、Boss、装备和配方，也可以说“帮我找魔镜在哪个箱子”或“带我去微光湖”。命中位置后，收起窗口即可跟随悬浮球箭头导航。", Color.FromArgb(166, 132, 255));
            return;
        }

        foreach (ConversationTurn turn in turns.TakeLast(_visibleHistoryTurns))
        {
            AppendMessage("你", turn.Question, Accent, turn.CreatedUtc);
            if (turn.AgentTraces.Count > 0)
            {
                foreach (ConversationAgentTrace trace in turn.AgentTraces)
                    AppendAgentTrace(trace);
            }
            else
            {
                foreach (ConversationToolTrace trace in turn.ToolTraces)
                {
                    AppendMessage(
                        trace.IsError ? "技能失败" : "Agent 技能",
                        (trace.IsError ? "⚠ " : "✓ ") + trace.DisplayName + "：" + trace.Summary,
                        trace.IsError ? Color.Salmon : Color.FromArgb(126, 218, 190),
                        trace.CreatedUtc);
                }
            }
            AppendMessage("Terra Agent", RemoveReferenceMarkers(turn.Answer), Color.FromArgb(166, 132, 255), turn.CreatedUtc);
        }
        _conversation.ShowLatestAfterLayout();
    }

    private void AppendAgentTrace(ConversationAgentTrace trace)
    {
        switch (trace.Kind)
        {
            case "reasoning":
                string retry = trace.Attempt > 1 ? $" · 重试 {trace.Attempt}" : "";
                AppendMessage(
                    "模型思考",
                    $"▸ 第 {trace.Step} 步{retry}思考已完成",
                    Color.FromArgb(132, 124, 166),
                    trace.CreatedUtc);
                break;
            case "tool_call":
                AppendMessage("Agent 技能",
                    $"第 {trace.Step} 步 · {trace.DisplayName} · {CompactArguments(trace.Arguments)}",
                    Color.FromArgb(135, 190, 178), trace.CreatedUtc);
                break;
            case "tool_result":
                AppendMessage(
                    trace.IsError ? "技能未完成" : "技能结果",
                    $"第 {trace.Step} 步 · {trace.Summary}",
                    trace.IsError ? Color.Salmon : Color.FromArgb(126, 218, 190),
                    trace.CreatedUtc);
                break;
        }
    }

    private static string CompactArguments(string json)
    {
        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            string compact = System.Text.Json.JsonSerializer.Serialize(document.RootElement);
            return compact.Length <= 72 ? compact : compact[..72] + "…";
        }
        catch
        {
            string compact = json.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return compact.Length <= 72 ? compact : compact[..72] + "…";
        }
    }

    private static string RemoveReferenceMarkers(string answer) =>
        System.Text.RegularExpressions.Regex.Replace(
            answer,
            @"\s*\[\d{1,2}\](?:\([^\r\n)]*\))?",
            "",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private void SwitchSession()
    {
        if (_loadingSessions || _busy || _sessionList.SelectedItem is not SessionListItem item)
            return;
        if (_assistant.ResumeSession(item.Session.Id))
        {
            ShowConversation();
            LoadCurrentConversation();
            RefreshStatus();
        }
    }

    private void CreateNewSession()
    {
        if (_busy)
            return;
        _assistant.NewSession();
        ShowConversation();
        RefreshSessions();
        RefreshStatus();
        _input.Focus();
    }

    private void DeleteSelectedSession()
    {
        if (_busy || _sessionList.SelectedItem is not SessionListItem item)
            return;
        if (MessageBox.Show(this, $"确定删除会话“{item.Session.Title}”？", "删除对话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _assistant.DeleteSession(item.Session.Id);
        RefreshSessions();
        RefreshStatus();
    }

    private void ClearAllHistory()
    {
        if (_busy || MessageBox.Show(this, "确定清空全部对话历史？此操作无法撤销。", "清空历史", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        int turns = _assistant.ClearAllHistory();
        RefreshSessions();
        _status.Text = $"已清空 {turns} 轮对话历史。";
    }

    private void OpenSettings(int tab = 0)
    {
        if (_settingsWindow is { IsDisposed: false })
        {
            _settingsWindow.SelectTab(tab);
            if (!_settingsWindow.Visible)
                _settingsWindow.Show(this);
            _settingsWindow.WindowState = FormWindowState.Normal;
            _settingsWindow.BringToFront();
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new AssistantSettingsForm(_assistant, tab)
        {
            TopMost = true
        };
        _settingsWindow.FormClosed += (_, _) =>
        {
            _settingsWindow = null;
            RefreshStatus();
        };
        _settingsWindow.Show(this);
        _settingsWindow.BringToFront();
        _settingsWindow.Activate();
        RefreshStatus();
    }

    private void AppendMessage(string sender, string text, Color color, DateTime? timestamp = null) =>
        _conversation.AppendMessage(sender, text, color, timestamp);

    private void RefreshStatus()
    {
        string key = _assistant.HasKey(out _) ? "Key 已就绪" : "未配置 Key";
        string gameData = !_assistant.Settings.GameContextSelection.HasAny
            ? "游戏技能无权限"
            : _assistant.Settings.IncludeMemoryContext ? "游戏技能按需可用" : "游戏技能已关闭";
        _status.Text = $"{key} · {DeepSeekClient.ModelName} · {gameData} · 会话 {_assistant.ActiveSessionId} · Ctrl+Shift+T";
        _usage.Text = ShortUsage(_assistant.UsageSummary);
        _toolTip.SetToolTip(_usage, _assistant.UsageSummary);
        _toolTip.SetToolTip(_status, _status.Text);
    }

    private void NextNavigationTarget()
    {
        if (_assistant.NextNavigationTarget())
        {
            NavigationSnapshot navigation = _assistant.Navigation;
            _agentActivity.Text = NavigationStatusText(navigation);
            Invalidate();
        }
        else
        {
            _agentActivity.Text = _assistant.Navigation.Active ? "● 导航只有一个目标" : "● 当前没有导航目标";
        }
    }

    private void StopNavigation()
    {
        _assistant.StopNavigation();
        _stopNavigationButton.Visible = false;
        _agentActivity.Text = "● 导航已停止  ·  Agent 已就绪";
        _agentActivity.ForeColor = Color.FromArgb(126, 218, 190);
        Invalidate();
    }

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
        _cancelButton.Visible = busy;
        _sendButton.Enabled = !busy && _input.TextLength is > 0 and <= 2000;
        _sendButton.Text = busy ? "执行中…" : "发送  ↑";
        _newSessionButton.Enabled = !busy;
        _deleteSessionButton.Enabled = !busy;
        _sessionList.Enabled = !busy;
        Invalidate();
    }

    private void Expand()
    {
        if (!_collapsed)
        {
            Show();
            Activate();
            BringToFront();
            return;
        }
        Point anchor = Location;
        _collapsed = false;
        Padding = new Padding(6);
        ShowInTaskbar = true;
        Region? previousRegion = Region;
        Region = null;
        previousRegion?.Dispose();
        MinimumSize = new Size(860, 600);
        Size desired = _expandedBounds.Size.Width > 0 ? _expandedBounds.Size : new Size(ExpandedWidth, ExpandedHeight);
        Rectangle area = Screen.FromPoint(anchor).WorkingArea;
        MinimumSize = new Size(Math.Min(860, area.Width - 24), Math.Min(600, area.Height - 24));
        ClientSize = new Size(Math.Min(desired.Width, area.Width - 24), Math.Min(desired.Height, area.Height - 24));
        ApplyExpandedRegion();
        Location = new Point(
            Math.Clamp(anchor.X - Width + BallSize, area.Left, area.Right - Width),
            Math.Clamp(anchor.Y, area.Top, area.Bottom - Height));
        _expandedPanel.Visible = true;
        if (_worldSearch.Visible) _worldSearch.FocusSearch();
        else if (_encyclopedia.Visible) _encyclopedia.FocusSearch();
        else if (_quickReference.Visible) _quickReference.FocusSearch();
        else _input.Focus();
        Invalidate();
    }

    private void ShowEncyclopedia()
    {
        Expand();
        _worldSearch.Visible = false;
        _quickReference.Visible = false;
        _encyclopedia.Visible = true;
        _encyclopedia.BringToFront();
        if (_quickReferenceButton is not null)
            _quickReferenceButton.BackColor = Color.FromArgb(43, 67, 90);
        _encyclopedia.Start();
    }

    private void ShowQuickReference()
    {
        Expand();
        _worldSearch.Visible = false;
        _encyclopedia.Visible = false;
        _quickReference.Visible = true;
        _quickReference.BringToFront();
        if (_quickReferenceButton is not null)
            _quickReferenceButton.BackColor = Color.FromArgb(43, 67, 90);
        _quickReference.FocusSearch();
    }

    private void ShowConversation()
    {
        _worldSearch.Visible = false;
        _quickReference.Visible = false;
        _encyclopedia.Visible = false;
        if (_quickReferenceButton is not null)
            _quickReferenceButton.BackColor = Surface;
        if (!_collapsed) _input.Focus();
    }

    private void ShowWorldSearch()
    {
        Expand();
        _quickReference.Visible = false;
        _encyclopedia.Visible = false;
        _worldSearch.Visible = true;
        _worldSearch.BringToFront();
        _worldSearch.FocusSearch();
    }

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

    private Button MakePromptChip(string text, string prompt)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 27,
            Padding = new Padding(9, 1, 9, 1),
            Margin = new Padding(0, 0, 7, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(34, 42, 57),
            ForeColor = Color.FromArgb(181, 198, 220),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(62, 77, 101);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(47, 59, 79);
        button.Click += (_, _) =>
        {
            _input.Text = prompt;
            _input.SelectionStart = _input.TextLength;
            _input.Focus();
        };
        return button;
    }

    private void LayoutComposer(Control composer, Control suggestions)
    {
        int contentWidth = Math.Max(320, composer.ClientSize.Width - 28);
        suggestions.SetBounds(14, 7, contentWidth, 30);
        _input.SetBounds(14, 42, contentWidth, 70);
        _sendButton.SetBounds(composer.ClientSize.Width - 112, 122, 98, 34);
        _inputCounter.SetBounds(composer.ClientSize.Width - 198, 125, 76, 28);
        _attachScreenshot.Top = 125;
    }

    private void UpdateInputCounter()
    {
        int count = _input.TextLength;
        _inputCounter.Text = $"{count} / 2000";
        _inputCounter.ForeColor = count > 2000
            ? Color.Salmon
            : count > 1750 ? Color.FromArgb(255, 205, 128) : Color.FromArgb(125, 140, 164);
        _sendButton.Enabled = !_busy && count is > 0 and <= 2000;
    }

    private void Collapse()
    {
        if (_collapsed) return;
        Point anchor = Location;
        _expandedBounds = Bounds;
        WindowPlacement.Save(_assistant.DataDirectory, _expandedBounds);
        _collapsed = true;
        Padding = Padding.Empty;
        ShowInTaskbar = false;
        _expandedPanel.Visible = false;
        MinimumSize = Size.Empty;
        ClientSize = new Size(BallSize, BallSize);
        using GraphicsPath path = new();
        path.AddEllipse(ClientRectangle);
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
        Rectangle area = Screen.FromPoint(anchor).WorkingArea;
        Location = new Point(
            Math.Clamp(anchor.X + Math.Max(0, _expandedBounds.Width - BallSize), area.Left, area.Right - Width),
            Math.Clamp(anchor.Y, area.Top, area.Bottom - Height));
        Invalidate();
    }


    private void BeginDrag()
    {
        _dragging = true;
        _dragMoved = false;
        _dragStart = Cursor.Position;
        _formStart = Location;
        Capture = true;
    }

    private void ContinueDrag()
    {
        if (!_dragging)
            return;
        Point delta = new(Cursor.Position.X - _dragStart.X, Cursor.Position.Y - _dragStart.Y);
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 4)
            _dragMoved = true;
        Location = new Point(_formStart.X + delta.X, _formStart.Y + delta.Y);
    }

    private bool EndDrag()
    {
        bool wasClick = _dragging && !_dragMoved;
        _dragging = false;
        Capture = false;
        return wasClick;
    }

    private static Point ClampLocation(Point point, Rectangle area, Size size) => new(
        Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));

    private static Button MakeHeaderButton(string text, int width)
    {
        Button button = new()
        {
            Text = text,
            Width = width,
            Height = 40,
            Margin = new Padding(2, 0, 2, 0),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Surface,
            Font = new Font("Microsoft YaHei UI", 9.5f)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 70, 92);
        return button;
    }

    private static void StyleSidebarButton(Button button, int width, Color backColor, Color foreColor)
    {
        button.Width = width;
        button.Height = 34;
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
    }

    private static Button MakeSidebarWideButton(string text)
    {
        Button button = new()
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Muted,
            Font = new Font("Microsoft YaHei UI", 9f)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(65, 76, 96);
        return button;
    }

    private sealed class SessionListItem
    {
        public SessionListItem(OverlaySessionInfo session) => Session = session;
        public OverlaySessionInfo Session { get; }
        public override string ToString() => Session.Title;
    }
}
