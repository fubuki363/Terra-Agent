#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TerraWikiAI.Services;

internal sealed class UsageTracker
{
    private sealed class UsageData
    {
        public int SchemaVersion { get; set; } = 1;
        public long LifetimeQuestions { get; set; }
        public long LifetimeCalls { get; set; }
        public long LifetimePromptTokens { get; set; }
        public long LifetimeCompletionTokens { get; set; }
        public long LifetimeCacheHitTokens { get; set; }
        public Dictionary<string, DailyUsage> Days { get; set; } = new();
    }

    private sealed class DailyUsage
    {
        public long Questions { get; set; }
        public long Calls { get; set; }
        public long PromptTokens { get; set; }
        public long CompletionTokens { get; set; }
        public long CacheHitTokens { get; set; }
        public long TotalTokens => PromptTokens + CompletionTokens;
    }

    private readonly string _path;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
    private UsageData _data;

    public UsageTracker(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "usage-v1.json");
        _data = Load();
    }

    public void RecordApiCall(DeepSeekUsage usage)
    {
        lock (_sync)
        {
            DailyUsage today = Today;
            today.Calls++;
            today.PromptTokens += usage.PromptTokens;
            today.CompletionTokens += usage.CompletionTokens;
            today.CacheHitTokens += usage.PromptCacheHitTokens;
            _data.LifetimeCalls++;
            _data.LifetimePromptTokens += usage.PromptTokens;
            _data.LifetimeCompletionTokens += usage.CompletionTokens;
            _data.LifetimeCacheHitTokens += usage.PromptCacheHitTokens;
            PruneAndSave();
        }
    }

    public void RecordQuestion()
    {
        lock (_sync)
        {
            Today.Questions++;
            _data.LifetimeQuestions++;
            PruneAndSave();
        }
    }

    public string GetSummary()
    {
        lock (_sync)
        {
            DailyUsage today = Today;
            long lifetimeTotal = _data.LifetimePromptTokens + _data.LifetimeCompletionTokens;
            double cacheRate = today.PromptTokens == 0 ? 0 : today.CacheHitTokens * 100d / today.PromptTokens;
            return $"今日：{today.Questions} 个问题 / {today.Calls} 次 API / {today.TotalTokens:N0} Token"
                 + $"（输入 {today.PromptTokens:N0}，输出 {today.CompletionTokens:N0}，缓存命中 {cacheRate:F1}%）；"
                 + $"累计：{_data.LifetimeQuestions:N0} 个问题 / {lifetimeTotal:N0} Token。";
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _data = new UsageData();
            Save();
        }
    }

    private DailyUsage Today
    {
        get
        {
            string key = DateTime.Now.ToString("yyyy-MM-dd");
            if (!_data.Days.TryGetValue(key, out DailyUsage? usage))
            {
                usage = new DailyUsage();
                _data.Days[key] = usage;
            }
            return usage;
        }
    }

    private UsageData Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<UsageData>(File.ReadAllText(_path), _jsonOptions) ?? new UsageData();
        }
        catch
        {
        }
        return new UsageData();
    }

    private void PruneAndSave()
    {
        DateTime cutoff = DateTime.Today.AddDays(-90);
        foreach (string key in _data.Days.Keys.ToList())
        {
            if (DateTime.TryParse(key, out DateTime date) && date < cutoff)
                _data.Days.Remove(key);
        }
        Save();
    }

    private void Save()
    {
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_data, _jsonOptions));
        File.Move(temporaryPath, _path, true);
    }
}
