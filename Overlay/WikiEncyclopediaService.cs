using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TerraWikiOverlay;

internal sealed record WikiCatalogHit(string Title, int PageId, string Language, string Snippet, int? ItemId = null);
internal sealed record WikiCatalogSearch(IReadOnlyList<WikiCatalogHit> Hits, bool Online, string Notice, int TotalMatches = 0);
internal sealed record WikiCatalogArticle(string Title, string Text, string Language, bool FromCache);
internal sealed record WikiPageTitle(int PageId, string Title);
internal sealed record WikiTitleIndex(DateTime RetrievedUtc, List<WikiPageTitle> Pages);

internal sealed class WikiEncyclopediaService : IDisposable
{
    private const string ChineseApi = "https://terraria.wiki.gg/zh/api.php";
    private const string EnglishApi = "https://terraria.wiki.gg/api.php";
    private static readonly Regex UnwantedBlocks = new(@"<(script|style|noscript)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"[ \t\f\v]+", RegexOptions.Compiled);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _directory;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private IReadOnlyList<WikiPageTitle> _titles = Array.Empty<WikiPageTitle>();
    private IReadOnlyList<WikiPageTitle> _englishTitles = Array.Empty<WikiPageTitle>();
    private DateTime _indexRetrievedUtc;
    private DateTime _englishIndexRetrievedUtc;

    internal int IndexedPageCount => _titles.Count + _englishTitles.Count;
    internal bool IndexIsFresh => _titles.Count > 0 && _englishTitles.Count > 0
        && DateTime.UtcNow - _indexRetrievedUtc < TimeSpan.FromDays(14)
        && DateTime.UtcNow - _englishIndexRetrievedUtc < TimeSpan.FromDays(14);

    internal WikiEncyclopediaService(string dataDirectory, HttpClient? http = null)
    {
        _directory = Path.Combine(dataDirectory, "encyclopedia-v1");
        Directory.CreateDirectory(_directory);
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttp = http is null;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(TerraWikiAI.Services.ClientIdentity.UserAgent);
        LoadIndex();
    }

    internal async Task SyncTitleIndexesAsync(Action<int>? progress, CancellationToken token, bool force = false)
    {
        await _syncGate.WaitAsync(token);
        try
        {
            if (IndexIsFresh && !force) return;
            foreach (string language in new[] { "zh", "en" })
            {
                if (!force && (language == "zh" ? _titles.Count > 0 && DateTime.UtcNow - _indexRetrievedUtc < TimeSpan.FromDays(14)
                    : _englishTitles.Count > 0 && DateTime.UtcNow - _englishIndexRetrievedUtc < TimeSpan.FromDays(14)))
                    continue;
                List<WikiPageTitle> pages = new();
                string? continuation = null;
                string api = language == "zh" ? ChineseApi : EnglishApi;
                do
                {
                    string url = api + "?action=query&list=allpages&apnamespace=0&apfilterredir=nonredirects"
                        + "&aplimit=500&format=json&formatversion=2"
                        + (continuation is null ? "" : "&apcontinue=" + Uri.EscapeDataString(continuation));
                    using JsonDocument document = await GetJsonAsync(url, token);
                    if (!document.RootElement.TryGetProperty("query", out JsonElement query)
                        || !query.TryGetProperty("allpages", out JsonElement allPages))
                        throw new InvalidDataException("官方 Wiki 没有返回页面索引。");
                    foreach (JsonElement page in allPages.EnumerateArray())
                        pages.Add(new WikiPageTitle(page.GetProperty("pageid").GetInt32(), page.GetProperty("title").GetString() ?? ""));
                    continuation = document.RootElement.TryGetProperty("continue", out JsonElement next)
                        && next.TryGetProperty("apcontinue", out JsonElement value)
                        ? value.GetString()
                        : null;
                    progress?.Invoke(pages.Count + (language == "en" ? _titles.Count : 0));
                    if (continuation is not null)
                        await Task.Delay(120, token);
                } while (continuation is not null);

                if (pages.Count == 0)
                    throw new InvalidDataException("官方 Wiki 页面索引为空。");
                WikiTitleIndex snapshot = new(DateTime.UtcNow, pages);
                string path = Path.Combine(_directory, "titles-" + language + ".json");
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot), token);
                    File.Move(temporary, path, true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                if (language == "zh")
                {
                    _titles = pages;
                    _indexRetrievedUtc = snapshot.RetrievedUtc;
                }
                else
                {
                    _englishTitles = pages;
                    _englishIndexRetrievedUtc = snapshot.RetrievedUtc;
                }
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    internal async Task<WikiCatalogSearch> SearchAsync(string query, CancellationToken token)
    {
        query = query.Trim();
        if (query.Length == 0)
            return new WikiCatalogSearch(Array.Empty<WikiCatalogHit>(), false, "输入物品、怪物、材料或配方名称开始搜索。");

        List<WikiCatalogHit> candidates = new();
        IReadOnlyList<WikiPageTitle> titles = _titles;
        candidates.AddRange(titles
            .Where(page => page.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(page => Score(page.Title, query))
            .ThenBy(page => page.Title.Length)
            .Select(page => new WikiCatalogHit(page.Title, page.PageId, "zh", "官方中文 Wiki 页面")));
        candidates.AddRange(_englishTitles
            .Where(page => page.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(page => Score(page.Title, query))
            .ThenBy(page => page.Title.Length)
            .Select(page => new WikiCatalogHit(page.Title, page.PageId, "en", "Official Terraria Wiki page")));

        IReadOnlyList<KeyValuePair<int, string>> items = await Task.Run(
            () => TerrariaItemNameCatalog.FindByName(query, Math.Max(1, TerrariaItemNameCatalog.Count)), token);
        candidates.AddRange(items.Select(item =>
            new WikiCatalogHit(item.Value, 0, "local", "本机原版物品 ID " + item.Key, item.Key)));

        bool online = false;
        string notice = "";
        try
        {
            IReadOnlyList<WikiCatalogHit> chinese = await SearchWikiAsync(query, "zh", token);
            candidates.AddRange(chinese);
            online = true;
            if (chinese.Count < 5 || query.All(character => character <= 127))
            {
                IReadOnlyList<WikiCatalogHit> english = await SearchWikiAsync(query, "en", token);
                candidates.AddRange(english);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            notice = "在线搜索暂不可用，显示已同步的标题和本机物品名：" + exception.GetBaseException().Message;
        }

        List<WikiCatalogHit> ordered = candidates
            .GroupBy(hit => hit.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(hit => hit.PageId == 0 ? 1 : 0)
                .ThenBy(hit => hit.Language == "zh" ? 0 : 1)
                .ThenBy(hit => hit.Snippet is "官方中文 Wiki 页面" or "Official Terraria Wiki page" ? 1 : 0)
                .First())
            .OrderBy(hit => Score(hit.Title, query))
            .ThenBy(hit => hit.Title.Length)
            .ToList();
        return new WikiCatalogSearch(ordered.Take(500).ToList(), online, notice, ordered.Count);
    }

    internal async Task<WikiCatalogArticle> GetArticleAsync(WikiCatalogHit hit, CancellationToken token)
    {
        if (hit.PageId <= 0)
        {
            IReadOnlyList<WikiCatalogHit> matches = await SearchWikiAsync(hit.Title, "zh", token);
            hit = matches.FirstOrDefault(candidate => candidate.Title.Equals(hit.Title, StringComparison.OrdinalIgnoreCase))
                ?? matches.FirstOrDefault()
                ?? throw new InvalidDataException("本机能识别该物品，但官方 Wiki 暂无可读取的对应页面。");
        }
        string language = hit.Language == "en" ? "en" : "zh";
        string path = Path.Combine(_directory, language + "-" + hit.PageId + ".json");
        if (File.Exists(path))
        {
            try
            {
                WikiArticleCache? cached = JsonSerializer.Deserialize<WikiArticleCache>(await File.ReadAllTextAsync(path, token));
                if (cached is not null && DateTime.UtcNow - cached.RetrievedUtc < TimeSpan.FromDays(14))
                    return new WikiCatalogArticle(cached.Title, cached.Text, language, true);
            }
            catch (Exception) when (!token.IsCancellationRequested) { }
        }

        string api = language == "en" ? EnglishApi : ChineseApi;
        string url = api + "?action=parse&pageid=" + hit.PageId
            + "&prop=text&disableeditsection=1&format=json&formatversion=2";
        using JsonDocument document = await GetJsonAsync(url, token);
        if (!document.RootElement.TryGetProperty("parse", out JsonElement parsed))
            throw new InvalidDataException("官方 Wiki 没有返回条目正文。");
        string title = parsed.GetProperty("title").GetString() ?? hit.Title;
        string html = parsed.GetProperty("text").GetString() ?? "";
        string text = CleanArticleHtml(html);
        if (text.Length == 0)
            throw new InvalidDataException("官方 Wiki 条目正文为空。");
        WikiArticleCache snapshot = new(title, text, DateTime.UtcNow);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot), token);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return new WikiCatalogArticle(title, text, language, false);
    }

    private async Task<IReadOnlyList<WikiCatalogHit>> SearchWikiAsync(string query, string language, CancellationToken token)
    {
        string api = language == "en" ? EnglishApi : ChineseApi;
        string url = api + "?action=query&list=search&srnamespace=0&srlimit=35&srsearch="
            + Uri.EscapeDataString(query) + "&format=json&formatversion=2";
        using JsonDocument document = await GetJsonAsync(url, token);
        if (!document.RootElement.TryGetProperty("query", out JsonElement result)
            || !result.TryGetProperty("search", out JsonElement hits))
            return Array.Empty<WikiCatalogHit>();
        List<WikiCatalogHit> pages = new();
        foreach (JsonElement page in hits.EnumerateArray())
            pages.Add(new WikiCatalogHit(
                page.GetProperty("title").GetString() ?? "",
                page.GetProperty("pageid").GetInt32(),
                language,
                CleanSnippet(page.TryGetProperty("snippet", out JsonElement snippet) ? snippet.GetString() ?? "" : "")));
        return pages;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
    {
        using HttpResponseMessage response = await _http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
    }

    private void LoadIndex()
    {
        foreach (string language in new[] { "zh", "en" })
        {
            string path = Path.Combine(_directory, "titles-" + language + ".json");
            if (!File.Exists(path)) continue;
            try
            {
                WikiTitleIndex? snapshot = JsonSerializer.Deserialize<WikiTitleIndex>(File.ReadAllText(path));
                if (snapshot?.Pages is not { Count: > 0 }) continue;
                if (language == "zh")
                {
                    _titles = snapshot.Pages;
                    _indexRetrievedUtc = snapshot.RetrievedUtc;
                }
                else
                {
                    _englishTitles = snapshot.Pages;
                    _englishIndexRetrievedUtc = snapshot.RetrievedUtc;
                }
            }
            catch { }
        }
    }

    private static int Score(string title, string query) => title.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
        : title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1
        : title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2
        : 3;

    private static string CleanSnippet(string html) => Whitespace.Replace(
        WebUtility.HtmlDecode(Tags.Replace(html, " ")).Trim(), " ");

    internal static string CleanArticleHtml(string html)
    {
        string value = UnwantedBlocks.Replace(html, " ");
        value = Regex.Replace(value, @"<br\s*/?>|</p>|</div>|</li>|</tr>|</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"</t[dh]>", " | ", RegexOptions.IgnoreCase);
        value = WebUtility.HtmlDecode(Tags.Replace(value, " ")).Replace('\r', '\n');
        value = Whitespace.Replace(value, " ");
        return string.Join("\n", value.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _syncGate.Dispose();
    }

    private sealed record WikiArticleCache(string Title, string Text, DateTime RetrievedUtc);
}
