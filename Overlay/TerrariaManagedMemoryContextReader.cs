using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace TerraWikiOverlay;

internal static class TerrariaManagedMemoryContextReader
{
    public static bool TryRead(GameContextSelection selection, out string context, out string failure)
    {
        context = "";
        failure = "";
        if (TmodReaderClient.NeedsWorker)
        {
            bool success = TmodReaderClient.TryRead("state", out string? remote, out failure, selection: selection);
            context = remote ?? ""; return success;
        }
        GameProcessInfo? game = GameProcessLocator.Find();
        using Process? process = GameProcessLocator.OpenClient(game);
        if (process is null)
        {
            failure = "没有检测到 Terraria 或 tModLoader 游戏客户端。";
            return false;
        }

        try
        {
            using IDisposable lease = TerrariaReadCoordinator.Enter();
            using DataTarget target = DataTarget.AttachToProcess(process.Id, suspend: game?.IsTmod != true);
            ClrInfo? clrInfo = target.ClrVersions.FirstOrDefault();
            if (clrInfo is null)
            {
                failure = "Terraria 进程中没有可读取的 CLR。";
                return false;
            }

            using ClrRuntime runtime = clrInfo.CreateRuntime();
            ClrModule? module = runtime.EnumerateModules().FirstOrDefault(candidate =>
                candidate.Name?.EndsWith(game?.IsTmod == true ? "tModLoader.dll" : "Terraria.exe", StringComparison.OrdinalIgnoreCase) == true);
            ClrAppDomain? domain = runtime.AppDomains.FirstOrDefault();
            if (module is null || domain is null)
            {
                failure = "无法定位 Terraria 托管模块或应用域。";
                return false;
            }

            ClrType mainType = RequireType(module, "Terraria.Main");
            if (ReadStatic(mainType, "gameMenu", domain, true))
            {
                failure = "Terraria 当前停留在主菜单，尚无活动角色/世界。";
                return false;
            }

            int playerIndex = ReadStatic(mainType, "myPlayer", domain, -1);
            ClrObject playerArrayObject = ReadStaticObject(mainType, "player", domain);
            if (!playerArrayObject.IsValid || !playerArrayObject.IsArray)
                throw new InvalidDataException("Main.player 不是有效数组。");
            ClrArray playerArray = playerArrayObject.AsArray();
            if (playerIndex < 0 || playerIndex >= playerArray.Length)
                throw new InvalidDataException("当前玩家索引无效。");
            ClrObject player = playerArray.GetObjectValue(playerIndex);
            if (!player.IsValid)
                throw new InvalidDataException("当前玩家对象无效。");

            ClrType npcType = RequireType(module, "Terraria.NPC");
            ClrType worldGenType = RequireType(module, "Terraria.WorldGen");
            ClrType langType = RequireType(module, "Terraria.Lang");
            ClrObject itemNameCache = ReadStaticObject(langType, "_itemNameCache", domain);
            ClrObject prefixCache = ReadStaticObject(langType, "prefix", domain);
            bool tmod = game?.IsTmod == true;
            StringBuilder builder = new();
            builder.AppendLine($"数据来源：正在运行的{game?.DisplayName ?? "Terraria"} 托管内存，只读即时快照；未注入、未修改游戏进程，也未发送原始内存。");
            builder.AppendLine("解释规则：当前值和上限是两个独立字段。生命 120/400 表示当前 120、上限 400；当前值不等于上限，除非两个数字确实相同。");

            if (selection.Vitals)
            {
                int life = ReadInstance(player, "statLife", 0);
                int lifeMax = ReadInstance(player, "statLifeMax2", ReadInstance(player, "statLifeMax", 0));
                int mana = ReadInstance(player, "statMana", 0);
                int manaMax = ReadInstance(player, "statManaMax2", ReadInstance(player, "statManaMax", 0));
                int defense = ReadDefense(player);
                string playerName = ReadString(player, "name", "未知角色");
                builder.AppendLine($"[角色] 活动角色={playerName}");
                if (selection.Health)
                    builder.AppendLine($"[血量] 当前生命={life}；生命上限={lifeMax}；生命比例={FormatRatio(life, lifeMax)}；当前防御={defense}");
                if (selection.Mana)
                    builder.AppendLine($"[魔力] 当前魔力={mana}；魔力上限={manaMax}；魔力比例={FormatRatio(mana, manaMax)}");
            }

            ClrObject inventory = player.ReadObjectField("inventory");
            if (selection.Inventory)
            {
                int selectedItem = ReadSelectedItem(player);
                builder.AppendLine("[背包] 当前选中槽=" + (selectedItem >= 0 ? selectedItem.ToString() : "未知") + "；物品：" + FormatItemArray(inventory, itemNameCache, 58, false, prefixCache, tmod));
            }

            if (selection.Equipment)
            {
                ClrObject armor = player.ReadObjectField("armor");
                ClrObject misc = player.ReadObjectField("miscEquips");
                builder.AppendLine("[装备] 当前生效的护甲/饰品：" + FormatItemArray(armor, itemNameCache, 10, true, prefixCache, tmod));
                builder.AppendLine("[杂项装备] 宠物/照明宠物/矿车/坐骑/钩爪（不是护甲或战斗饰品）：" + FormatItemArray(misc, itemNameCache, 5, false, prefixCache, tmod));
                if (tmod)
                {
                    ClrObject extraPlayers = TryReadObject(player, "modPlayers");
                    if (extraPlayers.IsValid && extraPlayers.IsArray)
                    for (int index = 0; index < Math.Min(extraPlayers.AsArray().Length, 500); index++)
                    {
                        ClrObject extension = extraPlayers.AsArray().GetObjectValue(index);
                        if (extension.Type?.Name != "Terraria.ModLoader.Default.ModAccessorySlotPlayer") continue;
                        ClrObject slots = TryReadObject(extension, "exAccessorySlot");
                        if (slots.IsValid && slots.IsArray)
                            builder.AppendLine("[模组额外饰品槽] " + FormatItemArray(slots, itemNameCache, slots.AsArray().Length / 2, false, prefixCache, true)
                                + "（列出槽位物品，是否生效由对应模组决定。）");
                    }
                }
            }

            bool hardMode = ReadStatic(mainType, "hardMode", domain, false);
            if (selection.WorldInfo)
            {
                if (tmod)
                {
                    ClrType? loader = module.GetTypeByName("Terraria.ModLoader.ModLoader");
                    ClrObject mods = loader is null ? default : ReadStaticObject(loader, "<Mods>k__BackingField", domain);
                    if (mods.IsValid && mods.IsArray)
                    {
                        List<string> labels = new();
                        for (int i = 0; i < Math.Min(mods.AsArray().Length, 100); i++)
                        {
                            ClrObject mod = mods.AsArray().GetObjectValue(i);
                            labels.Add(ReadString(mod, "<DisplayName>k__BackingField", mod.Type?.Name ?? "未知模组"));
                        }
                        builder.AppendLine("[已加载模组] " + string.Join("、", labels));
                    }
                }
                string worldName = ReadStaticString(mainType, "worldName", domain, "未知世界");
                bool crimson = ReadStatic(worldGenType, "crimson", domain, false);
                ClrObject activeWorldFile = ReadStaticObject(mainType, "ActiveWorldFileData", domain);
                int gameMode = ReadInstance(activeWorldFile, "GameMode", -1);
                bool day = ReadStatic(mainType, "dayTime", domain, false);
                bool bloodMoon = ReadStatic(mainType, "bloodMoon", domain, false);
                bool eclipse = ReadStatic(mainType, "eclipse", domain, false);
                bool raining = ReadStatic(mainType, "raining", domain, false);
                int width = ReadStatic(mainType, "maxTilesX", domain, 0);
                int height = ReadStatic(mainType, "maxTilesY", domain, 0);
                string difficulty = gameMode switch
                {
                    1 => "专家",
                    2 => "大师",
                    3 => "旅行",
                    0 => "经典",
                    _ => "未知"
                };
                builder.AppendLine($"[世界] 活动世界={worldName}；难度={difficulty}；邪恶={(crimson ? "猩红" : "腐化")}；尺寸={width}×{height}；阶段={(hardMode ? "困难模式" : "困难模式之前")}");
                builder.AppendLine($"[世界] 时段={(day ? "白天" : "夜晚")}；血月={(bloodMoon ? "是" : "否")}；日食={(eclipse ? "是" : "否")}；下雨={(raining ? "是" : "否")}");
            }

            if (selection.Progress)
            {
                if (game?.IsTmod == true) builder.AppendLine("[模组进度] 以下为原版 Boss 标记；各模组自定义 Boss 的完成进度未统一提供，不能据此判断其是否击败。");
                List<string> downed = new();
                AddFlag(downed, npcType, "downedSlimeKing", domain, "史莱姆王");
                AddFlag(downed, npcType, "downedBoss1", domain, "克苏鲁之眼");
                AddFlag(downed, npcType, "downedBoss2", domain, "世界吞噬怪/克苏鲁之脑");
                AddFlag(downed, npcType, "downedBoss3", domain, "骷髅王");
                AddFlag(downed, npcType, "downedQueenBee", domain, "蜂王");
                if (hardMode) downed.Add("血肉墙（世界已进入困难模式）");
                AddFlag(downed, npcType, "downedQueenSlime", domain, "史莱姆皇后");
                AddFlag(downed, npcType, "downedMechBoss1", domain, "毁灭者");
                AddFlag(downed, npcType, "downedMechBoss2", domain, "双子魔眼");
                AddFlag(downed, npcType, "downedMechBoss3", domain, "机械骷髅王");
                AddFlag(downed, npcType, "downedPlantBoss", domain, "世纪之花");
                AddFlag(downed, npcType, "downedGolemBoss", domain, "石巨人");
                AddFlag(downed, npcType, "downedFishron", domain, "猪龙鱼公爵");
                AddFlag(downed, npcType, "downedEmpressOfLight", domain, "光之女皇");
                AddFlag(downed, npcType, "downedAncientCultist", domain, "拜月教邪教徒");
                AddFlag(downed, npcType, "downedMoonlord", domain, "月亮领主");
                AddFlag(downed, npcType, "downedDeerclops", domain, "独眼巨鹿");
                builder.AppendLine("[进度] 已击败（活动世界实时标记）：" + (downed.Count == 0 ? "未检测到主要 Boss" : string.Join("、", downed)));
            }

            if (selection.Achievements)
                builder.AppendLine("[成就] " + FormatAchievements(mainType, domain));

            context = builder.ToString().Trim();
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

    private static string FormatItemArray(ClrObject arrayObject, ClrObject itemNameCache, int maximum, bool validateEquipment,
        ClrObject prefixCache = default, bool tmod = false)
    {
        if (!arrayObject.IsValid || !arrayObject.IsArray)
            return "读取不可用";
        ClrArray array = arrayObject.AsArray();
        List<string> items = new();
        int rejected = 0;
        for (int slot = 0; slot < Math.Min(maximum, array.Length); slot++)
        {
            ClrObject item = array.GetObjectValue(slot);
            if (!item.IsValid) continue;
            int type = ReadInstance(item, "type", ReadInstance(item, "netID", 0));
            int stack = ReadInstance(item, "stack", 0);
            if (type <= 0 || stack <= 0) continue;
            if (stack > ReadInstance(item, "maxStack", 9999) || validateEquipment && !IsValidEquipmentSlot(item, slot))
            {
                rejected++;
                continue;
            }
            int prefix = item.Type?.GetFieldByName("prefix")?.ElementType == ClrElementType.Int32
                ? ReadInstance(item, "prefix", 0) : ReadInstance(item, "prefix", (byte)0);
            string name = ResolveItemName(itemNameCache, type);
            if (name == "未知物品" && !tmod)
                name = TerrariaItemNameCatalog.Resolve(type) ?? name;
            string slotName = validateEquipment ? EquipmentSlotName(slot) : $"槽{slot}";
            string prefixText = prefix == 0 ? "" : tmod && prefix > 84
                ? $" {ResolveItemName(prefixCache, prefix).Replace("未知物品", "模组前缀")}[前缀ID {prefix}]"
                : prefix <= byte.MaxValue ? TerrariaPrefixCatalog.Format((byte)prefix, validateEquipment && slot >= 3) : $" 前缀ID {prefix}";
            items.Add($"{slotName}:{name}[ID {type}]×{stack}" + prefixText);
        }
        string result = items.Count == 0 ? "空/未装备" : string.Join("；", items);
        return rejected == 0 ? result : result + $"（已忽略 {rejected} 个字段不符合槽位或数量规则的异常项）";
    }

    private static bool IsValidEquipmentSlot(ClrObject item, int slot)
    {
        if (slot == 0 && TryReadInstance(item, "headSlot", out int headSlot)) return headSlot >= 0;
        if (slot == 1 && TryReadInstance(item, "bodySlot", out int bodySlot)) return bodySlot >= 0;
        if (slot == 2 && TryReadInstance(item, "legSlot", out int legSlot)) return legSlot >= 0;
        if (slot >= 3 && TryReadInstance(item, "accessory", out bool accessory)) return accessory;
        return true;
    }

    private static int ReadDefense(ClrObject player)
    {
        ClrInstanceField? field = player.Type?.GetFieldByName("statDefense");
        if (field?.ElementType == ClrElementType.Int32) return ReadInstance(player, "statDefense", 0);
        if (field is null) throw new InvalidDataException("防御字段不可用。");
        ClrValueType value = field.ReadStruct(player.Address, false);
        int positive = value.ReadField<int>("<Positive>k__BackingField"), negative = value.ReadField<int>("<Negative>k__BackingField");
        float additive = value.ReadValueTypeField("AdditiveBonus").ReadField<float>("<Value>k__BackingField");
        float multiplier = value.ReadValueTypeField("FinalMultiplier").ReadField<float>("<Value>k__BackingField");
        return Math.Max(0, (int)Math.Round((positive * (1 + additive) - negative) * multiplier));
    }

    private static string EquipmentSlotName(int slot) => slot switch
    {
        0 => "头盔槽",
        1 => "胸甲槽",
        2 => "护腿槽",
        >= 3 and <= 9 => $"饰品槽{slot - 2}",
        _ => $"槽{slot}"
    };

    private static int ReadSelectedItem(ClrObject player)
    {
        try
        {
            if (player.Type?.GetFieldByName("selectedItem") is not null) return player.ReadField<int>("selectedItem");
            ClrInstanceField? field = player.Type?.GetFieldByName("selectedItemState");
            if (field is null) return -1;
            ClrValueType state = field.ReadStruct(player.Address, interior: false);
            return state.ReadField<int>("selected");
        }
        catch
        {
            return -1;
        }
    }

    private static string ResolveItemName(ClrObject cacheObject, int type)
    {
        try
        {
            if (!cacheObject.IsValid || !cacheObject.IsArray) return "未知物品";
            ClrArray cache = cacheObject.AsArray();
            if (type < 0 || type >= cache.Length) return "未知物品";
            ClrObject localized = cache.GetObjectValue(type);
            if (!localized.IsValid) return "未知物品";
            ClrObject value = localized.ReadObjectField("_value");
            if (value.IsValid && value.Type?.Name == "System.String")
                return value.AsString(160) ?? "未知物品";
            return ReadString(localized, "<EnglishValue>k__BackingField", "未知物品");
        }
        catch
        {
            return "未知物品";
        }
    }

    private static string FormatAchievements(ClrType mainType, ClrAppDomain domain)
    {
        try
        {
            ClrObject main = ReadStaticObject(mainType, "instance", domain);
            ClrObject manager = main.ReadObjectField("_achievements");
            ClrObject dictionary = manager.ReadObjectField("_achievements");
            int total = ReadInstance(dictionary, "_count", ReadInstance(dictionary, "count", 0));
            ClrObject entriesObject = TryReadObject(dictionary, "_entries", "entries");
            if (!entriesObject.IsValid || !entriesObject.IsArray)
                return "读取不可用；不能据此判断完成状态。";
            ClrArray entries = entriesObject.AsArray();
            List<string> completed = new();
            for (int i = 0; i < Math.Min(total, entries.Length); i++)
            {
                ClrValueType entry = entries.GetStructValue(i);
                ClrObject achievement = TryReadObject(entry, "value", "_value");
                if (!achievement.IsValid) continue;
                int completedCount = ReadValue(entry: achievement, "_completedCount", 0);
                ClrObject conditions = achievement.ReadObjectField("_conditions");
                int conditionCount = ReadInstance(conditions, "_count", ReadInstance(conditions, "count", 0));
                if (conditionCount <= 0 || completedCount < conditionCount) continue;
                string name = ReadString(achievement, "Name", "未知成就");
                ClrObject friendly = achievement.ReadObjectField("FriendlyName");
                string friendlyName = ReadLocalizedText(friendly);
                completed.Add(string.IsNullOrWhiteSpace(friendlyName) ? name : friendlyName);
            }
            return $"已完成 {completed.Count}/{Math.Max(total, completed.Count)}：" + (completed.Count == 0 ? "未检测到已完成成就" : string.Join("、", completed));
        }
        catch (Exception exception)
        {
            return "读取不可用（" + exception.GetBaseException().Message + "）；不能据此判断完成状态。";
        }
    }

    private static string ReadLocalizedText(ClrObject localized)
    {
        if (!localized.IsValid) return "";
        ClrObject value = localized.ReadObjectField("_value");
        return value.IsValid && value.Type?.Name == "System.String" ? value.AsString(200) ?? "" : "";
    }

    private static ClrObject TryReadObject(ClrValueType value, params string[] names)
    {
        foreach (string name in names)
        {
            try
            {
                ClrObject result = value.ReadObjectField(name);
                if (result.IsValid) return result;
            }
            catch { }
        }
        return default;
    }

    private static ClrObject TryReadObject(ClrObject value, params string[] names)
    {
        foreach (string name in names)
        {
            try
            {
                ClrObject result = value.ReadObjectField(name);
                if (result.IsValid) return result;
            }
            catch { }
        }
        return default;
    }

    private static void AddFlag(List<string> values, ClrType type, string field, ClrAppDomain domain, string label)
    {
        if (ReadStatic(type, field, domain, false)) values.Add(label);
    }

    private static ClrType RequireType(ClrModule module, string name) => module.GetTypeByName(name)
        ?? throw new InvalidDataException("找不到类型 " + name + "，可能是游戏版本尚未适配。");

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

    private static string ReadStaticString(ClrType type, string name, ClrAppDomain domain, string fallback)
    {
        ClrObject value = ReadStaticObject(type, name, domain);
        return value.IsValid && value.Type?.Name == "System.String" ? value.AsString(200) ?? fallback : fallback;
    }

    private static T ReadInstance<T>(ClrObject value, string name, T fallback) where T : unmanaged
    {
        try { return value.IsValid ? value.ReadField<T>(name) : fallback; }
        catch { return fallback; }
    }

    private static bool TryReadInstance<T>(ClrObject value, string name, out T result) where T : unmanaged
    {
        try
        {
            result = value.ReadField<T>(name);
            return value.IsValid;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static int ReadValue(ClrObject entry, string name, int fallback) => ReadInstance(entry, name, fallback);

    private static string ReadString(ClrObject value, string name, string fallback)
    {
        try { return value.IsValid ? value.ReadStringField(name, 200) ?? fallback : fallback; }
        catch { return fallback; }
    }

    private static string FormatRatio(int current, int maximum) => maximum > 0
        ? $"{Math.Clamp(current * 100d / maximum, 0d, 999d):0.#}%"
        : "未知";
}
