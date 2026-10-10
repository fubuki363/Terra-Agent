using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Runtime;

namespace TerraWikiOverlay;

internal sealed record WorldObjectHit(string Name, ushort TileId, int Style, int TileX, int TileY,
    string Direction, float DistanceTiles, bool IsWall = false);
internal sealed record WorldObjectSearchResult(string Query, string ObjectName, IReadOnlyList<WorldObjectHit> Hits,
    IReadOnlyList<NavigationTarget> NavigationTargets, float PlayerTileX, float PlayerTileY,
    int TotalObjects, long ScannedTiles, long TotalTiles, bool Complete, bool MultiplayerClient, string Note, string CountUnit = "个");

internal static partial class TerrariaWorldLocator
{
    public static bool TryFindWorldObject(string query, int maximumResults, out WorldObjectSearchResult? result,
        out string failure, CancellationToken token = default)
    {
        result = null;
        failure = "";
        token.ThrowIfCancellationRequested();
        bool known = WorldObjectCatalog.TryResolve(query, out WorldObjectDefinition definition);
        using Process? process = FindTerrariaProcess();
        if (process is null)
        {
            failure = "没有检测到正在运行的原版 Terraria。";
            return false;
        }
        try
        {
            using IDisposable lease = TerrariaReadCoordinator.Enter(token);
            // Tiles are read in bounded pages. No world scan suspends gameplay.
            using DataTarget target = DataTarget.AttachToProcess(process.Id, suspend: false);
            if (target.DataReader.PointerSize != 4)
                throw new InvalidDataException("当前地图读取器支持 32 位原版 Terraria。");
            using RuntimeContext runtime = OpenRuntime(target);
            ClrType main = RequireType(runtime.Module, "Terraria.Main");
            ReadPlayerTilePosition(ReadLocalPlayer(main, runtime.Domain), out float playerX, out float playerY);
            if (!known || definition.Key == "chest")
            {
                RuntimePlacementCatalog catalog = RuntimePlacementCatalog.Load(runtime.Module, runtime.Domain, main, token);
                if (!catalog.TryResolve(definition.Key == "chest" ? "箱子" : query, out definition))
                    throw new InvalidDataException("未能从本机游戏的放置物体目录匹配名称。请使用完整中英文物品名，或用 TileID 内部名/图格ID查找；可以先让 Agent 检索正式名称。");
            }
            bool multiplayer = ReadStatic(main, "netMode", runtime.Domain, 0) == 1;
            ClrObject tileObject = ReadStaticObject(main, "tile", runtime.Domain);
            if (!tileObject.IsValid || !tileObject.IsArray || tileObject.AsArray().Rank != 2)
                throw new InvalidDataException("当前世界图格数组不可用。");
            ClrArray tiles = tileObject.AsArray();
            int width = tiles.GetLength(0), height = tiles.GetLength(1);
            if (width < 100 || height < 100 || (long)width * height > 50_000_000)
                throw new InvalidDataException("世界图格尺寸异常。");
            ulong arrayData = tiles.Type.GetArrayElementAddress(tiles.Address, 0);
            if (arrayData == 0) throw new InvalidDataException("无法确定图格数组的数据起点。");
            ClrType tileType = RequireType(runtime.Module, "Terraria.Tile");
            TilePageReader reader = new(target.DataReader, tileType, token);
            // Cross-check layout against ClrMD before accepting a complete scan.
            foreach (var point in new[] { (width / 2, height / 2), (width / 3, height / 3) })
            {
                ClrObject probe = tiles.GetObjectValue(new[] { point.Item1, point.Item2 });
                ulong raw = target.DataReader.ReadPointer(arrayData + (ulong)(((long)point.Item1 * height + point.Item2) * 4));
                if (raw != probe.Address || (probe.IsValid && (!reader.TryReadType(raw, out ushort type)
                    || type != probe.ReadField<ushort>("type"))))
                    throw new InvalidDataException("图格内存布局校验失败，无法可靠扫描此游戏版本。");
            }
            byte[] column = new byte[height * 4];
            int maximum = Math.Clamp(maximumResults, 1, 24);
            PriorityQueue<WorldObjectHit, float> nearest = new();
            Dictionary<(ushort Id, int X, int Y), WorldObjectHit> materialRegions = new();
            Stopwatch elapsed = Stopwatch.StartNew();
            long scanned = 0, unread = 0;
            int count = 0;
            bool complete = true;
            HashSet<ushort> wanted = definition.TileIds.ToHashSet();
            for (int x = 0; x < width; x++)
            {
                token.ThrowIfCancellationRequested();
                if (elapsed.Elapsed > TimeSpan.FromSeconds(45)) { complete = false; break; }
                int bytes = target.DataReader.Read(arrayData + (ulong)((long)x * column.Length), column);
                if (bytes != column.Length) { unread += height; continue; }
                for (int y = 0; y < height; y++)
                {
                    scanned++;
                    ulong address = BinaryPrimitives.ReadUInt32LittleEndian(column.AsSpan(y * 4, 4));
                    if (address == 0) continue; // Unsynchronized cells can be null on multiplayer clients.
                    bool readId = definition.MatchWalls ? reader.TryReadWall(address, out ushort id) : reader.TryReadType(address, out id);
                    if (!readId) { unread++; continue; }
                    if (!wanted.Contains(id)) continue;
                    WorldTile tile;
                    if (definition.MatchWalls) tile = new WorldTile(id, 32, 0, 0);
                    else if (!reader.TryReadTile(address, out tile)) { unread++; continue; }
                    if (!WorldObjectCatalog.TryMatch(definition, tile, out int style, out string name, out PlacedObjectSpec? placement)) continue;
                    int objectWidth = placement?.Width ?? definition.Width, objectHeight = placement?.Height ?? definition.Height;
                    bool intact = x + objectWidth <= width && y + objectHeight <= height;
                    for (int dx = 0; intact && dx < objectWidth; dx++)
                    {
                        for (int dy = 0; intact && dy < objectHeight; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            ulong partAddress = target.DataReader.ReadPointer(arrayData + (ulong)(((long)(x + dx) * height + y + dy) * 4));
                            intact = partAddress != 0 && reader.TryReadTile(partAddress, out WorldTile part) && part.Active
                                && part.Type == tile.Type && part.FrameX == tile.FrameX + dx * (placement?.StepX ?? 18)
                                && part.FrameY == tile.FrameY + (placement?.RowOffsets[dy] ?? dy * 18);
                        }
                    }
                    if (!intact) continue;
                    int centerX = x + objectWidth / 2, centerY = y + objectHeight / 2;
                    float distance = Distance(playerX, playerY, centerX, centerY);
                    WorldObjectHit hit = new(name, id, style, centerX, centerY,
                        Direction(centerX - playerX, centerY - playerY), distance, definition.MatchWalls);
                    count++;
                    if (placement is not null && (!placement.Framed || placement.IsWall))
                    {
                        var cell = (id, x / 12, y / 12);
                        if (!materialRegions.TryGetValue(cell, out WorldObjectHit? previous) || distance < previous.DistanceTiles)
                            materialRegions[cell] = hit;
                        continue;
                    }
                    nearest.Enqueue(hit, -distance);
                    if (nearest.Count > maximum) nearest.Dequeue();
                }
            }
            if (ReadStatic(main, "gameMenu", runtime.Domain, true)
                || ReadStaticObject(main, "tile", runtime.Domain).Address != tileObject.Address)
                throw new InvalidDataException("扫描期间游戏切换了世界，请在进入世界后重新查找。");
            complete &= unread == 0;
            List<WorldObjectHit> hits = nearest.UnorderedItems.Select(entry => entry.Element)
                .Concat(materialRegions.Values).OrderBy(hit => hit.DistanceTiles).Take(maximum).ToList();
            string note = definition.Note;
            if (!complete) note += $" 本次扫描未完成（已检查 {scanned:N0} 格，读取失败 {unread:N0} 格），未命中不能表示世界不存在该物体。";
            if (multiplayer) note += " 当前为多人客户端，只覆盖已同步到本机的地图图格，未命中不能表示整个世界没有。";
            result = new(query, definition.Name, hits, hits.Select(hit => new NavigationTarget("object", hit.Name,
                hit.TileX, hit.TileY, $"已确认{(hit.IsWall ? "墙体" : "图格")} {hit.TileId} / 样式 {hit.Style}；{hit.Direction} {hit.DistanceTiles:0} 格")).ToList(),
                playerX, playerY, count, scanned, (long)width * height, complete, multiplayer, note.Trim(),
                definition.Placements is not null && definition.Placements.Values.SelectMany(value => value).All(value => !value.Framed || value.IsWall) ? "格" : "个");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { failure = exception.GetBaseException().Message; return false; }
    }

    private sealed class TilePageReader
    {
        private const int PageSize = 4096;
        private readonly IDataReader _reader;
        private readonly CancellationToken _token;
        private readonly Dictionary<ulong, (byte[] Bytes, int Length)> _pages = new();
        private readonly Queue<ulong> _order = new();
        private readonly int _type, _wall, _header, _frameX, _frameY;
        private int _reads;

        public TilePageReader(IDataReader reader, ClrType type, CancellationToken token)
        {
            _reader = reader;
            _token = token;
            _type = RequireField(type, "type").Offset + reader.PointerSize;
            _wall = RequireField(type, "wall").Offset + reader.PointerSize;
            _header = RequireField(type, "sTileHeader").Offset + reader.PointerSize;
            _frameX = RequireField(type, "frameX").Offset + reader.PointerSize;
            _frameY = RequireField(type, "frameY").Offset + reader.PointerSize;
        }

        public bool TryReadType(ulong address, out ushort value) => TryRead16(address + (ulong)_type, out value);
        public bool TryReadWall(ulong address, out ushort value) => TryRead16(address + (ulong)_wall, out value);

        public bool TryReadTile(ulong address, out WorldTile tile)
        {
            tile = default;
            if (!TryReadType(address, out ushort type) || !TryRead16(address + (ulong)_header, out ushort header)
                || !TryRead16(address + (ulong)_frameX, out ushort x) || !TryRead16(address + (ulong)_frameY, out ushort y))
                return false;
            tile = new(type, header, (short)x, (short)y);
            return true;
        }

        private bool TryRead16(ulong address, out ushort value)
        {
            if ((++_reads & 8191) == 0) _token.ThrowIfCancellationRequested();
            value = 0;
            ulong pageAddress = address & ~(ulong)(PageSize - 1);
            if (!_pages.TryGetValue(pageAddress, out var page))
            {
                byte[] bytes = new byte[PageSize];
                page = (bytes, _reader.Read(pageAddress, bytes));
                _pages.Add(pageAddress, page);
                _order.Enqueue(pageAddress);
                if (_pages.Count > 1024) _pages.Remove(_order.Dequeue());
            }
            int offset = (int)(address - pageAddress);
            if (offset + 2 <= page.Length)
            {
                value = BinaryPrimitives.ReadUInt16LittleEndian(page.Bytes.AsSpan(offset, 2));
                return true;
            }
            Span<byte> fallback = stackalloc byte[2];
            if (_reader.Read(address, fallback) != 2) return false;
            value = BinaryPrimitives.ReadUInt16LittleEndian(fallback);
            return true;
        }
    }
}
