using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace TerraWikiOverlay;

internal sealed record WorldObjectDefinition(string Key, string Name, ushort[] TileIds, int Width, int Height,
    int? StatueStyle = null, bool Sword = false, string Note = "", int? RequiredStyle = null,
    IReadOnlyDictionary<ushort, PlacedObjectSpec[]>? Placements = null, bool MatchWalls = false);

internal readonly record struct WorldTile(ushort Type, ushort Header, short FrameX, short FrameY)
{
    public bool Active => (Header & 32) != 0;
}

internal static class WorldObjectCatalog
{
    private static readonly (WorldObjectDefinition Definition, string[] Aliases)[] Entries =
    {
        (new("life_crystal", "生命水晶", new ushort[] { 12 }, 2, 2), new[] { "生命水晶", "生命晶体", "心形水晶", "life crystal", "heart crystal", "heart" }),
        (new("life_fruit", "生命果", new ushort[] { 236 }, 2, 2), new[] { "生命果", "生命水果", "life fruit", "lifefruit" }),
        (new("plantera_bulb", "世纪之花花苞", new ushort[] { 238 }, 2, 2, Note: "定位的是召唤世纪之花的花苞；战斗中生成的孢子不是固定地图物体。"), new[] { "世纪之花花苞", "世纪之花孢子", "世纪之花花蕾", "花苞", "花蕾", "plantera bulb", "planterabulb" }),
        (new("statue", "雕像", new ushort[] { 105, 337, 349, 531 }, 2, 3), new[] { "雕像", "石像", "statue", "statues" }),
        (new("mushroom_statue", "蘑菇雕像", new ushort[] { 349 }, 2, 3), new[] { "蘑菇雕像", "mushroom statue" }),
        (new("boulder_statue", "巨石雕像", new ushort[] { 531 }, 2, 3), new[] { "巨石雕像", "boulder statue" }),
        (new("alphabet_statue", "字母雕像", new ushort[] { 337 }, 2, 3), new[] { "字母雕像", "alphabet statue" }),
        (new("enchanted_sword", "附魔剑石", new ushort[] { 187 }, 3, 2, Sword: true, Note: "逐格确认石中剑，不依赖花墙；也可能命中剑冢外单独生成的剑石。"), new[] { "附魔剑冢", "剑冢", "附魔剑神龛", "附魔剑石", "石中剑", "附魔剑", "enchanted sword shrine", "sword shrine", "enchanted sword" }),
        (new("larva", "幼虫（蜂王召唤物）", new ushort[] { 231 }, 3, 3), new[] { "幼虫", "蜂王幼虫", "蜂王召唤物", "larva" }),
        (new("evil_altar", "恶魔/猩红祭坛", new ushort[] { 26 }, 3, 2), new[] { "祭坛" }),
        (new("demon_altar", "恶魔祭坛", new ushort[] { 26 }, 3, 2, RequiredStyle: 0), new[] { "恶魔祭坛", "demon altar" }),
        (new("crimson_altar", "猩红祭坛", new ushort[] { 26 }, 3, 2, RequiredStyle: 1), new[] { "猩红祭坛", "crimson altar" }),
        (new("shadow_orb", "暗影珠", new ushort[] { 31 }, 2, 2, RequiredStyle: 0), new[] { "暗影珠", "shadow orb" }),
        (new("crimson_heart", "猩红之心", new ushort[] { 31 }, 2, 2, RequiredStyle: 1), new[] { "猩红之心", "crimson heart" }),
        (new("crystal_shard", "水晶碎块", new ushort[] { 129 }, 1, 1), new[] { "水晶碎块", "水晶碎片", "crystal shard", "crystals" }),
        (new("pot", "罐子", new ushort[] { 28 }, 2, 2), new[] { "罐子", "陶罐", "pots" }),
        (new("water_candle", "水蜡烛", new ushort[] { 49 }, 1, 1), new[] { "水蜡烛", "water candle" }),
        (new("chest", "箱子", new ushort[] { 21, 467 }, 2, 2), new[] { "箱子", "世界箱子", "宝箱", "chest", "containers" }),
        (new("lihzahrd_altar", "丛林蜥蜴祭坛", new ushort[] { 237 }, 3, 2), new[] { "丛林蜥蜴祭坛", "石巨人祭坛", "lihzahrd altar" }),
        (new("explosives", "炸药", new ushort[] { 141 }, 2, 2), new[] { "炸药", "explosives" }),
        (new("boulder", "巨石", new ushort[] { 138 }, 2, 2), new[] { "巨石", "落石", "boulder" })
    };
    private static readonly Lazy<IReadOnlyDictionary<int, string>> StatueNames = new(BuildStatueNames);
    private static readonly Lazy<IReadOnlyDictionary<string, int>> StatueAliases = new(BuildStatueAliases);
    private static readonly Lazy<IReadOnlyDictionary<string, ushort>> TileNames = new(BuildTileNames);

    public static string SupportedNames => string.Join("、", Entries.Select(entry => entry.Definition.Name));

    public static bool TryResolve(string query, out WorldObjectDefinition definition)
    {
        string normalized = Normalize(query);
        foreach (var entry in Entries)
        {
            if (Normalize(entry.Definition.Key) == normalized || entry.Aliases.Any(alias => Normalize(alias) == normalized))
            {
                definition = entry.Definition;
                return true;
            }
        }
        if (normalized.Contains("雕像", StringComparison.Ordinal) || normalized.Contains("statue", StringComparison.Ordinal))
        {
            if (StatueAliases.Value.TryGetValue(normalized, out int style))
            {
                definition = new("statue", StatueNames.Value[style], new ushort[] { 105 }, 2, 3, style);
                return true;
            }
        }
        string idText = normalized.Replace("图格id:", "", StringComparison.Ordinal).Replace("tile:", "", StringComparison.Ordinal);
        if ((ushort.TryParse(idText, out ushort tileId) && TileNames.Value.Values.Contains(tileId))
            || TileNames.Value.TryGetValue(normalized, out tileId))
        {
            definition = new("tile", query.Trim(), new[] { tileId }, 1, 1,
                Note: "按图格类型逐格查找；此类型可能包含不同样式，返回图格坐标。");
            return true;
        }
        definition = new("", "", Array.Empty<ushort>(), 1, 1);
        return false;
    }

    public static bool TryMatch(WorldObjectDefinition definition, WorldTile tile, out int style, out string name) =>
        TryMatch(definition, tile, out style, out name, out _);

    public static bool TryMatch(WorldObjectDefinition definition, WorldTile tile, out int style, out string name, out PlacedObjectSpec? placement)
    {
        placement = null;
        style = 0;
        name = definition.Name;
        if (definition.Placements is not null)
        {
            if (!definition.Placements.TryGetValue(tile.Type, out PlacedObjectSpec[]? candidates)) return false;
            foreach (PlacedObjectSpec candidate in candidates)
            {
                if (!candidate.TryMatch(tile, out style)) continue;
                placement = candidate;
                name = candidate.Name;
                return true;
            }
            return false;
        }
        if (!tile.Active || !definition.TileIds.Contains(tile.Type))
            return false;
        if (definition.Key == "tile") return true;
        if (tile.FrameX < 0 || tile.FrameY < 0) return false;
        if (definition.Sword && !TerrariaWorldLocator.IsSwordInStoneTile(tile.Type, tile.Header, tile.FrameX, tile.FrameY))
            return false;
        // Inspect the top-left cell only, so a 2x2 heart produces one result.
        if (tile.FrameX % (definition.Width * 18) != 0 || tile.FrameY % (definition.Height * 18) != 0)
            return false;
        style = tile.FrameX / (definition.Width * 18);
        if (definition.RequiredStyle.HasValue && style != definition.RequiredStyle.Value) return false;
        if (tile.Type == 105)
        {
            style = tile.FrameX / 36 + (tile.FrameY / 54 % 3) * 55;
            if (definition.StatueStyle.HasValue && definition.StatueStyle != style)
                return false;
            name = StatueNames.Value.TryGetValue(style, out string? localized) ? localized : $"雕像（样式 {style}）";
        }
        else if (tile.Type == 337)
            name = "字母雕像";
        else if (tile.Type == 349)
            name = "蘑菇雕像";
        else if (tile.Type == 531)
            name = "巨石雕像";
        else if (tile.Type == 31)
            name = tile.FrameX / 36 == 0 ? "暗影珠" : "猩红之心";
        else
            style = tile.FrameX / (definition.Width * 18);
        return true;
    }

    private static IReadOnlyDictionary<int, string> BuildStatueNames()
    {
        Dictionary<int, string> names = new();
        for (int style = 0; style <= 82; style++)
        {
            // Vanilla WorldGen.StatueStyleToItem (read from the installed game).
            int item = StatueItemId(style);
            names[style] = TerrariaItemNameCatalog.Resolve(item) ?? $"雕像（样式 {style}）";
        }
        return names;
    }

    private static int StatueItemId(int style) => style switch
    {
                0 => 360, 1 => 52, 43 => 1152, 44 => 1153, 45 => 1154, 46 => 1408,
                47 => 1409, 48 => 1410, 49 => 1462, 50 => 2672,
                >= 51 and <= 62 => 3651 + style - 51,
                >= 63 and <= 75 => 3708 + style - 63,
                76 => 4397, 77 => 4360, 78 => 4342, 79 => 4466,
                80 => 5317, 81 => 5318, 82 => 5319, _ => 438 + style - 2
    };

    private static IReadOnlyDictionary<string, int> BuildStatueAliases()
    {
        Dictionary<string, int> aliases = new(StringComparer.Ordinal);
        foreach (var entry in StatueNames.Value)
        {
            aliases[Normalize(entry.Value)] = entry.Key;
            aliases[Normalize(entry.Value.Replace("形雕像", "雕像", StringComparison.Ordinal))] = entry.Key;
        }
        aliases["红心雕像"] = 37;
        aliases["heartstatue"] = 37;
        aliases["starstatue"] = 2;
        aliases["slimestatue"] = 4;
        try
        {
            string? path = TerrariaItemNameCatalog.FindTerrariaExecutable();
            if (path is null) return aliases;
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);
            var fields = assembly.MainModule.GetType("Terraria.ID.ItemID")!.Fields.Where(field => field.HasConstant && field.Constant is not null);
            Dictionary<int, string> itemNames = fields.GroupBy(field => Convert.ToInt32(field.Constant))
                .ToDictionary(group => group.Key, group => group.First().Name);
            for (int style = 0; style <= 82; style++)
                if (itemNames.TryGetValue(StatueItemId(style), out string? name)) aliases[Normalize(name)] = style;
        }
        catch { }
        return aliases;
    }

    private static IReadOnlyDictionary<string, ushort> BuildTileNames()
    {
        Dictionary<string, ushort> names = new(StringComparer.Ordinal);
        try
        {
            string? path = TerrariaItemNameCatalog.FindTerrariaExecutable();
            if (path is null) return names;
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);
            foreach (FieldDefinition field in assembly.MainModule.GetType("Terraria.ID.TileID")?.Fields ?? Enumerable.Empty<FieldDefinition>())
            {
                if (field.HasConstant && field.Constant is not null && field.Name != "Count")
                    names[Normalize(field.Name)] = Convert.ToUInt16(field.Constant);
            }
        }
        catch { }
        return names;
    }

    private static string Normalize(string value) => new(value.Replace('：', ':').Where(character => !char.IsWhiteSpace(character)
        && character is not '_' and not '-').Select(char.ToLowerInvariant).ToArray());
}
