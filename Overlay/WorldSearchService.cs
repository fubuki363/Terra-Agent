using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TerraWikiOverlay;

internal enum WorldSearchKind { Objects, Biomes, Storage }

internal sealed record WorldSearchRow(string Name, string Place, string Detail, string Direction,
    float? DistanceTiles, int? TileX, int? TileY, NavigationTarget? Target);

internal sealed record WorldSearchResult(string Summary, IReadOnlyList<WorldSearchRow> Rows,
    IReadOnlyList<NavigationTarget> NavigationTargets, float PlayerTileX, float PlayerTileY);

internal static class WorldSearchService
{
    internal static string? PermissionError(WorldSearchKind kind, OverlaySettings settings)
    {
        if (!settings.IncludeMemoryContext) return "请在“设置 → 游戏数据”启用实时读取。";
        bool enabled = kind switch
        {
            WorldSearchKind.Objects => settings.EnableObjectLocator,
            WorldSearchKind.Biomes => settings.EnableBiomeLocator,
            WorldSearchKind.Storage => settings.EnableStorageLocator,
            _ => false
        };
        return enabled ? null : "请在“设置 → 游戏数据”启用对应的物体、群系或储物定位权限。";
    }

    internal static WorldSearchResult Search(WorldSearchKind kind, string query, bool personal,
        OverlaySettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string? denied = PermissionError(kind, settings);
        if (denied is not null) throw new InvalidOperationException(denied);
        if (string.IsNullOrWhiteSpace(query)) throw new InvalidOperationException("请输入要查找的名称。");
        switch (kind)
        {
            case WorldSearchKind.Objects:
                if (!TerrariaWorldLocator.TryFindWorldObject(query, 24, out WorldObjectSearchResult? objects,
                    out string objectFailure, token) || objects is null) throw new InvalidOperationException(objectFailure);
                return FromObjects(objects);
            case WorldSearchKind.Biomes:
                if (!TerrariaWorldLocator.TryFindBiome(query.Split('/')[0], 8, out BiomeSearchResult? biomes,
                    out string biomeFailure, token) || biomes is null) throw new InvalidOperationException(biomeFailure);
                return FromBiomes(biomes);
            case WorldSearchKind.Storage:
                if (!TerrariaWorldLocator.TryFindItem(query, 40, personal, out StorageSearchResult? storage,
                    out string storageFailure, token) || storage is null) throw new InvalidOperationException(storageFailure);
                return FromStorage(storage);
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    internal static WorldSearchResult FromObjects(WorldObjectSearchResult result) => new(
        (result.Hits.Count > 0
            ? $"确认 {result.TotalObjects} {result.CountUnit}{result.ObjectName}，显示最近 {result.Hits.Count} 个方向点。"
            : $"本次未确认{result.ObjectName}，不代表从未生成或已被开采。")
            + (!result.Complete || result.MultiplayerClient ? " 地图数据未完整覆盖。" : ""),
        result.Hits.Select(hit => new WorldSearchRow(hit.Name, hit.IsWall ? "背景墙" : "世界物体",
            $"{hit.Name} · {(hit.IsWall ? "WallID" : "TileID")} {hit.TileId} · 样式 {hit.Style}\r\n" + result.Note,
            hit.Direction, hit.DistanceTiles, hit.TileX, hit.TileY, TargetAt(result.NavigationTargets, hit.TileX, hit.TileY))).ToList(),
        result.NavigationTargets, result.PlayerTileX, result.PlayerTileY);

    internal static WorldSearchResult FromBiomes(BiomeSearchResult result) => new(
        $"找到 {result.Hits.Count} 个{result.Biome}{(result.ExactStructure ? "已确认结构" : "候选区域")}。"
            + (result.SampleStride > 1 ? " 方向点来自图格/墙体抽样，抵达后请结合地形确认。" : ""),
        result.Hits.Select(hit => new WorldSearchRow(hit.Biome, result.ExactStructure ? "精确结构" : "群系 / 世界层",
            $"{hit.Biome} · 命中特征 {hit.SampleCount} · 扫描采样 {result.ScannedSamples:N0}\r\n"
                + (result.ExactStructure ? "已确认仍存在的剑石；也可能位于剑冢外。" : "群系是区域而非单一格，箭头指向命中特征位置。"),
            hit.Direction, hit.DistanceTiles, hit.TileX, hit.TileY, TargetAt(result.NavigationTargets, hit.TileX, hit.TileY))).ToList(),
        result.NavigationTargets, result.PlayerTileX, result.PlayerTileY);

    internal static WorldSearchResult FromStorage(StorageSearchResult result) => new(
        $"已检查 {result.ScannedWorldChests} 个世界储物容器，找到 {result.Hits.Count} 条“{result.Query}”库存。"
            + (result.MultiplayerClient ? " 多人模式仅覆盖客户端已同步的箱子。" : "")
            + (result.Hits.Count > 0 && result.NavigationTargets.Count == 0 ? " 命中个人储物，无需方向导航。" : ""),
        result.Hits.Select(hit => new WorldSearchRow(hit.ItemName + $" ×{hit.Stack}", hit.StorageLabel,
            $"{hit.StorageLabel} · {hit.ItemName} ×{hit.Stack} · {hit.Slots}\r\n{hit.Placement}",
            hit.Direction, hit.DistanceTiles, hit.TileX, hit.TileY, TargetAt(result.NavigationTargets, hit.TileX, hit.TileY))).ToList(),
        result.NavigationTargets, result.PlayerTileX, result.PlayerTileY);

    private static NavigationTarget? TargetAt(IReadOnlyList<NavigationTarget> targets, int? x, int? y) =>
        x.HasValue && y.HasValue ? targets.FirstOrDefault(target => target.TileX == x && target.TileY == y) : null;
}
