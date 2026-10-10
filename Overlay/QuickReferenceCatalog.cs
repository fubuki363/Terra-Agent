using System;
using System.Collections.Generic;
using System.Linq;

namespace TerraWikiOverlay;

internal sealed record QuickReferenceEntry(
    string Category,
    string Name,
    string Stage,
    string Requirement,
    string Result,
    string Note,
    string Aliases = "")
{
    public bool Matches(string query)
    {
        string searchable = string.Join(' ', Category, Name, Stage, Requirement, Result, Note, Aliases);
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(part => searchable.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}

internal static class QuickReferenceCatalog
{
    // Curated vanilla Desktop references. Recipes and drops were checked against
    // the Official Terraria Wiki's Buff potions and individual enemy/item pages.
    // No live Wiki request or API key is needed to read these entries.
    internal static readonly IReadOnlyList<QuickReferenceEntry> Entries =
    [
        new("药水", "铁皮药水", "前期 / 战斗", "瓶装水 + 太阳花 + 铁矿或铅矿", "+8 防御 · 8 分钟", "低成本防御药水；铁矿与铅矿是可替代配方。", "Ironskin"),
        new("药水", "再生药水", "前期 / 战斗", "瓶装水 + 太阳花 + 蘑菇", "提高生命再生 · 8 分钟", "适合持续作战；不是瞬间治疗药水。", "Regeneration"),
        new("药水", "敏捷药水", "前期 / 移动", "瓶装水 + 闪耀根 + 仙人掌", "+25% 移动速度 · 8 分钟", "用于探索与躲避；与部分冲刺饰品的最高跑速不叠加。", "Swiftness"),
        new("药水", "耐力药水", "前期 / 战斗", "瓶装水 + 装甲洞穴鱼 + 闪耀根", "所受伤害降低 10% · 4 分钟", "装甲洞穴鱼可在地下水域钓到；与铁皮药水可以同时使用。", "Endurance"),
        new("药水", "黑曜石皮药水", "前期 / 地狱", "瓶装水 + 火焰花 + 水叶草 + 黑曜石", "免疫熔岩与燃烧 · 6 分钟", "常用于采集狱石和穿越地狱熔岩。", "Obsidian Skin"),
        new("药水", "洞穴探险药水", "前期 / 挖矿", "瓶装水 + 闪耀根 + 月光草 + 金矿或铂金矿", "显示矿石与宝藏 · 5 分钟", "金矿与铂金矿可互换；适合找矿、宝箱和生命水晶。", "Spelunker"),
        new("药水", "挖矿药水", "前期 / 挖矿", "瓶装水 + 蚁狮上颚 + 闪耀根", "挖矿速度提高 25% · 10 分钟", "蚁狮上颚来自沙漠蚁狮类敌怪。", "Mining"),
        new("药水", "光芒药水", "前期 / 探索", "瓶装水 + 太阳花 + 发光蘑菇", "自身发光 · 10 分钟", "不占手持光源位置，适合地下探索。", "Shine"),
        new("药水", "召唤药水", "前期 / 召唤", "瓶装水 + 斑驳油鱼 + 月光草", "召唤物上限 +1 · 8 分钟", "斑驳油鱼在地下丛林钓取。", "Summoning Variegated Lardfish"),
        new("药水", "魔能药水", "前期 / 法师", "瓶装水 + 月光草 + 死亡草 + 坠落之星", "魔法伤害 +20% · 4 分钟", "提升魔法武器伤害，不等同于提高魔力上限。", "Magic Power"),
        new("药水", "魔力再生药水", "前期 / 法师", "瓶装水 + 月光草 + 太阳花 + 坠落之星", "提高魔力再生 · 8 分钟", "与魔能药水作用不同，可同时使用。", "Mana Regeneration"),
        new("药水", "怒气药水", "腐化世界 / 战斗", "瓶装水 + 黑檀锦鲤 + 死亡草", "伤害 +10% · 4 分钟", "黑檀锦鲤可在腐化之地钓取；不要与暴怒药水的暴击效果混淆。", "Wrath Ebonkoi"),
        new("药水", "暴怒药水", "猩红世界 / 战斗", "瓶装水 + 血腥食人鱼 + 死亡草", "暴击率 +10% · 4 分钟", "血腥食人鱼可在猩红之地钓取；召唤物不受普通暴击率加成。", "Rage Hemopiranha"),
        new("药水", "生命力药水", "进阶 / 生存", "瓶装水 + 七彩矿鱼 + 月光草 + 寒颤棘 + 水叶草", "最大生命提高 20% · 8 分钟", "七彩矿鱼可在神圣之地钓取；提高上限不等于立即回满生命。", "Lifeforce Prismite"),

        new("掉落", "恶魔眼", "前期 / 夜晚", "夜间地表生成", "晶状体：约 33%", "晶状体可用于制作可疑眼球；黑晶状体是另一种稀有掉落。", "Demon Eye Lens"),
        new("掉落", "噬魂怪", "前期 / 腐化", "腐化之地生成", "腐肉：约 33%", "腐肉用于蠕虫诱饵等制作；别与猩红世界的椎骨混淆。", "Eater of Souls Rotten Chunk"),
        new("掉落", "猩红喀迈拉", "前期 / 猩红", "猩红之地生成", "椎骨：约 33%", "椎骨也可由血爬虫、脸怪等猩红敌怪掉落。", "Crimera Vertebra"),
        new("掉落", "地牢史莱姆", "前期 / 地牢", "地牢中生成；通常先击败骷髅王", "金钥匙：必掉", "身上能看到钥匙；金钥匙用于打开地牢锁住的金箱。", "Dungeon Slime Golden Key"),
        new("掉落", "鹰身女妖", "前期 / 高空", "太空层附近生成", "羽毛：概率掉落；巨型鹰身女妖之羽：稀有掉落", "羽毛用于羽落药水等；巨型羽毛可用于制作鹰身女妖之翼。", "Harpy Feather Giant Harpy Feather"),
        new("掉落", "飞龙", "困难模式 / 高空", "困难模式的太空层附近生成", "飞翔之魂：必掉", "制作翅膀的重要材料；掉落在飞龙头部位置并悬浮。", "Wyvern Soul of Flight"),
        new("掉落", "妖精", "困难模式 / 神圣", "地表神圣之地生成", "妖精尘：必掉 1–3 个", "常用于仙灵之翼、强效治疗药水等制作。", "Pixie Pixie Dust"),
        new("掉落", "独角兽", "困难模式 / 神圣", "地表神圣之地生成", "独角兽角：必掉", "还可能掉落恩赐苹果；雕像生成的独角兽不掉落物品。", "Unicorn Unicorn Horn"),
        new("掉落", "混沌精", "困难模式 / 地下神圣", "地下神圣之地的洞穴层生成", "混沌传送杖：经典 0.2%，专家/大师 0.25%", "极稀有传送工具；特殊种子可能改变掉率，普通站桩刷怪也可能因生成条件失效。", "Chaos Elemental Rod of Discord"),
        new("掉落", "冰雪巨人", "困难模式 / 暴雪", "暴雪期间的地表雪原生成", "寒霜核：必掉", "寒霜核用于寒霜装备等制作。", "Ice Golem Frost Core"),
        new("掉落", "沙尘精", "困难模式 / 沙尘暴", "沙尘暴期间的沙漠生成", "禁戒碎片：必掉", "禁戒碎片用于禁戒盔甲等制作。", "Sand Elemental Forbidden Fragment"),
        new("掉落", "地下神圣之地敌怪", "困难模式 / 地下神圣", "在地下神圣之地击败符合条件的敌怪", "光明之魂：概率掉落", "这是群系掉落，不专属于某一种怪物。", "Soul of Light"),
        new("掉落", "地下腐化 / 猩红敌怪", "困难模式 / 地下邪恶", "在地下腐化或地下猩红击败符合条件的敌怪", "暗影之魂：概率掉落", "这是群系掉落，不专属于噬魂怪或猩红喀迈拉。", "Soul of Night")
    ];

    internal static IReadOnlyList<QuickReferenceEntry> Search(string category, string query) =>
        Entries.Where(entry => (category == "全部" || entry.Category == category) && entry.Matches(query)).ToArray();
}
