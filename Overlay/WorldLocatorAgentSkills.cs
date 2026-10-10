using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerraWikiAI.Services;

namespace TerraWikiOverlay;

internal static class WorldLocatorAgentSkills
{
    public const string FindItemToolName = "find_item_in_storage";
    public const string FindBiomeToolName = "locate_biome";
    public const string FindObjectToolName = "locate_world_object";

    public static void Register(AgentToolRegistry registry, OverlaySettings settings, NavigationState navigation)
    {
        registry.Register(CreateNavigationTool(navigation));
        if (!settings.IncludeMemoryContext)
            return;
        if (settings.EnableStorageLocator)
            registry.Register(CreateStorageTool(navigation));
        if (settings.EnableBiomeLocator)
            registry.Register(CreateBiomeTool(navigation));
        if (settings.EnableObjectLocator)
            registry.Register(CreateObjectTool(navigation));
    }

    private static AgentToolDefinition CreateObjectTool(NavigationState navigation) => new()
    {
        Name = FindObjectToolName,
        DisplayName = "查找世界物体",
        Description = "在当前原版 Terraria 的实时地图中逐格定位世界物体。支持本机游戏样本库中的可放置物品，包括矿物、指定类型宝箱、各种家具、制作站、植物、装饰、背景墙，以及生命水晶、生命果、世纪之花花苞、指定雕像和石中剑等自然物体。下面的名称只是示例，不是白名单。可按完整中英文物品名、内部 TileID 名或图格ID定位，也可查矿物/家具/箱子等类别。查箱内库存用 find_item_in_storage，查区域用 locate_biome。未命中不等于从未生成。",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                object_name = new { type = "string", description = "正式物体名或类别，如 铁矿、金箱、木床、工作台、生命果、心雕像、矿物、家具。可放置物体目录由游戏数据生成，不限这些例子；也支持 TileID 内部英文名或 图格ID:数字。" },
                maximum_results = new { type = "integer", minimum = 1, maximum = 24, description = "最多返回的最近目标数，默认 8。" }
            },
            required = new[] { "object_name" }, additionalProperties = false
        },
        ExecuteAsync = (arguments, token) => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            string query = ReadString(arguments, "object_name");
            if (!TerrariaWorldLocator.TryFindWorldObject(query, ReadInteger(arguments, "maximum_results", 8, 1, 24),
                out WorldObjectSearchResult? result, out string failure, token) || result is null)
                return new AgentToolResult(true, "世界物体查找失败：" + failure, Safe(failure));
            if (result.NavigationTargets.Count > 0)
                navigation.SetTargets(result.NavigationTargets, result.PlayerTileX, result.PlayerTileY);
            string content = JsonSerializer.Serialize(new
            {
                object_name = result.ObjectName, found = result.Hits.Count > 0,
                total_confirmed_objects = result.TotalObjects, scanned_tiles = result.ScannedTiles,
                total_tiles = result.TotalTiles, scan_complete = result.Complete,
                count_unit = result.CountUnit,
                multiplayer_client = result.MultiplayerClient, note = result.Note,
                navigation_active = result.NavigationTargets.Count > 0,
                navigation_instruction = result.Hits.Count > 0
                    ? "已导航到最近物体。收起对话窗口观察悬浮球箭头；右键可切换下一个目标或停止。"
                    : "本次未确认目标，不要推断它从未生成、已被开采或尚未满足生成条件。",
                hits = result.Hits.Select(hit => new { name = hit.Name, tile_id = hit.IsWall ? (ushort?)null : hit.TileId,
                    wall_id = hit.IsWall ? (ushort?)hit.TileId : null, style = hit.Style,
                    tile_x = hit.TileX, tile_y = hit.TileY, direction = hit.Direction,
                    distance_tiles = MathF.Round(hit.DistanceTiles, 1) })
            });
            return new AgentToolResult(false, content, result.Hits.Count > 0
                ? $"确认 {result.TotalObjects} {result.CountUnit}{result.ObjectName}，返回最近 {result.Hits.Count} 个方向点并启动导航"
                : $"本次未确认{result.ObjectName}" + (result.Complete ? "" : "（扫描未完整完成）"));
        }, token)
    };

    private static AgentToolDefinition CreateNavigationTool(NavigationState navigation) => new()
    {
        Name = "control_navigation", DisplayName = "管理悬浮球导航",
        Description = "用户要求取消/停止导航时使用 stop；切换下一个已找到目标时使用 next；询问导航状态时使用 status。",
        Parameters = new { type = "object", properties = new { action = new { type = "string", @enum = new[] { "stop", "next", "status" } } }, required = new[] { "action" }, additionalProperties = false },
        ExecuteAsync = (arguments, token) =>
        {
            token.ThrowIfCancellationRequested();
            string action = ReadString(arguments, "action");
            if (action == "stop") navigation.Clear();
            else if (action == "next") navigation.MoveNext();
            else if (action != "status") return Task.FromResult(new AgentToolResult(true, "未知导航操作", "导航参数错误"));
            NavigationSnapshot current = navigation.GetSnapshot();
            return Task.FromResult(new AgentToolResult(false, JsonSerializer.Serialize(new { active = current.Active,
                target = current.Label, index = current.TargetIndex, count = current.TargetCount }),
                action == "stop" ? "导航已停止" : current.Active ? "当前目标：" + current.Label : "当前没有导航目标"));
        }
    };

    private static AgentToolDefinition CreateStorageTool(NavigationState navigation) => new()
    {
        Name = FindItemToolName,
        DisplayName = "搜索箱子与个人储物",
        Description = "在当前原版 Terraria 世界的所有已加载箱子、梳妆台以及玩家个人储物中查找指定物品。用户询问某件物品放在哪里、哪个箱子有某物或找不到物品时调用。请把 item_name 规范为游戏内正式简体中文物品名。命中世界箱子后会自动启动悬浮球方向导航。",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                item_name = new
                {
                    type = "string",
                    description = "要查找的单个 Terraria 正式物品名，例如 魔镜、回忆药水、铁锭。"
                },
                include_personal_storage = new
                {
                    type = "boolean",
                    description = "是否同时查找猪猪储蓄罐、保险箱、护卫熔炉和虚空保险库。默认 true。"
                },
                maximum_results = new
                {
                    type = "integer",
                    description = "最多返回多少个储物位置，范围 1 到 40，默认 20。",
                    minimum = 1,
                    maximum = 40
                }
            },
            required = new[] { "item_name" },
            additionalProperties = false
        },
        ExecuteAsync = (arguments, token) => Task.Run(() => FindItem(arguments, navigation, token), token)
    };

    private static AgentToolDefinition CreateBiomeTool(NavigationState navigation) => new()
    {
        Name = FindBiomeToolName,
        DisplayName = "扫描并导航到群系/结构",
        Description = "扫描当前已加载的原版 Terraria 世界，定位支持的主要群系、世界层、迷你群系或附魔剑冢结构并自动启动悬浮球方向导航。不要把示例名称当成白名单；用户询问群系、剑冢或结构位置时调用。返回坐标与本地导航摘要，不要把坐标作为唯一导航方式；提醒用户收起窗口观察悬浮球箭头。",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                biome = new
                {
                    type = "string",
                    description = "要定位的原版 Terraria 群系、迷你群系或结构，可使用中文或英文名称。支持森林、海洋、海滩、太空、地表、地下、洞穴、地狱、微光湖、猩红/腐化及地下变体、丛林及地下丛林、神圣、雪原、沙漠及地下沙漠、发光蘑菇、地牢、丛林神庙、花岗岩洞、大理石洞、蜘蛛洞、蜂巢、陨石地、墓地、浮空岛、生命树、绿洲、宝石洞、苔藓洞、附魔剑冢/剑冢等。"
                },
                maximum_results = new
                {
                    type = "integer",
                    description = "最多返回多少个候选区域，范围 1 到 8，默认 3。",
                    minimum = 1,
                    maximum = 8
                }
            },
            required = new[] { "biome" },
            additionalProperties = false
        },
        ExecuteAsync = (arguments, token) => Task.Run(() => FindBiome(arguments, navigation, token), token)
    };

    private static AgentToolResult FindItem(JsonElement arguments, NavigationState navigation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string itemName = ReadString(arguments, "item_name");
        if (itemName.Length == 0)
            return new(true, "缺少 item_name。", "缺少物品名");
        bool includePersonal = ReadBoolean(arguments, "include_personal_storage", true);
        int maximum = ReadInteger(arguments, "maximum_results", 20, 1, 40);
        if (!TerrariaWorldLocator.TryFindItem(itemName, maximum, includePersonal, out StorageSearchResult? result, out string failure, token)
            || result is null)
        {
            return new(true, "储物搜索失败：" + failure, "搜索失败：" + Safe(failure));
        }

        if (result.NavigationTargets.Count > 0)
            navigation.SetTargets(result.NavigationTargets, result.PlayerTileX, result.PlayerTileY);
        string content = JsonSerializer.Serialize(new
        {
            query = result.Query,
            matched_item_names = result.MatchedItemNames,
            found = result.Hits.Count > 0,
            scanned_world_chests = result.ScannedWorldChests,
            multiplayer_warning = result.MultiplayerClient
                ? "当前为多人客户端，只能保证搜索客户端已经同步的箱子数据。"
                : null,
            navigation_active = result.NavigationTargets.Count > 0,
            navigation_instruction = result.NavigationTargets.Count > 0
                ? "已选择距离玩家最近的世界箱子。收起对话窗口后，悬浮球箭头会持续指向目标；右键可切换下一个结果或停止导航。"
                : result.Hits.Count > 0 ? "命中结果只在个人储物中，不需要世界方向导航。" : "没有命中。",
            hits = result.Hits.Select(hit => new
            {
                storage = hit.StorageLabel,
                storage_kind = hit.StorageKind,
                item = hit.ItemName,
                item_id = hit.ItemId,
                stack = hit.Stack,
                slots = hit.Slots,
                placement = hit.Placement,
                direction = hit.Direction,
                distance_tiles = hit.DistanceTiles.HasValue ? (float?)MathF.Round(hit.DistanceTiles.Value, 1) : null
            })
        });
        string summary = result.Hits.Count == 0
            ? $"扫描 {result.ScannedWorldChests} 个世界箱子，未找到 {itemName}"
            : $"找到 {result.Hits.Count} 个储物位置" + (result.NavigationTargets.Count > 0 ? "，已启动悬浮球导航" : "");
        return new(false, content, summary);
    }

    private static AgentToolResult FindBiome(JsonElement arguments, NavigationState navigation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string biome = ReadString(arguments, "biome");
        if (biome.Length == 0)
            return new(true, "缺少 biome。", "缺少群系名称");
        int maximum = ReadInteger(arguments, "maximum_results", 3, 1, 8);
        if (!TerrariaWorldLocator.TryFindBiome(biome, maximum, out BiomeSearchResult? result, out string failure, token)
            || result is null)
        {
            return new(true, "定位扫描失败：" + failure, "扫描失败：" + Safe(failure));
        }

        navigation.SetTargets(result.NavigationTargets, result.PlayerTileX, result.PlayerTileY);
        string content = JsonSerializer.Serialize(new
        {
            biome = result.Biome,
            found = result.Hits.Count > 0,
            scan_stride_tiles = result.SampleStride,
            scanned_samples = result.ScannedSamples,
            exact_structure = result.ExactStructure,
            navigation_active = true,
            navigation_instruction = result.ExactStructure
                ? "已逐格确认仍存在的附魔剑石。收起对话窗口后，悬浮球箭头会指向剑石；右键可切换其他剑石或停止导航。此结果也可能包含剑冢外单独生成的石中剑。"
                : result.SampleStride == 0
                ? "已选择离玩家最近的世界层或边界目标。收起对话窗口后，悬浮球箭头会持续指向目标；右键可切换其他目标或停止导航。"
                : "已选择离玩家最近的候选区域。收起对话窗口后，悬浮球箭头会持续指向目标；右键可切换其他区域或停止导航。位置来自稀疏图格/墙体扫描，箭头指向最近的命中特征位置。",
            regions = result.Hits.Select(hit => new
            {
                tile_x = hit.TileX,
                tile_y = hit.TileY,
                direction = hit.Direction,
                distance_tiles = MathF.Round(hit.DistanceTiles, 1),
                confidence_samples = hit.SampleCount
            })
        });
        return new(false, content, $"找到 {result.Hits.Count} 个{result.Biome}候选区域，已启动悬浮球导航");
    }

    private static string ReadString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            return "";
        string text = (value.GetString() ?? "").Trim();
        return text.Length <= 80 ? text : text[..80];
    }

    private static bool ReadBoolean(JsonElement arguments, string name, bool fallback) =>
        arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int ReadInteger(JsonElement arguments, string name, int fallback, int minimum, int maximum) =>
        arguments.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static string Safe(string value)
    {
        string oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 180 ? oneLine : oneLine[..180] + "…";
    }
}
