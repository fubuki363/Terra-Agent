using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerraWikiAI.Services;

namespace CoreSmoke;

internal static class StreamingChecks
{
    internal static async Task Run(string directory)
    {
        int executions = 0;
        AgentToolRegistry tools = new();
        tools.Register(new AgentToolDefinition { Name = "inspect", DisplayName = "检查", Description = "测试", Parameters = new { type = "object" },
            ExecuteAsync = (args, _) => { executions++; return Task.FromResult(new AgentToolResult(false, args.GetProperty("target").GetString()!, "已检查")); } });
        List<AgentStreamEvent> streams = new(); List<AgentToolEvent> events = new();
        Handler handler = new((count, payload) =>
        {
            if (!payload.GetProperty("stream").GetBoolean() || !payload.GetProperty("stream_options").GetProperty("include_usage").GetBoolean()) throw new Exception("Streaming/usage not requested.");
            if (count == 1) return EventStream(
                Delta(new { reasoning_content = "先检查" }), Delta(new { reasoning_content = "两个目标。" }),
                Delta(new { content = "我先检查。" }),
                Delta(new { tool_calls = new[] { new { index = 1, id = "call-B", function = new { name = "ins", arguments = "{\"target\":\"" } }, new { index = 0, id = "call-A", function = new { name = "inspect", arguments = "{\"target\":\"甲" } } } }),
                Delta(new { tool_calls = new object[] { new { index = 0, function = new { arguments = "\"}" } }, new { index = 1, function = new { name = "pect", arguments = "乙\"}" } } } }),
                Finish("tool_calls"), Usage(), "[DONE]");
            JsonElement[] messages = payload.GetProperty("messages").EnumerateArray().ToArray();
            if (messages.Count(m => m.GetProperty("role").GetString() == "tool") != 2) throw new Exception("Missing tool result pair.");
            return EventStream(Delta(new { reasoning_content = "取得结果。" }), Delta(new { content = "两个目标" }), Delta(new { content = "检查完成。" }), Finish("stop"), Usage(), "[DONE]");
        });
        using (DeepSeekClient client = new(handler))
        {
            int usage = 0; client.UsageReported += _ => usage++;
            string answer = await client.AskAgentWithHistoryAsync("test-key", "检查", "", Array.Empty<ConversationTurn>(), tools, 0, true, CancellationToken.None,
                events.Add, streamProgress: streams.Add);
            if (answer != "两个目标检查完成。" || executions != 2 || events.Count != 4 || events[0].CallId != "call-A"
                || events[2].CallId != "call-B" || streams.Count < 5 || usage != 2) throw new Exception("Fragmented SSE assembly failed.");
        }
        // A disconnected stream with visible content must not be silently replayed.
        int before = executions;
        Handler broken = new((_, _) => EventStream(Delta(new { reasoning_content = "正在检查" }),
            Delta(new { tool_calls = new[] { new { index = 0, id = "partial", function = new { name = "inspect", arguments = "{\"target\":\"甲\"}" } } } }), Finish("tool_calls")));
        using (DeepSeekClient client = new(broken))
        {
            try { await client.AskAgentWithHistoryAsync("test-key", "检查", "", Array.Empty<ConversationTurn>(), tools, 0, true, CancellationToken.None, streamProgress: _ => { }); throw new Exception("Truncated SSE accepted."); }
            catch (IOException) { }
            if (broken.Calls != 1 || executions != before) throw new Exception("A partial stream was retried or its tools executed.");
        }
        using (CancellationTokenSource cancellation = new())
        using (DeepSeekClient client = new(new Handler((_, _) => EventStream(Delta(new { content = "部分内容" }), Finish("stop"), "[DONE]"))))
        {
            try { await client.AskAgentWithHistoryAsync("test-key", "检查", "", Array.Empty<ConversationTurn>(), tools, 0, true, cancellation.Token,
                streamProgress: _ => cancellation.Cancel()); throw new Exception("Streaming ignored cancellation."); }
            catch (OperationCanceledException) { }
        }
        List<ConversationAgentTrace> traces = new();
        for (int step = 1; step <= 40; step++)
        {
            traces.Add(new() { Kind = "reasoning", Step = step, Content = "已确认参数" });
            traces.Add(new() { Kind = "tool_call", Step = step, CallId = "history-" + step, ToolName = "inspect", Arguments = "{\"target\":\"甲\"}" });
            traces.Add(new() { Kind = "tool_result", Step = step, CallId = "history-" + step, ToolName = "inspect", Content = "甲", Summary = "已检查" });
        }
        traces.Add(new() { Kind = "tool_call", Step = 41, CallId = "unfinished", ToolName = "inspect", Arguments = "{}" });
        string historyFolder = Path.Combine(directory, "stream-history"); Directory.CreateDirectory(historyFolder);
        ConversationStore store = new(historyFolder);
        store.AddTurn("历史问题", "部分回答", Array.Empty<string>(), true, 30, agentTraces: traces, status: "cancelled", notice: "已停止");
        ConversationStore loaded = new(historyFolder);
        if (loaded.GetRecent(1).Single().AgentTraces.Count != 121 || loaded.GetRecent(1).Single().Status != "cancelled") throw new Exception("Long history truncated.");
        Handler replay = new((_, payload) =>
        {
            JsonElement[] messages = payload.GetProperty("messages").EnumerateArray().ToArray();
            if (messages.Count(m => m.GetProperty("role").GetString() == "tool") != 40
                || messages.Any(m => m.TryGetProperty("tool_call_id", out JsonElement id) && id.GetString() == "unfinished")) throw new Exception("Replay lost tool pairs or included a pending call.");
            return EventStream(Delta(new { content = "继续完成" }), Finish("stop"), "[DONE]");
        });
        using (DeepSeekClient client = new(replay)) await client.AskAgentWithHistoryAsync("test-key", "继续", "", loaded.GetRecent(1), tools, 0, true, CancellationToken.None, streamProgress: _ => { });
        ConversationTurn interrupted = new() { Status = "running", Question = "崩溃恢复", Answer = "已生成内容" };
        store.SaveTurn(interrupted, true, 30); store.SaveTurn(interrupted, true, 30);
        ConversationTurn restored = new ConversationStore(historyFolder).GetRecent(1).Single();
        if (restored.Status != "cancelled" || restored.Answer != "已生成内容" || store.ActiveTurnCount != 2) throw new Exception("Checkpoint duplicated or failed crash recovery.");
        Console.WriteLine("OK streaming utf8=fragmented tools=indexed+pairs delta=visible usage=counted disconnect=no-replay cancellation=observed history=121-events+rebuild crash=recovered");
    }

    private static string Delta(object value) => JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta = value } } });
    private static string Finish(string reason) => JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta = new { }, finish_reason = reason } } });
    private static string Usage() => "{\"choices\":[],\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":3,\"total_tokens\":5}}";
    private static HttpResponseMessage EventStream(params string[] events) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(string.Join("", events.Select(data => "data: " + data + "\r\n\r\n")))))
        { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") } }
    };
    private sealed class FragmentedStream : MemoryStream
    {
        internal FragmentedStream(byte[] bytes) : base(bytes) { }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], token);
    }
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<int, JsonElement, HttpResponseMessage> _respond;
        internal int Calls;
        internal Handler(Func<int, JsonElement, HttpResponseMessage> respond) => _respond = respond;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            return _respond(++Calls, document.RootElement);
        }
    }
}
