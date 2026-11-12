using Dapper;

namespace WorkLogger.Infrastructure.Data;

/// <summary>Agent'ın canlı durumu ve Desktop'tan gelen komutlar için küçük anahtar-değer tablosu.</summary>
public sealed class AgentStateRepository(Database db)
{
    public const string Heartbeat = "heartbeat";
    public const string ProcessId = "pid";
    public const string Version = "version";
    public const string CurrentApp = "current_app";
    public const string CurrentTitle = "current_title";
    public const string CurrentSince = "current_since_ms";
    public const string CurrentIdle = "current_idle";
    public const string PauseRequested = "pause_requested";
    public const string LastGitScan = "last_git_scan_ms";
    public const string ShutdownRequested = "shutdown_requested";

    public void Set(IReadOnlyDictionary<string, string?> values)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var (key, value) in values)
        {
            c.Execute(
                "INSERT INTO agent_state (key, value, updated_ms) VALUES (@key, @value, @now) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_ms = excluded.updated_ms",
                new { key, value, now },
                tx);
        }
        tx.Commit();
    }

    public void Set(string key, string? value) => Set(new Dictionary<string, string?> { [key] = value });

    public Dictionary<string, string?> GetAll()
    {
        using var c = db.Open();
        return c.Query<StateRow>("SELECT key AS Key, value AS Value FROM agent_state")
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
    }

    public string? Get(string key)
    {
        using var c = db.Open();
        return c.ExecuteScalar<string?>("SELECT value FROM agent_state WHERE key = @key", new { key });
    }

    private sealed class StateRow
    {
        public string Key { get; set; } = "";
        public string? Value { get; set; }
    }
}

/// <summary>Desktop'un gösterdiği canlı durum.</summary>
public sealed record AgentSnapshot(
    bool Running,
    bool Paused,
    DateTimeOffset? Heartbeat,
    string? App,
    string? Title,
    bool Idle,
    DateTimeOffset? CurrentSince)
{
    public static AgentSnapshot From(IReadOnlyDictionary<string, string?> state, TimeSpan staleAfter)
    {
        static DateTimeOffset? Ms(string? value) =>
            long.TryParse(value, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime() : null;

        var heartbeat = Ms(state.GetValueOrDefault(AgentStateRepository.Heartbeat));
        var running = heartbeat is { } hb && DateTimeOffset.Now - hb < staleAfter;
        return new AgentSnapshot(
            running,
            state.GetValueOrDefault(AgentStateRepository.PauseRequested) == "1",
            heartbeat,
            state.GetValueOrDefault(AgentStateRepository.CurrentApp),
            state.GetValueOrDefault(AgentStateRepository.CurrentTitle),
            state.GetValueOrDefault(AgentStateRepository.CurrentIdle) == "1",
            Ms(state.GetValueOrDefault(AgentStateRepository.CurrentSince)));
    }
}
