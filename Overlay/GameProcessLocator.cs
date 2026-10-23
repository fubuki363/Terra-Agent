using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TerraWikiOverlay;

internal sealed record GameProcessInfo(int Id, string Kind, string Executable, string AssemblyPath, string DisplayName)
{
    internal bool IsTmod => Kind == "tmod";
    internal string DotnetPath => Executable;
}

internal static class GameProcessLocator
{
    internal static int? WorkerProcessId { get; set; }
    private static readonly object Sync = new();
    private static GameProcessInfo? _cached;
    private static long _checked;

    internal static GameProcessInfo? Find(bool refresh = false)
    {
        if (WorkerProcessId is int id)
        {
            using Process process = Process.GetProcessById(id);
            return Describe(process, requireWindow: true) ?? throw new InvalidOperationException("目标已不是 Terraria/tModLoader 客户端。");
        }
        lock (Sync)
        {
            if (!refresh && Environment.TickCount64 - _checked < 750) return _cached;
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
            List<(GameProcessInfo Info, bool Foreground, DateTime Started)> found = new();
            foreach (string name in new[] { "Terraria", "tModLoader", "dotnet" })
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                try
                {
                    GameProcessInfo? info = Describe(process, requireWindow: true);
                    if (info is not null) found.Add((info, process.Id == foreground, process.StartTime));
                }
                catch (Exception) { }
            }
            int? previous = _cached?.Id;
            _cached = found.OrderByDescending(p => p.Foreground).ThenByDescending(p => p.Info.Id == previous)
                .ThenByDescending(p => p.Started).Select(p => p.Info).FirstOrDefault();
            _checked = Environment.TickCount64;
            return _cached;
        }
    }

    internal static GameProcessInfo? Describe(Process process, bool requireWindow)
    {
        if (process.HasExited || requireWindow && process.MainWindowHandle == IntPtr.Zero) return null;
        string? path = ExecutablePath(process.Id);
        if (path is null) return null;
        string name = Path.GetFileName(path);
        if (name.Equals("Terraria.exe", StringComparison.OrdinalIgnoreCase)) return new(process.Id, "vanilla", path, path, "Terraria");
        string folder = Path.GetDirectoryName(path)!;
        if (name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) folder = Path.GetDirectoryName(folder)!;
        else if (!name.Equals("tModLoader.exe", StringComparison.OrdinalIgnoreCase)) return null;
        string assembly = Path.Combine(folder, "tModLoader.dll");
        return File.Exists(assembly) ? new(process.Id, "tmod", path, assembly, "tModLoader") : null;
    }

    internal static Process? OpenClient(GameProcessInfo? game)
    {
        if (game is null) return null;
        try { return Process.GetProcessById(game.Id); }
        catch (ArgumentException) { return null; }
    }

    internal static string? ExecutablePath(int id)
    {
        IntPtr handle = OpenProcess(0x1000, false, id);
        if (handle == IntPtr.Zero) return null;
        try { StringBuilder buffer = new(32768); int size = buffer.Capacity; return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null; }
        finally { CloseHandle(handle); }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder text, ref int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
}
