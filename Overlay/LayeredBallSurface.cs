using System.ComponentModel;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TerraWikiOverlay;

// Reuse one premultiplied DIB and DC for every frame; transparent pixels also pass mouse input through.
internal sealed class LayeredBallSurface : IDisposable
{
    private readonly IntPtr _dc, _dib, _previous;
    private readonly Bitmap _bitmap;
    private readonly Size _size;
    private bool _disposed;
    public Graphics Graphics { get; }

    public LayeredBallSurface(int width, int height)
    {
        _size = new(width, height);
        _dc = CreateCompatibleDC(IntPtr.Zero);
        if (_dc == IntPtr.Zero) throw new Win32Exception();
        BitmapInfo info = new() { HeaderSize = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        _dib = CreateDIBSection(_dc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero) { DeleteDC(_dc); throw new Win32Exception(); }
        _previous = SelectObject(_dc, _dib);
        _bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        Graphics = Graphics.FromImage(_bitmap);
    }

    public void Present(IntPtr window, Point location)
    {
        Graphics.Flush(FlushIntention.Sync);
        Point source = Point.Empty;
        Size size = _size;
        Blend blend = new() { SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!UpdateLayeredWindow(window, IntPtr.Zero, ref location, ref size, _dc, ref source, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法更新悬浮球透明图层。");
    }

    public static bool ReducedMotion => SystemParametersInfo(0x1042, 0, out bool enabled, 0) && !enabled;

    internal Bitmap Snapshot()
    {
        Graphics.Flush(FlushIntention.Sync);
        return _bitmap.Clone(new Rectangle(Point.Empty, _size), PixelFormat.Format32bppPArgb);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Graphics.Dispose();
        _bitmap.Dispose();
        SelectObject(_dc, _previous);
        DeleteObject(_dib);
        DeleteDC(_dc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint HeaderSize;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Blend { public byte Operation, Flags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
