using System;
using System.Runtime.InteropServices;

namespace TerraWikiOverlay;

internal static class NativeMethods
{
    internal const int HotkeyId = 0x5441;
    internal const int WmHotkey = 0x0312;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint VkT = 0x54;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const uint PwRenderFullContent = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int valueSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, [Out] byte[] buffer, int size, out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation buffer, nuint length);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
