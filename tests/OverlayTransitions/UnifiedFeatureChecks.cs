using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using TerraWikiOverlay;

internal static class UnifiedFeatureChecks
{
    internal static async Task RunAsync(HarnessChatView view, string directory, string[] args, Form host)
    {
        string data = Path.Combine(directory, "features");
        using OverlayAskService service = new(data);
        using HarnessFeaturesController controller = new(service, view);
        CoreWebView2 core = view.Browser.CoreWebView2;
        void Command(string action, JsonElement json) => controller.Handle(action, json);
        view.Command += Command;
        List<bool> themes = new(); controller.ThemeChanged += (_, dark) => themes.Add(dark);
        List<WikiViewport> bounds = new(); controller.WikiBoundsChanged += bounds.Add;
        string? question = null; controller.AskRequested += text => question = text;
        async Task Drain() { await Task.Delay(100); await core.ExecuteScriptAsync("new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))"); }
        async Task<JsonElement> Read(string expression)
        {
            string json = await core.ExecuteScriptAsync("JSON.stringify(" + expression + ")");
            return JsonDocument.Parse(JsonSerializer.Deserialize<string>(json)!).RootElement.Clone();
        }
        async Task Save(string name)
        {
            int flag = Array.IndexOf(args, "--screenshots");
            if (flag < 0 || flag + 1 >= args.Length) return;
            string destination = Path.GetFullPath(args[flag + 1]); Directory.CreateDirectory(destination);
            using FileStream stream = File.Create(Path.Combine(destination, name + ".png"));
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }
        async Task Theme(string mode)
        {
            using JsonDocument command = JsonDocument.Parse(JsonSerializer.Serialize(new { value = mode }));
            controller.Handle("theme", command.RootElement); await Drain();
        }
        try
        {
            host.ClientSize = new Size(1180, 820);
            controller.Show("settings"); await Theme("light"); await Drain(); await Save("settings-light");
            await core.ExecuteScriptAsync("document.querySelector('[data-theme-mode=dark]').click()"); await Drain();
            if (new OverlaySettingsStore(data).Current.ThemeMode != "dark" || themes.LastOrDefault() != true) throw new Exception("Night mode was not persisted/resolved.");
            JsonElement settingsTheme = await Read("({page:document.body.dataset.page,dark:document.body.hasAttribute('data-ds-dark-theme'),background:getComputedStyle(document.querySelector('.settings-card')).backgroundColor})");
            if (!settingsTheme.GetProperty("dark").GetBoolean() || settingsTheme.GetProperty("background").GetString() == "rgb(255, 255, 255)") throw new Exception("Settings retained its light surface in night mode.");
            await Save("settings-night");
            await core.ExecuteScriptAsync("document.querySelector('[data-setting=contextTurns]').value=17;document.querySelector('[data-setting=includeMemoryContext]').checked=false;document.querySelector('.feature-footer .primary').click()"); await Drain();
            if (service.Settings.ContextTurns != 17 || service.Settings.IncludeMemoryContext) throw new Exception("Themed settings did not save permissions/context.");
            controller.Show("settings", 3); await Drain();
            await core.ExecuteScriptAsync("document.querySelector('#game-preview-button').click()"); await Drain();
            if (!(await Read("document.querySelector('#game-preview').textContent")).GetString()!.Contains("关闭")) throw new Exception("Game preview ignored the disabled live-read setting.");
            await Save("permissions-night");
            controller.Show("settings", 1); await Drain();
            await core.ExecuteScriptAsync("document.querySelector('#settings-key-input').value='sk-test-only-123456789012345678901234567890';document.querySelector('[data-panel=key] .primary').click()"); await Drain();
            if ((await Read("document.querySelector('#settings-key-input').value")).GetString() != ""
                || (await Read("document.body.textContent.includes('sk-test-only-123456789012345678901234567890')")).GetBoolean()) throw new Exception("A saved key was echoed in the feature UI.");
            await Save("api-key-night");
            controller.Show("settings", 2); await Drain(); await Save("usage-night");
            controller.Show("locator"); await Drain();
            await core.ExecuteScriptAsync("document.querySelector('[data-tab=biomes]').click()"); await Drain();
            if ((await Read("document.querySelector('#biome-presets').options.length")).GetInt32() != TerrariaWorldLocator.SupportedBiomeNames.Count + 1) throw new Exception("Biome choices were dropped during UI migration.");
            await core.ExecuteScriptAsync("document.querySelector('#locator-query').value='地下丛林';document.querySelector('[data-tab=storage]').click();document.querySelector('#locator-query').value='魔镜';document.querySelector('[data-tab=biomes]').click()"); await Drain();
            if ((await Read("document.querySelector('#locator-query').value")).GetString() != "地下丛林") throw new Exception("Switching locator modes lost queries.");
            await core.ExecuteScriptAsync("document.querySelector('#locator-search').click()"); await Drain();
            if (!(await Read("document.querySelector('#feature-status-locator').textContent")).GetString()!.Contains("设置")) throw new Exception("Direct search bypassed permission validation.");
            NavigationTarget first = new("item", "魔镜 · 材料箱", 100, 100, "上排第 2 个");
            NavigationTarget second = new("item", "魔镜 · 药水箱", 120, 100, "上排第 3 个");
            WorldSearchResult result = new("测试库存示例 · 3 条结果", new[]
            {
                new WorldSearchRow("魔镜 ×1", "材料箱", "上排第 2 个 · 槽4", "右", 100, 100, 100, first),
                new WorldSearchRow("魔镜 ×2", "药水箱", "上排第 3 个 · 槽2、槽7", "右", 120, 120, 100, second),
                new WorldSearchRow("魔镜 ×1", "猪猪储蓄罐", "随身个人储物 · 槽1", "无需导航", null, null, null, null)
            }, new[] { first, second }, 0, 0);
            await core.ExecuteScriptAsync("document.querySelector('[data-tab=storage]').click()"); await Drain(); controller.ShowResults(result); await Drain();
            await core.ExecuteScriptAsync("document.querySelectorAll('#locator-table tbody tr')[1].click();document.querySelector('#locator-navigate').click()"); await Drain();
            if (service.Navigation.TargetTileX != 120) throw new Exception("The new locator navigated to a different chest.");
            await core.ExecuteScriptAsync("document.querySelectorAll('#locator-table tbody tr')[2].click()"); await Drain();
            if (!(await Read("document.querySelector('#locator-navigate').disabled")).GetBoolean()) throw new Exception("Personal storage acquired a world target.");
            await Save("locator-night"); await Theme("light"); await Save("locator-light");
            await Theme("dark"); controller.Show("reference"); await Drain();
            await core.ExecuteScriptAsync("let field=document.querySelector('#reference-query');field.value='Ironskin';field.dispatchEvent(new Event('input'))"); await Drain();
            if (!(await Read("document.querySelector('#reference-table').textContent")).GetString()!.Contains("铁皮药水")) throw new Exception("Reference aliases no longer match.");
            await core.ExecuteScriptAsync("document.querySelector('#reference-detail .primary').click()"); await Drain();
            if (question is null || !question.Contains("铁皮药水") || controller.Page != "chat") throw new Exception("Reference no longer fills a follow-up question.");
            controller.Show("reference"); await Drain(); await Save("reference-night");
            controller.Show("wiki"); await Drain();
            if (bounds.Count == 0 || bounds.Last().Width < 100 || bounds.Last().Height < 60) throw new Exception("Wiki viewport was not aligned with the shared chrome.");
            await core.ExecuteScriptAsync("document.querySelector('#more').click()"); await Drain();
            if (bounds.Last().Visible) throw new Exception("Native Wiki covered a local popup.");
            await core.ExecuteScriptAsync("document.querySelector('#more').click()"); await Drain(); await Save("wiki-chrome-night");
            await WikiThemeChecks.RunAsync(view, controller, host, data, args, bounds.Last());
            foreach (Size size in new[] { new Size(860, 600), new Size(1180, 820), new Size(1920, 1080) })
            {
                host.ClientSize = size;
                foreach (string page in new[] { "settings", "locator", "reference", "wiki" })
                {
                    controller.Show(page); await Drain();
                    JsonElement geometry = await Read("({overflow:document.documentElement.scrollWidth>innerWidth,feature:document.querySelector('#feature-view').getBoundingClientRect().bottom,height:innerHeight,save:document.querySelector('.feature-footer .primary')?.getBoundingClientRect().bottom||0})");
                    if (geometry.GetProperty("overflow").GetBoolean() || geometry.GetProperty("feature").GetDouble() > geometry.GetProperty("height").GetDouble() + 1
                        || geometry.GetProperty("save").GetDouble() > geometry.GetProperty("height").GetDouble() + 1) throw new Exception("A unified feature page overflowed: " + page + " " + geometry);
                }
            }
            host.ClientSize = new Size(1180, 820);
            await Theme("system");
            if (new OverlaySettingsStore(data).Current.ThemeMode != "system") throw new Exception("System appearance preference not persisted.");
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"dark\"}]}"); await Drain();
            if (!(await Read("document.body.hasAttribute('data-ds-dark-theme')")).GetBoolean() || !themes.Last()) throw new Exception("System night mode did not propagate.");
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"light\"}]}"); await Drain();
            if ((await Read("document.body.hasAttribute('data-ds-dark-theme')")).GetBoolean() || themes.Last()) throw new Exception("System light mode did not propagate.");
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[]}");
            await Theme("dark"); controller.Show("settings"); await Drain();
            Console.WriteLine("OK unified-features pages=settings+permissions+key+usage+locator+reference+wiki themes=light/dark/system persisted=true locator=selected-chest permissions=enforced wiki-popups=unobscured");
        }
        finally { view.Command -= Command; }
    }
}
