using System.Drawing.Drawing2D;

namespace TerraWikiOverlay;

internal readonly record struct BallTrailSample(PointF Position, double Time);

internal sealed class FloatingBallTrailMotion
{
    internal const double Lifetime = .62;
    internal const float Reach = 126;
    private readonly List<BallTrailSample> _samples = new();
    internal IReadOnlyList<BallTrailSample> Samples => _samples;

    internal void Update(PointF head, double time, bool dragging, bool reducedMotion)
    {
        if (reducedMotion) { Clear(); return; }
        _samples.RemoveAll(sample => time - sample.Time > Lifetime || DistanceSquared(head, sample.Position) > Reach * Reach);
        if (dragging && (_samples.Count == 0 || DistanceSquared(_samples[^1].Position, head) >= 4))
            _samples.Add(new(head, time));
        if (_samples.Count > 48) _samples.RemoveRange(0, _samples.Count - 48);
    }

    internal void Clear() => _samples.Clear();
    private static float DistanceSquared(PointF a, PointF b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}

internal static class FloatingBallTrailRenderer
{
    internal static void Draw(Graphics graphics, Size size, PointF head, double time, bool dark, IReadOnlyList<BallTrailSample> samples)
    {
        graphics.ResetTransform();
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // The separate click-through canvas leaves the ball's face unobscured.
        using GraphicsPath face = new();
        face.AddEllipse(size.Width / 2f - 42, size.Height / 2f - 42, 84, 84);
        graphics.SetClip(face, CombineMode.Exclude);
        Color cyan = dark ? Color.FromArgb(110, 228, 255) : Color.FromArgb(18, 167, 201);
        Color blue = dark ? Color.FromArgb(140, 159, 255) : Color.FromArgb(65, 118, 230);
        PointF Local(PointF point) => new(size.Width / 2f + point.X - head.X, size.Height / 2f + point.Y - head.Y);
        for (int i = 1; i < samples.Count; i++)
        {
            BallTrailSample sample = samples[i];
            float strength = (float)Math.Pow(Math.Clamp(1 - (time - sample.Time) / FloatingBallTrailMotion.Lifetime, 0, 1), 1.5);
            if (strength < .01f) continue;
            PointF from = Local(samples[i - 1].Position), to = Local(sample.Position);
            for (int layer = 0; layer < 3; layer++)
            {
                float opacity = strength * (layer == 0 ? .08f : layer == 1 ? .23f : .85f);
                using Pen light = new(Color.FromArgb((int)(255 * opacity), layer == 2 ? cyan : blue),
                    (layer == 0 ? 28 : layer == 1 ? 14 : 3.5f) * (.35f + .65f * strength))
                    { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawLine(light, from, to);
            }
            if (i % 3 == 0)
            {
                float drift = (float)Math.Sin(sample.Time * 25 + time * 5) * 7;
                using SolidBrush spark = new(Color.FromArgb((int)(220 * strength), cyan));
                graphics.FillEllipse(spark, to.X - 1.4f, to.Y + drift - 1.4f, 2.8f, 2.8f);
            }
        }
        graphics.ResetClip();
    }
}

// A cached auxiliary canvas lets the trail extend past the 104px ball without changing its hit area.
internal sealed class FloatingBallTrailForm : Form
{
    private LayeredBallSurface? _surface;

    internal FloatingBallTrailForm()
    {
        Text = "Terra Agent 拖动光效";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(288, 288);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams value = base.CreateParams;
            value.ExStyle |= 0x08000000 | 0x00080000 | 0x00000080 | 0x00000020; // NOACTIVATE | LAYERED | TOOLWINDOW | TRANSPARENT
            return value;
        }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _surface = new LayeredBallSurface(Width, Height);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        _surface?.Dispose(); _surface = null;
        base.OnHandleDestroyed(e);
    }
    internal void Present(Form owner, PointF head, FloatingBallFrame frame, IReadOnlyList<BallTrailSample> samples)
    {
        _ = Handle;
        Location = new Point((int)head.X - Width / 2, (int)head.Y - Height / 2);
        FloatingBallTrailRenderer.Draw(_surface!.Graphics, ClientSize, head, frame.Time, frame.Dark > .5f, samples);
        _surface.Present(Handle, Location);
        if (!Visible) Show(owner);
    }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _surface?.Dispose(); _surface = null; }
        base.Dispose(disposing);
    }
}
