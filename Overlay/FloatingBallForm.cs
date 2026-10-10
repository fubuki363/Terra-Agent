namespace TerraWikiOverlay;

// A cached, separate window keeps the chat control tree and HWND untouched.
internal sealed class FloatingBallForm : Form
{
    private readonly Action<Graphics, Rectangle> _paintBall;
    private readonly Action _expand;
    private bool _dragging;
    private bool _moved;
    private Point _mouseStart;
    private Point _windowStart;

    public bool Hovered { get; private set; }

    public FloatingBallForm(int size, Action<Graphics, Rectangle> paintBall, Action expand)
    {
        _paintBall = paintBall;
        _expand = expand;
        Text = "Terra Agent 悬浮球";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(19, 23, 30);
        DoubleBuffered = true;
        ClientSize = new Size(size, size);
        MinimumSize = MaximumSize = Size;
        using System.Drawing.Drawing2D.GraphicsPath circle = new();
        circle.AddEllipse(ClientRectangle);
        Region = new Region(circle);
        MouseEnter += (_, _) => { Hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { Hovered = false; Invalidate(); };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: showing/dragging the ball preserves game focus.
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        _paintBall(args.Graphics, ClientRectangle);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button != MouseButtons.Left) return;
        _dragging = true;
        _moved = false;
        _mouseStart = Cursor.Position;
        _windowStart = Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (!_dragging) return;
        Point delta = new(Cursor.Position.X - _mouseStart.X, Cursor.Position.Y - _mouseStart.Y);
        _moved |= Math.Abs(delta.X) + Math.Abs(delta.Y) > 4;
        Location = new Point(_windowStart.X + delta.X, _windowStart.Y + delta.Y);
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        if (args.Button != MouseButtons.Left || !_dragging) return;
        bool clicked = !_moved;
        _dragging = false;
        Capture = false;
        Rectangle area = Screen.FromControl(this).WorkingArea;
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        if (clicked) _expand();
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        base.OnMouseCaptureChanged(args);
        if (!Capture) _dragging = false;
    }
}
