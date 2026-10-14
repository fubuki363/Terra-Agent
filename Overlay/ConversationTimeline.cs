using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace TerraWikiOverlay;

internal sealed class ConversationTimeline : UserControl
{
    private readonly ConversationScrollPanel _stack = new();
    private readonly Button _latest = new();
    private readonly System.Windows.Forms.Timer _settleScroll = new() { Interval = 80 };
    private int _settleTicks;
    private bool _layingOut;

    public ConversationTimeline()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(19, 23, 30);
        _stack.Dock = DockStyle.Fill;
        _stack.AutoScroll = true;
        _stack.FlowDirection = FlowDirection.TopDown;
        _stack.WrapContents = false;
        _stack.Padding = new Padding(14, 22, 14, 42);
        _stack.BackColor = BackColor;
        _stack.Scroll += (_, _) => UpdateLatestButton();
        _stack.MouseWheel += (_, _) => QueueUpdateLatestButton();
        _stack.ClientSizeChanged += (_, _) => ResizeCards();
        Controls.Add(_stack);
        _latest.Text = "↓ 回到最新";
        _latest.Size = new Size(114, 34);
        _latest.BackColor = Color.FromArgb(44, 58, 76);
        _latest.ForeColor = Color.FromArgb(211, 229, 246);
        _latest.FlatStyle = FlatStyle.Flat;
        _latest.FlatAppearance.BorderColor = Color.FromArgb(74, 97, 121);
        _latest.Cursor = Cursors.Hand;
        _latest.Visible = false;
        _latest.Click += (_, _) => ScrollToLatest();
        Controls.Add(_latest);
        _settleScroll.Tick += (_, _) =>
        {
            ScrollToLatest();
            if (++_settleTicks >= 2) _settleScroll.Stop();
        };
        _latest.BringToFront();
        Resize += (_, _) =>
        {
            ResizeCards();
            _latest.Location = new Point(Math.Max(8, (Width - _latest.Width) / 2), Math.Max(8, Height - 45));
        };
    }

    public void ClearMessages()
    {
        _settleScroll.Stop();
        _stack.SuspendLayout();
        try
        {
            while (_stack.Controls.Count > 0)
            {
                Control control = _stack.Controls[0];
                _stack.Controls.RemoveAt(0);
                control.Dispose();
            }
            _stack.AutoScrollPosition = Point.Empty;
        }
        finally { _stack.ResumeLayout(); }
        _latest.Visible = false;
    }

    public void AppendMessage(string sender, string markdown, Color accent, DateTime? timestamp = null)
    {
        bool follow = IsNearBottom() && !_stack.Controls.OfType<ConversationMessageCard>().Any(card => card.HasSelection);
        ConversationMessageCard card = new(sender, markdown, accent, timestamp ?? DateTime.Now, AvailableCardWidth());
        card.LinkClicked += OpenLink;
        card.ScrollRequested += ScrollConversation;
        _stack.Controls.Add(card);
        ResizeCards();
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() =>
            {
                if (IsDisposed || card.IsDisposed) return;
                if (follow)
                {
                    if (sender == "Terra Agent")
                    {
                        int top = card.Top - _stack.AutoScrollPosition.Y - _stack.Padding.Top;
                        SetScroll(top);
                    }
                    else ScrollToLatest();
                }
                UpdateLatestButton();
            });
        }
    }

    public void CompleteActivities()
    {
        int position = -_stack.AutoScrollPosition.Y;
        _stack.SuspendLayout();
        try
        {
            foreach (ConversationMessageCard card in _stack.Controls.OfType<ConversationMessageCard>())
                card.CompleteActivity();
        }
        finally { _stack.ResumeLayout(); }
        SetScroll(position);
    }

    public void ScrollToLatest()
    {
        _stack.PerformLayout();
        SetScroll(int.MaxValue);
        _latest.Visible = false;
    }

    public void ShowLatestAfterLayout()
    {
        _settleTicks = 0;
        _settleScroll.Start();
    }

    private void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception exception) { MessageBox.Show(FindForm(), "无法打开链接：" + exception.Message, "Terra Agent"); }
    }

    private bool IsNearBottom() => !_stack.VerticalScroll.Visible
        || _stack.VerticalScroll.Maximum - _stack.VerticalScroll.LargeChange + 1 - _stack.VerticalScroll.Value < 90;

    private void UpdateLatestButton() => _latest.Visible = !IsNearBottom();
    private void QueueUpdateLatestButton()
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) UpdateLatestButton(); });
    }

    private void SetScroll(int position)
    {
        int maximum = Math.Max(0, _stack.VerticalScroll.Maximum - _stack.VerticalScroll.LargeChange + 1);
        _stack.AutoScrollPosition = new Point(0, Math.Clamp(position, 0, maximum));
    }

    private void ScrollConversation(int delta)
    {
        _settleScroll.Stop();
        if (delta == 0) return;
        int lines = SystemInformation.MouseWheelScrollLines;
        int pixels = lines < 0 ? Math.Max(80, _stack.ClientSize.Height - 48) : Math.Max(36, lines * 18);
        int steps = Math.Max(1, Math.Abs(delta) / SystemInformation.MouseWheelScrollDelta);
        SetScroll(-_stack.AutoScrollPosition.Y - Math.Sign(delta) * pixels * steps);
        UpdateLatestButton();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _settleScroll.Dispose();
        base.Dispose(disposing);
    }

    private int AvailableCardWidth() => Math.Max(240, _stack.ClientSize.Width - _stack.Padding.Horizontal);

    private void ResizeCards()
    {
        if (_layingOut) return;
        _layingOut = true;
        try
        {
            int width = AvailableCardWidth();
            foreach (Control control in _stack.Controls)
                control.Width = Math.Max(200, width - control.Margin.Horizontal);
        }
        finally { _layingOut = false; }
    }
}

internal sealed class ConversationScrollPanel : FlowLayoutPanel
{
    // Focusing a RichEdit must not move its parent while a mouse drag is
    // choosing text. Native ScrollToControl otherwise changes the hit position.
    protected override Point ScrollToControl(Control activeControl) => AutoScrollPosition;
}

internal sealed class ConversationMessageCard : Panel
{
    private readonly MarkdownRichTextBox _content = new();
    private readonly Label _senderLabel = new();
    private readonly Label _timeLabel = new();
    private readonly Button _copy = new();
    private readonly ContextMenuStrip _copyMenu = new();
    private readonly System.Windows.Forms.Timer _copyFeedback = new() { Interval = 1600 };
    private readonly Color _accent;
    private readonly string _sender;
    private readonly string _markdown;
    private bool _compact;
    private bool _rendering;
    private bool _fitting;
    private int _contentHeight;

    public bool HasSelection => _content.SelectionLength > 0;
    public event Action<string>? LinkClicked;
    public event Action<int>? ScrollRequested;

    public ConversationMessageCard(string sender, string markdown, Color accent, DateTime timestamp, int width)
    {
        _sender = sender;
        _markdown = markdown;
        _accent = accent;
        _compact = sender switch
        {
            "模型思考" => markdown.Contains("思考已完成", StringComparison.Ordinal),
            "Agent 技能" or "技能结果" => !markdown.Contains("```", StringComparison.Ordinal),
            _ => false
        };
        DoubleBuffered = true;
        Width = width;
        Height = _compact ? 50 : 92;
        Margin = sender == "你" ? new Padding(58, 10, 0, 12)
            : sender is "模型思考" or "Agent 技能" or "技能结果" ? new Padding(8, 3, 16, 3)
            : new Padding(0, 10, 0, 14);
        BackColor = sender == "你" ? Color.FromArgb(34, 44, 58)
            : sender is "错误" or "技能失败" or "技能未完成" ? Color.FromArgb(48, 31, 38)
            : Color.FromArgb(24, 29, 38);
        _senderLabel.Text = SenderTitle(sender);
        _senderLabel.AutoSize = true;
        _senderLabel.ForeColor = accent;
        _senderLabel.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        _senderLabel.Location = new Point(17, 12);
        Controls.Add(_senderLabel);
        _timeLabel.Text = timestamp.ToLocalTime().ToString("HH:mm");
        _timeLabel.ForeColor = Color.FromArgb(120, 136, 157);
        _timeLabel.Font = new Font("Segoe UI", 8f);
        _timeLabel.Size = new Size(60, 24);
        _timeLabel.TextAlign = ContentAlignment.MiddleRight;
        Controls.Add(_timeLabel);
        _copy.Text = "复制";
        _copy.Size = new Size(84, 28);
        _copy.FlatStyle = FlatStyle.Flat;
        _copy.FlatAppearance.BorderSize = 0;
        _copy.BackColor = BackColor;
        _copy.ForeColor = Color.FromArgb(166, 186, 209);
        _copy.Font = new Font("Microsoft YaHei UI", 8f);
        _copy.Cursor = Cursors.Hand;
        _copy.Click += (_, _) =>
        {
            bool selected = HasSelection;
            bool copied = selected ? _content.CopySelection() : OverlayClipboard.TryCopy(_content.Text.TrimEnd());
            _copy.Text = copied ? "已复制" : "重试";
            _copyFeedback.Stop();
            _copyFeedback.Start();
        };
        _copyFeedback.Tick += (_, _) => { _copyFeedback.Stop(); UpdateCopyLabel(); };
        Controls.Add(_copy);

        ToolStripMenuItem selection = new("复制所选文字", null, (_, _) => _content.CopySelection());
        _copyMenu.Items.Add(selection);
        _copyMenu.Items.Add("复制这条消息（纯文本）", null, (_, _) => OverlayClipboard.TryCopy(_content.Text.TrimEnd()));
        _copyMenu.Items.Add("复制这条消息（Markdown）", null, (_, _) => OverlayClipboard.TryCopy(_markdown));
        _copyMenu.Items.Add(new ToolStripSeparator());
        _copyMenu.Items.Add("全选这条消息", null, (_, _) => { _content.Focus(); _content.SelectAll(); });
        _copyMenu.Opening += (_, _) => selection.Enabled = HasSelection;
        _content.ContextMenuStrip = _copyMenu;
        _content.SelectionChanged += (_, _) => UpdateCopyLabel();
        _content.BackColor = BackColor;
        _content.ForeColor = Color.FromArgb(226, 232, 242);
        _content.ScrollBars = RichTextBoxScrollBars.None;
        _content.WordWrap = true;
        _content.Dock = DockStyle.Fill;
        _content.ContentsResized += (_, args) =>
        {
            _contentHeight = args.NewRectangle.Height;
            if (!_rendering) FitHeight();
        };
        _content.LinkClicked += (_, args) => { if (args.LinkText is not null) LinkClicked?.Invoke(args.LinkText); };
        _content.WheelRequested += delta => ScrollRequested?.Invoke(delta);
        Controls.Add(_content);
        _content.BringToFront();
        ApplyLayout();
        _rendering = true;
        _content.RenderMarkdown(markdown.Trim());
        _content.Select(0, 0);
        _rendering = false;
        FitHeight();
        MouseWheel += (_, args) => ScrollRequested?.Invoke(args.Delta);
        _senderLabel.MouseWheel += (_, args) => ScrollRequested?.Invoke(args.Delta);
        _timeLabel.MouseWheel += (_, args) => ScrollRequested?.Invoke(args.Delta);
        Resize += (_, _) => ApplyLayout();
    }

    public void CompleteActivity()
    {
        if (_compact || _sender is not ("模型思考" or "Agent 技能" or "技能结果")) return;
        _compact = true;
        string summary = _sender == "模型思考" ? "本轮思考已完成"
            : _markdown.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Replace("**", "") ?? "技能已完成";
        _content.Clear();
        _content.Text = summary.Length > 95 ? summary[..95] + "…" : summary;
        Height = 50;
        ApplyLayout();
    }

    private void UpdateCopyLabel() => _copy.Text = HasSelection ? "复制所选" : "复制";

    private void ApplyLayout()
    {
        Padding = _compact ? new Padding(114, 6, 70, 6) : new Padding(18, 44, 18, 18);
        _senderLabel.Location = new Point(17, _compact ? 9 : 12);
        _copy.Visible = !_compact;
        _copy.Location = new Point(Math.Max(0, Width - 103), 8);
        _timeLabel.Location = new Point(Math.Max(0, Width - (_compact ? 70 : 170)), _compact ? 7 : 8);
    }

    private void FitHeight()
    {
        if (_compact || _fitting || _contentHeight <= 0) return;
        int desired = Math.Max(92, _contentHeight + Padding.Vertical + 10);
        if (Height == desired) return;
        _fitting = true;
        try { Height = desired; }
        finally { _fitting = false; }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen border = new(Color.FromArgb(_sender == "你" ? 56 : 32, _accent));
        args.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _copyFeedback.Dispose(); _copyMenu.Dispose(); }
        base.Dispose(disposing);
    }

    private static string SenderTitle(string sender) => sender switch
    {
        "你" => "你", "Terra Agent" => "✦ Terra Agent", "模型思考" => "思考过程",
        "Agent 技能" => "工具调用", "技能结果" => "执行结果", "错误" => "执行错误",
        "技能未完成" or "技能失败" => "未完成", _ => sender
    };
}
