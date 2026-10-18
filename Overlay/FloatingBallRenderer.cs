using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TerraWikiOverlay;

internal static class FloatingBallRenderer
{
    internal static void Draw(Graphics graphics, Size size, FloatingBallFrame frame)
    {
        graphics.ResetTransform();
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.ScaleTransform(size.Width / 104f, size.Height / 104f);
        float breath = frame.ReducedMotion ? 0 : (float)Math.Sin(frame.Time * Math.Tau / 4.8);
        float scale = .94f + .06f * frame.Reveal + .018f * frame.Hover - .065f * frame.Press + .012f * breath;
        graphics.TranslateTransform(52, 52);
        graphics.ScaleTransform(scale, scale);
        graphics.TranslateTransform(-52, -52);

        Color blue = Mix(Color.FromArgb(65, 118, 230), Color.FromArgb(122, 170, 255), frame.Dark);
        Color amber = Mix(Color.FromArgb(164, 108, 20), Color.FromArgb(239, 188, 93), frame.Dark);
        Color green = Mix(Color.FromArgb(31, 140, 90), Color.FromArgb(103, 214, 157), frame.Dark);
        Color accent = Mix(blue, Mix(amber, green, frame.Arrival), frame.Navigation);
        for (int spread = 7; spread >= 1; spread--)
        {
            using SolidBrush shadow = new(Color.FromArgb((int)((2 + frame.Hover * 2) * (1 - spread / 9f)), Color.Black));
            graphics.FillEllipse(shadow, 11 - spread, 14 - spread, 82 + spread * 2, 82 + spread * 2);
        }
        Color companion = Mix(Color.FromArgb(25, 172, 195), Color.FromArgb(100, 220, 245), frame.Dark);
        companion = Mix(companion, accent, frame.Navigation * .8f);
        using (Pen glow = new(Alpha(accent, .10f + .035f * breath + .07f * frame.Hover + .09f * frame.Drag), 7))
            graphics.DrawEllipse(glow, 6.5f, 6.5f, 91, 91);
        Color top = Mix(Color.White, Color.FromArgb(40, 40, 44), frame.Dark);
        Color bottom = Mix(Color.FromArgb(245, 247, 251), Color.FromArgb(27, 27, 30), frame.Dark);
        using (LinearGradientBrush surface = new(new PointF(12, 11), new PointF(12, 93), top, bottom))
            graphics.FillEllipse(surface, 11, 11, 82, 82);
        Color border = Mix(Mix(Color.FromArgb(216, 222, 232), Color.FromArgb(69, 70, 76), frame.Dark), accent,
            .25f * frame.Navigation + .35f * frame.Hover);
        using (Pen rim = new(border, 1)) graphics.DrawEllipse(rim, 11.5f, 11.5f, 81, 81);
        using (Pen inner = new(Alpha(Color.White, .55f - frame.Dark * .49f), 1))
            graphics.DrawArc(inner, 13, 13, 78, 78, 210, 120);

        DrawEnergy(graphics, frame, accent, companion);

        if (frame.Busy > .005f)
        {
            float angle = frame.ReducedMotion ? -90 : (float)(frame.Time / 1.65 % 1 * 360) - 90;
            using Pen track = new(Alpha(accent, frame.Busy * .10f), 2.5f);
            graphics.DrawEllipse(track, 6.5f, 6.5f, 91, 91);
            using Pen arc = new(Alpha(accent, frame.Busy * .95f), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(arc, 6.5f, 6.5f, 91, 91, angle, 100);
            arc.Color = Alpha(accent, frame.Busy * .32f);
            graphics.DrawArc(arc, 6.5f, 6.5f, 91, 91, angle + 180, 35);
        }

        float identity = 1 - frame.Navigation;
        if (identity > .005f)
        {
            float letter = identity * (1 - frame.Busy);
            if (letter > .005f)
            {
                using Font font = new("Segoe UI", 28, FontStyle.Bold, GraphicsUnit.Pixel);
                DrawText(graphics, "T", font, Alpha(accent, letter), new RectangleF(18, 25, 68, 49));
                using SolidBrush status = new(Alpha(accent, letter * .65f));
                graphics.FillEllipse(status, 50, 77, 4, 4);
            }
            for (int i = 0; i < 3 && frame.Busy > .005f; i++)
            {
                float wave = frame.ReducedMotion ? 0 : (float)Math.Sin(frame.Time * Math.Tau / 1.1 - i * .8);
                using SolidBrush dot = new(Alpha(accent, identity * frame.Busy * (.7f + .25f * wave)));
                graphics.FillEllipse(dot, 36 + i * 13, 49 - wave * 3, 6, 6);
            }
        }
        if (frame.Navigation > .005f)
        {
            GraphicsState saved = graphics.Save();
            graphics.TranslateTransform(52, 42);
            graphics.RotateTransform(frame.Angle * 180 / MathF.PI);
            using (SolidBrush arrow = new(Alpha(accent, frame.Navigation * (1 - frame.Arrival))))
                graphics.FillPolygon(arrow, new PointF[] { new(20, 0), new(-13, -13), new(-6, 0), new(-13, 13) });
            graphics.Restore(saved);
            using (Pen check = new(Alpha(green, frame.Navigation * frame.Arrival), 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                graphics.DrawLines(check, new PointF[] { new(39, 42), new(48, 51), new(65, 34) });
            string distance = frame.Target.DistanceTiles <= 8 ? "已到达" : frame.Target.DistanceTiles >= 1000
                ? $"{frame.Target.DistanceTiles / 1000:0.#}k 格" : $"{frame.Target.DistanceTiles:0} 格";
            using Font label = new("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            DrawText(graphics, distance, label, Alpha(accent, frame.Navigation), new RectangleF(17, 65, 70, 18));
        }
        graphics.ResetTransform();
    }

    private static void DrawEnergy(Graphics graphics, FloatingBallFrame frame, Color accent, Color companion)
    {
        float angle = frame.ReducedMotion ? -45 : (float)frame.Orbit * 360 - 45;
        float pulse = frame.ReducedMotion ? .5f : .5f + .5f * (float)Math.Sin(frame.Time * Math.Tau / 3.6);
        using (GraphicsPath core = new())
        {
            core.AddEllipse(22, 22, 60, 60);
            using PathGradientBrush aura = new(core) { CenterColor = Alpha(accent, .045f + .075f * pulse + .04f * frame.Drag), SurroundColors = new[] { Color.Transparent } };
            graphics.FillEllipse(aura, 22, 22, 60, 60);
        }
        using (Pen track = new(Alpha(accent, .17f + .12f * frame.Drag), 1))
            graphics.DrawEllipse(track, 6.5f, 6.5f, 91, 91);

        // The inner aurora and the counter-rotating orbit remain visible while completely idle.
        for (int i = 0; i < 2; i++)
        {
            float start = -angle * .7f + i * 180;
            using Pen haze = new(Alpha(i == 0 ? accent : companion, .075f + frame.Drag * .08f), 7);
            graphics.DrawArc(haze, 17, 17, 70, 70, start, 70);
            using Pen filament = new(Alpha(i == 0 ? accent : companion, .18f), 1);
            graphics.DrawArc(filament, 16, 16, 72, 72, start, 52);
        }
        FloatingBallOrbit.Draw(graphics, accent, companion, angle);
        for (int particle = 0; particle < 6; particle++)
        {
            float phase = -angle * .55f + particle * 60;
            float radians = phase * MathF.PI / 180;
            float radius = 37 + 2 * MathF.Sin(radians * 3);
            float size = 1.4f + (particle % 2) * .5f + frame.Drag;
            using SolidBrush spark = new(Alpha(companion, .4f + frame.Dark * .25f));
            graphics.FillEllipse(spark, 52 + MathF.Cos(radians) * radius - size / 2,
                52 + MathF.Sin(radians) * radius - size / 2, size, size);
        }
        if (frame.Drag > .01f)
        {
            using Pen charge = new(Alpha(companion, frame.Drag * .7f), 1.4f) { DashPattern = new[] { 3f, 4f }, DashOffset = -angle / 8 };
            graphics.DrawEllipse(charge, 11, 11, 82, 82);
        }
        if (frame.Release > .005f)
        {
            float radius = 40 + 8 * (1 - frame.Release);
            using Pen ripple = new(Alpha(companion, frame.Release * .65f), 2 * frame.Release + .5f);
            graphics.DrawEllipse(ripple, 52 - radius, 52 - radius, radius * 2, radius * 2);
        }
    }

    private static void DrawText(Graphics graphics, string text, Font font, Color color, RectangleF bounds)
    {
        using SolidBrush brush = new(color);
        using StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, brush, bounds, format);
    }
    private static Color Alpha(Color color, float amount) => Color.FromArgb((int)(Math.Clamp(amount, 0, 1) * 255), color);
    private static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
}
