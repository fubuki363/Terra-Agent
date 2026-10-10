namespace TerraWikiOverlay;

internal static class TerrariaPrefixCatalog
{
    // Vanilla Terraria prefix IDs. Keep the ID as the array index; the official
    // Chinese ID list is https://terraria.wiki.gg/zh/wiki/前缀_ID.
    private static readonly string[] Names =
    [
        "", "大", "巨大", "危险", "凶残", "锋利", "尖锐", "微小", "可怕", "小",
        "钝", "倒霉", "笨重", "可耻", "重", "轻", "精准", "迅速", "急速", "恐怖",
        "致命（远程武器）", "可靠", "讨厌", "无力", "粗笨", "强大", "神秘", "精巧", "精湛", "笨拙",
        "无知", "错乱", "威猛", "禁忌", "天界", "狂怒", "锐利", "高端", "强力", "碎裂",
        "破损", "粗劣", "迅捷", "致命", "灵活", "灵巧", "残暴", "缓慢", "迟钝", "呆滞",
        "惹恼", "凶险", "狂躁", "致伤", "强劲", "粗鲁", "虚弱", "无情", "暴怒", "神级",
        "恶魔", "狂热", "坚硬", "守护", "装甲", "护佑", "奥秘", "精确", "幸运", "锯齿",
        "尖刺", "愤怒", "险恶", "轻快", "快速", "急速", "迅捷", "狂野", "鲁莽", "勇猛",
        "暴力", "传奇", "虚幻", "神话", "传奇（泰拉悠悠球变体）", "传说", "忠诚", "英勇", "专注", "耐心",
        "狂暴", "暴躁", "吝啬", "虚弱", "胆小", "热切", "弹道", "虬结"
    ];

    internal static string Format(byte id, bool equippedAccessory)
    {
        if (id == 0) return "";
        if (id >= Names.Length) return $" 未识别前缀ID {id}";

        string bonus = AccessoryBonus(id);
        string effect = bonus.Length == 0 ? "" : equippedAccessory ? $"，{bonus}" : $"，饰品装备时{bonus}";
        return $" {Names[id]}（前缀ID {id}{effect}）";
    }

    private static string AccessoryBonus(byte id) => id switch
    {
        >= 62 and <= 65 => $"+{id - 61}防御",
        66 => "+20魔力上限",
        >= 67 and <= 68 => $"+{(id - 66) * 2}%暴击率",
        >= 69 and <= 72 => $"+{id - 68}%伤害",
        >= 73 and <= 76 => $"+{id - 72}%移动速度",
        >= 77 and <= 80 => $"+{id - 76}%近战速度",
        _ => ""
    };
}
