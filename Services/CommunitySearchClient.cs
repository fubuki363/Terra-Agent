#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TerraWikiAI.Services;

internal sealed class CommunitySearchClient
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex ForumLinks = new(
        "href=[\\\"'](?<url>/index\\.php\\?threads/[^\\\"'#\\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SteamLinks = new(
        "href=\\\"(?<url>https://steamcommunity\\.com/app/105600/discussions/\\d+/\\d+/)\\\"[^>]*>(?<title>[\\s\\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ForumPosts = new(
        "<article[^>]*class=\\\"[^\\\"]*message[^\\\"]*\\\"[^>]*>(?<text>[\\s\\S]*?)</article>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SteamPosts = new(
        "<(?:div|span)[^>]*class=\\\"[^\\\"]*(?:forum_op|commentthread_comment_text)[^\\\"]*\\\"[^>]*>(?<text>[\\s\\S]*?)</(?:div|span)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DangerousBlocks = new(
        @"<(script|style|noscript|svg)[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"[ \t\f\v]+", RegexOptions.Compiled);

    public async Task<IReadOnlyList<WikiDocument>> SearchAsync(
        IReadOnlyList<string> queries,
        int maximumDocuments,
        int charactersPerPage,
        CancellationToken token)
    {
        if (maximumDocuments <= 0 || queries.Count == 0)
            return Array.Empty<WikiDocument>();

        string[] selectedQueries = queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Select(query => query.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        Task<IReadOnlyList<SearchHit>> forumSearch = SearchWithFallbackAsync(
            selectedQueries, SearchOfficialForumAsync, token);
        Task<IReadOnlyList<SearchHit>> steamSearch = SearchWithFallbackAsync(
            selectedQueries, SearchSteamCommunityAsync, token);
        IReadOnlyList<SearchHit>[] batches = await Task.WhenAll(forumSearch, steamSearch);

        List<SearchHit> hits = new();
        int largestBatch = batches.Max(batch => batch.Count);
        for (int index = 0; index < largestBatch; index++)
        {
            foreach (IReadOnlyList<SearchHit> batch in batches)
            {
                if (index < batch.Count)
                    hits.Add(batch[index]);
            }
        }

        List<WikiDocument> documents = new();
        await Task.Delay(200, token);
        foreach (SearchHit hit in hits
            .GroupBy(value => value.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(maximumDocuments * 2))
        {
            WikiDocument? document = await FetchDocumentAsync(hit, charactersPerPage, token);
            if (document is not null && document.Text.Length > 40)
                documents.Add(document);
            if (documents.Count >= maximumDocuments)
                break;
            await Task.Delay(100, token);
        }
        return documents;
    }

    private static async Task<IReadOnlyList<SearchHit>> SearchWithFallbackAsync(
        IReadOnlyList<string> queries,
        Func<string, CancellationToken, Task<IReadOnlyList<SearchHit>>> search,
        CancellationToken token)
    {
        foreach (string query in queries)
        {
            IReadOnlyList<SearchHit> hits = await search(query, token);
            if (hits.Count > 0)
                return hits;
        }
        return Array.Empty<SearchHit>();
    }

    private static async Task<IReadOnlyList<SearchHit>> SearchOfficialForumAsync(string query, CancellationToken token)
    {
        try
        {
            string url = "https://forums.terraria.org/index.php?search/search&keywords=" + Uri.EscapeDataString(query);
            string html = await GetStringAsync(url, token);
            IReadOnlyList<SearchHit> hits = ForumLinks.Matches(html)
                .Cast<Match>()
                .Select(match =>
                {
                    string relative = WebUtility.HtmlDecode(match.Groups["url"].Value);
                    string absolute = new Uri(new Uri("https://forums.terraria.org/"), relative).AbsoluteUri;
                    return new SearchHit(
                        absolute,
                        TitleFromForumUrl(absolute),
                        "terraria_forum",
                        "Terraria 官方社区论坛",
                        "玩家经验/讨论");
                })
                .Where(hit => hit.Url.Contains("?threads/", StringComparison.OrdinalIgnoreCase))
                .GroupBy(hit => hit.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(3)
                .ToList();
            Diagnostic($"forum search chars={html.Length} threadLinks={html.Contains("threads/", StringComparison.OrdinalIgnoreCase)} hits={hits.Count} title={GetHtmlTitle(html)}");
            return hits;
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            Diagnostic("forum search failed: " + exception.Message);
            return Array.Empty<SearchHit>();
        }
    }

    private static async Task<IReadOnlyList<SearchHit>> SearchSteamCommunityAsync(string query, CancellationToken token)
    {
        try
        {
            string url = "https://steamcommunity.com/app/105600/discussions/search/?q=" + Uri.EscapeDataString(query);
            string html = await GetStringAsync(url, token);
            return SteamLinks.Matches(html)
                .Cast<Match>()
                .Select(match => new SearchHit(
                    WebUtility.HtmlDecode(match.Groups["url"].Value),
                    CleanInline(match.Groups["title"].Value, "Steam 社区讨论"),
                    "steam_community",
                    "Steam Terraria 社区",
                    "玩家经验/讨论"))
                .Where(hit => IsAllowedCommunityUrl(hit.Url))
                .GroupBy(hit => hit.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(3)
                .ToList();
        }
        catch when (!token.IsCancellationRequested)
        {
            return Array.Empty<SearchHit>();
        }
    }

    private static async Task<WikiDocument?> FetchDocumentAsync(SearchHit hit, int charactersPerPage, CancellationToken token)
    {
        try
        {
            if (!IsAllowedCommunityUrl(hit.Url))
                return null;
            string html = await GetStringAsync(hit.Url, token);
            Regex extractor = hit.SourceType == "terraria_forum" ? ForumPosts : SteamPosts;
            string selected = string.Join("\n", extractor.Matches(html)
                .Cast<Match>()
                .Take(12)
                .Select(match => match.Groups["text"].Value));
            if (string.IsNullOrWhiteSpace(selected))
                selected = html;
            string text = CleanHtml(selected, charactersPerPage);
            if (text.Length < 40)
                return null;
            return new WikiDocument
            {
                PageId = StablePageId(hit.Url),
                Title = hit.Title,
                Url = hit.Url,
                Text = text,
                RetrievedUtc = DateTime.UtcNow,
                SourceType = hit.SourceType,
                SourceName = hit.SourceName,
                Reliability = hit.Reliability
            };
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            Diagnostic(hit.SourceType + " fetch failed: " + exception.Message);
            return null;
        }
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken token)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 3_000_000)
            throw new InvalidOperationException("社区页面过大，已跳过。");
        return await response.Content.ReadAsStringAsync(token);
    }

    private static string CleanInline(string html, string fallback)
    {
        string value = Tags.Replace(html, " ");
        value = WebUtility.HtmlDecode(value);
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value.Length == 0 ? fallback : value.Length <= 160 ? value : value[..160] + "…";
    }

    private static string TitleFromForumUrl(string url)
    {
        int marker = url.IndexOf("?threads/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return "Terraria 论坛讨论";
        string slug = url[(marker + 9)..].Split('/')[0];
        int finalDot = slug.LastIndexOf('.');
        if (finalDot > 0)
            slug = slug[..finalDot];
        string title = WebUtility.UrlDecode(slug).Replace('-', ' ').Trim();
        return string.IsNullOrWhiteSpace(title) ? "Terraria 论坛讨论" : title;
    }

    private static string GetHtmlTitle(string html)
    {
        Match match = Regex.Match(html, "<title[^>]*>(?<title>[\\s\\S]*?)</title>", RegexOptions.IgnoreCase);
        return match.Success ? CleanInline(match.Groups["title"].Value, "unknown") : "unknown";
    }

    private static string CleanHtml(string html, int maximumCharacters)
    {
        string value = DangerousBlocks.Replace(html, " ");
        value = Regex.Replace(value, @"<br\s*/?>|</p>|</div>|</li>|</article>|</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        value = Tags.Replace(value, " ");
        value = WebUtility.HtmlDecode(value).Replace('\r', '\n');
        value = Whitespace.Replace(value, " ");
        value = string.Join("\n", value.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length > 1));
        value = Regex.Replace(value, @"\n{3,}", "\n\n").Trim();
        if (value.Length <= maximumCharacters)
            return value;
        int cut = value.LastIndexOf('\n', maximumCharacters);
        if (cut < maximumCharacters / 2) cut = maximumCharacters;
        return value[..cut] + "\n（社区页面内容已截断）";
    }

    private static bool IsAllowedCommunityUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return uri.Host.Equals("forums.terraria.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase);
    }

    private static int StablePageId(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value)
                hash = hash * 31 + character;
            return hash;
        }
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ClientIdentity.UserAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        return client;
    }

    private static void Diagnostic(string message)
    {
        if (Environment.GetEnvironmentVariable("TERRAWIKI_DIAGNOSTICS") == "1")
            Console.Error.WriteLine("[community] " + message);
    }

    private sealed record SearchHit(string Url, string Title, string SourceType, string SourceName, string Reliability);
}
