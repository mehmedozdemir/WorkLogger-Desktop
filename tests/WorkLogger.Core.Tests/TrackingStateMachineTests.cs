using WorkLogger.Core.Tracking;

namespace WorkLogger.Core.Tests;

public class TrackingStateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 12)));

    private static TrackingStateMachine Machine() =>
        new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), ["LockApp.exe"]);

    private static Observation At(int seconds, string? app = "devenv.exe", string title = "PAY-1 - Odeme.sln", double idleSeconds = 0) =>
        new(T0.AddSeconds(seconds), app, title, TimeSpan.FromSeconds(idleSeconds));

    [Fact]
    public void First_observation_opens_a_segment()
    {
        var m = Machine();
        var step = m.Observe(At(0));
        Assert.Null(step.Closed);
        Assert.NotNull(step.Opened);
        Assert.Equal("devenv.exe", m.Current!.App);
    }

    [Fact]
    public void Same_window_extends_without_new_segment()
    {
        var m = Machine();
        m.Observe(At(0));
        var step = m.Observe(At(2));
        Assert.Equal(TrackingStep.None, step);
        Assert.Equal(T0.AddSeconds(2), m.Current!.LastSeen);
    }

    [Fact]
    public void Window_change_closes_previous_at_change_time()
    {
        var m = Machine();
        m.Observe(At(0));
        m.Observe(At(2));
        var step = m.Observe(At(4, "chrome.exe", "Jira"));
        Assert.Equal(T0.AddSeconds(4), step.Closed!.LastSeen);
        Assert.Equal("chrome.exe", step.Opened!.App);
    }

    [Fact]
    public void Idle_transition_is_backdated_to_last_input()
    {
        var m = Machine();
        m.Observe(At(0));
        for (var s = 2; s <= 600; s += 2)
            m.Observe(At(s, idleSeconds: Math.Max(0, s - 100)));
        // 100. saniyeden sonra girdi yok; boşta eşiği (300 sn) 400. saniyede aşılır.
        var step = m.Observe(At(602, idleSeconds: 502));
        Assert.Equal(TrackingStep.None, step); // zaten boşta segmentindeyiz
        Assert.True(m.Current!.Idle);
        Assert.Equal(T0.AddSeconds(100), m.Current.Start);
    }

    [Fact]
    public void Sleep_gap_closes_segment_at_last_seen_and_skips_gap()
    {
        var m = Machine();
        m.Observe(At(0));
        m.Observe(At(10));
        // Makine 8 saat uyudu, uyanınca aynı pencere ön planda.
        var step = m.Observe(At(10 + 8 * 3600));
        Assert.Equal(T0.AddSeconds(10), step.Closed!.LastSeen);
        Assert.Equal(T0.AddSeconds(10 + 8 * 3600), step.Opened!.Start);
    }

    [Fact]
    public void Midnight_splits_segment()
    {
        var m = Machine();
        var lateStart = T0.Date.AddHours(23).AddMinutes(59).AddSeconds(50);
        var late = new DateTimeOffset(lateStart, T0.Offset);
        m.Observe(new Observation(late, "devenv.exe", "x", TimeSpan.Zero));
        var step = m.Observe(new Observation(late.AddSeconds(20), "devenv.exe", "x", TimeSpan.Zero));
        Assert.NotNull(step.Closed);
        Assert.Equal(0, step.Closed!.LastSeen.ToLocalTime().Hour);
        Assert.Equal(step.Closed.LastSeen, step.Opened!.Start);
    }

    [Fact]
    public void Locked_and_ignored_apps_are_idle()
    {
        var m = Machine();
        m.Observe(new Observation(T0, "devenv.exe", "x", TimeSpan.Zero, Locked: true));
        Assert.True(m.Current!.Idle);
        Assert.Equal(TrackingStateMachine.LockedApp, m.Current.App);

        var m2 = Machine();
        m2.Observe(At(0, "LockApp.exe", ""));
        Assert.True(m2.Current!.Idle);
    }

    [Fact]
    public void Stop_closes_current_segment()
    {
        var m = Machine();
        m.Observe(At(0));
        m.Observe(At(6));
        var closed = m.Stop(T0.AddSeconds(7));
        Assert.Equal(T0.AddSeconds(7), closed!.LastSeen);
        Assert.Null(m.Current);
    }

    [Fact]
    public void Stop_after_long_gap_does_not_count_the_gap()
    {
        var m = Machine();
        m.Observe(At(0));
        var closed = m.Stop(T0.AddHours(3));
        Assert.Equal(T0, closed!.LastSeen);
    }
}
