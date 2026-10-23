using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Buffers.Binary;
using Microsoft.Diagnostics.Runtime;

namespace TerraWikiOverlay;

internal sealed class TmodWorldMap
{
    private readonly IDataReader _reader;
    private readonly ClrType _main;
    private readonly ClrAppDomain _domain;
    private readonly int _worldId, _storageHeight;
    private readonly ArrayColumn _types, _states, _walls, _liquids;
    internal int Width { get; }
    internal int Height { get; }

    internal TmodWorldMap(DataTarget target, ClrRuntime runtime, ClrModule module, ClrAppDomain domain, ClrType main)
    {
        _reader = target.DataReader; _main = main; _domain = domain;
        if (_reader.PointerSize != 8) throw new InvalidDataException("tModLoader 地图读取需要 64 位读取器。");
        Width = main.GetStaticFieldByName("maxTilesX")!.Read<int>(domain);
        Height = main.GetStaticFieldByName("maxTilesY")!.Read<int>(domain);
        _worldId = main.GetStaticFieldByName("worldID")!.Read<int>(domain);
        ClrValueType map = main.GetStaticFieldByName("tile")!.ReadStruct(domain);
        int storageWidth = map.ReadField<ushort>("Width"); _storageHeight = map.ReadField<ushort>("Height");
        if (Width < 100 || Height < 100 || Width > storageWidth || Height > _storageHeight || (long)storageWidth * _storageHeight > 60_000_000)
            throw new InvalidDataException("tModLoader 地图容量校验失败。");
        var pinned = runtime.EnumerateHandles().Where(h => h.HandleKind == ClrHandleKind.Pinned && h.Object.IsArray)
            .Select(h => h.Object).ToList();
        _types = Column("Terraria.TileTypeData[]", 2, ("Type", 0));
        _states = Column("Terraria.TileWallWireStateData[]", 8, ("TileFrameX", 0), ("TileFrameY", 2), ("bitpack", 4));
        _walls = Column("Terraria.WallTypeData[]", 2, ("Type", 0));
        _liquids = Column("Terraria.LiquidData[]", 2, ("Amount", 0), ("typeAndFlags", 1));

        ArrayColumn Column(string name, int stride, params (string Name, int Offset)[] fields)
        {
            var matches = pinned.Where(o => o.Type?.Name == name && o.AsArray().Length == storageWidth * _storageHeight).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("无法唯一确认 tModLoader 图格数据：" + name);
            ClrArray array = matches[0].AsArray();
            if (array.Type.ComponentSize != stride || fields.Any(f => array.Type.ComponentType?.GetFieldByName(f.Name)?.Offset != f.Offset))
                throw new InvalidDataException("tModLoader 图格字段布局未适配：" + name);
            return new(array.Type.GetArrayElementAddress(array.Address, 0), stride, new byte[Height * stride]);
        }
    }
    internal bool ReadColumn(int x) => Read(_types, x) && Read(_states, x) && Read(_walls, x) && Read(_liquids, x);
    private bool Read(ArrayColumn column, int x) => _reader.Read(column.Address + (ulong)((long)x * _storageHeight * column.Stride), column.Buffer) == column.Buffer.Length;
    internal ushort Type(int y) => BinaryPrimitives.ReadUInt16LittleEndian(_types.Buffer.AsSpan(y * 2, 2));
    internal ushort Wall(int y) => BinaryPrimitives.ReadUInt16LittleEndian(_walls.Buffer.AsSpan(y * 2, 2));
    internal bool Shimmer(int y) => _liquids.Buffer[y * 2] > 0 && (_liquids.Buffer[y * 2 + 1] & 63) == 3;
    internal WorldTile Tile(int y) => Decode(Type(y), _states.Buffer.AsSpan(y * 8, 8));
    internal bool TryTile(int x, int y, out WorldTile tile)
    {
        Span<byte> type = stackalloc byte[2], state = stackalloc byte[8];
        ulong index = (ulong)((long)x * _storageHeight + y);
        tile = default;
        if (_reader.Read(_types.Address + index * 2, type) != 2 || _reader.Read(_states.Address + index * 8, state) != 8) return false;
        tile = Decode(BinaryPrimitives.ReadUInt16LittleEndian(type), state); return true;
    }
    internal static WorldTile Decode(ushort type, ReadOnlySpan<byte> state) => new(type,
        (ushort)((BinaryPrimitives.ReadInt32LittleEndian(state[4..]) & 1) != 0 ? 32 : 0),
        BinaryPrimitives.ReadInt16LittleEndian(state), BinaryPrimitives.ReadInt16LittleEndian(state[2..]));
    internal void VerifyWorld()
    {
        if (_main.GetStaticFieldByName("gameMenu")!.Read<bool>(_domain) || _main.GetStaticFieldByName("worldID")!.Read<int>(_domain) != _worldId)
            throw new InvalidDataException("扫描期间切换了世界，请重新查找。");
    }
    private sealed record ArrayColumn(ulong Address, int Stride, byte[] Buffer);
}

internal static partial class TerrariaWorldLocator
{
    private static bool TryFindTmodObjects(DataTarget target, string query, int maximumResults,
        out WorldObjectSearchResult? result, out string failure, CancellationToken token)
    {
        result = null; failure = "";
        using RuntimeContext context = OpenRuntime(target);
        ClrType main = RequireType(context.Module, "Terraria.Main");
        ReadPlayerTilePosition(ReadLocalPlayer(main, context.Domain), out float playerX, out float playerY);
        bool known = WorldObjectCatalog.TryResolve(query, out WorldObjectDefinition definition);
        if (!known || definition.Key == "chest")
        {
            RuntimePlacementCatalog catalog = RuntimePlacementCatalog.Load(context.Module, context.Domain, main, token);
            if (!catalog.TryResolve(definition.Key == "chest" ? "箱子" : query, out definition))
                throw new InvalidDataException("未匹配当前 tModLoader 的放置物品。请使用游戏内名称或图格 ID。");
        }
        TmodWorldMap map = new(target, context.Runtime, context.Module, context.Domain, main);
        int maximum = Math.Clamp(maximumResults, 1, 24), count = 0;
        long scanned = 0, unread = 0;
        bool complete = true, multiplayer = ReadStatic(main, "netMode", context.Domain, 0) == 1;
        var nearest = new PriorityQueue<WorldObjectHit, float>();
        var regions = new Dictionary<(ushort, int, int), WorldObjectHit>();
        var wanted = definition.TileIds.ToHashSet();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for (int x = 0; x < map.Width; x++)
        {
            token.ThrowIfCancellationRequested();
            if (elapsed.Elapsed.TotalSeconds > 45) { complete = false; break; }
            if (!map.ReadColumn(x)) { unread += map.Height; continue; }
            for (int y = 0; y < map.Height; y++)
            {
                scanned++;
                ushort id = definition.MatchWalls ? map.Wall(y) : map.Type(y);
                if (!wanted.Contains(id)) continue;
                WorldTile tile = definition.MatchWalls ? new(id, 32, 0, 0) : map.Tile(y);
                if (!WorldObjectCatalog.TryMatch(definition, tile, out int style, out string name, out PlacedObjectSpec? placement)) continue;
                int w = placement?.Width ?? definition.Width, h = placement?.Height ?? definition.Height;
                bool intact = x + w <= map.Width && y + h <= map.Height;
                for (int dx = 0; intact && dx < w; dx++)
                for (int dy = 0; intact && dy < h; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    intact = map.TryTile(x + dx, y + dy, out WorldTile part) && part.Active && part.Type == tile.Type
                        && part.FrameX == tile.FrameX + dx * (placement?.StepX ?? 18)
                        && part.FrameY == tile.FrameY + (placement?.RowOffsets[dy] ?? dy * 18);
                }
                if (!intact) continue;
                int cx = x + w / 2, cy = y + h / 2;
                float distance = Distance(playerX, playerY, cx, cy);
                WorldObjectHit hit = new(name, id, style, cx, cy, Direction(cx - playerX, cy - playerY), distance, definition.MatchWalls);
                count++;
                if (placement is not null && (!placement.Framed || placement.IsWall))
                {
                    var key = (id, x / 12, y / 12);
                    if (!regions.TryGetValue(key, out var old) || distance < old.DistanceTiles) regions[key] = hit;
                }
                else { nearest.Enqueue(hit, -distance); if (nearest.Count > maximum) nearest.Dequeue(); }
            }
        }
        map.VerifyWorld(); complete &= unread == 0;
        var hits = nearest.UnorderedItems.Select(e => e.Element).Concat(regions.Values).OrderBy(h => h.DistanceTiles).Take(maximum).ToList();
        string note = definition.Note + " 来源：tModLoader 当前已加载世界。";
        if (!complete) note += " 扫描未完整覆盖，未命中不代表不存在。";
        if (multiplayer) note += " 多人客户端仅覆盖已同步图格。";
        result = new(query, definition.Name, hits, hits.Select(h => new NavigationTarget("object", h.Name, h.TileX, h.TileY,
            $"tModLoader 图格 {h.TileId} / 样式 {h.Style}；{h.Direction} {h.DistanceTiles:0} 格")).ToList(), playerX, playerY,
            count, scanned, (long)map.Width * map.Height, complete, multiplayer, note,
            definition.Placements is not null && definition.Placements.Values.SelectMany(p => p).All(p => !p.Framed || p.IsWall) ? "格" : "个");
        return true;
    }
}
