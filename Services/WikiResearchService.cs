#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TerraWikiAI.Services;

internal sealed class WikiResearchService
{
    private readonly TerrariaWikiClient _wikiClient;
    private readonly DeepSeekClient _deepSeekClient;
    private readonly CommunitySearchClient _communityClient = new();

    public WikiResearchService(TerrariaWikiClient wikiClient, DeepSeekClient deepSeekClient)
    {
        _wikiClient = wikiClient;
        _deepSeekClient = deepSeekClient;
    }

    public async Task<IReadOnlyList<WikiDocument>> SearchAsync(
        string apiKey,
        string question,
        string? previousQuestion,
        string preferredLanguage,
        int maximumDocuments,
        int charactersPerPage,
        TimeSpan cacheLifetime,
        CancellationToken token)
    {
        List<string> queries = new();
        // Deterministic intent matches go first so broad model-generated terms
        // cannot crowd out a known high-confidence page.
        AddIntentFallbacks(queries, question, preferredLanguage);
        try
        {
            queries.AddRange(await _deepSeekClient.PlanWikiSearchQueriesAsync(
                apiKey, question, previousQuestion, token));
        }
        catch when (!token.IsCancellationRequested)
        {
            // The answer request can still succeed when query planning is
            // unavailable, so continue with deterministic fallback terms.
        }

        queries.Add(string.IsNullOrWhiteSpace(previousQuestion)
            ? question
            : previousQuestion + " " + question);

        string[] uniqueQueries = queries
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        int communityMaximum = Math.Clamp(maximumDocuments / 3, 2, 3);
        int wikiMaximum = Math.Max(2, maximumDocuments - communityMaximum);
        int pagesPerQuery = Math.Clamp(wikiMaximum / 2, 2, 3);
        Task<IReadOnlyList<WikiDocument>>[] searches = uniqueQueries
            .Select(query => _wikiClient.SearchAsync(
                query,
                preferredLanguage,
                pagesPerQuery,
                charactersPerPage,
                cacheLifetime,
                token))
            .ToArray();

        List<string> communityQueries = new();
        string? firstEnglish = uniqueQueries.FirstOrDefault(query =>
            ContainsLatinLetter(query) && query.Contains(' '))
            ?? uniqueQueries.FirstOrDefault(ContainsLatinLetter);
        if (firstEnglish is not null)
            communityQueries.Add(firstEnglish);
        string? firstOther = uniqueQueries.FirstOrDefault(query => !communityQueries.Contains(query, StringComparer.OrdinalIgnoreCase));
        if (firstOther is not null)
            communityQueries.Add(firstOther);
        Task<IReadOnlyList<WikiDocument>> communitySearch = _communityClient.SearchAsync(
            communityQueries,
            communityMaximum,
            Math.Min(charactersPerPage, 3200),
            token);

        IReadOnlyList<WikiDocument>[] batches = await Task.WhenAll(searches);
        IReadOnlyList<WikiDocument> wikiDocuments = batches
            .SelectMany(batch => batch)
            .GroupBy(document => document.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(wikiMaximum)
            .ToList();
        IReadOnlyList<WikiDocument> communityDocuments = await communitySearch;
        return wikiDocuments
            .Concat(communityDocuments)
            .GroupBy(document => document.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(maximumDocuments)
            .ToList();
    }

    private static void AddIntentFallbacks(List<string> queries, string question, string language)
    {
        string lower = question.ToLowerInvariant();
        bool asksAboutBoss = lower.Contains("boss") || question.Contains("首领") || question.Contains("头目");
        bool asksAboutFirst = question.Contains("第一") || question.Contains("首个") || question.Contains("最先")
            || question.Contains("先打") || question.Contains("新手") || question.Contains("前期")
            || lower.Contains("first");

        if (!asksAboutBoss || !asksAboutFirst)
            return;

        if (language == "en")
        {
            queries.Add("Eye of Cthulhu");
            queries.Add("Guide:Eye of Cthulhu strategies");
            queries.Add("Bosses");
            queries.Add("Suspicious Looking Eye");
        }
        else
        {
            queries.Add("克苏鲁之眼");
            queries.Add("指南:克苏鲁之眼攻略");
            queries.Add("Boss");
            queries.Add("可疑眼球");
            queries.Add("Eye of Cthulhu strategy");
        }
    }

    private static bool ContainsLatinLetter(string value) => value.Any(character =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
}
