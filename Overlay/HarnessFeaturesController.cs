using System.Text.Json;

namespace TerraWikiOverlay;

internal sealed record WikiViewport(int X, int Y, int Width, int Height, bool Visible);

// Feature pages share the chat WebView, theme tokens and navigation shell.
internal sealed class HarnessFeaturesController : IDisposable
{
    private readonly OverlayAskService _assistant;
    private readonly HarnessChatView _view;
    private CancellationTokenSource? _search;
    private WorldSearchResult? _result;
    private bool _disposed;
    private bool _previewBusy;
    internal string Page { get; private set; } = "chat";
    internal event Action<string>? PageChanged;
    internal event Action<string, bool>? ThemeChanged;
    internal event Action<WikiViewport>? WikiBoundsChanged;
    internal event Action<string, string>? WikiCommand;
    internal event Action<string>? AskRequested;
    internal event Action? SettingsChanged;

    internal HarnessFeaturesController(OverlayAskService assistant, HarnessChatView view)
    {
        _assistant = assistant; _view = view;
        _view.Post(new { type = "featureData", biomes = TerrariaWorldLocator.SupportedBiomeNames, reference = QuickReferenceCatalog.Entries });
        _view.Post(new { type = "theme", mode = assistant.Settings.ThemeMode });
    }

    internal void Show(string page, int tab = 0)
    {
        if (page is not ("chat" or "settings" or "locator" or "wiki" or "reference")) return;
        Page = page;
        PageChanged?.Invoke(page);
        if (page == "settings") SendSettings();
        _view.Post(new { type = "route", page, tab });
    }

    internal bool Handle(string action, JsonElement data)
    {
        JsonElement value = data.TryGetProperty("value", out JsonElement item) ? item : default;
        string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        switch (action)
        {
            case "theme":
                if (text is "light" or "dark" or "system")
                {
                    _assistant.Settings.ThemeMode = text; _assistant.SaveSettings();
                    _view.Post(new { type = "theme", mode = text });
                }
                return true;
            case "themeResolved":
                bool dark = text == "dark";
                _view.ApplyAppearance(_assistant.Settings.ThemeMode, dark);
                ThemeChanged?.Invoke(_assistant.Settings.ThemeMode, dark);
                return true;
            case "chat": Show("chat"); return true;
            case "settings": Show("settings"); return true;
            case "locator": Show("locator"); return true;
            case "wiki": Show("wiki"); return true;
            case "reference": Show("reference"); return true;
            case "featureAsk":
                if (text.Length is > 0 and <= 2000) { Show("chat"); AskRequested?.Invoke(text); }
                return true;
            case "settingsSave":
                if (value.ValueKind == JsonValueKind.Object) { SaveSettings(value); Status("settings", "设置已保存"); }
                return true;
            case "settingsRefresh": SendSettings(); return true;
            case "saveKey":
                bool savedOk = _assistant.SaveApiKey(text, out string saved); Status("key", saved); SendSettings();
                _view.Post(new { type = "keySaved", success = savedOk }); return true;
            case "removeKey":
                _assistant.RemoveLocalKey(out string removed); Status("key", removed); SendSettings(); return true;
            case "pasteKey":
                try { _view.Post(new { type = "keyPaste", value = Clipboard.GetText().Trim() }); }
                catch (Exception) { Status("key", "暂时无法读取剪贴板，请直接粘贴。"); }
                return true;
            case "resetUsage": _assistant.ResetUsage(); SendSettings(); Status("usage", "用量统计已重置"); return true;
            case "previewGame": _ = PreviewAsync(value.Clone()); return true;
            case "worldSearch": _ = SearchAsync(value.Clone()); return true;
            case "cancelSearch": CancelSearch(); return true;
            case "navigateResult":
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int index)) NavigateResult(index);
                return true;
            case "wikiBounds":
                if (Page == "wiki" && value.ValueKind == JsonValueKind.Object)
                {
                    double scale = Math.Clamp(Number(value, "scale", 1), 0.5, 8);
                    WikiBoundsChanged?.Invoke(new((int)Math.Round(Number(value, "x") * scale), (int)Math.Round(Number(value, "y") * scale),
                        (int)Math.Round(Number(value, "width") * scale), (int)Math.Round(Number(value, "height") * scale), Bool(value, "visible")));
                }
                return true;
            case "wikiNavigate": case "wikiBack": case "wikiForward": case "wikiReload": case "wikiHome": case "wikiAsk": case "wikiLanguage":
                WikiCommand?.Invoke(action, text); return true;
            default: return false;
        }
    }

    internal bool CancelSearch()
    {
        if (_search is null) return false;
        _search.Cancel(); return true;
    }

    private void SendSettings()
    {
        _view.Post(new { type = "settingsData", settings = _assistant.Settings,
            keyStatus = _assistant.KeyStatus, usage = _assistant.UsageSummary, hasKey = _assistant.HasKey(out _),
            gameStatus = GameProcessLocator.Find(refresh: true)?.DisplayName ?? "未检测到游戏客户端" });
        SettingsChanged?.Invoke();
    }

    private void SaveSettings(JsonElement values)
    {
        OverlaySettings settings = _assistant.Settings;
        settings.PersistHistory = Bool(values, "persistHistory", settings.PersistHistory);
        settings.ContextTurns = (int)Number(values, "contextTurns", settings.ContextTurns);
        settings.ContextCharacterBudget = (int)Number(values, "contextCharacterBudget", settings.ContextCharacterBudget);
        settings.HistoryRetentionDays = (int)Number(values, "historyRetentionDays", settings.HistoryRetentionDays);
        settings.IncludeMemoryContext = Bool(values, "includeMemoryContext", settings.IncludeMemoryContext);
        settings.IncludeHealth = Bool(values, "includeHealth", settings.IncludeHealth);
        settings.IncludeMana = Bool(values, "includeMana", settings.IncludeMana);
        settings.IncludeInventory = Bool(values, "includeInventory", settings.IncludeInventory);
        settings.IncludeEquipment = Bool(values, "includeEquipment", settings.IncludeEquipment);
        settings.IncludeWorldInfo = Bool(values, "includeWorldInfo", settings.IncludeWorldInfo);
        settings.IncludeProgress = Bool(values, "includeProgress", settings.IncludeProgress);
        settings.IncludeAchievements = Bool(values, "includeAchievements", settings.IncludeAchievements);
        settings.EnableStorageLocator = Bool(values, "enableStorageLocator", settings.EnableStorageLocator);
        settings.EnableBiomeLocator = Bool(values, "enableBiomeLocator", settings.EnableBiomeLocator);
        settings.EnableObjectLocator = Bool(values, "enableObjectLocator", settings.EnableObjectLocator);
        settings.IncludeSaveContext = settings.GameContextSelection.HasAny;
        _assistant.SaveSettings(); SendSettings();
    }

    private async Task PreviewAsync(JsonElement values)
    {
        if (_previewBusy || _disposed || values.ValueKind != JsonValueKind.Object) return;
        _previewBusy = true;
        _view.Post(new { type = "gamePreview", busy = true, value = "正在只读解析 Terraria 实时状态…" });
        try
        {
            GameContextSelection selection = new(Bool(values, "includeHealth"), Bool(values, "includeMana"), Bool(values, "includeInventory"),
                Bool(values, "includeEquipment"), Bool(values, "includeWorldInfo"), Bool(values, "includeProgress"), Bool(values, "includeAchievements"));
            bool enabled = Bool(values, "includeMemoryContext");
            string preview = await Task.Run(() => _assistant.GetGameContextPreview(selection, enabled));
            if (!_disposed) _view.Post(new { type = "gamePreview", busy = false, value = preview });
        }
        catch (Exception exception) { if (!_disposed) _view.Post(new { type = "gamePreview", busy = false, value = exception.GetBaseException().Message }); }
        finally { _previewBusy = false; }
    }

    private async Task SearchAsync(JsonElement values)
    {
        if (_search is not null || _disposed || values.ValueKind != JsonValueKind.Object) return;
        string kindName = values.TryGetProperty("kind", out JsonElement kindValue) ? kindValue.GetString() ?? "objects" : "objects";
        WorldSearchKind kind = kindName switch { "biomes" => WorldSearchKind.Biomes, "storage" => WorldSearchKind.Storage, _ => WorldSearchKind.Objects };
        string query = values.TryGetProperty("query", out JsonElement queryValue) ? queryValue.GetString()?.Trim() ?? "" : "";
        if (query.Length is < 1 or > 200) { Status("locator", "请输入 1–200 字的查找名称。"); return; }
        string? denied = WorldSearchService.PermissionError(kind, _assistant.Settings);
        if (denied is not null) { Status("locator", denied); return; }
        using CancellationTokenSource request = new();
        _search = request; _result = null;
        _view.Post(new { type = "searchState", busy = true, status = "正在读取实时游戏数据…" });
        try
        {
            WorldSearchResult result = await Task.Run(() => WorldSearchService.Search(kind, query, Bool(values, "personal", true), _assistant.Settings, request.Token), request.Token);
            if (!_disposed && !request.IsCancellationRequested) ShowResults(result);
        }
        catch (OperationCanceledException) { if (!_disposed) Status("locator", "查找已取消，原有导航未受影响。"); }
        catch (Exception exception) { if (!_disposed) Status("locator", exception.GetBaseException().Message); }
        finally { _search = null; if (!_disposed) _view.Post(new { type = "searchState", busy = false }); }
    }

    internal void ShowResults(WorldSearchResult result)
    {
        _result = result;
        _view.Post(new { type = "searchResults", summary = result.Summary, rows = result.Rows.Select((row, index) => new
        { index, row.Name, row.Place, row.Direction, row.DistanceTiles, row.TileX, row.TileY, row.Detail, canNavigate = row.Target is not null }).ToList() });
    }

    internal void NavigateResult(int index)
    {
        if (_search is not null || _result is null || index < 0 || index >= _result.Rows.Count || _result.Rows[index].Target is not { } target) return;
        _assistant.NavigateToSearchResult(_result, target);
        Status("locator", "已指向 " + target.Label + "。收起窗口跟随悬浮球箭头。");
    }

    private void Status(string target, string value) { if (!_disposed) _view.Post(new { type = "featureStatus", target, value }); }
    private static bool Bool(JsonElement values, string name, bool fallback = false) => values.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static double Number(JsonElement values, string name, double fallback = 0) => values.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number) ? Math.Clamp(number, -100000, 100000) : fallback;
    public void Dispose() { _disposed = true; _search?.Cancel(); }
}
