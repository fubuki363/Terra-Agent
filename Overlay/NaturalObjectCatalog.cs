using System;
using System.Collections.Generic;

namespace TerraWikiOverlay;

internal sealed record NaturalObjectRegistration(int ItemId, string EnglishName, ushort TileId, int Style,
    bool SingleCell, bool Plant, string[] Aliases, string Note);

// Item.createTile is not a reverse drop table. These non-placeable pickups need their
// own natural tile/style mapping. IDs are resolved from the installed game's constants.
internal static class NaturalObjectCatalog
{
    public static IEnumerable<NaturalObjectRegistration> Build(IReadOnlyDictionary<string, int> items,
        IReadOnlyDictionary<string, ushort> tiles)
    {
        foreach (var rule in Rules)
        {
            if (!items.TryGetValue(rule.Item, out int id) || !tiles.TryGetValue(rule.Tile, out ushort tile)) continue;
            yield return new(id, rule.Item, tile, rule.Style, rule.SingleCell, rule.Plant, rule.Aliases, rule.Note);
        }
    }

    private static readonly (string Item, string Tile, int Style, bool SingleCell, bool Plant, string[] Aliases, string Note)[] Rules =
    {
        ("NaturesGift", "JunglePlants", 9, true, true, new[] { "Nature's Gift", "自然的恩赐", "自然恩赐" }, "按丛林植物的专属样式确认大自然的恩赐，不将普通丛林花草算作目标。"),
        ("JungleSpores", "JunglePlants", 8, true, true, Array.Empty<string>(), "定位的是地图上生长的丛林孢子植物，不是背包数量。"),
        ("JungleRose", "JunglePlants", 6, true, true, Array.Empty<string>(), "自然丛林玫瑰为候选植物，破坏时是否掉落丛林玫瑰由游戏随机决定，不保证掉落。"),
        ("JungleRose", "JunglePlants", 7, true, true, Array.Empty<string>(), "自然丛林玫瑰为候选植物，破坏时是否掉落丛林玫瑰由游戏随机决定，不保证掉落。"),
        ("Mushroom", "Plants", 8, true, true, Array.Empty<string>(), ""),
        ("Mushroom", "HallowedPlants", 8, true, true, Array.Empty<string>(), ""),
        ("VileMushroom", "CorruptPlants", 8, true, true, Array.Empty<string>(), ""),
        ("ViciousMushroom", "CrimsonPlants", 15, true, true, Array.Empty<string>(), ""),
        ("GlowingMushroom", "MushroomPlants", -1, true, true, Array.Empty<string>(), "定位小型自然发光蘑菇；蘑菇树和放置的蘑菇材料块不是同一物体。"),
        ("AbigailsFlower", "AbigailsFlower", -1, true, true, new[] { "Abigail's Flower", "阿比盖尔之花" }, ""),
        ("BoulderPet", "BoulderThatSpawnsPet", -1, false, false, new[] { "Friendly Boulder", "宠物巨石", "友善巨石" }, "定位会产生友好巨石道具的特殊静止巨石，不是普通巨石，也不是已召唤的移动宠物。不要直接触发巨石陷阱。"),
        ("PalworldChilletEgg", "PalworldChilletEgg", -1, false, false, new[] { "Huge Dragon Egg", "巨大龙蛋" }, "定位地图中的巨型龙蛋；不是龙蛋（DD2PetDragon），也不是已拾取的背包物品。"),
        ("PalworldDigtoise", "PalworldDigtoiseSleeping", -1, false, false, new[] { "Digtoise", "碎岩龟", "沉睡的碎岩龟" }, "定位地图中尚未拾取的沉睡帕鲁，不是已召唤的宠物。")
    };

    public static bool IsPlantTile(string tileName) => tileName is "Plants" or "Plants2" or "CorruptPlants"
        or "CrimsonPlants" or "JunglePlants" or "JunglePlants2" or "JunglePlantsEcho"
        or "MushroomPlants" or "HallowedPlants" or "HallowedPlants2" or "DyePlants"
        or "AbigailsFlower" or "AbigailsFlowerReplica" or "Sunflower" or "Coral"
        or "ImmatureHerbs" or "MatureHerbs" or "BloomingHerbs" or "AshPlants" or "OasisPlants" or "OasisPlantsEcho";
}
