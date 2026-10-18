using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TerraWikiOverlay;

internal static class FloatingBallOrbit
{
    // Cache the expensive tapered arcs at 2x resolution; theme transitions use a bounded cache.
    private static readonly Dictionary<(int, int), Bitmap> Sprites = new();
    private static readonly Queue<(int, int)> Order = new();

    static FloatingBallOrbit()
    {
        Application.ApplicationExit += (_, _) =>
        {
            lock (Sprites) { foreach (Bitmap image in Sprites.Values) image.Dispose(); Sprites.Clear(); Order.Clear(); }
        };
    }

    internal static void Draw(Graphics graphics, Color accent, Color companion, float angle)
    {
        lock (Sprites)
        {
            var key = (accent.ToArgb(), companion.ToArgb());
            if (!Sprites.TryGetValue(key, out Bitmap? sprite))
            {
                if (Sprites.Count >= 8) { var oldest = Order.Dequeue(); Sprites[oldest].Dispose(); Sprites.Remove(oldest); }
                sprite = Create(accent, companion); Sprites.Add(key, sprite); Order.Enqueue(key);
            }
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(52, 52);
            graphics.RotateTransform(angle);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(sprite, new RectangleF(-52, -52, 104, 104), new RectangleF(0, 0, 208, 208), GraphicsUnit.Pixel);
            graphics.Restore(state);
        }
    }

    private static Bitmap Create(Color accent, Color companion)
    {
        Bitmap sprite = new(208, 208, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(sprite);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(2, 2);
        for (int comet = 0; comet < 3; comet++)
        {
            Color color = comet == 1 ? companion : accent;
            float head = comet * 120;
            for (int part = 0; part < 10; part++)
            {
                float intensity = (part + 1) / 10f;
                using Pen arc = new(Color.FromArgb((int)(255 * (.25f + .6f * intensity) * (comet == 2 ? .65f : 1)), color), 1 + intensity * 1.3f)
                    { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawArc(arc, 6.5f, 6.5f, 91, 91, head - 62 + part * 6, 7);
            }
            float radians = head * MathF.PI / 180;
            float x = 52 + MathF.Cos(radians) * 45.5f, y = 52 + MathF.Sin(radians) * 45.5f;
            using (SolidBrush halo = new(Color.FromArgb(45, color))) graphics.FillEllipse(halo, x - 5, y - 5, 10, 10);
            using (SolidBrush tip = new(Color.FromArgb(245, color))) graphics.FillEllipse(tip, x - 2, y - 2, 4, 4);
            using (SolidBrush glint = new(Color.FromArgb(230, Color.White))) graphics.FillEllipse(glint, x - .7f, y - .7f, 1.4f, 1.4f);
        }
        return sprite;
    }
}
