using WorkLogger.Core.Model;
using WorkLogger.Core.Settings;
using WorkLogger.Infrastructure.Data;
using WorkLogger.Infrastructure.Git;

namespace WorkLogger.Infrastructure.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "worklogger-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly Database _db;

    public StorageTests()
    {
        _paths = new AppPaths(_root);
        _db = new Database(_paths.DatabaseFile);
        _db.Migrate();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Migrate_is_idempotent()
    {
        _db.Migrate();
        Assert.Equal(1, _db.SchemaVersion);
    }

    [Fact]
    public void Segment_lifecycle_roundtrip()
    {
        var repo = new ActivityRepository(_db);
        var start = DateTimeOffset.Now.AddMinutes(-10);
        var id = repo.InsertSegment(start, start, "devenv.exe", "PAY-1 x", false);
        repo.UpdateSegmentEnd(id, start.AddMinutes(5));
        repo.CloseSegment(id, start.AddMinutes(6));

        var tiny = repo.InsertSegment(start.AddMinutes(7), start.AddMinutes(7), "x.exe", "", false);
        repo.CloseSegment(tiny, start.AddMinutes(7).AddMilliseconds(300)); // <1 sn → silinir

        var segments = repo.Segments(start.AddHours(-1), start.AddHours(1));
        var s = Assert.Single(segments);
        Assert.True((s.Duration - TimeSpan.FromMinutes(6)).Duration() < TimeSpan.FromMilliseconds(5));
        Assert.Equal("PAY-1 x", s.Title);
    }

    [Fact]
    public void Commits_are_deduplicated_by_sha()
    {
        var repo = new ActivityRepository(_db);
        var commit = new CommitInfo("r", "sha1", "main", "msg", DateTimeOffset.Now, 1, 2, 3);
        Assert.True(repo.UpsertCommit(commit));
        Assert.False(repo.UpsertCommit(commit));
        var stored = Assert.Single(repo.Commits(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1)));
        Assert.Equal(3, stored.Deletions);
    }

    [Fact]
    public void Agent_state_snapshot()
    {
        var state = new AgentStateRepository(_db);
        state.Set(AgentStateRepository.Heartbeat, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        state.Set(AgentStateRepository.PauseRequested, "1");
        var snapshot = AgentSnapshot.From(state.GetAll(), TimeSpan.FromSeconds(30));
        Assert.True(snapshot.Running);
        Assert.True(snapshot.Paused);
    }

    [Fact]
    public void Settings_store_survives_corrupt_file()
    {
        var store = new SettingsStore(_paths);
        var (initial, error) = store.Load();
        Assert.Null(error);
        initial.Worklog.DailyTargetHours = 7.5;
        store.Save(initial);
        Assert.Equal(7.5, store.Load().Settings.Worklog.DailyTargetHours);

        File.WriteAllText(store.FilePath, "{ bozuk");
        var (fallback, err) = store.Load();
        Assert.NotNull(err);
        Assert.Equal(8, fallback.Worklog.DailyTargetHours);
    }

    [Fact]
    public void Settings_store_rejects_invalid_settings()
    {
        var store = new SettingsStore(_paths);
        var s = new WorkLoggerSettings();
        s.Worklog.DayEndTime = "25:99";
        Assert.Throws<InvalidOperationException>(() => store.Save(s));
    }

    [Fact]
    public void Git_log_output_is_parsed()
    {
        const string output = "\u001eabc\u001f1762934400\u001frefs/heads/feature/PAY-1\u001fPAY-1 ilk commit\n\n 3 files changed, 10 insertions(+), 2 deletions(-)\n" +
                              "\u001edef\u001f1762938000\u001frefs/remotes/origin/main\u001fsadece mesaj\n";
        var commits = GitScanner.Parse("repo", output);
        Assert.Equal(2, commits.Count);
        Assert.Equal("feature/PAY-1", commits[0].Branch);
        Assert.Equal((3, 10, 2), (commits[0].FilesChanged, commits[0].Insertions, commits[0].Deletions));
        Assert.Equal("main", commits[1].Branch);
        Assert.Equal(0, commits[1].FilesChanged);
    }
}
