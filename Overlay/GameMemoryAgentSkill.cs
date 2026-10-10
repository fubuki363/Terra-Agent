using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerraWikiAI.Services;

namespace TerraWikiOverlay;

internal static class GameMemoryAgentSkill
{
    public const string ToolName = "read_game_state";

    public static AgentToolRegistry CreateRegistry(OverlaySettings settings)
    {
        AgentToolRegistry registry = new();
        if (!settings.IncludeMemoryContext || !settings.GameContextSelection.HasAny)
            return registry;

        registry.Register(new AgentToolDefinition
        {
            Name = ToolName,
            DisplayName = "读取 Terraria 实时状态",
            Description = "按需只读获取当前正在运行的原版 Terraria 状态。仅当回答依赖玩家当前状态时调用，并且只请求必要类别；一般游戏知识、配方或纯攻略问题不要调用。",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    categories = new
                    {
                        type = "array",
                        description = "需要读取的最小类别集合。",
                        items = new
                        {
                            type = "string",
                            @enum = new[] { "health", "mana", "inventory", "equipment", "world", "progress", "achievements" }
                        },
                        minItems = 1,
                        uniqueItems = true
                    }
                },
                required = new[] { "categories" },
                additionalProperties = false
            },
            ExecuteAsync = (arguments, token) => ExecuteAsync(arguments, settings.GameContextSelection, token)
        });
        return registry;
    }

    private static Task<AgentToolResult> ExecuteAsync(
        JsonElement arguments,
        GameContextSelection allowed,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!arguments.TryGetProperty("categories", out JsonElement categories)
            || categories.ValueKind != JsonValueKind.Array)
            return Task.FromResult(new AgentToolResult(true, "缺少 categories 数组。", "参数缺少数据类别"));

        HashSet<string> requested = categories.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? "")
            .Where(value => value.Length > 0)
            .Take(7)
            .ToHashSet(StringComparer.Ordinal);
        List<string> unknown = requested.Where(value => value is not (
            "health" or "mana" or "inventory" or "equipment" or "world" or "progress" or "achievements")).ToList();
        if (unknown.Count > 0)
            return Task.FromResult(new AgentToolResult(true, "不支持的数据类别：" + string.Join("、", unknown), "包含未知类别"));

        GameContextSelection selection = new(
            requested.Contains("health") && allowed.Health,
            requested.Contains("mana") && allowed.Mana,
            requested.Contains("inventory") && allowed.Inventory,
            requested.Contains("equipment") && allowed.Equipment,
            requested.Contains("world") && allowed.WorldInfo,
            requested.Contains("progress") && allowed.Progress,
            requested.Contains("achievements") && allowed.Achievements);
        List<string> denied = requested.Where(value => !IsAllowed(value, allowed)).ToList();
        if (!selection.HasAny)
        {
            string reason = denied.Count == 0 ? "没有请求任何有效类别。" : "这些类别未获用户授权：" + string.Join("、", denied);
            return Task.FromResult(new AgentToolResult(true, reason, "权限不足"));
        }

        if (!TerrariaManagedMemoryContextReader.TryRead(selection, out string context, out string failure))
            return Task.FromResult(new AgentToolResult(true, "实时游戏状态读取失败：" + failure, "读取失败：" + Safe(failure)));

        string allowedText = string.Join("、", requested.Where(value => IsAllowed(value, allowed)).Select(DisplayCategory));
        string deniedText = denied.Count == 0 ? "" : "\n未返回（未获用户授权）：" + string.Join("、", denied.Select(DisplayCategory));
        return Task.FromResult(new AgentToolResult(
            false,
            context + deniedText,
            "已读取 " + allowedText));
    }

    private static bool IsAllowed(string category, GameContextSelection allowed) => category switch
    {
        "health" => allowed.Health,
        "mana" => allowed.Mana,
        "inventory" => allowed.Inventory,
        "equipment" => allowed.Equipment,
        "world" => allowed.WorldInfo,
        "progress" => allowed.Progress,
        "achievements" => allowed.Achievements,
        _ => false
    };

    private static string DisplayCategory(string category) => category switch
    {
        "health" => "血量",
        "mana" => "魔力",
        "inventory" => "背包",
        "equipment" => "装备",
        "world" => "世界",
        "progress" => "进度",
        "achievements" => "成就",
        _ => category
    };

    private static string Safe(string value)
    {
        string oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 180 ? oneLine : oneLine[..180] + "…";
    }
}
