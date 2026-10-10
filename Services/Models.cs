#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TerraWikiAI.Services;

internal sealed class WikiDocument
{
    public int PageId { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime RetrievedUtc { get; set; }
    public string SourceType { get; set; } = "official_wiki";
    public string SourceName { get; set; } = "官方 Terraria Wiki";
    public string Reliability { get; set; } = "官方资料";
}

internal sealed class WikiSearchRoot
{
    [JsonPropertyName("query")]
    public WikiSearchQuery? Query { get; set; }
}

internal sealed class WikiSearchQuery
{
    [JsonPropertyName("search")]
    public List<WikiSearchHit> Search { get; set; } = new();
}

internal sealed class WikiSearchHit
{
    [JsonPropertyName("pageid")]
    public int PageId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = "";
}

internal sealed class WikiParseRoot
{
    [JsonPropertyName("parse")]
    public WikiParsePage? Parse { get; set; }
}

internal sealed class WikiParsePage
{
    [JsonPropertyName("pageid")]
    public int PageId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

internal sealed class DeepSeekResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<DeepSeekChoice> Choices { get; set; } = new();

    [JsonPropertyName("error")]
    public DeepSeekError? Error { get; set; }

    [JsonPropertyName("usage")]
    public DeepSeekUsage? Usage { get; set; }
}

internal sealed class DeepSeekUsage
{
    [JsonPropertyName("prompt_tokens")]
    public long PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public long CompletionTokens { get; set; }

    [JsonPropertyName("prompt_cache_hit_tokens")]
    public long PromptCacheHitTokens { get; set; }

    [JsonPropertyName("prompt_cache_miss_tokens")]
    public long PromptCacheMissTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }
}

internal sealed class ConversationTurn
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public string ReasoningContent { get; set; } = "";
    public List<string> Sources { get; set; } = new();
    public List<ConversationToolTrace> ToolTraces { get; set; } = new();
    public List<ConversationAgentTrace> AgentTraces { get; set; } = new();
}

internal sealed class ConversationAgentTrace
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Kind { get; set; } = "";
    public int Step { get; set; }
    public int Attempt { get; set; } = 1;
    public string ToolName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Content { get; set; } = "";
    public string Summary { get; set; } = "";
    public string FinishReason { get; set; } = "";
    public bool IsError { get; set; }
}

internal sealed class ConversationToolTrace
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string ToolName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Arguments { get; set; } = "{}";
    public bool IsError { get; set; }
    public string Summary { get; set; } = "";
}

internal sealed class DeepSeekChoice
{
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("message")]
    public DeepSeekMessage Message { get; set; } = new();
}

internal sealed class DeepSeekMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<DeepSeekToolCall> ToolCalls { get; set; } = new();
}

internal sealed class DeepSeekToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public DeepSeekFunctionCall Function { get; set; } = new();
}

internal sealed class DeepSeekFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

internal sealed class DeepSeekError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
