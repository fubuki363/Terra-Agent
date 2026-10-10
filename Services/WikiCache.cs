#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerraWikiAI.Services;

internal sealed class WikiCache
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public WikiCache(string dataDirectory)
    {
        _directory = Path.Combine(dataDirectory, "wiki-cache-v1");
        Directory.CreateDirectory(_directory);
    }

    public int Count => Directory.Exists(_directory)
        ? Directory.GetFiles(_directory, "*.json", SearchOption.TopDirectoryOnly).Length
        : 0;

    public async Task<WikiDocument?> TryReadAsync(string language, int pageId, TimeSpan lifetime, CancellationToken token)
    {
        string path = GetPath(language, pageId);
        if (!File.Exists(path))
            return null;

        try
        {
            string json = await File.ReadAllTextAsync(path, token);
            WikiDocument? document = JsonSerializer.Deserialize<WikiDocument>(json, _jsonOptions);
            if (document is not null && DateTime.UtcNow - document.RetrievedUtc <= lifetime)
                return document;
        }
        catch
        {
            // A corrupt/stale cache is simply refreshed from the wiki.
        }

        return null;
    }

    public async Task WriteAsync(string language, WikiDocument document, CancellationToken token)
    {
        string path = GetPath(language, document.PageId);
        // Smart search may fetch the same page through several related queries at
        // once. A unique temporary name prevents those writes from colliding.
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string json = JsonSerializer.Serialize(document, _jsonOptions);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, token);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public int Clear()
    {
        if (!Directory.Exists(_directory))
            return 0;

        int count = 0;
        foreach (string file in Directory.GetFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
            count++;
        }

        return count;
    }

    private string GetPath(string language, int pageId)
    {
        string safeLanguage = language == "en" ? "en" : "zh";
        return Path.Combine(_directory, $"{safeLanguage}-{pageId}.json");
    }
}
