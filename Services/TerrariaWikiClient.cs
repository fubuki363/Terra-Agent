#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TerraWikiAI.Services;

internal sealed class TerrariaWikiClient : IDisposable
{
    private static readonly Regex DangerousBlocks = new(
        @"<(script|style|noscript)[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HorizontalWhitespace = new(@"[ \t\f\v]+", RegexOptions.Compiled);
    private static readonly Regex ExcessBlankLines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex ChineseQuestionWords = new(
        @"请问|告诉我|怎么样|怎么|怎样|如何|哪里|在哪(?:里)?|是什么|什么|为什么|为何|能不能|可不可以|可以|应该|之后|以前|获得|获取|得到|找到|制作|合成|掉落|召唤|打法|打败|击败|需要|用途|用来|方法|条件|配方|多少|概率|几率|的|是|吗|呢|啊|呀",
        RegexOptions.Compiled);
    private static readonly Regex SearchPunctuation = new(@"[，。！？、；：,.!?;:\[\]()（）{}<>《》\""']+", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly WikiCache _cache;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TerrariaWikiClient(WikiCache cache)
    {
        _cache = cache;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(ClientIdentity.UserAgent);
    }

    public async Task<IReadOnlyList<WikiDocument>> SearchAsync(
        string question,
        string preferredLanguage,
        int limit,
        int charactersPerPage,
        TimeSpan cacheLifetime,
        CancellationToken token)
    {
        IReadOnlyList<WikiDocument> documents = await SearchLanguageAsync(
            question, preferredLanguage, limit, charactersPerPage, cacheLifetime, token);

        if (documents.Count > 0 || preferredLanguage == "en")
            return documents;

        return await SearchLanguageAsync(question, "en", limit, charactersPerPage, cacheLifetime, token);
    }

    private async Task<IReadOnlyList<WikiDocument>> SearchLanguageAsync(
        string question,
        string language,
        int limit,
        int charactersPerPage,
        TimeSpan cacheLifetime,
        CancellationToken token)
    {
        string apiBase = language == "zh"
            ? "https://terraria.wiki.gg/zh/api.php"
            : "https://terraria.wiki.gg/api.php";
        string wikiBase = language == "zh"
            ? "https://terraria.wiki.gg/zh/wiki/"
            : "https://terraria.wiki.gg/wiki/";

        string searchQuery = BuildSearchQuery(question, language);
        string searchUrl = apiBase
            + "?action=query&list=search&srnamespace=0&srlimit=" + limit
            + "&srsearch=" + Uri.EscapeDataString(searchQuery)
            + "&format=json&formatversion=2&origin=*";

        using HttpResponseMessage searchResponse = await _httpClient.GetAsync(searchUrl, token);
        searchResponse.EnsureSuccessStatusCode();
        string searchJson = await searchResponse.Content.ReadAsStringAsync(token);
        WikiSearchRoot? root = JsonSerializer.Deserialize<WikiSearchRoot>(searchJson, _jsonOptions);
        List<WikiSearchHit> hits = root?.Query?.Search.Take(limit).ToList() ?? new();

        Task<WikiDocument?>[] fetches = hits
            .Select(hit => GetDocumentAsync(apiBase, wikiBase, language, hit, charactersPerPage, cacheLifetime, token))
            .ToArray();
        WikiDocument?[] results = await Task.WhenAll(fetches);
        return results.Where(document => document is not null && document.Text.Length > 0).Cast<WikiDocument>().ToList();
    }

    private async Task<WikiDocument?> GetDocumentAsync(
        string apiBase,
        string wikiBase,
        string language,
        WikiSearchHit hit,
        int charactersPerPage,
        TimeSpan cacheLifetime,
        CancellationToken token)
    {
        WikiDocument? cached = await _cache.TryReadAsync(language, hit.PageId, cacheLifetime, token);
        if (cached is not null)
            return cached;

        try
        {
            string parseUrl = apiBase
                + "?action=parse&pageid=" + hit.PageId
                + "&prop=text%7Crevid%7Cdisplaytitle&disableeditsection=1&format=json&formatversion=2&origin=*";
            using HttpResponseMessage response = await _httpClient.GetAsync(parseUrl, token);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(token);
            WikiParseRoot? parseRoot = JsonSerializer.Deserialize<WikiParseRoot>(json, _jsonOptions);
            WikiParsePage? page = parseRoot?.Parse;

            string text = CleanHtml(page?.Text ?? hit.Snippet, charactersPerPage);
            if (text.Length == 0)
                return null;

            WikiDocument document = new()
            {
                PageId = hit.PageId,
                Title = page?.Title ?? hit.Title,
                Url = wikiBase + Uri.EscapeDataString(hit.Title.Replace(' ', '_')),
                Text = text,
                RetrievedUtc = DateTime.UtcNow,
                SourceType = "official_wiki",
                SourceName = "官方 Terraria Wiki",
                Reliability = "官方资料"
            };
            await _cache.WriteAsync(language, document, token);
            return document;
        }
        catch when (!token.IsCancellationRequested)
        {
            string snippet = CleanHtml(hit.Snippet, charactersPerPage);
            return snippet.Length == 0
                ? null
                : new WikiDocument
                {
                    PageId = hit.PageId,
                    Title = hit.Title,
                    Url = wikiBase + Uri.EscapeDataString(hit.Title.Replace(' ', '_')),
                    Text = snippet,
                    RetrievedUtc = DateTime.UtcNow,
                    SourceType = "official_wiki",
                    SourceName = "官方 Terraria Wiki",
                    Reliability = "官方资料"
                };
        }
    }

    private static string CleanHtml(string html, int maximumCharacters)
    {
        string value = DangerousBlocks.Replace(html, " ");
        value = Regex.Replace(value, @"<br\s*/?>|</p>|</div>|</li>|</tr>|</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"</t[dh]>", " | ", RegexOptions.IgnoreCase);
        value = Tags.Replace(value, " ");
        value = WebUtility.HtmlDecode(value).Replace('\r', '\n');
        value = HorizontalWhitespace.Replace(value, " ");

        string[] lines = value.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        value = string.Join("\n", lines.Where(line => line.Length > 1));
        value = ExcessBlankLines.Replace(value, "\n\n").Trim();

        if (value.Length <= maximumCharacters)
            return value;

        int cut = value.LastIndexOf('\n', maximumCharacters);
        if (cut < maximumCharacters / 2)
            cut = maximumCharacters;
        return value[..cut] + "\n（页面内容已截断）";
    }

    private static string BuildSearchQuery(string question, string language)
    {
        string value = SearchPunctuation.Replace(question, " ");
        if (language == "zh")
            value = ChineseQuestionWords.Replace(value, " ");
        value = HorizontalWhitespace.Replace(value, " ").Trim();
        return value.Length >= 2 ? value : question.Trim();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
