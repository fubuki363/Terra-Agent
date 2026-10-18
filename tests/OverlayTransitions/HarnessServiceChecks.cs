using System.Net;
using System.Text;
using TerraWikiAI.Services;
using TerraWikiOverlay;

internal static class HarnessServiceChecks
{
    internal static async Task Run(string root)
    {
        string directory = Path.Combine(root, "service");
        Directory.CreateDirectory(directory);
        using OverlayAskService service = new(directory, new DeepSeekClient(new Handler()));
        if (!service.SaveApiKey("sk-test-only-123456789012345678901234567890", out string message)) throw new Exception(message);
        List<OverlayMessage> received = new();
        await service.AskAsync("测试调用并保留记录", false, new InlineProgress(value => received.Add(value)));
        ConversationTurn turn = service.GetCurrentTurns().Single();
        if (turn.Status != "completed" || turn.Answer != "已完成测试" || turn.AgentTraces.Count(t => t.Kind == "tool_result" && t.IsError) != 1
            || !received.Any(value => value.Kind == OverlayMessageKind.Stream)) throw new Exception("Service stream/tool failure projection failed.");
        using OverlayAskService loaded = new(directory, new DeepSeekClient(new Handler()));
        if (loaded.GetCurrentTurns().Single().AgentTraces.Single(t => t.Kind == "tool_result").CallId != "missing-1") throw new Exception("Service lost durable call ID.");
        // Cancel from a live delta; the final partial snapshot must survive a reload.
        using OverlayAskService cancellable = new(Path.Combine(root, "cancel"), new DeepSeekClient(new PartialHandler()));
        cancellable.SaveApiKey("sk-test-only-123456789012345678901234567890", out _);
        await cancellable.AskAsync("中断测试", false, new InlineProgress(value =>
        {
            if (value.Kind == OverlayMessageKind.Stream) cancellable.CancelCurrentRequest();
        }));
        ConversationTurn cancelled = cancellable.GetCurrentTurns().Single();
        if (cancelled.Status != "cancelled" || cancelled.Answer != "正在生成的部分回答") throw new Exception("Service discarded a cancelled partial response.");
        Console.WriteLine("OK harness-service retrieval=on-demand stream=forwarded tool-error=normalized checkpoints=durable cancelled-answer=retained");
    }

    private sealed class InlineProgress : IProgress<OverlayMessage>
    {
        private readonly Action<OverlayMessage> _report;
        internal InlineProgress(Action<OverlayMessage> report) => _report = report;
        public void Report(OverlayMessage value) => _report(value);
    }
    private sealed class Handler : HttpMessageHandler
    {
        private int _calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await Task.Delay(50, token);
            string body = ++_calls == 1
                ? "data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"检查工具错误处理\",\"tool_calls\":[{\"index\":0,\"id\":\"missing-1\",\"function\":{\"name\":\"missing_tool\",\"arguments\":\"{}\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\ndata: [DONE]\n\n"
                : "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"已完成测试\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        }
    }
    private sealed class PartialHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            // Ensure the service publishes this first snapshot (40ms UI coalescing window).
            await Task.Delay(60, token);
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"正在生成的部分回答\"}}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }
}
