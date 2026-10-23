using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using TerraWikiOverlay;

internal static class TmodCompatibilityChecks
{
    internal static void Offline()
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, 54);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[2..], 36);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], 1);
        WorldTile tile = TmodWorldMap.Decode(900, bytes);
        Require(tile.Active && tile.Type == 900 && tile.FrameX == 54 && tile.FrameY == 36, "tModLoader tile decoding lost mod IDs/frame coordinates.");
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], 32);
        Require(!TmodWorldMap.Decode(900, bytes).Active, "tModLoader uses bit 0, not the vanilla bit 5, for HasTile.");
        var catalog = new Dictionary<int, string> { [9] = "木材", [9001] = "模组矿石", [9002] = "模组矿石砖" };
        Require(RuntimeItemCatalog.Find(catalog, "模组矿石").Single().Key == 9001, "Mod item names were not matched exactly.");
        Require(RuntimeItemCatalog.Find(catalog, "9002").Single().Value == "模组矿石砖", "Mod runtime IDs were not matched.");
        Require(RuntimeItemCatalog.Find(catalog, "矿石").Count == 2, "Mod item partial search failed.");
        Require(RuntimeItemCatalog.Find(catalog, " ").Count == 0, "Empty runtime item query matched items.");
        Console.WriteLine("OK tmod-fixtures tile=soa+mod-ids+hasTile-bit names=runtime+exact+partial+numeric");
    }

    internal static void Live()
    {
        Require(!Environment.Is64BitProcess, "The overlay must exercise its real 32 → 64-bit bridge.");
        GameProcessInfo game = GameProcessLocator.Find(refresh: true) ?? throw new InvalidOperationException("请先进入 tModLoader 世界；测试不会启动或重载游戏。");
        Require(game.IsTmod, "当前识别到的不是 tModLoader。");
        Require(typeof(TmodReaderClient).Assembly.GetManifestResourceNames().Contains("TerraAgent.GameReader.zip"), "Missing embedded reader payload.");
        string? previousReader = Environment.GetEnvironmentVariable("TERRA_AGENT_READER_PATH");
        Environment.SetEnvironmentVariable("TERRA_AGENT_READER_PATH", null);
        IntPtr foreground = GetForegroundWindow();
        Stopwatch elapsed = Stopwatch.StartNew();
        try
        {
            Require(TerrariaManagedMemoryContextReader.TryRead(new(true, true, false, false, false, false, false), out string state, out string failure), failure);
            Require(state.Contains("tModLoader") && state.Contains("[血量]") && state.Contains("[魔力]") && !state.Contains("[背包]") && !state.Contains("[世界]"), "Live state did not respect requested categories.");
            Require(TerrariaManagedMemoryContextReader.TryRead(new(false, false, true, true, true, true, false), out string items, out failure), failure);
            Require(items.Contains("[已加载模组]") && items.Contains("[背包]") && items.Contains("[装备]") && !items.Contains("[血量]"), "Mod metadata or category filtering failed.");
            Require(TerrariaWorldLocator.TryReadPlayerTilePosition(out float x, out float y, out failure) && float.IsFinite(x) && x > 0 && y > 0, failure);
            Require(TerrariaWorldLocator.TryFindItem("木材", 5, true, out var storage, out failure), failure);
            Require(storage is not null && storage.ScannedWorldChests > 0, "No tModLoader chests were scanned.");
            Require(TerrariaWorldLocator.TryFindBiome("森林", 3, out var spawn, out failure) && spawn?.Hits.Count > 0, failure);
            Require(TerrariaWorldLocator.TryFindBiome("微光湖", 3, out var shimmer, out failure) && shimmer?.Hits.Count > 0, failure);
            Require(TerrariaWorldLocator.TryFindWorldObject("生命水晶", 5, out var objects, out failure), failure);
            Require(objects is not null && objects.Complete && objects.ScannedTiles == objects.TotalTiles && objects.Hits.Count > 0, "Full tModLoader scan failed.");
            NavigationState navigation = new();
            navigation.SetTargets(objects!.NavigationTargets, objects.PlayerTileX, objects.PlayerTileY);
            navigation.UpdatePlayer(x, y);
            Require(navigation.GetSnapshot().Active && float.IsFinite(navigation.GetSnapshot().DistanceTiles), "tModLoader navigation failed.");
            Require(!TerrariaWorldLocator.TryFindBiome("不存在的测试群系", 3, out _, out failure) && failure.Length > 0, "Unknown biome must return an explicit failure.");
            Require(TerrariaWorldLocator.TryReadPlayerTilePosition(out _, out _, out failure), "Worker did not recover after a rejected query: " + failure);
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            bool observed = false;
            try { TerrariaWorldLocator.TryFindWorldObject("生命水晶", 5, out _, out _, cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            Require(observed, "Cancellation was ignored.");
            using CancellationTokenSource interrupted = new(30);
            bool stopped = false;
            try { TerrariaWorldLocator.TryFindWorldObject("生命水晶", 5, out _, out _, interrupted.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Require(stopped, "An in-flight 64-bit scan ignored cancellation.");
            using (Process runningGame = Process.GetProcessById(game.Id)) Require(!runningGame.HasExited, "Cancelling the helper ended the game.");
            Require(TerrariaWorldLocator.TryReadPlayerTilePosition(out _, out _, out failure), "Reader did not restart after scan cancellation: " + failure);
            var settings = new OverlaySettings { IncludeMemoryContext = false };
            bool denied = false;
            try { WorldSearchService.Search(WorldSearchKind.Storage, "木材", true, settings, CancellationToken.None); }
            catch (InvalidOperationException e) { denied = e.Message.Contains("实时读取"); }
            Require(denied, "tModLoader bypassed game data permissions.");
            string capture = TerrariaWindowCapture.CaptureAsDataUrl();
            Require(capture.StartsWith("data:image/jpeg;base64,"), "tModLoader window capture failed.");
            using MemoryStream encoded = new(Convert.FromBase64String(capture[(capture.IndexOf(',') + 1)..]));
            using Bitmap screenshot = new(encoded);
            var colors = new HashSet<int>();
            for (int py = 0; py < screenshot.Height; py += 16)
            for (int px = 0; px < screenshot.Width; px += 16) colors.Add(screenshot.GetPixel(px, py).ToArgb());
            Require(colors.Count > 20, "Game screenshot appears blank.");
            Require(GetForegroundWindow() == foreground, "Compatibility checks stole foreground focus.");
            Console.WriteLine($"OK tmod-live pid={game.Id} architecture=x86→x64 embedded=true state=filtered+mods storage={storage!.ScannedWorldChests} chests objects={objects.TotalObjects}/{objects.ScannedTiles} tiles shimmer={shimmer!.Hits.Count} navigation=active screenshot=game-only permissions=honored cancel=observed elapsedMs={elapsed.ElapsedMilliseconds}");
        }
        finally { Environment.SetEnvironmentVariable("TERRA_AGENT_READER_PATH", previousReader); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
