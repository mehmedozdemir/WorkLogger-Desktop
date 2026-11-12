using Microsoft.Data.Sqlite;
using WorkLogger.Core.Classification;
using WorkLogger.Infrastructure.Data;
using WorkLogger.Infrastructure.Import;

namespace WorkLogger.Infrastructure.Tests;

public sealed class JournalAndImportTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 11, 12);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "worklogger-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Database _db;

    public JournalAndImportTests()
    {
        var paths = new AppPaths(_root);
        _db = new Database(paths.DatabaseFile);
        _db.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Drafts_roundtrip_and_upsert()
    {
        var journal = new JournalRepository(_db);
        journal.SaveDraft(new WorklogDraft(Day, "PAY-1", 45, "09:00", "ilk", false));
        journal.SaveDraft(new WorklogDraft(Day, "PAY-1", 60, "09:15", "güncel", false));
        journal.SaveDraft(new WorklogDraft(Day, "PAY-2", 30, "13:00", "elle", true));
        journal.SaveDraft(new WorklogDraft(Day.AddDays(1), "PAY-3", 15, "09:00", "", false));

        var drafts = journal.Drafts(Day);
        Assert.Equal(2, drafts.Count);
        Assert.Equal(new WorklogDraft(Day, "PAY-1", 60, "09:15", "güncel", false), drafts[0]);
        Assert.True(drafts[1].Manual);

        journal.DeleteDraft(Day, "PAY-2");
        Assert.Single(journal.Drafts(Day));
        journal.ClearDrafts(Day);
        Assert.Empty(journal.Drafts(Day));
        Assert.Single(journal.Drafts(Day.AddDays(1)));
    }

    [Fact]
    public void Assignments_are_per_day()
    {
        var journal = new JournalRepository(_db);
        var group = new GroupRef("chrome.exe", "Stack Overflow");
        journal.Assign(Day, group, "PAY-1");
        journal.Assign(Day, group, "PAY-2");
        Assert.Equal("PAY-2", journal.Assignments(Day)[group]);
        Assert.Empty(journal.Assignments(Day.AddDays(-1)));
        journal.Unassign(Day, group);
        Assert.Empty(journal.Assignments(Day));
    }

    [Fact]
    public void Closures_roundtrip()
    {
        var journal = new JournalRepository(_db);
        Assert.Null(journal.Closure(Day));
        journal.Close(new DayClosure(Day, DateTimeOffset.Now, 420, 480, "özet"));
        Assert.Equal(480, journal.Closure(Day)!.WorklogMinutes);
        Assert.Equal([Day], journal.ClosedDays(Day.AddDays(-7), Day));
        journal.Reopen(Day);
        Assert.Null(journal.Closure(Day));
    }

    [Fact]
    public void Legacy_import_stops_where_new_tracker_started()
    {
        // Eski (Python) veritabanı: 09:00-10:00 ve 10:30-11:30 segmentleri.
        var legacyPath = Path.Combine(_root, "legacy.db");
        var nine = new DateTimeOffset(Day.ToDateTime(new TimeOnly(9, 0)), TimeZoneInfo.Local.GetUtcOffset(Day.ToDateTime(new TimeOnly(9, 0))));
        using (var legacy = new SqliteConnection($"Data Source={legacyPath}"))
        {
            legacy.Open();
            using var cmd = legacy.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE segments (id INTEGER PRIMARY KEY, day TEXT, start_ts REAL, end_ts REAL, app TEXT, title TEXT, idle INTEGER);" +
                "CREATE TABLE commits (id INTEGER PRIMARY KEY, day TEXT, repo TEXT, sha TEXT, branch TEXT, message TEXT, ts REAL);" +
                $"INSERT INTO segments (start_ts, end_ts, app, title, idle) VALUES ({nine.ToUnixTimeSeconds()}, {nine.AddHours(1).ToUnixTimeSeconds()}, 'devenv.exe', 'PAY-1', 0);" +
                $"INSERT INTO segments (start_ts, end_ts, app, title, idle) VALUES ({nine.AddMinutes(90).ToUnixTimeSeconds()}, {nine.AddMinutes(150).ToUnixTimeSeconds()}, 'devenv.exe', 'PAY-2', 0);" +
                $"INSERT INTO commits (repo, sha, branch, message, ts) VALUES ('r', 'abc', 'main', 'm', {nine.ToUnixTimeSeconds()});";
            cmd.ExecuteNonQuery();
        }

        // Yeni izleyici 10:45'te başladı.
        var activity = new ActivityRepository(_db);
        activity.InsertSegment(nine.AddMinutes(105), nine.AddMinutes(120), "devenv.exe", "PAY-2", false);

        var importer = new LegacyTrackerImporter(_db, activity);
        var result = importer.Import(legacyPath);
        Assert.Equal(2, result.Segments);
        Assert.Equal(1, result.Commits);

        var all = activity.Segments(nine.AddHours(-1), nine.AddHours(5));
        var payTwo = all.Where(s => s.Title == "PAY-2").OrderBy(s => s.Start).ToList();
        Assert.Equal(TimeSpan.FromMinutes(15), payTwo[0].Duration); // eski veri 10:30-10:45'e kırpıldı
        Assert.Equal(TimeSpan.FromMinutes(15), payTwo[1].Duration); // yeni izleyicinin kaydı

        // Tekrar çalıştırmak çift kayıt üretmez.
        importer.Import(legacyPath);
        Assert.Equal(3, activity.Segments(nine.AddHours(-1), nine.AddHours(5)).Count);
    }
}
