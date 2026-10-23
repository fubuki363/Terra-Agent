using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TerraWikiOverlay;

internal static class TerrariaMemoryContextReader
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const long MaximumBytesToScan = 128L * 1024 * 1024;

    public static string Read(IReadOnlyList<string> playerNames, IReadOnlyList<string> worldNames)
    {
        GameProcessInfo? info = GameProcessLocator.Find();
        using Process? game = GameProcessLocator.OpenClient(info);
        if (game is null)
            return "实时进程：Terraria / tModLoader 当前未运行；未读取进程内存。";
        if (info?.IsTmod == true)
            return "实时进程：tModLoader 正在运行；实时状态通过内置的 64 位托管读取器按需获取，不使用原版存档或字节扫描猜测状态。";

        StringBuilder result = new();
        string version = "未知";
        try
        {
            version = game.MainModule?.FileVersionInfo.FileVersion ?? "未知";
        }
        catch
        {
        }
        result.AppendLine($"实时进程：Terraria 正在运行；版本 {version}；进程已运行 {FormatRuntime(DateTime.Now - game.StartTime)}。");

        IntPtr handle = NativeMethods.OpenProcess(ProcessVmRead | ProcessQueryInformation, false, game.Id);
        if (handle == IntPtr.Zero)
        {
            result.Append("只读内存采样：Windows 拒绝访问，已回退到存档快照。");
            return result.ToString().Trim();
        }

        try
        {
            List<string> candidates = playerNames.Concat(worldNames)
                .Where(name => name.Length is >= 2 and <= 80)
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .ToList();
            HashSet<string> matches = ScanForKnownNames(handle, candidates);
            if (matches.Count == 0)
            {
                result.Append("只读内存采样：成功读取可读页，但未可靠识别当前角色或世界；不使用猜测值。");
            }
            else
            {
                IEnumerable<string> matchedPlayers = matches.Where(name => playerNames.Contains(name, StringComparer.Ordinal));
                IEnumerable<string> matchedWorlds = matches.Where(name => worldNames.Contains(name, StringComparer.Ordinal));
                string players = string.Join("、", matchedPlayers.Take(3));
                string worlds = string.Join("、", matchedWorlds.Take(3));
                result.Append("只读内存采样：检测到已加载的存档名称候选");
                if (players.Length > 0)
                    result.Append("；角色候选 ").Append(players);
                if (worlds.Length > 0)
                    result.Append("；世界候选 ").Append(worlds);
                result.Append("。候选只用于辅助判断，不冒充确定的当前选择。");
            }
        }
        catch (Exception exception)
        {
            result.Append("只读内存采样失败，已回退到存档快照：").Append(exception.GetType().Name);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
        return result.ToString().Trim();
    }

    private static HashSet<string> ScanForKnownNames(IntPtr process, IReadOnlyList<string> candidates)
    {
        Dictionary<string, byte[]> needles = candidates.ToDictionary(
            name => name,
            Encoding.Unicode.GetBytes,
            StringComparer.Ordinal);
        HashSet<string> matches = new(StringComparer.Ordinal);
        ulong address = 0x10000;
        long scanned = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        int infoSize = Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();

        while (address < 0x00007FFFFFFEFFFF && scanned < MaximumBytesToScan && stopwatch.ElapsedMilliseconds < 900)
        {
            if (NativeMethods.VirtualQueryEx(process, new IntPtr(unchecked((long)address)), out NativeMethods.MemoryBasicInformation info, (nuint)infoSize) == 0)
                break;
            ulong baseAddress = unchecked((ulong)info.BaseAddress.ToInt64());
            ulong regionSize = info.RegionSize;
            ulong next = baseAddress + Math.Max(regionSize, 0x1000);
            if (next <= address)
                break;

            bool readable = info.State == MemCommit
                && (info.Protect & PageGuard) == 0
                && (info.Protect & PageNoAccess) == 0
                && regionSize <= 64UL * 1024 * 1024;
            if (readable)
            {
                ulong offset = 0;
                while (offset < regionSize && scanned < MaximumBytesToScan && stopwatch.ElapsedMilliseconds < 900)
                {
                    int requested = (int)Math.Min(1024UL * 1024, regionSize - offset);
                    byte[] buffer = new byte[requested];
                    IntPtr chunkAddress = new(unchecked((long)(baseAddress + offset)));
                    if (NativeMethods.ReadProcessMemory(process, chunkAddress, buffer, buffer.Length, out nuint bytesRead) && bytesRead > 0)
                    {
                        ReadOnlySpan<byte> span = buffer.AsSpan(0, checked((int)bytesRead));
                        foreach ((string name, byte[] needle) in needles)
                        {
                            if (!matches.Contains(name) && span.IndexOf(needle) >= 0)
                                matches.Add(name);
                        }
                        scanned += (long)bytesRead;
                    }
                    offset += (ulong)requested;
                    if (matches.Count == needles.Count)
                        return matches;
                }
            }
            address = next;
        }
        return matches;
    }

    private static string FormatRuntime(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours} 小时 {value.Minutes} 分钟"
        : $"{Math.Max(1, (int)value.TotalMinutes)} 分钟";
}
