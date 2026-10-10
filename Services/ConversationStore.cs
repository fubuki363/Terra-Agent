#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TerraWikiAI.Services;

internal sealed class ConversationStore
{
    private sealed class HistoryData
    {
        public int SchemaVersion { get; set; } = 1;
        public string ActiveSessionId { get; set; } = "";
        public List<ConversationSession> Sessions { get; set; } = new();
    }

    internal sealed class ConversationSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "新会话";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public List<ConversationTurn> Turns { get; set; } = new();
    }

    private readonly string _path;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
    private HistoryData _data;

    public ConversationStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "history-v1.json");
        _data = Load();
        EnsureActiveSession();
    }

    public string ActiveSessionId
    {
        get { lock (_sync) return ActiveSession.Id[..8]; }
    }

    public int SessionCount
    {
        get { lock (_sync) return _data.Sessions.Count; }
    }

    public int ActiveTurnCount
    {
        get { lock (_sync) return ActiveSession.Turns.Count; }
    }

    public IReadOnlyList<ConversationTurn> GetContext(int maximumTurns, int maximumCharacters)
    {
        lock (_sync)
        {
            List<ConversationTurn> selected = new();
            int characters = 0;
            foreach (ConversationTurn turn in ActiveSession.Turns.AsEnumerable().Reverse())
            {
                int length = turn.Question.Length + turn.Answer.Length;
                if (selected.Count >= maximumTurns || (selected.Count > 0 && characters + length > maximumCharacters))
                    break;
                selected.Add(turn);
                characters += length;
            }
            selected.Reverse();
            return selected;
        }
    }

    public IReadOnlyList<ConversationTurn> GetRecent(int maximumTurns)
    {
        lock (_sync)
            return ActiveSession.Turns.TakeLast(Math.Max(1, maximumTurns)).ToList();
    }

    public IReadOnlyList<ConversationSession> ListSessions(int maximumSessions)
    {
        lock (_sync)
            return _data.Sessions.OrderByDescending(session => session.UpdatedUtc).Take(maximumSessions).ToList();
    }

    public void AddTurn(
        string question,
        string answer,
        IEnumerable<string> sources,
        bool persist,
        int retentionDays,
        IEnumerable<ConversationToolTrace>? toolTraces = null,
        IEnumerable<ConversationAgentTrace>? agentTraces = null,
        string? reasoningContent = null)
    {
        lock (_sync)
        {
            ConversationSession session = ActiveSession;
            if (session.Turns.Count == 0)
                session.Title = MakeTitle(question);
            session.Turns.Add(new ConversationTurn
            {
                Question = question,
                Answer = answer,
                ReasoningContent = reasoningContent ?? "",
                Sources = sources.Take(8).ToList(),
                ToolTraces = toolTraces?.Take(8).ToList() ?? new List<ConversationToolTrace>(),
                AgentTraces = agentTraces?.Take(64).ToList() ?? new List<ConversationAgentTrace>(),
                CreatedUtc = DateTime.UtcNow
            });
            session.UpdatedUtc = DateTime.UtcNow;
            Prune(retentionDays);
            if (persist)
                Save();
        }
    }

    public string NewSession(bool persist, int retentionDays)
    {
        lock (_sync)
        {
            ConversationSession session = new();
            _data.Sessions.Add(session);
            _data.ActiveSessionId = session.Id;
            Prune(retentionDays);
            if (persist)
                Save();
            return session.Id[..8];
        }
    }

    public bool Resume(string idPrefix, bool persist)
    {
        lock (_sync)
        {
            List<ConversationSession> matches = _data.Sessions
                .Where(session => session.Id.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
                return false;
            _data.ActiveSessionId = matches[0].Id;
            if (persist)
                Save();
            return true;
        }
    }

    public bool DeleteSession(string idPrefix, bool persist)
    {
        lock (_sync)
        {
            List<ConversationSession> matches = _data.Sessions
                .Where(session => session.Id.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
                return false;

            ConversationSession target = matches[0];
            _data.Sessions.Remove(target);
            if (_data.Sessions.Count == 0)
                _data.Sessions.Add(new ConversationSession());
            if (_data.ActiveSessionId == target.Id)
                _data.ActiveSessionId = _data.Sessions.OrderByDescending(session => session.UpdatedUtc).First().Id;
            if (persist)
                Save();
            return true;
        }
    }

    public int Clear(bool persist)
    {
        lock (_sync)
        {
            int turns = _data.Sessions.Sum(session => session.Turns.Count);
            _data = new HistoryData();
            EnsureActiveSession();
            if (persist)
                Save();
            else if (File.Exists(_path))
                File.Delete(_path);
            return turns;
        }
    }

    private ConversationSession ActiveSession =>
        _data.Sessions.First(session => session.Id == _data.ActiveSessionId);

    private HistoryData Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<HistoryData>(File.ReadAllText(_path), _jsonOptions) ?? new HistoryData();
        }
        catch
        {
        }
        return new HistoryData();
    }

    private void EnsureActiveSession()
    {
        if (_data.Sessions.Count == 0)
            _data.Sessions.Add(new ConversationSession());
        if (!_data.Sessions.Any(session => session.Id == _data.ActiveSessionId))
            _data.ActiveSessionId = _data.Sessions[^1].Id;
    }

    private void Prune(int retentionDays)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
        _data.Sessions.RemoveAll(session => session.Id != _data.ActiveSessionId && session.UpdatedUtc < cutoff);
        foreach (ConversationSession session in _data.Sessions)
            session.Turns = session.Turns.TakeLast(100).ToList();
        _data.Sessions = _data.Sessions.OrderByDescending(session => session.UpdatedUtc).Take(30).ToList();
        EnsureActiveSession();
    }

    private void Save()
    {
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_data, _jsonOptions));
        File.Move(temporaryPath, _path, true);
    }

    private static string MakeTitle(string question)
    {
        string value = question.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 36 ? value : value[..36] + "…";
    }
}
