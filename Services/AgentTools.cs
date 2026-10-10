#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerraWikiAI.Services;

internal sealed record AgentToolResult(bool IsError, string ModelContent, string UiSummary);

internal sealed record AgentToolEvent(
    string ToolName,
    string DisplayName,
    string Arguments,
    bool Completed,
    bool IsError,
    string Summary,
    string ResultContent,
    int Step);

internal sealed record AgentReasoningEvent(
    int Step,
    int Attempt,
    string Content,
    string FinishReason,
    bool HasToolCalls);

internal sealed class AgentToolDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required object Parameters { get; init; }
    public required Func<JsonElement, CancellationToken, Task<AgentToolResult>> ExecuteAsync { get; init; }

    public object ToApiSchema() => new
    {
        type = "function",
        function = new
        {
            name = Name,
            description = Description,
            parameters = Parameters
        }
    };
}

internal sealed class AgentToolRegistry
{
    private readonly Dictionary<string, AgentToolDefinition> _tools = new(StringComparer.Ordinal);

    public int Count => _tools.Count;
    public IReadOnlyList<object> ApiSchemas => _tools.Values.Select(tool => tool.ToApiSchema()).ToList();

    public void Register(AgentToolDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new ArgumentException("工具名称不能为空。", nameof(definition));
        if (!_tools.TryAdd(definition.Name, definition))
            throw new InvalidOperationException("工具名称重复：" + definition.Name);
    }

    public string GetDisplayName(string name) => _tools.TryGetValue(name, out AgentToolDefinition? tool)
        ? tool.DisplayName
        : name;

    public async Task<AgentToolResult> ExecuteAsync(
        string name,
        string arguments,
        CancellationToken token)
    {
        if (!_tools.TryGetValue(name, out AgentToolDefinition? tool))
            return new(true, "工具不存在或当前未启用：" + name, "工具不可用");

        try
        {
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new(true, "工具参数必须是 JSON 对象。", "参数格式错误");
            return await tool.ExecuteAsync(document.RootElement, token);
        }
        catch (JsonException exception)
        {
            return new(true, "工具参数不是有效 JSON：" + Safe(exception.Message), "参数解析失败");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(true, "工具执行失败：" + Safe(exception.GetBaseException().Message), "执行失败");
        }
    }

    private static string Safe(string value)
    {
        string oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 240 ? oneLine : oneLine[..240] + "…";
    }
}
