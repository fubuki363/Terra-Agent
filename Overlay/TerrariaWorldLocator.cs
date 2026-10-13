using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Runtime;

namespace TerraWikiOverlay;

internal sealed record StorageLocationHit(
    string StorageKind,
    string StorageLabel,
    string ItemName,
    int ItemId,
    int Stack,
    string Slots,
    int? TileX,
    int? TileY,
    string Placement,
    string Direction,
    float? DistanceTiles);

internal sealed record StorageSearchResult(
    string Query,
    IReadOnlyList<string> MatchedItemNames,
    IReadOnlyList<StorageLocationHit> Hits,
    IReadOnlyList<NavigationTarget> NavigationTargets,
    float PlayerTileX,
    float PlayerTileY,
    bool MultiplayerClient,
    int ScannedWorldChests);

internal sealed record BiomeLocationHit(
    string Biome,
    int TileX,
    int TileY,
    string Direction,
    float DistanceTiles,
    int SampleCount);

internal sealed record BiomeSearchResult(
    string Biome,
    IReadOnlyList<BiomeLocationHit> Hits,
    IReadOnlyList<NavigationTarget> NavigationTargets,
    float PlayerTileX,
    float PlayerTileY,
    int SampleStride,
    int ScannedSamples,
    bool ExactStructure);

internal static partial class TerrariaWorldLocator
{
    private const int DefaultTileSampleStride = 16;
    private const int SwordShrineSampleStride = 1;
    private const ushort SwordInStoneTileId = 187;
    private const short SwordInStoneFrameX = 17 * 54;
    public static IReadOnlyList<string> SupportedBiomeNames { get; } = new[]
    {
        "森林/出生区域", "海洋", "海滩", "太空", "地表", "地下层", "洞穴层", "地狱",
        "微光湖/以太", "猩红之地", "地下猩红", "腐化之地", "地下腐化",
        "丛林", "地表丛林", "地下丛林", "神圣之地", "地下神圣",
        "雪原", "地下雪原", "沙漠", "地下沙漠", "发光蘑菇群系", "地牢", "丛林神庙",
        "花岗岩洞", "大理石洞", "蜘蛛洞", "蜂巢", "陨石地", "墓地", "浮空岛",
        "生命树", "绿洲", "宝石洞窟", "苔藓洞", "熔岩苔藓洞", "发光苔藓洞", "附魔剑冢"
    };

    internal static bool SupportsBiomeName(string name) => TryGetBiomeDefinition(name, out _);

    internal static bool IsSwordInStoneTile(ushort tileId, ushort header, short frameX, short frameY) =>
        tileId == SwordInStoneTileId
        && (header & 32) != 0
        && frameX >= SwordInStoneFrameX
        && frameX < SwordInStoneFrameX + 54
        && (frameX - SwordInStoneFrameX) % 18 == 0
        && (frameY == 0 || frameY == 18);

    public static bool TryFindItem(
        string query,
        int maximumResults,
        bool includePersonalStorage,
        out StorageSearchResult? result,
        out string failure,
        CancellationToken token = default)
    {
        result = null;
        failure = "";
        IReadOnlyList<KeyValuePair<int, string>> candidates = TerrariaItemNameCatalog.FindByName(query, 24);
        if (candidates.Count == 0)
        {
            failure = "无法把“" + Safe(query) + "”匹配到当前 Terraria 的物品名称，请使用更完整的中文物品名。";
            return false;
        }

        HashSet<int> candidateIds = candidates.Select(candidate => candidate.Key).ToHashSet();
        Process? process = FindTerrariaProcess();
        if (process is null)
        {
            failure = "没有检测到正在运行的原版 Terraria。";
            return false;
        }

        try
        {
            using IDisposable lease = TerrariaReadCoordinator.Enter(token);
            List<RawStorageHit> rawHits = new();
            List<(int X, int Y)> chestPositions = new();
            float playerTileX;
            float playerTileY;
            int scannedWorldChests = 0;
            bool multiplayerClient;
            using (DataTarget target = DataTarget.AttachToProcess(process.Id, suspend: true))
            {
                using RuntimeContext runtimeContext = OpenRuntime(target);
                ClrType mainType = RequireType(runtimeContext.Module, "Terraria.Main");
                ClrObject player = ReadLocalPlayer(mainType, runtimeContext.Domain);
                ReadPlayerTilePosition(player, out playerTileX, out playerTileY);
                multiplayerClient = ReadStatic(mainType, "netMode", runtimeContext.Domain, 0) == 1;

                ClrObject chestArrayObject = ReadStaticObject(mainType, "chest", runtimeContext.Domain);
                if (!chestArrayObject.IsValid || !chestArrayObject.IsArray)
                    throw new InvalidDataException("Main.chest 不是有效数组。");
                ClrArray chests = chestArrayObject.AsArray();
                for (int chestIndex = 0; chestIndex < chests.Length; chestIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    ClrObject chest = chests.GetObjectValue(chestIndex);
                    if (!chest.IsValid)
                        continue;
                    int x = ReadInstance(chest, "x", -1);
                    int y = ReadInstance(chest, "y", -1);
                    if (x < 0 || y < 0)
                        continue;
                    scannedWorldChests++;
                    chestPositions.Add((x, y));
                    string customName = ReadString(chest, "name", "").Trim();
                    string label = customName.Length == 0 ? "未命名箱子" : "“" + customName + "”";
                    ScanChestItems(chest, "world_chest", label, x, y, candidateIds, rawHits);
                }

                if (includePersonalStorage)
                {
                    ScanChestItems(TryReadObject(player, "bank"), "piggy_bank", "猪猪储蓄罐", null, null, candidateIds, rawHits);
                    ScanChestItems(TryReadObject(player, "bank2"), "safe", "保险箱", null, null, candidateIds, rawHits);
                    ScanChestItems(TryReadObject(player, "bank3"), "defenders_forge", "护卫熔炉", null, null, candidateIds, rawHits);
                    ScanChestItems(TryReadObject(player, "bank4"), "void_vault", "虚空保险库", null, null, candidateIds, rawHits);
                }
            }

            List<StorageLocationHit> hits = rawHits
                .GroupBy(hit => (hit.StorageKind, hit.StorageLabel, hit.ItemId, hit.TileX, hit.TileY))
                .Select(group =>
                {
                    RawStorageHit first = group.First();
                    float? distance = first.TileX.HasValue && first.TileY.HasValue
                        ? Distance(playerTileX, playerTileY, first.TileX.Value, first.TileY.Value)
                        : null;
                    return new StorageLocationHit(
                        first.StorageKind,
                        first.StorageLabel,
                        TerrariaItemNameCatalog.Resolve(first.ItemId) ?? "物品ID " + first.ItemId,
                        first.ItemId,
                        group.Sum(value => value.Stack),
                        string.Join("、", group.Select(value => "槽" + value.Slot).Distinct()),
                        first.TileX,
                        first.TileY,
                        first.TileX.HasValue && first.TileY.HasValue
                            ? DescribePlacement(first.TileX.Value, first.TileY.Value, chestPositions)
                            : "随身个人储物",
                        first.TileX.HasValue && first.TileY.HasValue
                            ? Direction(first.TileX.Value - playerTileX, first.TileY.Value - playerTileY)
                            : "无需世界导航",
                        distance);
                })
                .OrderBy(hit => hit.DistanceTiles ?? float.MaxValue)
                .ThenBy(hit => hit.StorageLabel, StringComparer.Ordinal)
                .Take(Math.Clamp(maximumResults, 1, 40))
                .ToList();

            List<NavigationTarget> navigationTargets = hits
                .Where(hit => hit.TileX.HasValue && hit.TileY.HasValue)
                .Select(hit => new NavigationTarget(
                    "item",
                    hit.ItemName + " · " + hit.StorageLabel,
                    hit.TileX!.Value,
                    hit.TileY!.Value,
                    hit.Placement + "；" + hit.Stack + " 个"))
                .ToList();
            result = new StorageSearchResult(
                query,
                candidates.Select(candidate => candidate.Value).Distinct().ToList(),
                hits,
                navigationTargets,
                playerTileX,
                playerTileY,
                multiplayerClient,
                scannedWorldChests);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }

    public static bool TryFindBiome(string biome, int maximumResults, out BiomeSearchResult? result, out string failure,
        CancellationToken token = default)
    {
        result = null;
        failure = "";
        if (!TryGetBiomeDefinition(biome, out BiomeDefinition definition))
        {
            failure = "暂不支持或无法可靠定位的群系/结构：“" + Safe(biome) + "”。当前可检测：" + string.Join("、", SupportedBiomeNames);
            return false;
        }
        if (definition.SwordShrine)
        {
            if (!TryFindWorldObject("附魔剑冢", maximumResults, out WorldObjectSearchResult? objects, out failure, token)
                || objects is null) return false;
            if (objects.Hits.Count == 0)
            {
                failure = (objects.Complete && !objects.MultiplayerClient
                    ? "本次逐格扫描未确认任何仍存在的附魔剑石；不能判断是否曾生成过、是否已经挖掉。"
                    : "本次扫描未确认剑石；地图数据不完整，不能判断世界里是否存在。") + " " + objects.Note;
                return false;
            }
            result = new(definition.DisplayName, objects.Hits.Select(hit => new BiomeLocationHit(definition.DisplayName,
                hit.TileX, hit.TileY, hit.Direction, hit.DistanceTiles, 1)).ToList(), objects.NavigationTargets,
                objects.PlayerTileX, objects.PlayerTileY, 1, (int)Math.Min(int.MaxValue, objects.ScannedTiles), true);
            return true;
        }

        Process? process = FindTerrariaProcess();
        if (process is null)
        {
            failure = "没有检测到正在运行的原版 Terraria。";
            return false;
        }

        try
        {
            using IDisposable lease = TerrariaReadCoordinator.Enter(token);
            List<BiomeSample> samples = new();
            float playerTileX;
            float playerTileY;
            int sampledCells = 0;
            using (DataTarget target = DataTarget.AttachToProcess(process.Id, suspend: false))
            {
                if (target.DataReader.PointerSize != 4)
                    throw new InvalidDataException("当前只支持 32 位原版 Terraria 的世界群系扫描。");
                using RuntimeContext runtimeContext = OpenRuntime(target);
                ClrType mainType = RequireType(runtimeContext.Module, "Terraria.Main");
                ClrObject player = ReadLocalPlayer(mainType, runtimeContext.Domain);
                ReadPlayerTilePosition(player, out playerTileX, out playerTileY);

                ClrObject tileArrayObject = ReadStaticObject(mainType, "tile", runtimeContext.Domain);
                if (!tileArrayObject.IsValid || !tileArrayObject.IsArray)
                    throw new InvalidDataException("Main.tile 不是有效数组。");
                ClrArray tiles = tileArrayObject.AsArray();
                if (tiles.Rank != 2)
                    throw new InvalidDataException("Main.tile 的数组维度不符合预期。");
                int width = tiles.GetLength(0);
                int height = tiles.GetLength(1);
                double worldSurface = ReadStatic(mainType, "worldSurface", runtimeContext.Domain, height * 0.3d);
                double rockLayer = ReadStatic(mainType, "rockLayer", runtimeContext.Domain, height * 0.45d);
                int spawnX = ReadStatic(mainType, "spawnTileX", runtimeContext.Domain, width / 2);
                int spawnY = ReadStatic(mainType, "spawnTileY", runtimeContext.Domain, (int)worldSurface);
                if (definition.DirectKind.Length > 0)
                {
                    samples.AddRange(CreateDirectSamples(
                        definition.DirectKind,
                        width,
                        height,
                        worldSurface,
                        rockLayer,
                        spawnX,
                        spawnY,
                        playerTileX));
                }

                ClrType tileType = RequireType(runtimeContext.Module, "Terraria.Tile");
                ClrInstanceField typeField = RequireField(tileType, "type");
                ClrInstanceField wallField = RequireField(tileType, "wall");
                ClrInstanceField liquidField = RequireField(tileType, "liquid");
                ClrInstanceField solidHeaderField = RequireField(tileType, "sTileHeader");
                ClrInstanceField liquidHeaderField = RequireField(tileType, "bTileHeader");

                ulong tileArrayData = tiles.Type.GetArrayElementAddress(tiles.Address, 0);
                for (int x = definition.SampleStride; definition.DirectKind.Length == 0 && x < width - definition.SampleStride; x += definition.SampleStride)
                {
                    token.ThrowIfCancellationRequested();
                    for (int y = definition.SampleStride; y < height - definition.SampleStride; y += definition.SampleStride)
                    {
                        if (!MatchesVerticalZone(definition.Zone, y, height, worldSurface, rockLayer))
                            continue;
                        sampledCells++;
                        long linearIndex = (long)x * height + y;
                        ulong addressSlot = tileArrayData + (ulong)(linearIndex * target.DataReader.PointerSize);
                        ulong tileAddress = target.DataReader.ReadPointer(addressSlot);
                        if (tileAddress == 0)
                            continue;

                        bool match;
                        if (definition.Shimmer)
                        {
                            byte liquid = liquidField.Read<byte>(tileAddress, interior: false);
                            byte header = liquidHeaderField.Read<byte>(tileAddress, interior: false);
                            match = liquid > 0 && ((header & 96) >> 5) == 3;
                        }
                        else
                        {
                            ushort header = solidHeaderField.Read<ushort>(tileAddress, interior: false);
                            ushort tileId = typeField.Read<ushort>(tileAddress, interior: false);
                            ushort wallId = wallField.Read<ushort>(tileAddress, interior: false);
                            match = ((header & 32) != 0 && definition.TileIds.Contains(tileId))
                                || definition.WallIds.Contains(wallId);
                        }
                        if (match)
                            samples.Add(new BiomeSample(x, y));
                    }
                }
            }

            if (samples.Count == 0)
            {
                failure = "本次没有在当前已加载地图中确认“" + definition.DisplayName + "”的特征图格。稀疏扫描可能漏掉很小或被改造过的区域；多人客户端也可能尚未同步。";
                return false;
            }

            List<List<BiomeSample>> clusters = ClusterSamples(samples, definition.SampleStride);
            List<BiomeLocationHit> hits = clusters
                .Where(cluster => cluster.Count >= definition.MinimumSamples)
                .Select(cluster =>
                {
                    BiomeSample nearest = cluster.MinBy(sample => Distance(playerTileX, playerTileY, sample.X, sample.Y))!;
                    float distance = Distance(playerTileX, playerTileY, nearest.X, nearest.Y);
                    return new BiomeLocationHit(
                        definition.DisplayName,
                        nearest.X,
                        nearest.Y,
                        Direction(nearest.X - playerTileX, nearest.Y - playerTileY),
                        distance,
                        cluster.Count);
                })
                .OrderBy(hit => hit.DistanceTiles)
                .Take(Math.Clamp(maximumResults, 1, 8))
                .ToList();
            if (hits.Count == 0)
            {
                failure = "发现了少量疑似“" + definition.DisplayName + "”图格，但不足以确认一个群系区域。";
                return false;
            }

            List<NavigationTarget> targets = hits.Select((hit, index) => new NavigationTarget(
                "biome",
                hit.Biome + (hits.Count > 1 ? "区域 " + (index + 1) : ""),
                hit.TileX,
                hit.TileY,
                definition.DirectKind.Length > 0
                    ? "依据世界层、出生点或世界边界定位"
                    : "稀疏扫描命中 " + hit.SampleCount + " 个图格/墙体特征点"))
                .ToList();
            result = new BiomeSearchResult(
                definition.DisplayName,
                hits,
                targets,
                playerTileX,
                playerTileY,
                definition.DirectKind.Length > 0 ? 0 : definition.SampleStride,
                sampledCells,
                definition.SwordShrine);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }

    public static bool TryReadPlayerTilePosition(out float tileX, out float tileY, out string failure)
    {
        tileX = 0;
        tileY = 0;
        failure = "";
        Process? process = FindTerrariaProcess();
        if (process is null)
        {
            failure = "Terraria 未运行。";
            return false;
        }
        try
        {
            using IDisposable lease = TerrariaReadCoordinator.Enter(timeoutMilliseconds: 0);
            using DataTarget target = DataTarget.AttachToProcess(process.Id, suspend: false);
            using RuntimeContext runtimeContext = OpenRuntime(target);
            ClrType mainType = RequireType(runtimeContext.Module, "Terraria.Main");
            ClrObject player = ReadLocalPlayer(mainType, runtimeContext.Domain);
            ReadPlayerTilePosition(player, out tileX, out tileY);
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void ScanChestItems(
        ClrObject chest,
        string storageKind,
        string storageLabel,
        int? tileX,
        int? tileY,
        HashSet<int> candidateIds,
        List<RawStorageHit> output)
    {
        if (!chest.IsValid)
            return;
        ClrObject itemsObject = TryReadObject(chest, "item");
        if (!itemsObject.IsValid || !itemsObject.IsArray)
            return;
        ClrArray items = itemsObject.AsArray();
        for (int slot = 0; slot < Math.Min(items.Length, 80); slot++)
        {
            ClrObject item = items.GetObjectValue(slot);
            if (!item.IsValid)
                continue;
            int itemId = ReadInstance(item, "type", ReadInstance(item, "netID", 0));
            int stack = ReadInstance(item, "stack", 0);
            if (stack <= 0 || stack > 9999 || !candidateIds.Contains(itemId))
                continue;
            output.Add(new RawStorageHit(storageKind, storageLabel, itemId, stack, slot, tileX, tileY));
        }
    }

    private static string DescribePlacement(int targetX, int targetY, IReadOnlyList<(int X, int Y)> allChests)
    {
        List<(int X, int Y)> nearby = allChests
            .Where(chest => Math.Abs(chest.X - targetX) <= 48 && Math.Abs(chest.Y - targetY) <= 28)
            .ToList();
        if (nearby.Count < 2)
            return "独立箱子";

        List<int> rows = nearby.Select(chest => chest.Y).Distinct().OrderBy(value => value).ToList();
        int rowY = rows.OrderBy(value => Math.Abs(value - targetY)).First();
        List<int> columns = nearby.Where(chest => Math.Abs(chest.Y - rowY) <= 3)
            .Select(chest => chest.X)
            .Distinct()
            .OrderBy(value => value)
            .ToList();
        int row = rows.IndexOf(rowY) + 1;
        int column = columns.FindIndex(value => value == targetX) + 1;
        if (column <= 0)
            column = columns.Count(value => value < targetX) + 1;
        return $"附近箱子区第 {row} 排、从左第 {column} 个";
    }

    private static List<List<BiomeSample>> ClusterSamples(IReadOnlyList<BiomeSample> samples, int sampleStride)
    {
        Dictionary<(int X, int Y), BiomeSample> grid = samples
            .GroupBy(sample => (sample.X / sampleStride, sample.Y / sampleStride))
            .ToDictionary(group => group.Key, group => group.First());
        HashSet<(int X, int Y)> remaining = grid.Keys.ToHashSet();
        List<List<BiomeSample>> clusters = new();
        while (remaining.Count > 0)
        {
            (int X, int Y) seed = remaining.First();
            remaining.Remove(seed);
            Queue<(int X, int Y)> queue = new();
            queue.Enqueue(seed);
            List<BiomeSample> cluster = new();
            while (queue.Count > 0)
            {
                (int X, int Y) current = queue.Dequeue();
                cluster.Add(grid[current]);
                for (int offsetX = -3; offsetX <= 3; offsetX++)
                {
                    for (int offsetY = -3; offsetY <= 3; offsetY++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                            continue;
                        (int X, int Y) neighbor = (current.X + offsetX, current.Y + offsetY);
                        if (remaining.Remove(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }
            }
            clusters.Add(cluster);
        }
        return clusters.OrderByDescending(cluster => cluster.Count).ToList();
    }


    private static IReadOnlyList<BiomeSample> CreateDirectSamples(
        string kind,
        int width,
        int height,
        double worldSurface,
        double rockLayer,
        int spawnX,
        int spawnY,
        float playerTileX)
    {
        int playerX = Math.Clamp((int)MathF.Round(playerTileX), 120, width - 120);
        int surface = Math.Clamp((int)Math.Round(worldSurface), 100, height - 400);
        int rock = Math.Clamp((int)Math.Round(rockLayer), surface + 50, height - 300);
        return kind switch
        {
            "forest" => new[] { new BiomeSample(Math.Clamp(spawnX, 120, width - 120), Math.Clamp(spawnY, 80, height - 300)) },
            "ocean" => new[] { new BiomeSample(180, surface), new BiomeSample(width - 180, surface) },
            "beach" => new[] { new BiomeSample(320, surface), new BiomeSample(width - 320, surface) },
            "space" => new[] { new BiomeSample(playerX, Math.Max(80, (int)(surface * 0.22))) },
            "surface" => new[] { new BiomeSample(playerX, surface) },
            "underground" => new[] { new BiomeSample(playerX, (surface + rock) / 2) },
            "cavern" => new[] { new BiomeSample(playerX, (rock + height - 260) / 2) },
            "underworld" => new[] { new BiomeSample(playerX, height - 180) },
            _ => Array.Empty<BiomeSample>()
        };
    }

    private static bool MatchesVerticalZone(VerticalZone zone, int y, int height, double worldSurface, double rockLayer) => zone switch
    {
        VerticalZone.Sky => y < worldSurface * 0.5,
        VerticalZone.Surface => y <= worldSurface + 110,
        VerticalZone.Underground => y >= worldSurface - 20 && y < height - 220,
        VerticalZone.Cavern => y >= rockLayer - 40 && y < height - 180,
        VerticalZone.SurfaceAndUnderground => y < height - 220,
        _ => true
    };

    private static bool TryGetBiomeDefinition(string value, out BiomeDefinition definition)
    {
        string key = value.Split('/')[0].Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        definition = key switch
        {
            "forest" or "森林" or "纯净森林" => Direct("forest", "森林/出生区域", "forest"),
            "ocean" or "海洋" => Direct("ocean", "海洋", "ocean"),
            "beach" or "海滩" => Direct("beach", "海滩", "beach"),
            "space" or "sky" or "太空" or "天空" => Direct("space", "太空", "space"),
            "surface" or "地表" => Direct("surface", "地表", "surface"),
            "underground" or "地下" or "地下层" => Direct("underground", "地下层", "underground"),
            "cavern" or "洞穴" or "洞穴层" => Direct("cavern", "洞穴层", "cavern"),
            "underworld" or "hell" or "地狱" or "地狱层" => Direct("underworld", "地狱", "underworld"),
            "shimmer" or "aether" or "微光" or "微光湖" or "以太" => Tiles("shimmer", "微光湖/以太", true, Array.Empty<ushort>(), Array.Empty<ushort>(), 1),
            "crimson" or "猩红" or "猩红之地" => Tiles("crimson", "猩红之地", false, new ushort[] { 199, 200, 201, 203, 205, 234, 352, 399, 401, 662 }, new ushort[] { 69, 81, 83, 192, 193, 194, 195, 218, 221 }, 3),
            "underground_crimson" or "地下猩红" => Tiles("underground_crimson", "地下猩红", false, new ushort[] { 199, 200, 203, 234, 399, 401, 662 }, new ushort[] { 81, 83, 192, 193, 194, 195, 218, 221 }, 3, VerticalZone.Underground),
            "corruption" or "腐化" or "腐化之地" => Tiles("corruption", "腐化之地", false, new ushort[] { 23, 24, 25, 32, 112, 157, 163, 398, 400, 636, 661 }, new ushort[] { 3, 69, 188, 189, 190, 191, 217, 220 }, 3),
            "underground_corruption" or "地下腐化" => Tiles("underground_corruption", "地下腐化", false, new ushort[] { 23, 25, 112, 163, 398, 400, 661 }, new ushort[] { 3, 69, 188, 189, 190, 191, 217, 220 }, 3, VerticalZone.Underground),
            "jungle" or "丛林" => Tiles("jungle", "丛林", false, new ushort[] { 60, 61, 62, 69, 74, 661, 662, 703 }, new ushort[] { 64, 67, 204, 205, 206, 207 }, 4),
            "surface_jungle" or "地表丛林" => Tiles("surface_jungle", "地表丛林", false, new ushort[] { 60, 61, 62, 69, 74, 661, 662, 703 }, new ushort[] { 64, 67 }, 3, VerticalZone.Surface),
            "underground_jungle" or "地下丛林" => Tiles("underground_jungle", "地下丛林", false, new ushort[] { 60, 61, 62, 69, 74, 661, 662, 703 }, new ushort[] { 64, 67, 204, 205, 206, 207 }, 4, VerticalZone.Underground),
            "hallow" or "神圣" or "神圣之地" => Tiles("hallow", "神圣之地", false, new ushort[] { 109, 110, 113, 115, 116, 117, 164, 402, 403, 492 }, new ushort[] { 70, 200, 201, 202, 203, 219, 222 }, 3),
            "underground_hallow" or "地下神圣" => Tiles("underground_hallow", "地下神圣", false, new ushort[] { 109, 116, 117, 164, 402, 403 }, new ushort[] { 70, 200, 201, 202, 203, 219, 222 }, 3, VerticalZone.Underground),
            "snow" or "雪原" or "冰雪" => Tiles("snow", "雪原", false, new ushort[] { 147, 161, 162, 163, 164 }, new ushort[] { 40, 71 }, 5),
            "underground_snow" or "地下雪原" or "地下冰雪" => Tiles("underground_snow", "地下雪原", false, new ushort[] { 147, 161, 162, 163, 164 }, new ushort[] { 40, 71 }, 5, VerticalZone.Underground),
            "desert" or "沙漠" => Tiles("desert", "沙漠", false, new ushort[] { 53, 112, 116, 234, 396, 397, 398, 399, 400, 401, 402, 403, 479 }, Array.Empty<ushort>(), 5),
            "underground_desert" or "地下沙漠" => Tiles("underground_desert", "地下沙漠", false, new ushort[] { 53, 112, 116, 234, 396, 397, 398, 399, 400, 401, 402, 403, 404, 479 }, new ushort[] { 187, 188, 189, 190, 191, 192, 193, 194, 195, 216, 217, 218, 219, 220, 221, 222, 223, 235 }, 5, VerticalZone.Underground),
            "glowing_mushroom" or "mushroom" or "发光蘑菇" or "发光蘑菇群系" or "蘑菇地" => Tiles("glowing_mushroom", "发光蘑菇群系", false, new ushort[] { 70, 71, 72, 190, 528, 578 }, new ushort[] { 74, 80 }, 2),
            "dungeon" or "地牢" => Tiles("dungeon", "地牢", false, new ushort[] { 41, 43, 44, 481, 482, 483 }, new ushort[] { 7, 8, 9, 17, 18, 19, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105 }, 3),
            "jungle_temple" or "temple" or "丛林神庙" or "神庙" => Tiles("jungle_temple", "丛林神庙", false, new ushort[] { 226, 237 }, new ushort[] { 87, 112 }, 2),
            "granite" or "granite_cave" or "花岗岩" or "花岗岩洞" => Tiles("granite", "花岗岩洞", false, new ushort[] { 368, 369, 576 }, new ushort[] { 180, 181, 184 }, 2),
            "marble" or "marble_cave" or "大理石" or "大理石洞" => Tiles("marble", "大理石洞", false, new ushort[] { 357, 367, 561 }, new ushort[] { 178, 179, 183 }, 2),
            "spider" or "spider_cave" or "蜘蛛洞" => Tiles("spider", "蜘蛛洞", false, new ushort[] { 498, 714 }, new ushort[] { 62, 236 }, 2, VerticalZone.Underground, 8),
            "hive" or "蜂巢" or "蜂巢群系" => Tiles("hive", "蜂巢", false, new ushort[] { 225, 229, 444 }, new ushort[] { 86, 108 }, 2, VerticalZone.Underground, 8),
            "meteor" or "meteorite" or "陨石" or "陨石地" => Tiles("meteor", "陨石地", false, new ushort[] { 37, 370 }, new ushort[] { 182 }, 2, VerticalZone.Surface, 8),
            "graveyard" or "墓地" or "墓园" => Tiles("graveyard", "墓地", false, new ushort[] { 85 }, Array.Empty<ushort>(), 2, VerticalZone.Surface, 4),
            "floating_island" or "sky_island" or "浮空岛" or "空岛" => Tiles("floating_island", "浮空岛", false, new ushort[] { 189, 196, 202, 460, 717, 718, 719 }, new ushort[] { 73 }, 2, VerticalZone.Sky, 8),
            "living_tree" or "living_wood" or "生命树" or "生命木" or "大树" => Tiles("living_tree", "生命树", false, new ushort[] { 191, 192, 383, 384 }, new ushort[] { 60, 78, 244 }, 2, VerticalZone.SurfaceAndUnderground, 8),
            "oasis" or "绿洲" => Tiles("oasis", "绿洲", false, new ushort[] { 530, 705 }, Array.Empty<ushort>(), 1, VerticalZone.Surface, 4),
            "gem_cave" or "gemstone_cave" or "宝石洞" or "宝石洞窟" => Tiles("gem_cave", "宝石洞窟", false, new ushort[] { 178, 566 }, new ushort[] { 48, 49, 50, 51, 52, 53 }, 1, VerticalZone.Underground, 8),
            "moss_cave" or "苔藓洞" or "苔藓群系" => Tiles("moss_cave", "苔藓洞", false, new ushort[] { 179, 180, 181, 182, 183, 184, 381, 534, 536, 539, 625, 627 }, Array.Empty<ushort>(), 2, VerticalZone.Cavern, 8),
            "lava_moss" or "熔岩苔藓" or "熔岩苔藓洞" => Tiles("lava_moss", "熔岩苔藓洞", false, new ushort[] { 381 }, new ushort[] { 341 }, 1, VerticalZone.Cavern, 8),
            "glowing_moss" or "发光苔藓" or "发光苔藓洞" => Tiles("glowing_moss", "发光苔藓洞", false, new ushort[] { 534, 536, 539, 625, 627 }, new ushort[] { 342, 343, 344, 345, 346 }, 1, VerticalZone.Cavern, 8),
            "enchanted_sword_shrine" or "enchantedswordshrine" or "sword_shrine" or "附魔剑冢" or "剑冢" or "附魔剑神龛" or "剑之神龛" =>
                new("enchanted_sword_shrine", "附魔剑冢", false, new HashSet<ushort>(), new HashSet<ushort>(), 1, SwordShrineSampleStride, VerticalZone.SurfaceAndUnderground, "", true),
            _ => new("", "", false, new HashSet<ushort>(), new HashSet<ushort>(), int.MaxValue, DefaultTileSampleStride, VerticalZone.Any, "", false)
        };
        return definition.Key.Length > 0;
    }

    private static BiomeDefinition Direct(string key, string displayName, string directKind) =>
        new(key, displayName, false, new HashSet<ushort>(), new HashSet<ushort>(), 1, DefaultTileSampleStride, VerticalZone.Any, directKind, false);

    private static BiomeDefinition Tiles(
        string key,
        string displayName,
        bool shimmer,
        IEnumerable<ushort> tileIds,
        IEnumerable<ushort> wallIds,
        int minimumSamples,
        VerticalZone zone = VerticalZone.Any,
        int sampleStride = DefaultTileSampleStride) =>
        new(key, displayName, shimmer, tileIds.ToHashSet(), wallIds.ToHashSet(), minimumSamples, sampleStride, zone, "", false);

    private static RuntimeContext OpenRuntime(DataTarget target)
    {
        ClrInfo clrInfo = target.ClrVersions.FirstOrDefault()
            ?? throw new InvalidDataException("Terraria 进程中没有可读取的 CLR。");
        ClrRuntime runtime = clrInfo.CreateRuntime();
        ClrModule module = runtime.EnumerateModules().FirstOrDefault(candidate =>
            candidate.Name?.EndsWith("Terraria.exe", StringComparison.OrdinalIgnoreCase) == true)
            ?? throw new InvalidDataException("无法定位 Terraria 托管模块。");
        ClrAppDomain domain = runtime.AppDomains.FirstOrDefault()
            ?? throw new InvalidDataException("无法定位 Terraria 应用域。");
        return new RuntimeContext(runtime, module, domain);
    }

    private static ClrObject ReadLocalPlayer(ClrType mainType, ClrAppDomain domain)
    {
        if (ReadStatic(mainType, "gameMenu", domain, true))
            throw new InvalidDataException("Terraria 当前停留在主菜单。");
        int playerIndex = ReadStatic(mainType, "myPlayer", domain, -1);
        ClrObject playersObject = ReadStaticObject(mainType, "player", domain);
        if (!playersObject.IsValid || !playersObject.IsArray)
            throw new InvalidDataException("Main.player 不是有效数组。");
        ClrArray players = playersObject.AsArray();
        if (playerIndex < 0 || playerIndex >= players.Length)
            throw new InvalidDataException("当前玩家索引无效。");
        ClrObject player = players.GetObjectValue(playerIndex);
        return player.IsValid ? player : throw new InvalidDataException("当前玩家对象无效。");
    }

    private static void ReadPlayerTilePosition(ClrObject player, out float tileX, out float tileY)
    {
        ClrValueType position = player.ReadValueTypeField("position");
        float x = position.ReadField<float>("X");
        float y = position.ReadField<float>("Y");
        int width = ReadInstance(player, "width", 20);
        int height = ReadInstance(player, "height", 42);
        tileX = (x + width / 2f) / 16f;
        tileY = (y + height / 2f) / 16f;
    }

    private static Process? FindTerrariaProcess()
    {
        Process[] processes = Process.GetProcessesByName("Terraria");
        Process? selected = processes.Where(process => !process.HasExited)
            .OrderByDescending(process => process.StartTime)
            .FirstOrDefault();
        foreach (Process process in processes)
        {
            if (!ReferenceEquals(process, selected))
                process.Dispose();
        }
        return selected;
    }

    private static ClrType RequireType(ClrModule module, string name) => module.GetTypeByName(name)
        ?? throw new InvalidDataException("找不到类型 " + name + "，可能是游戏版本尚未适配。");

    private static ClrInstanceField RequireField(ClrType type, string name) => type.GetFieldByName(name)
        ?? throw new InvalidDataException("找不到字段 " + type.Name + "." + name + "。");

    private static T ReadStatic<T>(ClrType type, string name, ClrAppDomain domain, T fallback) where T : unmanaged
    {
        try
        {
            ClrStaticField? field = type.StaticFields.FirstOrDefault(candidate => candidate.Name == name);
            return field is null ? fallback : field.Read<T>(domain);
        }
        catch { return fallback; }
    }

    private static ClrObject ReadStaticObject(ClrType type, string name, ClrAppDomain domain)
    {
        ClrStaticField? field = type.StaticFields.FirstOrDefault(candidate => candidate.Name == name);
        return field?.ReadObject(domain) ?? default;
    }

    private static ClrObject TryReadObject(ClrObject value, string name)
    {
        try { return value.IsValid ? value.ReadObjectField(name) : default; }
        catch { return default; }
    }

    private static T ReadInstance<T>(ClrObject value, string name, T fallback) where T : unmanaged
    {
        try { return value.IsValid ? value.ReadField<T>(name) : fallback; }
        catch { return fallback; }
    }

    private static string ReadString(ClrObject value, string name, string fallback)
    {
        try { return value.IsValid ? value.ReadStringField(name, 160) ?? fallback : fallback; }
        catch { return fallback; }
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static string Direction(float dx, float dy)
    {
        if (MathF.Abs(dx) < 8 && MathF.Abs(dy) < 8)
            return "就在附近";
        string horizontal = MathF.Abs(dx) < 8 ? "" : dx > 0 ? "右" : "左";
        string vertical = MathF.Abs(dy) < 8 ? "" : dy > 0 ? "下" : "上";
        return horizontal + vertical + "方";
    }

    private static string Safe(string value)
    {
        string oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 120 ? oneLine : oneLine[..120] + "…";
    }

    private sealed record RawStorageHit(
        string StorageKind,
        string StorageLabel,
        int ItemId,
        int Stack,
        int Slot,
        int? TileX,
        int? TileY);

    private sealed record BiomeSample(int X, int Y);

    private enum VerticalZone
    {
        Any,
        Sky,
        Surface,
        Underground,
        Cavern,
        SurfaceAndUnderground
    }

    private sealed record BiomeDefinition(
        string Key,
        string DisplayName,
        bool Shimmer,
        IReadOnlySet<ushort> TileIds,
        IReadOnlySet<ushort> WallIds,
        int MinimumSamples,
        int SampleStride,
        VerticalZone Zone,
        string DirectKind,
        bool SwordShrine);

    private sealed record RuntimeContext(ClrRuntime Runtime, ClrModule Module, ClrAppDomain Domain) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
    }
}
