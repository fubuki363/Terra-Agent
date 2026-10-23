using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Runtime;
using Mono.Cecil;

namespace TerraWikiOverlay;

internal sealed record PlacedObjectSpec(string Name, string EnglishName, ushort TypeId, int Style, bool IsWall,
    bool Framed, int Width, int Height, int StepX, int[] RowOffsets, int FrameWidth, int FrameHeight,
    bool Horizontal, int Wrap, int Multiplier, int LineSkip, bool Mineral = false,
    bool Plant = false, string[]? Aliases = null, string Note = "", bool NaturalPickup = false)
{
    public bool TryMatch(WorldTile tile, out int style)
    {
        style = 0;
        if (!tile.Active || tile.Type != TypeId) return false;
        if (!Framed || IsWall) return true;
        if (tile.FrameX < 0 || tile.FrameY < 0 || tile.FrameX % FrameWidth != 0 || tile.FrameY % FrameHeight != 0) return false;
        int column = tile.FrameX / FrameWidth, row = tile.FrameY / FrameHeight;
        // A zero wrap limit means an unwrapped strip; another texture row/column
        // can be a variation, not another placement style.
        style = Horizontal ? row * Math.Max(0, Wrap) + column : column * Math.Max(0, Wrap) + row;
        style /= Math.Max(1, Multiplier);
        if (LineSkip > 1) style = Horizontal ? row / LineSkip * Math.Max(0, Wrap) + column
            : column / LineSkip * Math.Max(0, Wrap) + row;
        return Style < 0 || style == Style;
    }
}

internal sealed class RuntimePlacementCatalog
{
    private static RuntimePlacementCatalog? _cached;
    private static string _cacheStamp = "";
    private readonly List<PlacedObjectSpec> _entries;
    private static ClrType RequireMainLang(ClrModule module) => module.GetTypeByName("Terraria.Lang") ?? throw new InvalidDataException("语言目录不可用。");
    public int Count => _entries.Count;
    internal RuntimePlacementCatalog(IEnumerable<PlacedObjectSpec> entries) => _entries = entries.ToList();

    public static RuntimePlacementCatalog Load(ClrModule module, ClrAppDomain domain, ClrType main, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string stamp = GameProcessLocator.Find()?.Id + ":" + StaticObject(RequireMainLang(module), "_itemNameCache", domain).Address + ":" + module.Name + (File.Exists(module.Name) ? File.GetLastWriteTimeUtc(module.Name!).Ticks.ToString() : "");
        if (_cached is not null && _cacheStamp == stamp) return _cached;
        ClrType? samples = module.GetTypeByName("Terraria.ID.ContentSamples") ?? module.GetTypeByName("Terraria.GameContent.ContentSamples");
        if (samples is null) throw new InvalidDataException("此游戏版本的物体样本目录不可用。");
        ClrObject items = StaticObject(samples, "ItemsByType", domain);
        ClrObject entriesObject = Object(items, "entries", "_entries");
        if (!entriesObject.IsValid || !entriesObject.IsArray) throw new InvalidDataException("游戏物体样本目录尚未初始化。");
        ClrArray entries = entriesObject.AsArray();
        ClrType? dataType = module.GetTypeByName("Terraria.ObjectData.TileObjectData");
        ClrObject data = dataType is null ? default : StaticObject(dataType, "_data", domain);
        ClrObject baseData = dataType is null ? default : StaticObject(dataType, "_baseObject", domain);
        ClrObject dataArray = Object(data, "_items", "items");
        ClrObject frameFlags = StaticObject(main, "tileFrameImportant", domain);
        bool[] important = frameFlags.IsValid && frameFlags.IsArray
            ? frameFlags.AsArray().ReadValues<bool>(0, frameFlags.AsArray().Length) ?? Array.Empty<bool>() : Array.Empty<bool>();
        ClrType? tileSets = module.GetTypeByName("Terraria.ID.TileID+Sets") ?? module.GetTypeByName("Terraria.ID.TileID/Sets");
        bool[] ores = Flags(tileSets, "Ore", domain), gems = Flags(tileSets, "Gems", domain);
        Dictionary<int, string> english = new();
        Dictionary<string, int> itemIds = new(StringComparer.Ordinal);
        Dictionary<string, ushort> naturalTypes = new(StringComparer.Ordinal);
        string? gamePath = File.Exists(module.Name) ? module.Name : TerrariaItemNameCatalog.FindTerrariaExecutable();
        if (gamePath is not null)
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(gamePath);
            foreach (FieldDefinition field in assembly.MainModule.GetType("Terraria.ID.ItemID")!.Fields)
                if (field.HasConstant && field.Constant is not null && field.Name != "Count")
                {
                    int id = Convert.ToInt32(field.Constant);
                    english.TryAdd(id, field.Name);
                    itemIds[field.Name] = id;
                }
            foreach (FieldDefinition field in assembly.MainModule.GetType("Terraria.ID.TileID")!.Fields)
                if (field.HasConstant && field.Constant is not null && field.Name != "Count") naturalTypes[field.Name] = Convert.ToUInt16(field.Constant);
        }
        Dictionary<int, string>? runtimeNames = GameProcessLocator.Find()?.IsTmod == true ? RuntimeItemCatalog.Load(module, domain) : null;
        List<PlacedObjectSpec> catalog = new();
        HashSet<ushort> plantTypes = naturalTypes.Where(pair => NaturalObjectCatalog.IsPlantTile(pair.Key)).Select(pair => pair.Value).ToHashSet();
        for (int index = 0; index < entries.Length; index++)
        {
            if ((index & 63) == 0) token.ThrowIfCancellationRequested();
            ClrValueType entry = entries.GetStructValue(index);
            ClrObject item;
            int id;
            try { id = entry.ReadField<int>("key"); item = entry.ReadObjectField("value"); }
            catch { continue; }
            if (id <= 0 || !item.IsValid) continue;
            string name = runtimeNames is not null ? runtimeNames.GetValueOrDefault(id, "物品 " + id) : TerrariaItemNameCatalog.Resolve(id) ?? english.GetValueOrDefault(id, "物品 " + id);
            int tile = Read(item, "createTile", -1), wall = Read(item, "createWall", -1), style = Read(item, "placeStyle", 0);
            if (wall > 0 && wall < ushort.MaxValue)
                catalog.Add(new(name, english.GetValueOrDefault(id, ""), (ushort)wall, 0, true, false, 1, 1, 18, new[] { 0 }, 18, 18, true, 1, 1, 1));
            if (tile < 0 || tile >= ushort.MaxValue) continue;
            catalog.Add(CreateSpec(id, (ushort)tile, style));
        }

        PlacedObjectSpec CreateSpec(int id, ushort tile, int style)
        {
            string name = runtimeNames is not null ? runtimeNames.GetValueOrDefault(id, "物品 " + id) : TerrariaItemNameCatalog.Resolve(id) ?? english.GetValueOrDefault(id, "物品 " + id);
            bool framed = tile < important.Length && important[tile];
            ClrObject shape = dataArray.IsValid && dataArray.IsArray && tile < dataArray.AsArray().Length
                ? dataArray.AsArray().GetObjectValue(tile) : default;
            ClrObject subData = Object(Object(shape, "_subTiles"), "data");
            ClrObject subArray = Object(subData, "_items", "items");
            if (subArray.IsValid && subArray.IsArray && style >= 0 && style < subArray.AsArray().Length)
            {
                ClrObject sub = subArray.AsArray().GetObjectValue(style);
                if (sub.IsValid) shape = sub;
            }
            ClrObject dimensions = Module(shape, baseData, "_tileObjectBase");
            ClrObject coords = Module(shape, baseData, "_tileObjectCoords");
            ClrObject styles = Module(shape, baseData, "_tileObjectStyle");
            int width = Math.Clamp(Read(dimensions, "width", 1), 1, 40), height = Math.Clamp(Read(dimensions, "height", 1), 1, 40);
            int padding = Math.Clamp(Read(coords, "padding", 2), 0, 16);
            int stepX = Math.Clamp(Read(coords, "width", 16) + padding, 1, 64);
            ClrObject heights = Object(coords, "heights");
            int[] coordinateHeights = heights.IsValid && heights.IsArray
                ? heights.AsArray().ReadValues<int>(0, heights.AsArray().Length) ?? Array.Empty<int>() : Array.Empty<int>();
            int[] rowOffsets = new int[height];
            int fullHeight = 0;
            for (int row = 0; row < height; row++)
            {
                rowOffsets[row] = fullHeight;
                fullHeight += (row < coordinateHeights.Length ? coordinateHeights[row] : 16) + padding;
            }
            int fullWidth = Read(coords, "styleWidth", 0), recordedHeight = Read(coords, "styleHeight", 0);
            if (fullWidth <= 0) fullWidth = width * stepX;
            if (recordedHeight > 0) fullHeight = recordedHeight;
            if (!framed) { width = height = 1; rowOffsets = new[] { 0 }; fullWidth = fullHeight = 18; }
            return new(name, english.GetValueOrDefault(id, ""), tile, style, false, framed, width, height,
                stepX, rowOffsets, Math.Max(1, fullWidth), Math.Max(1, fullHeight), Read(styles, "horizontal", true),
                Read(styles, "styleWrapLimit", 1), Math.Max(1, Read(styles, "styleMultiplier", 1)), Math.Max(1, Read(styles, "styleLineSkip", 1)),
                (tile < ores.Length && ores[tile]) || (tile < gems.Length && gems[tile]) || english.GetValueOrDefault(id, "").EndsWith("Ore", StringComparison.Ordinal)
                    || english.GetValueOrDefault(id, "") is "Hellstone" or "Obsidian", Plant: plantTypes.Contains(tile));
        }

        // Flexible wands include Rubblemaker decorations, shells, coral, gems and
        // other objects which an item's createTile alone cannot describe.
        ClrType? wands = module.GetTypeByName("Terraria.GameContent.FlexibleTileWand");
        if (wands is not null)
        {
            foreach (ClrStaticField field in wands.StaticFields.Where(field => field.Type?.Name == wands.Name))
            {
                token.ThrowIfCancellationRequested();
                ClrObject wand = field.ReadObject(domain);
                ClrObject buckets = Object(Object(wand, "_options"), "entries", "_entries");
                if (!buckets.IsValid || !buckets.IsArray) continue;
                ClrArray bucketEntries = buckets.AsArray();
                for (int index = 0; index < bucketEntries.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    ClrValueType entry = bucketEntries.GetStructValue(index);
                    int id = entry.ReadField<int>("key");
                    ClrObject bucket = entry.ReadObjectField("value");
                    if (id <= 0 || !bucket.IsValid) continue;
                    ClrObject options = Object(bucket, "Options");
                    ClrObject optionArray = Object(options, "_items", "items");
                    if (!optionArray.IsValid || !optionArray.IsArray) continue;
                    ClrArray values = optionArray.AsArray();
                    int size = Math.Min(Read(options, "_size", 0), values.Length);
                    for (int optionIndex = 0; optionIndex < size; optionIndex++)
                    {
                        ClrObject option = values.GetObjectValue(optionIndex);
                        int tile = Read(option, "TileIdToPlace", -1), style = Read(option, "TileStyleToPlace", -1);
                        if (tile < 0 || tile >= important.Length || style < 0) continue;
                        PlacedObjectSpec spec = CreateSpec(id, (ushort)tile, style);
                        string tileName = naturalTypes.FirstOrDefault(pair => pair.Value == tile).Key ?? "";
                        // Rubble consumes building materials, but is not an ore deposit.
                        if (tileName.Contains("Pile", StringComparison.Ordinal) || tileName.Contains("Detritus", StringComparison.Ordinal)
                            || tileName.Contains("Stalactite", StringComparison.Ordinal))
                            spec = spec with { Name = spec.Name + "（装饰）", EnglishName = spec.EnglishName + "Decoration", Mineral = false,
                                Note = "装饰名称对应碎石机耗材，不保证挖掘后掉落该材料；不是矿脉或箱内库存。" };
                        catalog.Add(spec);
                    }
                }
            }
        }
        foreach (NaturalObjectRegistration natural in NaturalObjectCatalog.Build(itemIds, naturalTypes))
        {
            token.ThrowIfCancellationRequested();
            PlacedObjectSpec spec = CreateSpec(natural.ItemId, natural.TileId, natural.Style) with
                { Plant = natural.Plant, Aliases = natural.Aliases, Note = natural.Note, NaturalPickup = true };
            // Wild single-cell plants use 18px frames even when their drawn sprite
            // is taller than one map cell; they have no TileObjectData entry.
            if (natural.SingleCell)
                spec = spec with { Framed = true, Width = 1, Height = 1, StepX = 18, RowOffsets = new[] { 0 },
                    FrameWidth = 18, FrameHeight = 18, Horizontal = true, Wrap = 0, Multiplier = 1, LineSkip = 1 };
            catalog.Add(spec);
        }
        foreach (PlacedObjectSpec spec in catalog.ToArray())
        {
            if (!spec.IsWall && naturalTypes.TryGetValue(spec.EnglishName, out ushort natural) && natural != spec.TypeId
                && ((natural < gems.Length && gems[natural]) || spec.EnglishName is "Ruby" or "Diamond" or "Sapphire" or "Emerald" or "Topaz" or "Amethyst"))
                catalog.Add(spec with { TypeId = natural, Framed = false, Width = 1, Height = 1, Style = 0, Mineral = true });
            if (!spec.IsWall && spec.TypeId == 21)
            {
                int locked = spec.Style switch { 1 => 2, 3 => 4, 18 => 23, 19 => 24, 20 => 25, 21 => 26, 22 => 27, _ => -1 };
                if (locked >= 0) catalog.Add(spec with { Name = spec.Name + "（上锁）", Style = locked });
            }
            if (!spec.IsWall && spec.TypeId == 82 && spec.Name.EndsWith("种子", StringComparison.Ordinal))
            {
                foreach (ushort herb in new ushort[] { 82, 83, 84 })
                    catalog.Add(spec with { Name = spec.Name[..^2], TypeId = herb });
            }
        }
        if (catalog.Count < 100) throw new InvalidDataException("本机物体目录不完整，不能可靠匹配名称。");
        _cacheStamp = stamp;
        return _cached = new(catalog);
    }

    public bool TryResolve(string query, out WorldObjectDefinition definition)
    {
        string key = Normalize(query);
        bool family = false;
        foreach (string prefix in new[] { "所有", "全部", "任意", "all" })
            if (key.StartsWith(prefix, StringComparison.Ordinal)) { key = key[prefix.Length..]; family = true; break; }
        if (key is "beds" or "chairs" or "tables" or "doors" or "torches") { key = key[..^1]; family = true; }
        bool lockedOnly = key.StartsWith("锁", StringComparison.Ordinal) || key.StartsWith("locked", StringComparison.OrdinalIgnoreCase);
        if (lockedOnly) key = key.StartsWith("locked", StringComparison.OrdinalIgnoreCase) ? key[6..] : key[1..];
        IEnumerable<PlacedObjectSpec> selected;
        if (key is "矿物" or "矿" or "ores" or "ore") selected = _entries.Where(item => !item.IsWall
            && item.Mineral);
        else if (key is "植物" or "花草" or "plants" or "plant") selected = _entries.Where(item => !item.IsWall && item.Plant);
        else if (key is "家具" or "furniture") selected = _entries.Where(item => !item.IsWall && item.Framed && (item.Width > 1 || item.Height > 1));
        else if (key is "箱子" or "宝箱" or "chest" or "containers" or "世界箱子") selected = _entries.Where(item => !item.IsWall && item.TypeId is 21 or 467);
        else
        {
            List<PlacedObjectSpec> exact = family ? new() : _entries.Where(item => Normalize(item.Name.Replace("（上锁）", "", StringComparison.Ordinal)) == key || Normalize(item.EnglishName) == key
                || (item.Aliases?.Any(alias => Normalize(alias) == key) ?? false)
                || (item.Name.EndsWith('箱') && Normalize(item.Name[..^1] + "宝箱") == key)).ToList();
            if (exact.Any(item => item.NaturalPickup)) exact.RemoveAll(item => !item.NaturalPickup && !item.Plant);
            if (exact.Count == 0 && key is "木床" or "木椅" or "木桌" or "木宝箱")
            {
                string vanilla = key switch { "木床" => "Bed", "木椅" => "WoodenChair", "木桌" => "WoodenTable", _ => "Chest" };
                exact = _entries.Where(item => item.EnglishName == vanilla).ToList();
            }
            if (exact.Count == 0) exact = _entries.Where(item => Normalize(item.Name) == key + "矿").ToList();
            selected = exact.Count > 0 ? exact : _entries.Where(item => Normalize(item.Name).Contains(key, StringComparison.OrdinalIgnoreCase)
                || Normalize(item.EnglishName).Contains(key, StringComparison.OrdinalIgnoreCase));
        }
        List<PlacedObjectSpec> matches = selected.DistinctBy(item => (item.TypeId, item.Style, item.IsWall, item.Name)).ToList();
        if (lockedOnly) matches.RemoveAll(item => !item.Name.EndsWith("（上锁）", StringComparison.Ordinal));
        if (matches.Count == 0 || key.Length == 0) { definition = new("", "", Array.Empty<ushort>(), 1, 1); return false; }
        bool walls = matches.All(item => item.IsWall);
        if (!walls) matches.RemoveAll(item => item.IsWall);
        definition = new("dynamic", matches.Select(item => item.Name).Distinct().Count() == 1 ? matches[0].Name : query.Trim(),
            matches.Select(item => item.TypeId).Distinct().ToArray(), 1, 1,
            Note: $"按本机游戏样本库匹配 {matches.Count} 种放置样式（目录共 {Count} 项）。"
                + (matches.Any(item => !item.Framed || item.IsWall) ? "材料图格按 12 格网格合并方向点；材料计数为实际匹配图格数。" : "完整多格物体只计数一次，并验证其各部分图格。")
                + (matches.Any(item => item.Plant) ? "植物包括不同生长阶段；数量为地图植株数，不是背包数量或保证收获数量。" : "")
                + string.Join(" ", matches.Select(item => item.Note).Where(note => note.Length > 0).Distinct()),
            Placements: matches.GroupBy(item => item.TypeId).ToDictionary(group => group.Key, group => group.ToArray()), MatchWalls: walls);
        return true;
    }

    private static string Normalize(string text) => new(text.Replace("矿石", "矿", StringComparison.Ordinal)
        .Replace("椅子", "椅", StringComparison.Ordinal).Replace("桌子", "桌", StringComparison.Ordinal)
        .Where(character => !char.IsWhiteSpace(character) && character is not '_' and not '-' and not '\'' and not '’').Select(char.ToLowerInvariant).ToArray());
    private static ClrObject Module(ClrObject shape, ClrObject fallback, string name)
    { ClrObject value = Object(shape, name); return value.IsValid ? value : Object(fallback, name); }
    private static ClrObject Object(ClrObject value, params string[] fields)
    {
        if (!value.IsValid) return default;
        foreach (string field in fields) { try { ClrObject result = value.ReadObjectField(field); if (result.IsValid) return result; } catch { } }
        return default;
    }
    private static ClrObject StaticObject(ClrType type, string field, ClrAppDomain domain) =>
        type.StaticFields.FirstOrDefault(candidate => candidate.Name == field)?.ReadObject(domain) ?? default;
    private static T Read<T>(ClrObject value, string field, T fallback) where T : unmanaged
    { try { return value.IsValid ? value.ReadField<T>(field) : fallback; } catch { return fallback; } }
    private static bool[] Flags(ClrType? type, string field, ClrAppDomain domain)
    {
        if (type is null) return Array.Empty<bool>();
        ClrObject flags = StaticObject(type, field, domain);
        return flags.IsValid && flags.IsArray ? flags.AsArray().ReadValues<bool>(0, flags.AsArray().Length) ?? Array.Empty<bool>() : Array.Empty<bool>();
    }
}
