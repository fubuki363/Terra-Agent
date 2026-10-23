using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TerraWikiOverlay;

internal static class TmodReaderClient
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Process? _worker;
    internal static bool NeedsWorker => !Environment.Is64BitProcess && GameProcessLocator.Find()?.IsTmod == true;

    internal static bool TryRead<T>(string operation, out T? result, out string failure, string query = "", int maximum = 8,
        bool personal = true, GameContextSelection? selection = null, CancellationToken token = default)
    {
        result = default; failure = "";
        if (!Gate.Wait(operation == "position" ? 0 : 5000, token)) { failure = "游戏读取器忙碌，请稍后重试。"; return false; }
        try
        {
            GameProcessInfo game = GameProcessLocator.Find() ?? throw new InvalidOperationException("未检测到游戏客户端。");
            EnsureWorker(game);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(operation == "objects" ? TimeSpan.FromSeconds(55) : TimeSpan.FromSeconds(30));
            var request = new GameReaderRequest(1, game.Id, operation, query, maximum, personal, selection);
            _worker!.StandardInput.WriteLine(JsonSerializer.Serialize(request));
            _worker.StandardInput.Flush();
            string line = _worker.StandardOutput.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult()
                ?? throw new IOException("64 位读取器已退出。");
            if (line.Length > 2_000_000) throw new InvalidDataException("读取器响应过大。");
            GameReaderResponse reply = JsonSerializer.Deserialize<GameReaderResponse>(line) ?? throw new InvalidDataException("读取器没有返回有效结果。");
            if (reply.Protocol != 1) throw new InvalidDataException("游戏读取器协议不兼容。");
            failure = reply.Failure;
            if (!reply.Success) return false;
            result = reply.Data!.Value.Deserialize<T>();
            return result is not null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { StopWorker(); throw; }
        catch (Exception exception) { StopWorker(); failure = "tModLoader 读取失败：" + exception.GetBaseException().Message; return false; }
        finally { Gate.Release(); }
    }

    private static void EnsureWorker(GameProcessInfo game)
    {
        if (_worker is { HasExited: false }) return;
        string? root = Environment.GetEnvironmentVariable("TERRA_AGENT_READER_PATH");
        if (string.IsNullOrWhiteSpace(root))
        {
            using Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("TerraAgent.GameReader.zip")
                ?? throw new FileNotFoundException("未包含 64 位读取器，请使用 tools/Build-Overlay.ps1 重新发布。");
            string hash = Convert.ToHexString(SHA256.HashData(payload))[..16]; payload.Position = 0;
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerraWikiOverlay", "readers", hash);
            if (!File.Exists(Path.Combine(root, "ready")))
            {
                Directory.CreateDirectory(root);
                using ZipArchive archive = new(payload, ZipArchiveMode.Read);
                archive.ExtractToDirectory(root, overwriteFiles: true);
                File.WriteAllText(Path.Combine(root, "ready"), hash);
            }
            root = Path.Combine(root, "TerraAgent.GameReader.dll");
        }
        string dotnet = game.DotnetPath;
        if (!Path.GetFileName(dotnet).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            dotnet = Path.Combine(Path.GetDirectoryName(game.AssemblyPath)!, "dotnet", "dotnet.exe");
        if (!File.Exists(dotnet)) throw new FileNotFoundException("未找到 tModLoader 自带的 64 位 .NET 运行时。", dotnet);
        ProcessStartInfo start = new(dotnet) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(root); start.ArgumentList.Add("--worker");
        _worker = Process.Start(start) ?? throw new IOException("无法启动 64 位游戏读取器。");
        _worker.ErrorDataReceived += (_, _) => { }; _worker.BeginErrorReadLine();
    }
    private static void StopWorker()
    {
        try { if (_worker is { HasExited: false }) _worker.Kill(entireProcessTree: true); } catch (Exception) { }
        _worker?.Dispose(); _worker = null;
    }
}
