using System.Diagnostics;

namespace TerraWikiOverlay;

// Keep this HWND cached: collapsing never rebuilds or resizes the chat's WebView.
internal sealed class FloatingBallForm : Form
{
    private readonly Func<FloatingBallState> _readState;
    private readonly Action _expand;
    private readonly FloatingBallMotion _motion = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Stopwatch _clock = new();
    private LayeredBallSurface? _surface;
    private readonly FloatingBallTrailMotion _trailMotion = new();
    private FloatingBallTrailForm? _trail;
    private bool _dragging, _moved, _hovered, _reducedMotion;
    private Point _mouseStart, _windowStart;
    private double _previousTime;

    public FloatingBallForm(int size, Func<FloatingBallState> readState, Action expand)
    {
        _readState = readState;
        _expand = expand;
        Text = "Terra Agent 悬浮球";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(size, size);
        MinimumSize = MaximumSize = Size;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        _timer.Tick += (_, _) => RefreshState();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00080000 | 0x00000080; // NOACTIVATE | LAYERED | TOOLWINDOW
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _surface = new LayeredBallSurface(Width, Height);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _surface?.Dispose();
        _surface = null;
        base.OnHandleDestroyed(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _reducedMotion = LayeredBallSurface.ReducedMotion;
            _hovered = _dragging = _moved = false;
            _previousTime = 0;
            _clock.Restart();
            _trailMotion.Clear();
            _motion.Reset(_readState(), _reducedMotion);
            RefreshState();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _clock.Stop();
            _dragging = _hovered = false;
            _trail?.Hide();
            _trailMotion.Clear();
            Capture = false;
        }
    }

    public void RefreshState()
    {
        if (!Visible || _surface is null || IsDisposed) return;
        double now = _clock.Elapsed.TotalSeconds;
        FloatingBallState state = _readState();
        bool dragging = _dragging && _moved;
        FloatingBallFrame frame = _motion.Advance(now - _previousTime, state, _hovered, _dragging && !_moved, _reducedMotion, dragging);
        _previousTime = now;
        FloatingBallRenderer.Draw(_surface.Graphics, ClientSize, frame);
        _surface.Present(Handle, Location);
        PointF head = new(Left + Width / 2f, Top + Height / 2f);
        _trailMotion.Update(head, frame.Time, dragging, _reducedMotion);
        if (_trailMotion.Samples.Count > 1)
        {
            _trail ??= new FloatingBallTrailForm();
            _trail.Present(this, head, frame, _trailMotion.Samples);
        }
        else _trail?.Hide();
        _timer.Interval = _reducedMotion ? 250 : state.Busy || _hovered || _dragging || _motion.Settling || _trailMotion.Samples.Count > 0 ? 16 : 25;
    }

    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x001A) // WM_SETTINGCHANGE: respect Windows' animation preference live.
        {
            _reducedMotion = LayeredBallSurface.ReducedMotion;
            RefreshState();
        }
        base.WndProc(ref message);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; RefreshState(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; RefreshState(); }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button != MouseButtons.Left) return;
        BeginPointerDrag(Cursor.Position);
    }

    internal void BeginPointerDrag(Point pointer, bool capturePointer = true)
    {
        _dragging = true;
        _moved = false;
        _mouseStart = pointer;
        _windowStart = Location;
        if (capturePointer) Capture = true;
        RefreshState();
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        MovePointerDrag(Cursor.Position);
    }

    internal void MovePointerDrag(Point pointer)
    {
        if (!_dragging) return;
        Point delta = new(pointer.X - _mouseStart.X, pointer.Y - _mouseStart.Y);
        _moved |= Math.Abs(delta.X) + Math.Abs(delta.Y) > 4;
        if (_moved)
        {
            // Seed the trail at the old center, then sample movement on the animation clock.
            if (_trailMotion.Samples.Count == 0)
                _trailMotion.Update(new PointF(Left + Width / 2f, Top + Height / 2f), _clock.Elapsed.TotalSeconds, true, _reducedMotion);
            Location = new Point(_windowStart.X + delta.X, _windowStart.Y + delta.Y);
        }
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
        RefreshState();
        if (clicked) _expand();
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        base.OnMouseCaptureChanged(args);
        if (!Capture) { _dragging = false; RefreshState(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _trail?.Dispose(); _surface?.Dispose(); _surface = null; }
        base.Dispose(disposing);
    }
}
