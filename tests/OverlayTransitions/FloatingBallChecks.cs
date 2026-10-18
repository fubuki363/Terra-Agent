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
            Math.Abs(a.Busy - b.Busy) < .00001 && Math.Abs(a.Time - b.Time) < .00001, "Motion depends on frame rate.");
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
            for (int i = 0; i < 150; i++)
            {
                bool nav = i >= 75;
                var state = new FloatingBallState(i >= 30 && i < 125, false,
                    nav ? Target(-80 + (i - 75) * 1.6f, i >= 125 ? 4 : 240 - (i - 75) * 3) : default);
                bool hover = i >= 10 && i < 30, press = i >= 20 && i < 25;
                using Bitmap canvas = new(320, 188, PixelFormat.Format32bppPArgb);
                using Graphics graphics = Graphics.FromImage(canvas);
                graphics.Clear(Color.FromArgb(243, 245, 249));
                using (SolidBrush background = new(Color.FromArgb(21, 21, 23))) graphics.FillRectangle(background, 160, 0, 160, 188);
                using (Bitmap left = Render(light.Advance(1d / 30, state, hover, press, false))) graphics.DrawImageUnscaled(left, 28, 30);
                using (Bitmap right = Render(dark.Advance(1d / 30, state with { Dark = true }, hover, press, false))) graphics.DrawImageUnscaled(right, 188, 30);
                using Font font = new("Microsoft YaHei UI", 10, GraphicsUnit.Pixel);
                using SolidBrush grey = new(Color.FromArgb(130, 137, 151));
                string caption = i < 10 ? "待机" : i < 20 ? "悬停" : i < 25 ? "按下" : i < 30 ? "松开" : i < 75 ? "执行工具" : i < 125 ? "导航 + 执行" : "已到达";
                graphics.DrawString(caption, font, grey, 52, 148);
                graphics.DrawString(caption, font, grey, 212, 148);
                canvas.Save(Path.Combine(frames, $"{i:000}.png"), ImageFormat.Png);
            }
        }

        IntPtr foreground = GetForegroundWindow();
        using (FloatingBallForm ball = new(104, () => navigating, () => { }))
        {
            ball.Location = new Point(-30000, -30000);
            IntPtr handle = ball.Handle;
            ball.Show(); Application.DoEvents();
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
            Console.WriteLine($"OK ball: alpha light/dark × 4 states, 30/60fps parity, short turns, reduced motion, 600 native frames ({renderTime.Elapsed.TotalMilliseconds / 600:0.00}ms/frame), GDI {before}→{after}, hidden timer stopped, no activation/recreation");
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
    private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
}
