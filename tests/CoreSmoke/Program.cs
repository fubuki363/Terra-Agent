using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using TerraWikiAI.Services;
using TerraWikiOverlay;

namespace CoreSmoke;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        RunPrefixCatalogTests();
        RunQuickReferenceCatalogTests();
        RunSwordShrineDefinitionTests();
        RunWorldObjectTests();
        int objectFlag = Array.IndexOf(args, "--object-live");
        if (objectFlag >= 0)
        {
            string query = args.Length > objectFlag + 1 ? args[objectFlag + 1] : "生命水晶";
            Stopwatch timer = Stopwatch.StartNew();
            if (!TerrariaWorldLocator.TryFindWorldObject(query, 8, out WorldObjectSearchResult? objects, out string reason)
                || objects is null) throw new InvalidOperationException(reason);
            Console.WriteLine($"OK objects name={objects.ObjectName} total={objects.TotalObjects} returned={objects.Hits.Count} complete={objects.Complete} scanned={objects.ScannedTiles}/{objects.TotalTiles} elapsedMs={timer.ElapsedMilliseconds}");
            foreach (WorldObjectHit hit in objects.Hits.Take(3)) Console.WriteLine($"TARGET {hit.Name} id={hit.TileId} style={hit.Style} tile={hit.TileX},{hit.TileY} direction={hit.Direction} distance={hit.DistanceTiles:0}");
            Console.WriteLine(objects.Note);
            return 0;
        }
        if (Array.Exists(args, argument => argument == "--reference-only"))
        {
            Console.WriteLine($"OK quick-reference entries={QuickReferenceCatalog.Entries.Count}");
            return 0;
        }
        if (Array.Exists(args, argument => argument == "--shrine-only"))
        {
            bool found = TerrariaWorldLocator.TryFindBiome("附魔剑冢", 3, out BiomeSearchResult? shrine, out string reason);
            if (found && (shrine is null || !shrine.ExactStructure || shrine.NavigationTargets.Count == 0))
                throw new InvalidOperationException("剑冢扫描没有生成精确导航目标。");
            if (!found && reason.StartsWith("暂不支持", StringComparison.Ordinal))
                throw new InvalidOperationException("剑冢没有接入定位器：" + reason);
            Console.WriteLine(found
                ? $"OK shrine targets={shrine!.NavigationTargets.Count} scanned={shrine.ScannedSamples}"
                : "OK shrine scanner completed without confirmed sword: " + reason);
            return 0;
        }
        if (Array.Exists(args, argument => argument == "--encyclopedia-live"))
        {
            await RunEncyclopediaLiveTest();
            return 0;
        }
        if (Array.Exists(args, argument => argument == "--prefix-only"))
        {
            Console.WriteLine("OK prefix-catalog");
            return 0;
        }
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "TerraWikiAI-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            RunLocalStateTests(temporaryDirectory);
            await RunEmptyResponseRecoveryTest();
            await RunAgentToolLoopTest();
            await RunUnlimitedAgentToolLoopTests();
            await RunUnlimitedAgentCancellationTests();
            await RunAgentLengthRecoveryTest();
            await RunTransportAndNavigationTests();
            if (Array.Exists(args, argument => argument == "--offline")) return 0;
            if (Array.Exists(args, argument => argument == "--agent-knowledge-live"))
                await RunLiveAgentKnowledgeTest(temporaryDirectory);
            if (TerrariaItemNameCatalog.Count < 6000
                || TerrariaItemNameCatalog.Resolve(267) != "向导巫毒娃娃"
                || TerrariaItemNameCatalog.Resolve(168) != "手榴弹"
                || TerrariaItemNameCatalog.Resolve(188) != "治疗药水")
                throw new InvalidOperationException($"Terraria 官方物品 ID→简体中文名称映射失败：count={TerrariaItemNameCatalog.Count}, doll={TerrariaItemNameCatalog.Resolve(267)}, grenade={TerrariaItemNameCatalog.Resolve(168)}, potion={TerrariaItemNameCatalog.Resolve(188)}, diagnostic={TerrariaItemNameCatalog.Diagnostic}。");
            TerrariaLocalSnapshot localSnapshot = TerrariaSaveContextReader.ReadLatest();
            if (string.IsNullOrWhiteSpace(localSnapshot.Context)
                || !localSnapshot.Context.Contains("[血量] 当前生命=", StringComparison.Ordinal)
                || !localSnapshot.Context.Contains("[背包]", StringComparison.Ordinal)
                || !localSnapshot.Context.Contains("[进度]", StringComparison.Ordinal))
                throw new InvalidOperationException("Terraria 本地存档上下文生成失败。");
            if (localSnapshot.Context.Contains("物品ID ", StringComparison.Ordinal)
                || !System.Text.RegularExpressions.Regex.IsMatch(localSnapshot.Context, @"槽\d+:.+\[ID \d+\]"))
                throw new InvalidOperationException("存档背包没有补充物品名称。");
            GameContextSelection vitalsOnly = new(true, false, false, false, false, false, false);
            string filteredContext = TerrariaSaveContextReader.ReadLatest(vitalsOnly).Context;
            if (!filteredContext.Contains("[血量]", StringComparison.Ordinal)
                || filteredContext.Contains("[魔力]", StringComparison.Ordinal)
                || filteredContext.Contains("[背包]", StringComparison.Ordinal)
                || filteredContext.Contains("[装备]", StringComparison.Ordinal)
                || filteredContext.Contains("[世界]", StringComparison.Ordinal))
                throw new InvalidOperationException("游戏数据类别筛选失败。");
            if (DeepSeekClient.ModelName != "deepseek-flash")
                throw new InvalidOperationException("DeepSeek 模型名没有更新。");
            string memoryContext = TerrariaMemoryContextReader.Read(localSnapshot.PlayerNames, localSnapshot.WorldNames);
            if (string.IsNullOrWhiteSpace(memoryContext) || !memoryContext.Contains("实时进程", StringComparison.Ordinal))
                throw new InvalidOperationException("Terraria 只读内存上下文生成失败。");
            bool managedRead = TerrariaManagedMemoryContextReader.TryRead(GameContextSelection.All, out string managedContext, out string managedFailure);
            if (Array.Exists(args, argument => argument == "--locator-diagnostic"))
                RunLocatorDiagnostic();
            if (Array.Exists(args, argument => argument == "--context-diagnostic"))
            {
                Console.WriteLine("SAVE_CONTEXT_BEGIN");
                Console.WriteLine(localSnapshot.Context);
                Console.WriteLine("SAVE_CONTEXT_END");
                Console.WriteLine("LIVE_CONTEXT_BEGIN");
                Console.WriteLine(managedRead ? managedContext : managedFailure);
                Console.WriteLine("LIVE_CONTEXT_END");
            }
            bool hasVitals = managedContext.Contains("[血量] 当前生命=", StringComparison.Ordinal)
                && managedContext.Contains("[魔力] 当前魔力=", StringComparison.Ordinal);
            bool hasInventory = managedContext.Contains("[背包]", StringComparison.Ordinal);
            bool hasNamedInventory = System.Text.RegularExpressions.Regex.IsMatch(
                managedContext,
                @"\[背包\].*槽\d+:(?!未知物品)");
            bool hasProgress = managedContext.Contains("[进度]", StringComparison.Ordinal);
            bool hasAchievements = managedContext.Contains("[成就] 已完成", StringComparison.Ordinal)
                && !managedContext.Contains("[成就] 读取不可用", StringComparison.Ordinal);
            if (managedRead && (!hasVitals || !hasInventory || !hasNamedInventory || !hasProgress || !hasAchievements))
            {
                string achievementDiagnostic = System.Linq.Enumerable.FirstOrDefault(
                    managedContext.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                    line => line.StartsWith("[成就]", StringComparison.Ordinal)) ?? "missing";
                if (achievementDiagnostic.Contains('：')) achievementDiagnostic = achievementDiagnostic[..achievementDiagnostic.IndexOf('：')];
                throw new InvalidOperationException($"Terraria 托管内存上下文字段缺失：vitals={hasVitals} inventory={hasInventory} namedItems={hasNamedInventory} progress={hasProgress} achievements={hasAchievements}；{achievementDiagnostic}。");
            }
            if (managedRead && TerrariaManagedMemoryContextReader.TryRead(vitalsOnly, out string managedVitalsOnly, out _)
                && (managedVitalsOnly.Contains("[背包]", StringComparison.Ordinal)
                    || managedVitalsOnly.Contains("[世界]", StringComparison.Ordinal)
                    || managedVitalsOnly.Contains("[成就]", StringComparison.Ordinal)))
                throw new InvalidOperationException("实时内存游戏数据类别筛选失败。");
            if (managedRead)
                RunWorldLocatorTests();
            Console.WriteLine($"OK game-context players={localSnapshot.PlayerNames.Count} worlds={localSnapshot.WorldNames.Count} item-names={TerrariaItemNameCatalog.Count} categories=filtered managed-memory={(managedRead ? "live" : "fallback:" + managedFailure)} model={DeepSeekClient.ModelName}");
            WikiCache cache = new(temporaryDirectory);
            using TerrariaWikiClient client = new(cache);
            var documents = await client.SearchAsync(
                "泰拉刃怎么合成",
                "zh",
                2,
                3000,
                TimeSpan.FromHours(1),
                CancellationToken.None);

            if (documents.Count == 0)
                throw new InvalidOperationException("官方中文 Wiki 未返回文档。");
            if (string.IsNullOrWhiteSpace(documents[0].Text) || !documents[0].Url.StartsWith("https://terraria.wiki.gg/", StringComparison.Ordinal))
                throw new InvalidOperationException("文档内容或来源 URL 无效。");
            if (cache.Count == 0)
                throw new InvalidOperationException("页面没有写入本地缓存。");

            Console.WriteLine($"OK pages={documents.Count} first={documents[0].Title} chars={documents[0].Text.Length} cache={cache.Count}");

            CommunitySearchClient community = new();
            var communityDocuments = await community.SearchAsync(
                new[] { "Eye of Cthulhu strategy" },
                8,
                1800,
                CancellationToken.None);
            if (communityDocuments.Count == 0
                || System.Linq.Enumerable.Any(communityDocuments, document =>
                    document.SourceType is not ("terraria_forum" or "steam_community")
                    || string.IsNullOrWhiteSpace(document.SourceName)
                    || document.Text.Length < 40)
                || !System.Linq.Enumerable.Any(communityDocuments, document => document.SourceType == "terraria_forum")
                || !System.Linq.Enumerable.Any(communityDocuments, document => document.SourceType == "steam_community"))
                throw new InvalidOperationException("社区检索没有返回有效的论坛或 Steam 讨论。");
            Console.WriteLine("OK community-search sources=" + string.Join(",",
                System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(communityDocuments, document => document.SourceType)))
                + " documents=" + communityDocuments.Count);

            if (Array.Exists(args, argument => argument == "--agent-live"))
                await RunLiveAgentToolTest(temporaryDirectory);
            if (Array.Exists(args, argument => argument == "--locator-agent-live"))
                await RunLiveLocatorAgentTest(temporaryDirectory);
            if (Array.Exists(args, argument => argument == "--deepseek"))
            {
                ApiKeyProvider keyProvider = new(temporaryDirectory);
                if (!keyProvider.TryGetKey(out string key, out _))
                    throw new InvalidOperationException("没有通过环境变量读取到测试 Key。");

                using DeepSeekClient deepSeek = new();
                long reportedTokens = 0;
                deepSeek.UsageReported += usage => reportedTokens += usage.TotalTokens > 0
                    ? usage.TotalTokens
                    : usage.PromptTokens + usage.CompletionTokens;
                bool testVision = Array.Exists(args, argument => argument == "--vision");
                const string onePixelPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
                WikiResearchService research = new(client, deepSeek);
                var smartDocuments = await research.SearchAsync(
                    key,
                    "第一个boss应该打什么，怎么打，准备些什么",
                    null,
                    "zh",
                    6,
                    3000,
                    TimeSpan.FromHours(1),
                    CancellationToken.None);
                if (smartDocuments.Count == 0 || !System.Linq.Enumerable.Any(
                        smartDocuments,
                        document => document.Title.Contains("克苏鲁之眼", StringComparison.Ordinal)
                            || document.Title.Equals("Boss", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("智能检索没有找到第一个 Boss 的相关页面。");

                string answer = await deepSeek.AskAsync(
                    key,
                    testVision ? "结合附图，用一句中文回答；图片看不清也请明确说明。" : "第一个boss应该打什么，怎么打，准备些什么",
                    BuildContext(smartDocuments),
                    "",
                    null,
                    null,
                    96,
                    false,
                    CancellationToken.None,
                    testVision ? onePixelPng : null);
                if (string.IsNullOrWhiteSpace(answer))
                    throw new InvalidOperationException("DeepSeek 返回空回答。");
                if (reportedTokens <= 0)
                    throw new InvalidOperationException("DeepSeek 响应没有返回可记录的 Token 用量。");
                Console.WriteLine($"OK model={DeepSeekClient.ModelName} vision={testVision} smartPages={smartDocuments.Count} answerChars={answer.Length} usageTokens={reportedTokens}");
                Console.WriteLine("SMART_TITLES=" + string.Join(" | ", System.Linq.Enumerable.Select(smartDocuments, document => document.Title)));
                Console.WriteLine("SMART_ANSWER=" + answer);
            }

            return 0;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, true);
        }
    }

    private static void RunPrefixCatalogTests()
    {
        if (TerrariaPrefixCatalog.Format(65, true) != " 护佑（前缀ID 65，+4防御）"
            || TerrariaPrefixCatalog.Format(68, true) != " 幸运（前缀ID 68，+4%暴击率）"
            || TerrariaPrefixCatalog.Format(72, false) != " 险恶（前缀ID 72，饰品装备时+4%伤害）"
            || TerrariaPrefixCatalog.Format(250, true) != " 未识别前缀ID 250")
            throw new InvalidOperationException("Terraria 前缀 ID→中文名称/饰品效果映射失败。");
    }

    private static void RunQuickReferenceCatalogTests()
    {
        if (QuickReferenceCatalog.Search("药水", "").Count < 12
            || QuickReferenceCatalog.Search("掉落", "").Count < 10
            || QuickReferenceCatalog.Search("药水", "铁矿").SingleOrDefault()?.Name != "铁皮药水"
            || QuickReferenceCatalog.Search("掉落", "混沌传送杖").SingleOrDefault()?.Name != "混沌精"
            || QuickReferenceCatalog.Search("全部", "Soul of Flight").SingleOrDefault()?.Name != "飞龙"
            || QuickReferenceCatalog.Search("药水", "地牢史莱姆").Count != 0
            || QuickReferenceCatalog.Entries.Select(entry => entry.Category + ":" + entry.Name).Distinct().Count() != QuickReferenceCatalog.Entries.Count)
            throw new InvalidOperationException("离线速查表分类、搜索或条目数据校验失败。");
    }

    private static async Task RunEncyclopediaLiveTest()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerraWikiAI-encyclopedia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using WikiEncyclopediaService encyclopedia = new(directory);
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
            WikiCatalogSearch found = await encyclopedia.SearchAsync("铁皮药水", timeout.Token);
            WikiCatalogHit hit = found.Hits.FirstOrDefault(item => item.Title == "铁皮药水" && item.PageId > 0)
                ?? throw new InvalidOperationException("全图鉴没有找到铁皮药水官方页面。");
            WikiCatalogArticle article = await encyclopedia.GetArticleAsync(hit, timeout.Token);
            if (!article.Text.Contains("瓶装水", StringComparison.Ordinal)
                || !article.Text.Contains("太阳花", StringComparison.Ordinal)
                || !article.Text.Contains("铅矿", StringComparison.Ordinal))
                throw new InvalidOperationException("全图鉴没有读取到铁皮药水完整配方。");
            WikiCatalogSearch enemySearch = await encyclopedia.SearchAsync("混沌精", timeout.Token);
            WikiCatalogHit enemy = enemySearch.Hits.FirstOrDefault(item => item.Title == "混沌精" && item.PageId > 0)
                ?? throw new InvalidOperationException("全图鉴没有找到混沌精官方页面。");
            WikiCatalogArticle enemyArticle = await encyclopedia.GetArticleAsync(enemy, timeout.Token);
            if (!enemyArticle.Text.Contains("混沌传送杖", StringComparison.Ordinal)
                || !enemyArticle.Text.Contains("掉落", StringComparison.Ordinal))
                throw new InvalidOperationException("全图鉴没有读取到怪物掉落说明。");
            if (!(await encyclopedia.GetArticleAsync(enemy, timeout.Token)).FromCache)
                throw new InvalidOperationException("全图鉴页面缓存没有生效。");
            await encyclopedia.SyncTitleIndexesAsync(null, timeout.Token);
            if (encyclopedia.IndexedPageCount < 8000)
                throw new InvalidOperationException($"全图鉴中英文页面索引明显不完整：{encyclopedia.IndexedPageCount}。");
            WikiCatalogSearch broad = await encyclopedia.SearchAsync("药水", timeout.Token);
            if (broad.TotalMatches <= QuickReferenceCatalog.Entries.Count)
                throw new InvalidOperationException($"全图鉴搜索结果仍局限于精选条目：{broad.TotalMatches}。");
            using WikiEncyclopediaService reopened = new(directory);
            if (reopened.IndexedPageCount != encyclopedia.IndexedPageCount)
                throw new InvalidOperationException("全图鉴标题索引没有正确持久化。 ");
            Console.WriteLine($"OK encyclopedia index={encyclopedia.IndexedPageCount} potion={article.Title} enemy={enemyArticle.Title} broad={broad.TotalMatches} cache=hit");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void RunLocalStateTests(string temporaryDirectory)
    {
        string settingsDirectory = Path.Combine(temporaryDirectory, "settings-migration");
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(Path.Combine(settingsDirectory, "settings-v1.json"),
            "{\"SchemaVersion\":2,\"IncludeMemoryContext\":false}");
        OverlaySettingsStore settingsStore = new(settingsDirectory);
        if (settingsStore.Current.SchemaVersion != 8 || !settingsStore.Current.IncludeMemoryContext
            || !settingsStore.Current.EnableStorageLocator || !settingsStore.Current.EnableBiomeLocator
            || settingsStore.Current.MaxOutputTokens != 0 || !settingsStore.Current.EnableThinking)
            throw new InvalidOperationException("旧设置没有迁移为默认实时内存读取。");
        string migratedSettings = File.ReadAllText(Path.Combine(settingsDirectory, "settings-v1.json"));
        if (!migratedSettings.Contains("\"SchemaVersion\": 8", StringComparison.Ordinal)
            || !migratedSettings.Contains("\"IncludeMemoryContext\": true", StringComparison.Ordinal)
            || !migratedSettings.Contains("\"EnableStorageLocator\": true", StringComparison.Ordinal)
            || !migratedSettings.Contains("\"EnableBiomeLocator\": true", StringComparison.Ordinal)
            || migratedSettings.Contains("\"GameContextSelection\"", StringComparison.Ordinal))
            throw new InvalidOperationException("实时内存默认设置没有持久化。");

        ConversationStore history = new(temporaryDirectory);
        history.AddTurn("问题一", "回答一", Array.Empty<string>(), true, 30);
        history.AddTurn(
            "问题二",
            "回答二",
            new[] { "https://terraria.wiki.gg/wiki/Test" },
            true,
            30,
            new[]
            {
                new ConversationToolTrace
                {
                    ToolName = "read_game_state",
                    DisplayName = "读取实时游戏状态",
                    Arguments = "{\"categories\":[\"equipment\"]}",
                    Summary = "已读取装备"
                }
            },
            new[]
            {
                new ConversationAgentTrace
                {
                    Kind = "reasoning",
                    Step = 1,
                    Content = "需要先读取装备",
                    FinishReason = "tool_calls"
                }
            },
            "已经结合装备完成回答");
        if (history.GetContext(1, 1000).Count != 1 || history.ActiveTurnCount != 2)
            throw new InvalidOperationException("历史上下文裁剪失败。");
        ConversationStore reloadedHistory = new(temporaryDirectory);
        ConversationTurn persistedTurn = reloadedHistory.GetRecent(2).Last();
        if (persistedTurn.ToolTraces.Single().Summary != "已读取装备"
            || persistedTurn.AgentTraces.Single().Content != "需要先读取装备"
            || persistedTurn.ReasoningContent != "已经结合装备完成回答")
            throw new InvalidOperationException("Agent 思考与技能调用轨迹没有随会话持久化。");
        string firstSession = history.ActiveSessionId;
        string secondSession = history.NewSession(true, 30);
        if (!history.Resume(firstSession, true) || history.ActiveTurnCount != 2)
            throw new InvalidOperationException("历史会话恢复失败。");
        if (!history.DeleteSession(secondSession, true) || history.SessionCount != 1)
            throw new InvalidOperationException("单个历史会话删除失败。");

        UsageTracker usage = new(temporaryDirectory);
        usage.RecordApiCall(new DeepSeekUsage
        {
            PromptTokens = 100,
            CompletionTokens = 25,
            PromptCacheHitTokens = 40,
            TotalTokens = 125
        });
        usage.RecordQuestion();
        if (!usage.GetSummary().Contains("125", StringComparison.Ordinal))
            throw new InvalidOperationException("用量统计失败。");

        ApiKeyProvider keys = new(temporaryDirectory);
        const string fakeKey = "sk-test-only-12345678901234567890";
        if (!keys.SaveLocalKey(fakeKey, out _) || !File.Exists(keys.KeyPath))
            throw new InvalidOperationException("本地 Key 原子写入失败。");

        Console.WriteLine("OK local-state history=context+sessions+tool-traces usage=statistics-only key=atomic memory-default=live");
    }

    private static async Task RunEmptyResponseRecoveryTest()
    {
        SequenceHandler handler = new();
        using DeepSeekClient client = new(handler);
        string answer = await client.AskAsync(
            "sk-test-only-12345678901234567890",
            "测试空响应恢复",
            "",
            "",
            null,
            null,
            96,
            false,
            CancellationToken.None);
        if (answer != "恢复成功" || handler.CallCount != 2)
            throw new InvalidOperationException("DeepSeek 空响应自动重试失败。");
        Console.WriteLine("OK deepseek-empty-response retry=recovered");
    }

    private static async Task RunTransportAndNavigationTests()
    {
        TransientHandler handler = new();
        using (DeepSeekClient client = new(handler))
        {
            string answer = await client.AskAsync("sk-test-only-12345678901234567890", "测试连接中断", "", "",
                null, null, 0, true, CancellationToken.None);
            if (answer != "连接恢复" || handler.Calls != 2) throw new InvalidOperationException("网络中断重试未恢复。");
        }
        CancellationHandler cancelled = new();
        using (DeepSeekClient client = new(cancelled))
        using (CancellationTokenSource source = new(40))
        {
            try
            {
                await client.AskAsync("sk-test-only-12345678901234567890", "测试取消", "", "", null, null, 0, true, source.Token);
                throw new InvalidOperationException("取消请求没有生效。");
            }
            catch (OperationCanceledException) { if (cancelled.Calls != 1) throw new InvalidOperationException("用户取消后仍在重试。"); }
        }
        NavigationState navigation = new();
        navigation.SetTargets(new[] { new NavigationTarget("object", "水晶", 10, 20, "") }, 0, 0);
        AgentToolRegistry registry = new();
        WorldLocatorAgentSkills.Register(registry, new OverlaySettings { EnableObjectLocator = false }, navigation);
        AgentToolResult denied = await registry.ExecuteAsync("locate_world_object", "{\"object_name\":\"生命果\"}", CancellationToken.None);
        if (!denied.IsError) throw new InvalidOperationException("物体读取权限未生效。");
        AgentToolResult stopped = await registry.ExecuteAsync("control_navigation", "{\"action\":\"stop\"}", CancellationToken.None);
        if (stopped.IsError || navigation.IsActive) throw new InvalidOperationException("Agent 导航取消失败。");
        using CancellationTokenSource alreadyCancelled = new();
        alreadyCancelled.Cancel();
        try
        {
            TerrariaWorldLocator.TryFindWorldObject("生命果", 1, out _, out _, alreadyCancelled.Token);
            throw new InvalidOperationException("地图扫描未响应取消。");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("OK stability transport=retry cancellation=no-retry navigation=stop permissions=object-off");
    }

    private sealed class TransientHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (++Calls == 1) throw new HttpRequestException("测试连接中断");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"连接恢复\"}}]}", Encoding.UTF8, "application/json") });
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static void RunLocatorDiagnostic()
    {
        using Process process = Process.GetProcessesByName("Terraria").First(candidate => !candidate.HasExited);
        using DataTarget target = DataTarget.AttachToProcess(process.Id, suspend: true);
        using ClrRuntime runtime = target.ClrVersions.First().CreateRuntime();
        ClrModule module = runtime.EnumerateModules().First(candidate => candidate.Name?.EndsWith("Terraria.exe", StringComparison.OrdinalIgnoreCase) == true);
        ClrAppDomain domain = runtime.AppDomains.First();
        ClrType main = module.GetTypeByName("Terraria.Main")!;
        ClrType gen = module.GetTypeByName("Terraria.WorldBuilding.GenVars")!;
        ClrValueType shimmer = gen.StaticFields.First(field => field.Name == "shimmerPosition").ReadStruct(domain);
        double shimmerX = shimmer.ReadField<double>("X");
        double shimmerY = shimmer.ReadField<double>("Y");
        int jungleMinX = gen.StaticFields.First(field => field.Name == "jungleMinX").Read<int>(domain);
        int jungleMaxX = gen.StaticFields.First(field => field.Name == "jungleMaxX").Read<int>(domain);
        int jungleOriginX = gen.StaticFields.First(field => field.Name == "jungleOriginX").Read<int>(domain);
        ClrObject tilesObject = main.StaticFields.First(field => field.Name == "tile").ReadObject(domain);
        ClrArray tiles = tilesObject.AsArray();
        Console.WriteLine($"LOCATOR shimmer={shimmerX:0.##},{shimmerY:0.##} jungle={jungleMinX},{jungleMaxX},{jungleOriginX} tileRank={tiles.Rank} dims={tiles.GetLength(0)}x{tiles.GetLength(1)} componentSize={tiles.Type.ComponentType?.StaticSize}/{tiles.Type.ComponentSize}");
        Console.WriteLine("LOCATOR arrayWords=" + string.Join(',', Enumerable.Range(0, 12).Select(index => target.DataReader.Read<uint>(tiles.Address + (uint)(index * 4)).ToString("X8"))));
        Stopwatch stopwatch = Stopwatch.StartNew();
        Dictionary<ushort, int> counts = new();
        List<(int X, int Y)> shimmerSamples = new();
        const int stride = 16;
        int height = tiles.GetLength(1);
        for (int x = stride; x < tiles.GetLength(0) - stride; x += stride)
        {
            for (int y = stride; y < height - stride; y += stride)
            {
                ulong tileAddress = target.DataReader.ReadPointer(tiles.Address + 24u + (uint)((x * height + y) * 4));
                ClrObject tile = runtime.Heap.GetObject(tileAddress);
                if (!tile.IsValid) continue;
                ushort header = tile.ReadField<ushort>("sTileHeader");
                if ((header & 32) != 0)
                {
                    ushort type = tile.ReadField<ushort>("type");
                    counts[type] = counts.GetValueOrDefault(type) + 1;
                }
                byte liquid = tile.ReadField<byte>("liquid");
                byte liquidHeader = tile.ReadField<byte>("bTileHeader");
                if (liquid > 0 && ((liquidHeader & 96) >> 5) == 3)
                    shimmerSamples.Add((x, y));
            }
        }
        stopwatch.Stop();
        Console.WriteLine($"LOCATOR scanSamples={counts.Values.Sum()} shimmerSamples={shimmerSamples.Count} elapsedMs={stopwatch.ElapsedMilliseconds} top={string.Join(',', counts.OrderByDescending(value => value.Value).Take(12).Select(value => value.Key + ":" + value.Value))}");
        if (shimmerSamples.Count > 0)
            Console.WriteLine($"LOCATOR shimmerCenter={shimmerSamples.Average(value => value.X):0},{shimmerSamples.Average(value => value.Y):0}");
    }

    private static void RunWorldLocatorTests()
    {
        if (!TerrariaWorldLocator.TryReadPlayerTilePosition(out float playerX, out float playerY, out string positionFailure)
            || playerX <= 0 || playerY <= 0)
            throw new InvalidOperationException("导航玩家位置读取失败：" + positionFailure);

        if (!TerrariaWorldLocator.TryFindItem("木材", 5, true, out StorageSearchResult? storage, out string storageFailure)
            || storage is null || storage.ScannedWorldChests <= 0)
            throw new InvalidOperationException("世界箱子搜索失败：" + storageFailure);

        if (!TerrariaWorldLocator.TryFindBiome("shimmer", 3, out BiomeSearchResult? biome, out string biomeFailure)
            || biome is null || biome.Hits.Count == 0 || biome.NavigationTargets.Count == 0)
            throw new InvalidOperationException("微光湖定位失败：" + biomeFailure);
        if (!TerrariaWorldLocator.TryFindBiome("jungle", 3, out BiomeSearchResult? jungle, out string jungleFailure)
            || jungle is null || jungle.Hits.Count == 0)
            throw new InvalidOperationException("丛林定位失败：" + jungleFailure);
        if (!TerrariaWorldLocator.TryFindBiome("ocean", 3, out BiomeSearchResult? ocean, out string oceanFailure)
            || ocean is null || ocean.Hits.Count != 2 || ocean.SampleStride != 0)
            throw new InvalidOperationException("海洋边界定位失败：" + oceanFailure);
        if (!TerrariaWorldLocator.TryFindBiome("洞穴层", 1, out BiomeSearchResult? cavern, out string cavernFailure)
            || cavern is null || cavern.Hits.Count != 1 || cavern.SampleStride != 0)
            throw new InvalidOperationException("洞穴层定位失败：" + cavernFailure);
        if (!TerrariaWorldLocator.TryFindBiome("花岗岩洞", 3, out BiomeSearchResult? granite, out string graniteFailure)
            || granite is null || granite.Hits.Count == 0 || granite.SampleStride == 0)
            throw new InvalidOperationException("花岗岩洞定位失败：" + graniteFailure);
        if (TerrariaWorldLocator.SupportedBiomeNames.Count < 35)
            throw new InvalidOperationException("可检测群系列表不完整。count=" + TerrariaWorldLocator.SupportedBiomeNames.Count);
        bool evilFound = TerrariaWorldLocator.TryFindBiome("crimson", 3, out BiomeSearchResult? evil, out string evilFailure)
            || TerrariaWorldLocator.TryFindBiome("corruption", 3, out evil, out evilFailure);
        if (!evilFound || evil is null || evil.Hits.Count == 0)
            throw new InvalidOperationException("猩红/腐化定位失败：" + evilFailure);

        NavigationState navigation = new();
        navigation.SetTargets(biome.NavigationTargets, biome.PlayerTileX, biome.PlayerTileY);
        NavigationSnapshot before = navigation.GetSnapshot();
        navigation.UpdatePlayer(before.TargetTileX - 2, before.TargetTileY - 2);
        NavigationSnapshot after = navigation.GetSnapshot();
        if (!before.Active || after.DistanceTiles >= before.DistanceTiles)
            throw new InvalidOperationException("悬浮球导航方向状态没有随玩家位置更新。");

        OverlaySettings settings = new();
        AgentToolRegistry tools = GameMemoryAgentSkill.CreateRegistry(settings);
        WorldLocatorAgentSkills.Register(tools, settings, navigation);
        if (tools.Count != 5)
            throw new InvalidOperationException("Agent 没有注册状态、储物、群系、世界物体和导航管理技能。");
        Console.WriteLine($"OK world-locator chests={storage.ScannedWorldChests} woodHits={storage.Hits.Count} biomes={TerrariaWorldLocator.SupportedBiomeNames.Count} shimmerRegions={biome.Hits.Count} jungleRegions={jungle.Hits.Count} evilRegions={evil.Hits.Count} oceanTargets={ocean.Hits.Count} graniteRegions={granite.Hits.Count} player={playerX:0},{playerY:0} skills={tools.Count}");
    }

    private static void RunSwordShrineDefinitionTests()
    {
        foreach (string name in new[] { "附魔剑冢", "剑冢", "附魔剑神龛", "enchanted sword shrine", "sword_shrine" })
        {
            if (!TerrariaWorldLocator.SupportsBiomeName(name))
                throw new InvalidOperationException("剑冢别名不受支持：" + name);
        }
        foreach (short frameX in new short[] { 918, 936, 954 })
        {
            if (!TerrariaWorldLocator.IsSwordInStoneTile(187, 32, frameX, 0)
                || !TerrariaWorldLocator.IsSwordInStoneTile(187, 32, frameX, 18))
                throw new InvalidOperationException("剑石图格样式识别失败：" + frameX);
        }
        if (TerrariaWorldLocator.IsSwordInStoneTile(187, 32, 900, 0)
            || TerrariaWorldLocator.IsSwordInStoneTile(187, 0, 918, 0)
            || TerrariaWorldLocator.IsSwordInStoneTile(186, 32, 918, 0)
            || TerrariaWorldLocator.IsSwordInStoneTile(187, 32, 918, 36))
            throw new InvalidOperationException("非剑石图格被误认为附魔剑冢。");
    }

    private static void RunWorldObjectTests()
    {
        foreach (string query in new[] { "生命水晶", "生命果", "世纪之花孢子", "世纪之花花苞", "雕像", "心雕像", "Heart Statue", "剑冢" })
            if (!WorldObjectCatalog.TryResolve(query, out _)) throw new InvalidOperationException("物体别名失效：" + query);
        WorldObjectCatalog.TryResolve("生命水晶", out WorldObjectDefinition heart);
        int confirmed = 0;
        foreach (short x in new short[] { 0, 18 })
            foreach (short y in new short[] { 0, 18 })
                if (WorldObjectCatalog.TryMatch(heart, new WorldTile(12, 32, x, y), out _, out _)) confirmed++;
        if (confirmed != 1 || WorldObjectCatalog.TryMatch(heart, new WorldTile(12, 0, 0, 0), out _, out _))
            throw new InvalidOperationException("水晶计数不应重复或包含已开采图格。");
        WorldObjectCatalog.TryResolve("世纪之花孢子", out WorldObjectDefinition bulb);
        if (bulb.TileIds[0] != 238 || !bulb.Note.Contains("花苞", StringComparison.Ordinal))
            throw new InvalidOperationException("花苞/孢子名称未明确处理。");
        string heartStatue = TerrariaItemNameCatalog.Resolve(473) ?? "心雕像";
        if (!WorldObjectCatalog.TryResolve(heartStatue, out WorldObjectDefinition statue)
            || !WorldObjectCatalog.TryMatch(statue, new WorldTile(105, 32, 37 * 36, 0), out int style, out string name)
            || style != 37 || name != heartStatue
            || WorldObjectCatalog.TryMatch(statue, new WorldTile(105, 32, 1 * 36, 0), out _, out _))
            throw new InvalidOperationException("指定雕像样式过滤失败。");
        WorldObjectCatalog.TryResolve("雕像", out WorldObjectDefinition allStatues);
        if (!WorldObjectCatalog.TryMatch(allStatues, new WorldTile(105, 32, 0, 54), out int wrapped, out _) || wrapped != 55)
            throw new InvalidOperationException("雕像纹理换行样式解析失败。");
        Console.WriteLine("OK objects aliases=bulb+heart+fruit statues=localized+style dedup=top-left");
    }

    private static async Task RunAgentToolLoopTest()
    {
        ToolLoopHandler handler = new();
        using DeepSeekClient client = new(handler);
        AgentToolRegistry tools = new();
        int executions = 0;
        tools.Register(new AgentToolDefinition
        {
            Name = "read_game_state",
            DisplayName = "读取游戏状态",
            Description = "测试工具",
            Parameters = new { type = "object" },
            ExecuteAsync = (arguments, _) =>
            {
                executions++;
                bool requestedEquipment = arguments.TryGetProperty("categories", out JsonElement categories)
                    && System.Linq.Enumerable.Any(categories.EnumerateArray(), value => value.GetString() == "equipment");
                if (!requestedEquipment)
                    throw new InvalidOperationException("Agent 没有请求预期的装备类别。");
                return Task.FromResult(new AgentToolResult(false, "[装备] 测试装备", "已读取装备"));
            }
        });
        List<AgentToolEvent> events = new();
        List<AgentReasoningEvent> reasoningEvents = new();
        string answer = await client.AskAgentWithHistoryAsync(
            "sk-test-only-12345678901234567890",
            "看看我当前的装备",
            "",
            new[] { new ConversationTurn { Question = "上一问", Answer = "上一答", ReasoningContent = "历史最终思考" } },
            tools,
            256,
            true,
            CancellationToken.None,
            events.Add,
            reasoningEvents.Add);
        if (answer != "装备读取完成" || executions != 1 || events.Count != 2
            || events[0].Completed || !events[1].Completed
            || reasoningEvents.Count != 2 || reasoningEvents[0].Content != "需要读取装备"
            || !handler.SawToolSchema || !handler.SawReasoningReplay || !handler.SawHistoricalReasoning
            || !handler.SawToolResult || handler.SawUnexpectedToolChoice)
            throw new InvalidOperationException($"Agent 工具循环、推理回传或事件通知失败：answer={answer} executions={executions} events={events.Count} reasoningEvents={reasoningEvents.Count} schema={handler.SawToolSchema} reasoning={handler.SawReasoningReplay} historyReasoning={handler.SawHistoricalReasoning} result={handler.SawToolResult} toolChoice={handler.SawUnexpectedToolChoice}。");
        Console.WriteLine("OK agent-loop tools=on-demand reasoning=replayed events=visible");
    }

    private static async Task RunUnlimitedAgentToolLoopTests()
    {
        foreach (bool enableThinking in new[] { true, false })
        {
            MultiStepToolHandler handler = new() { ToolRounds = 10, CallsPerRound = 8, ThinkingEnabled = enableThinking };
            using DeepSeekClient client = new(handler);
            AgentToolRegistry tools = new();
            int executions = 0;
            tools.Register(new AgentToolDefinition
            {
                Name = "test_tool",
                DisplayName = "连续调用测试工具",
                Description = "测试长任务和完整工具批次。",
                Parameters = new { type = "object" },
                ExecuteAsync = (arguments, _) =>
                {
                    int expectedRound = executions / handler.CallsPerRound + 1;
                    int expectedIndex = executions % handler.CallsPerRound;
                    if (arguments.GetProperty("round").GetInt32() != expectedRound
                        || arguments.GetProperty("index").GetInt32() != expectedIndex)
                        throw new InvalidOperationException("工具批次执行顺序或参数不完整。");
                    executions++;
                    return Task.FromResult(new AgentToolResult(false, "测试结果", "已完成"));
                }
            });
            List<AgentToolEvent> events = new();
            List<AgentReasoningEvent> reasoningEvents = new();
            int usageEvents = 0;
            client.UsageReported += _ => usageEvents++;
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            string answer = await client.AskAgentWithHistoryAsync(
                "sk-test-only-12345678901234567890", "连续执行多轮工具", "",
                Array.Empty<ConversationTurn>(), tools, 0, enableThinking, timeout.Token,
                events.Add, reasoningEvents.Add);
            int expectedCalls = handler.ToolRounds * handler.CallsPerRound;
            if (answer != "不限次数工具调用完成" || executions != expectedCalls
                || handler.CallCount != handler.ToolRounds + 1 || usageEvents != handler.CallCount
                || events.Count != expectedCalls * 2 || events.Any(value => value.IsError)
                || events.Where(value => value.Completed).Count() != expectedCalls
                || reasoningEvents.Count != handler.CallCount
                || !reasoningEvents.Select(value => value.Step).SequenceEqual(Enumerable.Range(1, handler.CallCount))
                || events.Where(value => value.Completed).Any(value => value.Step < 1 || value.Step > handler.ToolRounds))
                throw new InvalidOperationException($"不限次数 Agent 回归失败：thinking={enableThinking}, calls={executions}, steps={handler.CallCount}。");
            Console.WriteLine($"OK agent-unlimited thinking={enableThinking} toolCalls={executions} batch={handler.CallsPerRound} modelSteps={handler.CallCount} results=paired usage=counted");
        }
    }

    private static async Task RunUnlimitedAgentCancellationTests()
    {
        // Cancel once between rounds and once inside a batch, beyond the former six-call cap.
        foreach (int cancelAfter in new[] { 16, 20 })
        {
            MultiStepToolHandler handler = new() { ToolRounds = 100, CallsPerRound = 8 };
            using DeepSeekClient client = new(handler);
            using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
            AgentToolRegistry tools = new();
            int executions = 0;
            tools.Register(new AgentToolDefinition
            {
                Name = "test_tool",
                DisplayName = "取消测试工具",
                Description = "测试工具忽略取消令牌时，调度仍能停止。",
                Parameters = new { type = "object" },
                ExecuteAsync = (_, _) =>
                {
                    if (++executions == cancelAfter) cancellation.Cancel();
                    return Task.FromResult(new AgentToolResult(false, "测试结果", "已完成"));
                }
            });
            try
            {
                await client.AskAgentWithHistoryAsync(
                    "sk-test-only-12345678901234567890", "连续调用直到取消", "",
                    Array.Empty<ConversationTurn>(), tools, 0, true, cancellation.Token);
                throw new InvalidOperationException("取消后 Agent 仍然返回了最终回答。");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                int expectedRounds = (cancelAfter + handler.CallsPerRound - 1) / handler.CallsPerRound;
                if (executions != cancelAfter || handler.CallCount != expectedRounds)
                    throw new InvalidOperationException($"取消后仍然执行了工具或请求：calls={executions}, steps={handler.CallCount}。");
            }
            Console.WriteLine($"OK agent-unlimited-cancel afterCalls={executions} apiCalls={handler.CallCount} pending=stopped");
        }
    }

    private static async Task RunAgentLengthRecoveryTest()
    {
        AgentLengthRecoveryHandler handler = new();
        using DeepSeekClient client = new(handler);
        AgentToolRegistry tools = new();
        tools.Register(new AgentToolDefinition
        {
            Name = "unused_test_tool",
            DisplayName = "未使用测试工具",
            Description = "仅用于保证请求进入 Agent 工具模式。",
            Parameters = new { type = "object" },
            ExecuteAsync = (_, _) => throw new InvalidOperationException("恢复测试不应执行工具。")
        });
        List<AgentReasoningEvent> reasoningEvents = new();
        string answer = await client.AskAgentWithHistoryAsync(
            "sk-test-only-12345678901234567890",
            "微光湖有什么作用",
            "",
            Array.Empty<ConversationTurn>(),
            tools,
            0,
            true,
            CancellationToken.None,
            reasoningProgress: reasoningEvents.Add);
        if (answer != "微光湖作用回答" || handler.CallCount != 2
            || !handler.SawThinkingOnAllCalls || !handler.SawNoOutputLimitOnAllCalls
            || reasoningEvents.Count != 1 || reasoningEvents[0].Content != "推理耗尽输出额度")
            throw new InvalidOperationException($"Agent length 空回答恢复失败：answer={answer} calls={handler.CallCount} thinking={handler.SawThinkingOnAllCalls} unlimited={handler.SawNoOutputLimitOnAllCalls} reasoningEvents={reasoningEvents.Count}。");
        Console.WriteLine("OK agent-length-recovery retry=thinking-on max_tokens=omitted trace=visible");
    }

    private static async Task RunLiveAgentToolTest(string temporaryDirectory)
    {
        ApiKeyProvider keyProvider = new(temporaryDirectory);
        if (!keyProvider.TryGetKey(out string key, out _))
            throw new InvalidOperationException("没有通过环境变量读取到测试 Key。");

        AgentToolRegistry tools = new();
        tools.Register(new AgentToolDefinition
        {
            Name = "read_game_state",
            DisplayName = "读取实时游戏状态",
            Description = "读取当前 Terraria 状态。用户明确询问当前装备时必须调用，只请求 equipment。",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    categories = new
                    {
                        type = "array",
                        items = new { type = "string", @enum = new[] { "equipment" } },
                        minItems = 1
                    }
                },
                required = new[] { "categories" },
                additionalProperties = false
            },
            ExecuteAsync = (_, _) => Task.FromResult(new AgentToolResult(false, "[装备] 护甲：测试木盔甲；饰品：测试跑鞋。", "已读取装备"))
        });

        using DeepSeekClient client = new();
        List<AgentToolEvent> events = new();
        string answer = await client.AskAgentWithHistoryAsync(
            key,
            "请先使用技能读取我当前的装备，再用一句话说明你读到了什么。",
            "",
            Array.Empty<ConversationTurn>(),
            tools,
            0,
            true,
            CancellationToken.None,
            events.Add);
        if (string.IsNullOrWhiteSpace(answer)
            || !events.Any(value => value.Completed && value.ToolName == "read_game_state" && !value.IsError))
            throw new InvalidOperationException("线上 Agent 没有完成结构化工具调用。answerChars=" + answer.Length + " events=" + events.Count);
        Console.WriteLine($"OK live-agent model={DeepSeekClient.ModelName} calls={events.Count / 2} answerChars={answer.Length}");
    }

    private static async Task RunLiveLocatorAgentTest(string temporaryDirectory)
    {
        ApiKeyProvider keyProvider = new(temporaryDirectory);
        if (!keyProvider.TryGetKey(out string key, out _))
            throw new InvalidOperationException("没有通过环境变量读取到测试 Key。");

        OverlaySettings settings = new();
        NavigationState navigation = new();
        AgentToolRegistry tools = GameMemoryAgentSkill.CreateRegistry(settings);
        WorldLocatorAgentSkills.Register(tools, settings, navigation);
        using DeepSeekClient client = new();
        List<AgentToolEvent> events = new();
        string answer = await client.AskAgentWithHistoryAsync(
            key,
            "带我去当前世界里最近的花岗岩洞，并告诉我怎么跟随悬浮球导航。",
            "",
            Array.Empty<ConversationTurn>(),
            tools,
            0,
            true,
            CancellationToken.None,
            events.Add);
        if (string.IsNullOrWhiteSpace(answer)
            || !events.Any(value => value.Completed && value.ToolName == WorldLocatorAgentSkills.FindBiomeToolName && !value.IsError)
            || !navigation.GetSnapshot().Active)
            throw new InvalidOperationException("线上 Agent 没有完成扩展群系搜索或启动导航。answerChars=" + answer.Length + " events=" + events.Count);
        Console.WriteLine($"OK live-locator-agent model={DeepSeekClient.ModelName} calls={events.Count / 2} navigation={navigation.GetSnapshot().Label} answerChars={answer.Length}");
    }

    private static async Task RunLiveAgentKnowledgeTest(string temporaryDirectory)
    {
        ApiKeyProvider keyProvider = new(temporaryDirectory);
        if (!keyProvider.TryGetKey(out string key, out _))
            throw new InvalidOperationException("没有通过环境变量读取到测试 Key。");
        OverlaySettings settings = new();
        NavigationState navigation = new();
        AgentToolRegistry tools = GameMemoryAgentSkill.CreateRegistry(settings);
        WorldLocatorAgentSkills.Register(tools, settings, navigation);
        using DeepSeekClient client = new();
        int apiCalls = 0;
        client.UsageReported += _ => apiCalls++;
        string answer = await client.AskAgentWithHistoryAsync(
            key,
            "微光湖有什么作用",
            "",
            Array.Empty<ConversationTurn>(),
            tools,
            0,
            true,
            CancellationToken.None);
        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidOperationException("线上 Agent 知识问答仍然返回空回答。");
        Console.WriteLine($"OK live-agent-knowledge model={DeepSeekClient.ModelName} apiCalls={apiCalls} answerChars={answer.Length}");
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            string content = CallCount == 1 ? "" : "恢复成功";
            string finishReason = CallCount == 1 ? "insufficient_system_resource" : "stop";
            string json = "{\"choices\":[{\"finish_reason\":\"" + finishReason
                + "\",\"message\":{\"content\":\"" + content
                + "\",\"reasoning_content\":null}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ToolLoopHandler : HttpMessageHandler
    {
        private int _callCount;
        public bool SawToolSchema { get; private set; }
        public bool SawReasoningReplay { get; private set; }
        public bool SawHistoricalReasoning { get; private set; }
        public bool SawToolResult { get; private set; }
        public bool SawUnexpectedToolChoice { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            string requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            using (JsonDocument requestDocument = JsonDocument.Parse(requestJson))
            {
                SawUnexpectedToolChoice |= requestDocument.RootElement.TryGetProperty("tool_choice", out _);
                if (requestDocument.RootElement.TryGetProperty("tools", out JsonElement toolSchemas))
                    SawToolSchema |= toolSchemas.EnumerateArray().Any(tool =>
                        tool.GetProperty("function").GetProperty("name").GetString() == "read_game_state");
                if (_callCount > 1)
                {
                    foreach (JsonElement message in requestDocument.RootElement.GetProperty("messages").EnumerateArray())
                    {
                        if (message.TryGetProperty("reasoning_content", out JsonElement reasoning)
                            && reasoning.ValueKind == JsonValueKind.String)
                        {
                            SawReasoningReplay |= reasoning.GetString() == "需要读取装备";
                            SawHistoricalReasoning |= reasoning.GetString() == "历史最终思考";
                        }
                        if (message.GetProperty("role").GetString() == "tool"
                            && message.GetProperty("content").GetString() is string toolContent)
                        {
                            using JsonDocument toolResult = JsonDocument.Parse(toolContent);
                            SawToolResult |= toolResult.RootElement.GetProperty("result").GetString()
                                ?.Contains("[装备] 测试装备", StringComparison.Ordinal) == true;
                        }
                    }
                }
            }

            string json = _callCount == 1
                ? "{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"content\":null,\"reasoning_content\":\"需要读取装备\",\"tool_calls\":[{\"id\":\"call-1\",\"type\":\"function\",\"function\":{\"name\":\"read_game_state\",\"arguments\":\"{\\\"categories\\\":[\\\"equipment\\\"]}\"}}]}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}"
                : "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"装备读取完成\",\"reasoning_content\":\"已取得工具结果\",\"tool_calls\":[]}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class MultiStepToolHandler : HttpMessageHandler
    {
        public int ToolRounds { get; init; }
        public int CallsPerRound { get; init; }
        public bool ThinkingEnabled { get; init; } = true;
        public int CallCount { get; private set; }
        private readonly List<string> _issuedCallIds = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement payload = document.RootElement;
            if (payload.TryGetProperty("max_tokens", out _)
                || payload.GetProperty("thinking").GetProperty("type").GetString() != (ThinkingEnabled ? "enabled" : "disabled")
                || !payload.TryGetProperty("tools", out JsonElement schemas)
                || !schemas.EnumerateArray().Any(tool => tool.GetProperty("function").GetProperty("name").GetString() == "test_tool"))
                throw new InvalidOperationException("长任务丢失了工具声明、思考设置或取消了不限输出设置。");
            bool hasToolChoice = payload.TryGetProperty("tool_choice", out JsonElement choice);
            if (ThinkingEnabled ? hasToolChoice : !hasToolChoice || choice.GetString() != "auto")
                throw new InvalidOperationException("Agent 强制终止了工具规划。");

            List<string> declaredIds = new();
            List<string> resultIds = new();
            foreach (JsonElement message in payload.GetProperty("messages").EnumerateArray())
            {
                if (message.TryGetProperty("tool_calls", out JsonElement calls))
                    declaredIds.AddRange(calls.EnumerateArray().Select(call => call.GetProperty("id").GetString()!));
                if (message.GetProperty("role").GetString() == "tool")
                {
                    resultIds.Add(message.GetProperty("tool_call_id").GetString()!);
                    using JsonDocument result = JsonDocument.Parse(message.GetProperty("content").GetString()!);
                    if (!result.RootElement.GetProperty("ok").GetBoolean())
                        throw new InvalidOperationException("长任务测试工具执行失败。");
                }
            }
            if (!declaredIds.SequenceEqual(_issuedCallIds) || !resultIds.SequenceEqual(_issuedCallIds))
                throw new InvalidOperationException("Agent 丢弃了部分工具调用或没有完整回传工具结果。");

            bool finished = CallCount > ToolRounds;
            List<object> toolCalls = new();
            if (!finished)
            {
                for (int index = 0; index < CallsPerRound; index++)
                {
                    string id = $"call-{CallCount}-{index}";
                    _issuedCallIds.Add(id);
                    toolCalls.Add(new
                    {
                        id,
                        type = "function",
                        function = new { name = "test_tool", arguments = JsonSerializer.Serialize(new { round = CallCount, index }) }
                    });
                }
            }
            string json = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = finished ? "stop" : "tool_calls",
                        message = new
                        {
                            content = finished ? "不限次数工具调用完成" : (string?)null,
                            reasoning_content = $"规划第 {CallCount} 步",
                            tool_calls = toolCalls
                        }
                    }
                },
                usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class AgentLengthRecoveryHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public bool SawThinkingOnAllCalls { get; private set; } = true;
        public bool SawNoOutputLimitOnAllCalls { get; private set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            string requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(requestJson);
            SawThinkingOnAllCalls &= document.RootElement.GetProperty("thinking").GetProperty("type").GetString() == "enabled";
            SawNoOutputLimitOnAllCalls &= !document.RootElement.TryGetProperty("max_tokens", out _);
            string json = CallCount == 1
                ? "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":null,\"reasoning_content\":\"推理耗尽输出额度\",\"tool_calls\":[]}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":256,\"total_tokens\":257}}"
                : "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"微光湖作用回答\",\"reasoning_content\":null,\"tool_calls\":[]}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":8,\"total_tokens\":9}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static string BuildContext(System.Collections.Generic.IReadOnlyList<WikiDocument> documents)
    {
        System.Text.StringBuilder builder = new();
        for (int i = 0; i < documents.Count; i++)
        {
            builder.AppendLine($"[{i + 1}] 来源：{documents[i].SourceName}；类型={documents[i].SourceType}；可信度={documents[i].Reliability}");
            builder.AppendLine($"标题：{documents[i].Title}");
            builder.AppendLine($"URL：{documents[i].Url}");
            builder.AppendLine(documents[i].Text);
        }
        return builder.ToString();
    }
}
