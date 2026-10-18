using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using TerraWikiAI.Services;
using TerraWikiOverlay;

internal static class HarnessChatChecks
{
    private sealed class QuietHost : Form
    {
        protected override bool ShowWithoutActivation => true;
        internal QuietHost()
        {
            ShowInTaskbar = false; Opacity = 0; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(-16000, -16000, 1180, 820);
        }
    }

    internal static void Run(string directory, string[] args)
    {
        using QuietHost host = new();
        using HarnessChatView chat = new(directory);
        host.Controls.Add(chat);
        Exception? failure = null;
        host.Shown += async (_, _) =>
        {
            try
            {
                await HarnessServiceChecks.Run(directory);
                Stopwatch deadline = Stopwatch.StartNew();
                while (!chat.Ready && deadline.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(40);
                if (!chat.Ready) throw new Exception("Embedded chat failed to initialize.");
                CoreWebView2 core = chat.Browser.CoreWebView2;
                async Task<JsonElement> Read(string expression)
                {
                    string json = await core.ExecuteScriptAsync("JSON.stringify(" + expression + ")");
                    return JsonDocument.Parse(JsonSerializer.Deserialize<string>(json)!).RootElement.Clone();
                }
                async Task Drain() { await core.ExecuteScriptAsync("new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))"); await Task.Delay(80); }
                async Task Save(string name)
                {
                    int flag = Array.IndexOf(args, "--screenshots");
                    if (flag < 0 || flag + 1 >= args.Length) return;
                    string folder = Path.GetFullPath(args[flag + 1]); Directory.CreateDirectory(folder);
                    using FileStream file = File.Create(Path.Combine(folder, name + ".png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                }
                chat.Post(new { type = "info", hasKey = true, status = "测试界面", usage = "今日 3 次提问" });
                chat.Post(new { type = "sessions", active = "demo", sessions = new[] { new { id = "demo", title = "微光湖探索与装备建议" }, new { id = "second", title = "肉山之前的准备" } } });
                chat.Post(new { type = "history", turns = Array.Empty<ConversationTurn>() });
                host.ClientSize = new Size(1180, 820);
                await Drain(); await Save("harness-welcome");
                foreach (Size size in new[] { new Size(860, 600), new Size(1180, 820), new Size(1920, 1080) })
                {
                    host.ClientSize = size; await Drain();
                    JsonElement geometry = await Read("(()=>{let r=document.querySelector('#composer').getBoundingClientRect(),s=document.querySelector('#send').getBoundingClientRect();return {width:innerWidth,height:innerHeight,right:r.right,bottom:r.bottom,sendRight:s.right,sendBottom:s.bottom,overflow:document.documentElement.scrollWidth>innerWidth}})()");
                    if (geometry.GetProperty("overflow").GetBoolean() || geometry.GetProperty("right").GetDouble() > geometry.GetProperty("width").GetDouble()
                        || geometry.GetProperty("sendRight").GetDouble() > geometry.GetProperty("width").GetDouble()
                        || geometry.GetProperty("bottom").GetDouble() > geometry.GetProperty("height").GetDouble())
                        throw new Exception("Chat layout overflow: " + geometry);
                }
                host.ClientSize = new Size(860, 600);
                chat.Browser.ZoomFactor = 4d / 3d;
                await Drain();
                JsonElement largeText = await Read("(()=>{let r=document.querySelector('#composer').getBoundingClientRect(),s=document.querySelector('#send').getBoundingClientRect();return {right:r.right,bottom:r.bottom,send:s.right,width:innerWidth,height:innerHeight}})()");
                if (largeText.GetProperty("right").GetDouble() > largeText.GetProperty("width").GetDouble()
                    || largeText.GetProperty("bottom").GetDouble() > largeText.GetProperty("height").GetDouble()
                    || largeText.GetProperty("send").GetDouble() > largeText.GetProperty("width").GetDouble()) throw new Exception("Large text clipped the composer: " + largeText);
                chat.Browser.ZoomFactor = 1;
                host.ClientSize = new Size(1180, 820);
                ConversationTurn turn = new() { Id = "demo-turn", Question = "帮我找到微光湖，再看看我的装备适不适合探索。", Answer = "找到了，**收起窗口，跟随悬浮球箭头**就能前往。\n\n### 探索建议\n- 带上回忆药水，预留背包空间。\n- 微光湖附近先搭平台，避免直接跌入液体。\n\n你当前的护甲适合地下探索。建议先补充照明和移动能力，再进入新的洞穴。", DurationMilliseconds = 8400 };
                turn.AgentTraces.Add(new() { Kind = "reasoning", Step = 1, Content = "先确认世界位置，再读取装备。\n\n需要分别调用群系定位和装备读取工具，只请求本轮需要的数据。" });
                turn.AgentTraces.Add(new() { Kind = "tool_call", Step = 1, CallId = "locate-1", ToolName = "locate_biome", DisplayName = "定位群系", Arguments = "{\"biome\":\"微光湖\"}" });
                turn.AgentTraces.Add(new() { Kind = "tool_result", Step = 1, CallId = "locate-1", ToolName = "locate_biome", DisplayName = "定位群系", Arguments = "{\"biome\":\"微光湖\"}", Content = "{\"found\":true,\"navigation_active\":true,\"distance_tiles\":846}", Summary = "微光湖 · 左下方 846 格 · 导航已开启", DurationMilliseconds = 1200 });
                turn.AgentTraces.Add(new() { Kind = "tool_call", Step = 1, CallId = "equipment-2", ToolName = "read_game_state", DisplayName = "读取游戏状态", Arguments = "{\"categories\":[\"equipment\"]}" });
                turn.AgentTraces.Add(new() { Kind = "tool_result", Step = 1, CallId = "equipment-2", ToolName = "read_game_state", DisplayName = "读取游戏状态", Arguments = "{\"categories\":[\"equipment\"]}", Content = "[装备] 矿工头盔，铂金链甲，铂金护胫。", Summary = "已读取装备 · 3 件护甲", DurationMilliseconds = 260 });
                turn.AgentTraces.Add(new() { Kind = "reasoning", Step = 2, Content = "已取得目标与装备信息，给出导航入口与探索建议。" });
                chat.Post(new { type = "history", turns = new[] { turn } });
                await Drain();
                JsonElement initial = await Read("({tools:document.querySelectorAll('[data-kind=tool]').length,processHidden:document.querySelector('.process').hidden})");
                if (initial.GetProperty("tools").GetInt32() != 2 || !initial.GetProperty("processHidden").GetBoolean()) throw new Exception("Settled process projection is incorrect.");
                await Save("harness-chat");
                await core.ExecuteScriptAsync("document.querySelector('.turn-process').click();document.querySelector('[data-kind=tool]').open=true");
                await Drain();await Save("harness-tools");
                JsonElement details = await Read("document.querySelector('[data-kind=tool]').textContent");
                if (!details.GetString()!.Contains("navigation_active")) throw new Exception("Historical tool result was not restored.");
                // Tool identity, live snapshots, cancellation, and process folding.
                ConversationTurn live = new() { Id = "live", Question = "再检查一次", Status = "running" };
                chat.Post(new { type = "begin", turn = live });
                chat.Post(new { type = "busy", value = true });
                chat.Post(new { type = "stream", stream = new AgentStreamEvent(1, 1, "reasoning", "正在核对装备。") });
                foreach (ConversationAgentTrace trace in turn.AgentTraces.Take(5)) chat.Post(new { type = "trace", trace });
                await Drain();
                JsonElement liveState = await Read("({tools:document.querySelector('[data-turn-id=live]').querySelectorAll('[data-kind=tool]').length,stop:document.querySelector('#send').getAttribute('aria-label')})");
                if (liveState.GetProperty("tools").GetInt32() != 2 || liveState.GetProperty("stop").GetString() != "停止生成") throw new Exception("Live tool rows duplicated or stop missing.");
                live.Status = "cancelled";live.Notice = "已停止生成";live.Answer = "已收到的部分内容";live.AgentTraces.AddRange(turn.AgentTraces.Take(4));
                chat.Post(new { type = "finish", turn = live });chat.Post(new { type = "busy", value = false });
                await Drain();
                JsonElement stopped = await Read("document.querySelector('[data-turn-id=live]').textContent");
                if (!stopped.GetString()!.Contains("已中断") || !stopped.GetString()!.Contains("已收到的部分内容")) throw new Exception("Interrupted turn content lost.");
                // Preserve a reader's selection and position while another turn streams.
                chat.Post(new { type = "history", turns = Enumerable.Range(0, 20).Select(i => new ConversationTurn { Id = "long-" + i,
                    Question = "历史问题 " + i, Answer = string.Join("\n\n", Enumerable.Repeat("这是一段供滚动阅读的历史回答。", 12)) }).ToArray() });
                await Drain();
                await core.ExecuteScriptAsync("document.querySelector('#viewport').scrollTop=0;let r=document.createRange();r.selectNodeContents(document.querySelector('.markdown p'));getSelection().removeAllRanges();getSelection().addRange(r)");
                chat.Post(new { type = "begin", turn = new ConversationTurn { Id = "background", Question = "继续" } });
                chat.Post(new { type = "stream", stream = new AgentStreamEvent(1, 1, "content", "正在生成新回答。") });
                await Drain();
                JsonElement selection = await Read("({text:getSelection().toString(),top:document.querySelector('#viewport').scrollTop})");
                if (selection.GetProperty("text").GetString() != "这是一段供滚动阅读的历史回答。" || selection.GetProperty("top").GetDouble() > 2) throw new Exception("Streaming interrupted the reader.");
                await core.ExecuteScriptAsync("getSelection().removeAllRanges()");
                List<(string Action, string Value)> commands = new();
                chat.Command += (action, value) => commands.Add((action, value.TryGetProperty("value", out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : ""));
                await core.ExecuteScriptAsync("let input=document.querySelector('#input');input.value='测试快捷键';input.dispatchEvent(new Event('input'));input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',isComposing:true,bubbles:true}));input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',shiftKey:true,bubbles:true}));input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}))");
                await Drain();
                if (commands.Count(command => command.Action == "send") != 1 || commands.Single(command => command.Action == "send").Value != "测试快捷键") throw new Exception("IME/Enter send handling failed.");
                // A model/website cannot create a local action control or execute HTML.
                ConversationTurn hostile = new() { Id = "hostile", Question = "安全测试", Answer = "<img src=x onerror=alert(1)><button data-action=close>close</button><script>window.pwned=true</script>[恶意链接](javascript:alert(1))\n\n```html\n<button>普通代码</button>\n```" };
                chat.Post(new { type = "history", turns = new[] { hostile } });await Drain();
                JsonElement safe = await Read("({script:!!window.pwned,buttons:document.querySelector('.markdown').querySelectorAll('[data-action],img,script').length,jsLinks:document.querySelector('.markdown').querySelectorAll('a[href^=javascript]').length})");
                if (safe.GetProperty("script").GetBoolean() || safe.GetProperty("buttons").GetInt32() != 0 || safe.GetProperty("jsLinks").GetInt32() != 0) throw new Exception("Unsafe model HTML reached the bridge.");
                chat.Post(new { type = "history", turns = new[] { turn } });await Drain();
                host.ClientSize=new Size(860,600);await Drain();await Save("harness-compact");
                host.ClientSize=new Size(1180,820);await core.ExecuteScriptAsync("document.querySelector('#theme').click()");await Drain();await Save("harness-dark");
                await UnifiedFeatureChecks.RunAsync(chat, directory, args, host);
                Console.WriteLine("OK harness-ui embedded=offline responsive=860/1180/1920 process=foldable tools=paired history=full stop=retained markdown=sanitized");
            }
            catch (Exception exception) { failure = exception; }
            finally { host.Close(); }
        };
        Application.Run(host);
        if (failure is not null) throw failure;
    }
}
