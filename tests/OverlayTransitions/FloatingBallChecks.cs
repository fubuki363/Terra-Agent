using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using TerraWikiOverlay;

internal static class FloatingBallChecks
{
    internal static void Run(string[] args)
    {
        FloatingBallState idle = new(false, false, default);
        FloatingBallMotion slow = new(), fast = new();
        slow.Reset(idle, false); fast.Reset(idle, false);
        FloatingBallFrame a = default, b = default;
        for (int i = 0; i < 30; i++) a = slow.Advance(1d / 30, idle with { Busy = true, Dark = true }, true, true, false);
        for (int i = 0; i < 60; i++) b = fast.Advance(1d / 60, idle with { Busy = true, Dark = true }, true, true, false);
        Require(Math.Abs(a.Press - b.Press) < .00001 && Math.Abs(a.Reveal - b.Reveal) < .00001 &&
            Math.Abs(a.Busy - b.Busy) < .00001 && Math.Abs(a.Time - b.Time) < .00001 &&
            Math.Abs(a.Orbit - b.Orbit) < .00001, "Motion depends on frame rate.");
        slow.Reset(idle, false); fast.Reset(idle, false);
        a = slow.Advance(3, idle with { Busy = true }, true, false, false);
        for (int i = 0; i < 90; i++) b = fast.Advance(1d / 30, idle with { Busy = true }, true, false, false);
        Require(Math.Abs(a.Time - b.Time) < .00001 && Math.Abs(a.Hover - b.Hover) < .00001,
            "A delayed Windows timer tick slowed the animation clock.");

        FloatingBallState target = idle with { Navigation = Target(179, 120) };
        slow.Reset(target, false);
        a = slow.Advance(.05, target with { Navigation = Target(-179, 120) }, false, false, false);
        Require(Math.Abs(MathF.IEEERemainder(a.Angle - 179 * MathF.PI / 180, MathF.Tau)) < .02,
            "Navigation did not turn along the shortest arc across ±180°.");
        a = slow.Advance(double.NaN, target, false, false, false);
        Require(float.IsFinite(a.Angle) && double.IsFinite(a.Time), "Invalid elapsed time corrupted the animation.");

        FloatingBallState navigating = new(true, true, Target(-40, 240));
        slow.Reset(navigating, true);
        a = slow.Advance(.1, navigating, true, false, true);
        b = slow.Advance(.7, navigating, true, false, true);
        using (Bitmap first = Render(a))
        using (Bitmap second = Render(b))
            Require(EqualPixels(first, second), "Reduced-motion mode still animates decorative effects.");

        slow.Reset(idle, false);
        a = slow.Advance(.2, idle, false, false, false);
        b = slow.Advance(.5, idle, false, false, false);
        using (Bitmap first = Render(a))
        using (Bitmap second = Render(b))
            Require(DifferentPixels(first, second) > 250, "Idle animation is too subtle to see.");
        a = slow.Advance(.15, idle, true, false, false, true);
        b = slow.Advance(.1, idle, true, false, false, true);
        Require(a.Drag > .8f && b.Orbit > a.Orbit, "Dragging did not boost the energy orbit.");
        b = slow.Advance(.02, idle, false, false, false);
        Require(b.Release == 1, "Dropping the ball did not trigger its release ripple.");
        FloatingBallTrailMotion trace = new();
        for (int i = 0; i <= 16; i++) trace.Update(new PointF(i * 8, 0), i / 60d, true, false);
        Require(trace.Samples.Count > 2, "Dragging did not retain a trail.");
        trace.Update(new PointF(128, 0), 1, false, false);
        Require(trace.Samples.Count == 0, "Released trail did not expire.");
        trace.Update(new PointF(0, 0), 2, true, true);
        Require(trace.Samples.Count == 0, "Reduced motion retained a drag trail.");

        string? screenshots = null;
        int option = Array.IndexOf(args, "--screenshots");
        if (option >= 0 && option + 1 < args.Length) screenshots = Path.GetFullPath(args[option + 1]);
        if (screenshots is not null) Directory.CreateDirectory(screenshots);
        for (int dark = 0; dark < 2; dark++)
        for (int mode = 0; mode < 4; mode++)
        {
            FloatingBallState state = new(mode == 1 || mode == 2, dark == 1, mode >= 2 ? Target(-40, mode == 3 ? 3 : 240) : default);
            slow.Reset(state, false);
            a = slow.Advance(1, state, false, false, false);
            using Bitmap bitmap = Render(a);
            Require(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(52, 0).A == 0 && bitmap.GetPixel(103, 103).A == 0,
                "Floating ball has an opaque outer background.");
            Require(bitmap.GetPixel(52, 52).A == 255, "Floating ball body is not opaque.");
            Require(Enumerable.Range(0, 104).Any(x => bitmap.GetPixel(x, 52).A is > 0 and < 255), "Missing alpha antialiasing.");
            if (screenshots is not null) bitmap.Save(Path.Combine(screenshots, $"ball-{dark}-{mode}.png"), ImageFormat.Png);
        }
        // Frames use the actual renderer, allowing visual review of the full busy-loop boundary and state transitions.
        if (screenshots is not null)
        {
            string frames = Path.Combine(screenshots, "ball-motion");
            Directory.CreateDirectory(frames);
            FloatingBallMotion light = new(), dark = new();
            light.Reset(idle, false); dark.Reset(idle with { Dark = true }, false);
            FloatingBallTrailMotion lightTrail = new(), darkTrail = new();
            for (int i = 0; i < 240; i++)
            {
                bool dragging = i >= 90 && i < 150, nav = i >= 205;
                var state = new FloatingBallState(i >= 180 && i < 230, false,
                    nav ? Target(-40, i >= 230 ? 4 : 240) : default);
                bool hover = i >= 60 && i < 90 || dragging, press = i >= 80 && i < 85;
                float phase = dragging ? (i - 90) / 60f * MathF.Tau : 0;
                PointF head = new(160 + MathF.Sin(phase) * 88, 88 + MathF.Sin(phase * 2) * 18);
                using Bitmap canvas = new(640, 210, PixelFormat.Format32bppPArgb);
                using Graphics graphics = Graphics.FromImage(canvas);
                string caption = i < 60 ? "待机 · 环绕光流" : i < 80 ? "悬停" : i < 85 ? "按下" : i < 90 ? "松开" : dragging ? "拖动 · 能量拖尾" : i < 180 ? "松手 · 渐隐" : !nav ? "执行工具" : i < 230 ? "导航 + 执行" : "已到达";
                for (int side = 0; side < 2; side++)
                {
                    bool night = side == 1;
                    FloatingBallFrame current = (night ? dark : light).Advance(1d / 30, state with { Dark = night }, hover, press, false, dragging);
                    FloatingBallTrailMotion trail = night ? darkTrail : lightTrail;
                    trail.Update(head, current.Time, dragging, false);
                    using Bitmap tile = new(320, 210, PixelFormat.Format32bppPArgb);
                    using Graphics tileGraphics = Graphics.FromImage(tile);
                    tileGraphics.Clear(night ? Color.FromArgb(21, 21, 23) : Color.FromArgb(243, 245, 249));
                    using Bitmap tail = new(288, 288, PixelFormat.Format32bppPArgb);
                    using (Graphics tailGraphics = Graphics.FromImage(tail))
                        FloatingBallTrailRenderer.Draw(tailGraphics, tail.Size, head, current.Time, night, trail.Samples);
                    tileGraphics.DrawImageUnscaled(tail, (int)head.X - 144, (int)head.Y - 144);
                    using (Bitmap ballImage = Render(current)) tileGraphics.DrawImageUnscaled(ballImage, (int)head.X - 52, (int)head.Y - 52);
                    using Font font = new("Microsoft YaHei UI", 11, GraphicsUnit.Pixel);
                    using SolidBrush grey = new(night ? Color.FromArgb(159, 171, 197) : Color.FromArgb(91, 111, 142));
                    tileGraphics.DrawString(caption, font, grey, 105, 168);
                    graphics.DrawImageUnscaled(tile, side * 320, 0);
                }
                canvas.Save(Path.Combine(frames, $"{i:000}.png"), ImageFormat.Png);
            }
        }

        IntPtr foreground = GetForegroundWindow();
        FloatingBallState liveState = idle;
        using (FloatingBallForm ball = new(104, () => liveState, () => { }))
        {
            ball.Location = new Point(-30000, -30000);
            IntPtr handle = ball.Handle;
            ball.Show(); Application.DoEvents();
            typeof(FloatingBallForm).GetField("_reducedMotion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ball, false);
            Pump(300);
            using (Bitmap first = Surface(ball).Snapshot())
            {
                Pump(220);
                using Bitmap second = Surface(ball).Snapshot();
                Require(DifferentPixels(first, second) > 150, "Actual idle window timer is not visibly animating.");
            }
            Point origin = new(ball.Left + 52, ball.Top + 52);
            ball.BeginPointerDrag(origin, capturePointer: false); // Exercise the real path without moving/capturing the user's mouse.
            for (int i = 1; i <= 22; i++) { ball.MovePointerDrag(new Point(origin.X + i * 8, origin.Y)); Pump(20); }
            Form? trailWindow = (Form?)typeof(FloatingBallForm).GetField("_trail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ball);
            Require(trailWindow?.Visible == true, "Actual drag did not display the trail window.");
            int extendedStyle = GetWindowLong(trailWindow!.Handle, -20);
            Require((extendedStyle & 0x08080020) == 0x08080020, "Trail must be layered, click-through and non-activating.");
            using (Bitmap tail = Surface(trailWindow).Snapshot())
                Require(Enumerable.Range(0, tail.Width).Any(x => Enumerable.Range(0, tail.Height).Any(y => tail.GetPixel(x, y).A > 30)),
                    "Actual trail window has no visible light pixels.");
            typeof(FloatingBallForm).GetMethod("OnMouseCaptureChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ball, new object[] { EventArgs.Empty });
            Pump(750);
            Require(!trailWindow.Visible, "Drag trail did not hide after release/capture loss.");
            Require(ball.Handle == handle && ball.Size == new Size(104, 104), "Dragging changed ball HWND/size.");
            liveState = navigating;
            Pump(800);
            ball.RefreshState(); // Also exercises the real UpdateLayeredWindow and persistent DIB.
            uint before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            Stopwatch renderTime = Stopwatch.StartNew();
            for (int i = 0; i < 600; i++) ball.RefreshState();
            renderTime.Stop();
            uint after = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            Require(after <= before + 2, $"GDI resources grew across 600 frames: {before} → {after}.");
            for (int i = 0; i < 20; i++)
            {
                ball.Hide();
                var timer = (System.Windows.Forms.Timer)typeof(FloatingBallForm).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ball)!;
                Require(!timer.Enabled, "Hidden ball is still rendering frames.");
                ball.Show(); ball.RefreshState();
                Require(ball.Handle == handle, "Ball HWND was recreated.");
            }
            Require(GetForegroundWindow() == foreground, "Ball activated while being shown.");
            ball.Hide();
            Console.WriteLine($"OK ball: visible native idle timer + drag trail + fade/click-through, alpha light/dark × 4 states, 30/60fps parity, short turns, reduced motion, 600 native frames ({renderTime.Elapsed.TotalMilliseconds / 600:0.00}ms/frame), GDI {before}→{after}, hidden timer stopped, no activation/recreation");
        }
    }

    private static NavigationSnapshot Target(float degrees, float distance) => new(true, "fixture", "目标", "", 1, 1,
        0, 0, 200, 100, MathF.Cos(degrees * MathF.PI / 180), MathF.Sin(degrees * MathF.PI / 180), distance, DateTime.UtcNow);
    private static Bitmap Render(FloatingBallFrame frame)
    {
        Bitmap bitmap = new(104, 104, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        FloatingBallRenderer.Draw(graphics, bitmap.Size, frame);
        return bitmap;
    }
    private static bool EqualPixels(Bitmap a, Bitmap b)
    {
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
    private static int DifferentPixels(Bitmap a, Bitmap b)
    {
        int count = 0;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) count++;
        return count;
    }
    private static LayeredBallSurface Surface(Form form) => (LayeredBallSurface)form.GetType().GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static void Pump(int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(4); }
    }
    private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
}
