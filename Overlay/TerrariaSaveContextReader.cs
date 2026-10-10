using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TerraWikiOverlay;

internal sealed record TerrariaItemSnapshot(int Slot, int NetId, int Stack, byte Prefix, bool Favorited);

internal sealed record TerrariaPlayerSnapshot(
    string Name,
    int SaveVersion,
    byte Difficulty,
    TimeSpan PlayTime,
    int Life,
    int LifeMax,
    int Mana,
    int ManaMax,
    IReadOnlyList<TerrariaItemSnapshot> Inventory,
    IReadOnlyList<TerrariaItemSnapshot> Equipment,
    IReadOnlyList<TerrariaItemSnapshot> MiscEquipment,
    DateTime LastSavedUtc);

internal sealed record TerrariaWorldSnapshot(
    string Title,
    int SaveVersion,
    int GameMode,
    int Width,
    int Height,
    bool IsCrimson,
    bool HardMode,
    bool DayTime,
    bool BloodMoon,
    bool Eclipse,
    bool Raining,
    IReadOnlyList<string> SpecialSeeds,
    IReadOnlyList<string> DownedBosses,
    DateTime LastSavedUtc);

internal sealed record TerrariaLocalSnapshot(
    string Context,
    IReadOnlyList<string> PlayerNames,
    IReadOnlyList<string> WorldNames);

internal static class TerrariaSaveContextReader
{
    private static readonly byte[] EncryptionKey = Encoding.Unicode.GetBytes("h3y_gUyZ");
    private const int MaxInventorySlots = 58;
    private const int MaxArmorSlots = 20;
    private const int MaxDyeSlots = 10;
    private const int MaxMiscEquipSlots = 5;

    public static TerrariaLocalSnapshot ReadLatest() => ReadLatest(GameContextSelection.All);

    public static TerrariaLocalSnapshot ReadLatest(GameContextSelection selection)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Terraria");
        string playersDirectory = Path.Combine(root, "Players");
        string worldsDirectory = Path.Combine(root, "Worlds");

        List<TerrariaPlayerSnapshot> players = new();
        if (Directory.Exists(playersDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(playersDirectory, "*.plr", SearchOption.TopDirectoryOnly))
            {
                if (TryReadPlayer(path, out TerrariaPlayerSnapshot? player) && player is not null)
                    players.Add(player);
            }
        }

        List<FileInfo> worldFiles = Directory.Exists(worldsDirectory)
            ? Directory.EnumerateFiles(worldsDirectory, "*.wld", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList()
            : new List<FileInfo>();
        List<TerrariaWorldSnapshot> worlds = new();
        foreach (FileInfo worldFile in worldFiles)
        {
            if (TryReadWorld(worldFile.FullName, out TerrariaWorldSnapshot? world) && world is not null)
                worlds.Add(world);
        }

        TerrariaPlayerSnapshot? latestPlayer = players.OrderByDescending(player => player.LastSavedUtc).FirstOrDefault();
        TerrariaWorldSnapshot? latestWorld = worlds.OrderByDescending(world => world.LastSavedUtc).FirstOrDefault();
        StringBuilder context = new();
        context.AppendLine("数据来源：最近一次原版存档的只读快照，不一定等于游戏当前画面。未读取或发送存档原始字节。");
        context.AppendLine("解释规则：斜杠左侧是当前值，右侧是上限；例如生命 120/400 表示当前 120、上限 400，绝不表示满血。");

        if (latestPlayer is null && (selection.Vitals || selection.Inventory || selection.Equipment))
        {
            context.AppendLine("角色数据：没有找到可解析的本地 .plr 存档。");
        }
        else if (latestPlayer is not null)
        {
            if (selection.Vitals)
            {
                context.AppendLine($"[角色] {latestPlayer.Name}；角色难度={DifficultyName(latestPlayer.Difficulty)}；累计游玩={FormatPlayTime(latestPlayer.PlayTime)}；存档版本={latestPlayer.SaveVersion}；最后保存={FormatAge(latestPlayer.LastSavedUtc)}");
                if (selection.Health)
                    context.AppendLine($"[血量] 当前生命={latestPlayer.Life}；生命上限={latestPlayer.LifeMax}；生命比例={FormatRatio(latestPlayer.Life, latestPlayer.LifeMax)}");
                if (selection.Mana)
                    context.AppendLine($"[魔力] 当前魔力={latestPlayer.Mana}；魔力上限={latestPlayer.ManaMax}；魔力比例={FormatRatio(latestPlayer.Mana, latestPlayer.ManaMax)}");
            }
            if (selection.Inventory)
                context.AppendLine("[背包] " + FormatItems(latestPlayer.Inventory, "空；该结论仅针对这次存档快照"));
            if (selection.Equipment)
            {
                IReadOnlyList<TerrariaItemSnapshot> activeEquipment = latestPlayer.Equipment.Where(item => item.Slot < 10).ToList();
                IReadOnlyList<TerrariaItemSnapshot> vanityEquipment = latestPlayer.Equipment.Where(item => item.Slot >= 10).ToList();
                context.AppendLine("[装备] 当前生效的护甲/饰品槽：" + FormatEquipmentItems(activeEquipment, "未装备"));
                context.AppendLine("[外观] 时装护甲/时装饰品槽（仅外观，不提供战斗属性）：" + FormatEquipmentItems(vanityEquipment, "未装备"));
                context.AppendLine("[杂项装备] 宠物/照明宠物/矿车/坐骑/钩爪槽（不是护甲或战斗饰品）：" + FormatItems(latestPlayer.MiscEquipment, "未装备"));
                context.AppendLine("[装备] 物品名称由本机 Terraria.exe 内置的官方简体中文 ItemID 映射解析；无法映射的 ID 会明确标为未知。");
            }
        }

        if (latestWorld is null && (selection.WorldInfo || selection.Progress))
        {
            context.AppendLine("世界数据：没有找到可解析的本地 .wld 存档。");
        }
        else if (latestWorld is not null)
        {
            if (selection.WorldInfo)
            {
                string specialSeeds = latestWorld.SpecialSeeds.Count == 0 ? "无" : string.Join("、", latestWorld.SpecialSeeds);
                context.AppendLine($"[世界] {latestWorld.Title}；难度={WorldDifficultyName(latestWorld.GameMode)}；邪恶={(latestWorld.IsCrimson ? "猩红" : "腐化")}；尺寸={latestWorld.Width}×{latestWorld.Height}");
                context.AppendLine($"[世界] 阶段={(latestWorld.HardMode ? "困难模式" : "困难模式之前")}；时段={(latestWorld.DayTime ? "白天" : "夜晚")}；血月={(latestWorld.BloodMoon ? "是" : "否")}；日食={(latestWorld.Eclipse ? "是" : "否")}；下雨={(latestWorld.Raining ? "是" : "否")}；特殊种子={specialSeeds}");
                context.AppendLine($"[世界] 存档版本 {latestWorld.SaveVersion}；最后保存 {FormatAge(latestWorld.LastSavedUtc)}");
            }
            if (selection.Progress)
            {
                string progress = latestWorld.DownedBosses.Count == 0 ? "这次存档快照未记录已击败的主要 Boss" : string.Join("、", latestWorld.DownedBosses);
                context.AppendLine("[进度] 已击败（世界级标记）：" + progress);
                context.AppendLine("[进度] 没列出的 Boss 可能是未击败，也可能是当前回退解析器未覆盖；不可据此武断判断。");
            }
        }

        if (selection.Achievements)
            context.AppendLine("[成就] 存档回退模式无法可靠取得 Steam/平台成就；该类别当前不可用，不能据此判断成就是否完成。");

        return new TerrariaLocalSnapshot(
            context.ToString().Trim(),
            players.Select(player => player.Name).Distinct(StringComparer.Ordinal).ToList(),
            worldFiles.Select(file => Path.GetFileNameWithoutExtension(file.Name)).Distinct(StringComparer.Ordinal).ToList());
    }

    internal static bool TryReadPlayer(string path, out TerrariaPlayerSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(path);
            if (encrypted.Length == 0 || encrypted.Length % 16 != 0)
                return false;

            using Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = EncryptionKey;
            aes.IV = EncryptionKey;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plain = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            using MemoryStream stream = new(plain, writable: false);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);

            int version = reader.ReadInt32();
            if (version < 135 || version > 1000)
                return false;
            string magic = Encoding.ASCII.GetString(reader.ReadBytes(7));
            byte fileType = reader.ReadByte();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt64();
            if (magic != "relogic" || fileType != 3)
                return false;

            string name = reader.ReadString();
            byte difficulty = version >= 17 ? reader.ReadByte() : (byte)0;
            long playTimeTicks = version >= 138 ? reader.ReadInt64() : 0;
            _ = reader.ReadInt32();
            if (version >= 82) _ = reader.ReadByte();
            if (version >= 283) _ = reader.ReadByte();
            if (version >= 124) { _ = reader.ReadByte(); _ = reader.ReadByte(); }
            else if (version >= 83) _ = reader.ReadByte();
            if (version >= 119) _ = reader.ReadByte();
            if (version >= 107) _ = reader.ReadByte();
            else if (version > 17) _ = reader.ReadBoolean();

            int life = reader.ReadInt32();
            int lifeMax = reader.ReadInt32();
            int mana = reader.ReadInt32();
            int manaMax = reader.ReadInt32();
            if (version >= 125) _ = reader.ReadBoolean();
            if (version >= 229)
            {
                _ = reader.ReadBoolean();
                _ = reader.ReadBoolean();
                if (version >= 256) _ = reader.ReadBoolean();
                if (version >= 324) _ = reader.ReadBoolean();
                if (version >= 260)
                    for (int i = 0; i < 6; i++) _ = reader.ReadBoolean();
            }
            if (version >= 182) _ = reader.ReadBoolean();
            if (version >= 128) _ = reader.ReadInt32();
            if (version >= 254) { _ = reader.ReadInt32(); _ = reader.ReadInt32(); }
            reader.BaseStream.Seek(21, SeekOrigin.Current);

            List<TerrariaItemSnapshot> equipment = new();
            List<TerrariaItemSnapshot> inventory = new();
            List<TerrariaItemSnapshot> miscEquipment = new();
            if (version >= 124)
            {
                for (int i = 0; i < MaxArmorSlots; i++)
                {
                    int netId = reader.ReadInt32();
                    byte prefix = reader.ReadByte();
                    bool favorited = version >= 322 && reader.ReadBoolean();
                    AddItem(equipment, i, netId, 1, prefix, favorited);
                }
                for (int i = 0; i < MaxDyeSlots; i++)
                {
                    _ = reader.ReadInt32();
                    _ = reader.ReadByte();
                    if (version >= 322) _ = reader.ReadBoolean();
                }
                for (int i = 0; i < MaxInventorySlots; i++)
                    AddItem(inventory, i, reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte(), version >= 114 && reader.ReadBoolean());
                if (version >= 117)
                {
                    for (int i = 0; i < MaxMiscEquipSlots; i++)
                    {
                        int netId = reader.ReadInt32();
                        byte prefix = reader.ReadByte();
                        AddItem(miscEquipment, i, netId, 1, prefix, false);
                        _ = reader.ReadInt32();
                        _ = reader.ReadByte();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || difficulty > 3
                || life is < 0 or > 100000 || lifeMax is < 1 or > 100000
                || mana is < 0 or > 100000 || manaMax is < 0 or > 100000)
                return false;

            TimeSpan playTime = playTimeTicks >= 0 && playTimeTicks <= TimeSpan.MaxValue.Ticks ? TimeSpan.FromTicks(playTimeTicks) : TimeSpan.Zero;
            snapshot = new TerrariaPlayerSnapshot(name, version, difficulty, playTime, life, lifeMax, mana, manaMax,
                inventory, equipment, miscEquipment, File.GetLastWriteTimeUtc(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryReadWorld(string path, out TerrariaWorldSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
            int version = reader.ReadInt32();
            if (version < 140 || version > 1000) return false;
            string magic = Encoding.ASCII.GetString(reader.ReadBytes(7));
            byte fileType = reader.ReadByte();
            _ = reader.ReadUInt32(); _ = reader.ReadUInt64();
            if (magic != "relogic" || fileType != 2) return false;
            int sectionCount = reader.ReadInt16();
            if (sectionCount is < 2 or > 100) return false;
            int[] pointers = new int[sectionCount];
            for (int i = 0; i < sectionCount; i++) pointers[i] = reader.ReadInt32();
            int importantTileCount = reader.ReadInt16();
            if (importantTileCount is < 0 or > 10000) return false;
            reader.BaseStream.Seek((importantTileCount + 7) / 8, SeekOrigin.Current);
            reader.BaseStream.Position = pointers[0];

            string title = reader.ReadString();
            _ = version == 179 ? reader.ReadInt32().ToString() : reader.ReadString();
            _ = reader.ReadUInt64();
            if (version >= 181) _ = reader.ReadBytes(16);
            _ = reader.ReadInt32();
            for (int i = 0; i < 4; i++) _ = reader.ReadInt32();
            int height = reader.ReadInt32();
            int width = reader.ReadInt32();
            int gameMode = version >= 209 ? reader.ReadInt32() : reader.ReadBoolean() ? 1 : 0;
            List<string> specialSeeds = new();
            if (version >= 222 && reader.ReadBoolean()) specialSeeds.Add("醉酒世界");
            if (version >= 227 && reader.ReadBoolean()) specialSeeds.Add("高难种子");
            if (version >= 238 && reader.ReadBoolean()) specialSeeds.Add("十周年");
            if (version >= 239 && reader.ReadBoolean()) specialSeeds.Add("永恒领域");
            if (version >= 241 && reader.ReadBoolean()) specialSeeds.Add("不要蜜蜂");
            if (version >= 249 && reader.ReadBoolean()) specialSeeds.Add("颠倒世界");
            if (version >= 266 && reader.ReadBoolean()) specialSeeds.Add("陷阱世界");
            if (version >= 267 && reader.ReadBoolean()) specialSeeds.Add("天顶世界");
            if (version >= 302 && reader.ReadBoolean()) specialSeeds.Add("空岛世界");
            if (version >= 141) _ = reader.ReadInt64();
            if (version >= 284) _ = reader.ReadInt64();
            _ = reader.ReadByte();
            for (int i = 0; i < 3; i++) _ = reader.ReadInt32();
            for (int i = 0; i < 4; i++) _ = reader.ReadInt32();
            for (int i = 0; i < 3; i++) _ = reader.ReadInt32();
            for (int i = 0; i < 7; i++) _ = reader.ReadInt32();
            _ = reader.ReadInt32(); _ = reader.ReadInt32();
            _ = reader.ReadDouble(); _ = reader.ReadDouble(); _ = reader.ReadDouble();
            bool dayTime = reader.ReadBoolean();
            _ = reader.ReadInt32();
            bool bloodMoon = reader.ReadBoolean();
            bool eclipse = reader.ReadBoolean();
            _ = reader.ReadInt32(); _ = reader.ReadInt32();
            bool crimson = reader.ReadBoolean();

            List<string> downed = new();
            AddBoss(downed, reader.ReadBoolean(), "克苏鲁之眼");
            AddBoss(downed, reader.ReadBoolean(), crimson ? "克苏鲁之脑" : "世界吞噬怪");
            AddBoss(downed, reader.ReadBoolean(), "骷髅王");
            AddBoss(downed, reader.ReadBoolean(), "蜂王");
            AddBoss(downed, reader.ReadBoolean(), "毁灭者");
            AddBoss(downed, reader.ReadBoolean(), "双子魔眼");
            AddBoss(downed, reader.ReadBoolean(), "机械骷髅王");
            _ = reader.ReadBoolean();
            AddBoss(downed, reader.ReadBoolean(), "世纪之花");
            AddBoss(downed, reader.ReadBoolean(), "石巨人");
            if (version >= 118) AddBoss(downed, reader.ReadBoolean(), "史莱姆王");
            for (int i = 0; i < 7; i++) _ = reader.ReadBoolean();
            _ = reader.ReadBoolean(); _ = reader.ReadBoolean(); _ = reader.ReadByte(); _ = reader.ReadInt32();
            bool hardMode = reader.ReadBoolean();
            if (version >= 257) _ = reader.ReadBoolean();
            _ = reader.ReadInt32(); _ = reader.ReadInt32(); _ = reader.ReadInt32(); _ = reader.ReadDouble();
            if (version >= 118) _ = reader.ReadDouble();
            if (version >= 113) _ = reader.ReadByte();
            bool raining = reader.ReadBoolean();
            _ = reader.ReadInt32(); _ = reader.ReadSingle();
            for (int i = 0; i < 3; i++) _ = reader.ReadInt32();
            for (int i = 0; i < 8; i++) _ = reader.ReadByte();
            _ = reader.ReadInt32(); _ = reader.ReadInt16(); _ = reader.ReadSingle();
            int anglerCount = ReadBoundedCount(reader.ReadInt32(), 10000);
            for (int i = 0; i < anglerCount; i++) _ = reader.ReadString();
            _ = reader.ReadBoolean(); _ = reader.ReadInt32(); _ = reader.ReadBoolean();
            if (version >= 140) _ = reader.ReadBoolean();
            if (version >= 201) _ = reader.ReadBoolean();
            _ = reader.ReadInt32(); _ = reader.ReadInt32();
            int bannerCount = ReadBoundedCount(reader.ReadInt16(), 10000);
            for (int i = 0; i < bannerCount; i++) _ = reader.ReadInt32();
            if (version >= 289)
            {
                int claimableCount = ReadBoundedCount(reader.ReadInt16(), 10000);
                for (int i = 0; i < claimableCount; i++) _ = reader.ReadUInt16();
            }
            if (version >= 140) _ = reader.ReadBoolean();
            AddBoss(downed, reader.ReadBoolean(), "猪龙鱼公爵");
            if (version >= 140)
            {
                _ = reader.ReadBoolean();
                AddBoss(downed, reader.ReadBoolean(), "拜月教邪教徒");
                AddBoss(downed, reader.ReadBoolean(), "月亮领主");
            }

            snapshot = new TerrariaWorldSnapshot(title, version, gameMode, width, height, crimson, hardMode, dayTime,
                bloodMoon, eclipse, raining, specialSeeds, downed, File.GetLastWriteTimeUtc(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddItem(List<TerrariaItemSnapshot> items, int slot, int netId, int stack, byte prefix, bool favorited)
    {
        if (netId > 0 && netId < 100000 && stack > 0 && stack <= 9999)
            items.Add(new TerrariaItemSnapshot(slot, netId, stack, prefix, favorited));
    }

    private static void AddBoss(List<string> bosses, bool downed, string name)
    {
        if (downed) bosses.Add(name);
    }

    private static int ReadBoundedCount(int value, int maximum) => value is >= 0 && value <= maximum
        ? value
        : throw new InvalidDataException("存档中的集合数量无效。");

    private static string FormatItems(IReadOnlyList<TerrariaItemSnapshot> items, string emptyText)
    {
        if (items.Count == 0) return emptyText;
        return string.Join("；", items.Select(item => $"槽{item.Slot}: {FormatItemName(item.NetId)} ×{item.Stack}"
            + TerrariaPrefixCatalog.Format(item.Prefix, false) + (item.Favorited ? "（已收藏）" : "")));
    }

    private static string FormatEquipmentItems(IReadOnlyList<TerrariaItemSnapshot> items, string emptyText)
    {
        if (items.Count == 0) return emptyText;
        return string.Join("；", items.Select(item => $"{EquipmentSlotName(item.Slot)}: {FormatItemName(item.NetId)}"
            + TerrariaPrefixCatalog.Format(item.Prefix, item.Slot is >= 3 and <= 9) + (item.Favorited ? "（已收藏）" : "")));
    }

    private static string EquipmentSlotName(int slot) => slot switch
    {
        0 => "头盔槽",
        1 => "胸甲槽",
        2 => "护腿槽",
        >= 3 and <= 9 => $"饰品槽{slot - 2}",
        10 => "时装头盔槽",
        11 => "时装胸甲槽",
        12 => "时装护腿槽",
        >= 13 and <= 19 => $"时装饰品槽{slot - 12}",
        _ => $"槽{slot}"
    };

    private static string FormatItemName(int itemId)
    {
        string? name = TerrariaItemNameCatalog.Resolve(itemId);
        return string.IsNullOrWhiteSpace(name) ? $"未知物品 [ID {itemId}]" : $"{name} [ID {itemId}]";
    }

    private static string DifficultyName(byte difficulty) => difficulty switch
    {
        0 => "经典", 1 => "中核", 2 => "硬核", 3 => "旅行", _ => "未知"
    };

    private static string WorldDifficultyName(int gameMode) => gameMode switch
    {
        0 => "经典", 1 => "专家", 2 => "大师", 3 => "旅行", _ => "未知"
    };

    private static string FormatPlayTime(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours} 小时 {value.Minutes} 分钟" : $"{value.Minutes} 分钟";

    private static string FormatRatio(int current, int maximum) => maximum > 0
        ? $"{Math.Clamp(current * 100d / maximum, 0d, 999d):0.#}%"
        : "未知";

    private static string FormatAge(DateTime utc)
    {
        TimeSpan age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 2) return "刚刚";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} 分钟前";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} 小时前";
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
