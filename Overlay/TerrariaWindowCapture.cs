using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TerraWikiOverlay;

internal static class TerrariaWindowCapture
{
    public static string CaptureAsDataUrl()
    {
        GameProcessInfo? info = GameProcessLocator.Find(refresh: true);
        using Process? game = GameProcessLocator.OpenClient(info);
        if (game is null)
            throw new InvalidOperationException("没有检测到正在运行的Terraria 或 tModLoader 窗口。请先进入游戏，且不要将窗口最小化。");

        IntPtr window = game.MainWindowHandle;
        if (NativeMethods.DwmGetWindowAttribute(
                window,
                NativeMethods.DwmwaExtendedFrameBounds,
                out NativeRect bounds,
                Marshal.SizeOf<NativeRect>()) != 0)
            throw new InvalidOperationException("无法读取 Terraria 窗口范围。");

        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        if (width < 32 || height < 32)
            throw new InvalidOperationException("Terraria 窗口尺寸无效；窗口可能已最小化。");

        using Bitmap original = new(width, height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(original))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(window, hdc, NativeMethods.PwRenderFullContent))
                    throw new InvalidOperationException("无法捕获 Terraria 窗口。请切换到窗口化或无边框窗口模式后重试。");
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        using Bitmap resized = ResizeToFit(original, 1280, 800);
        using MemoryStream stream = new();
        ImageCodecInfo jpegCodec = ImageCodecInfo.GetImageEncoders().First(codec => codec.MimeType == "image/jpeg");
        using EncoderParameters encoderParameters = new(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 78L);
        resized.Save(stream, jpegCodec, encoderParameters);
        return "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static Bitmap ResizeToFit(Bitmap source, int maximumWidth, int maximumHeight)
    {
        double scale = Math.Min(1d, Math.Min((double)maximumWidth / source.Width, (double)maximumHeight / source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        Bitmap result = new(width, height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);
        return result;
    }
}

