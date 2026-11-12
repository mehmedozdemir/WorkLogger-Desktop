using WorkLogger.Core.Analysis;
using WorkLogger.Core.Classification;
using WorkLogger.Core.Model;
using WorkLogger.Core.Settings;
using static WorkLogger.Core.Tests.DayAnalyzerTests;

namespace WorkLogger.Core.Tests;

public class AssignmentAndRangeTests
{
    private static readonly string SoTitle = "EF Core Oracle mapping - Stack Overflow - Google Chrome";

    [Fact]
    public void Unclassified_group_suggests_neighbouring_issue()
    {
        var a = Analyzer().Analyze(Day, Segments(), Commits());
        var so = a.Unclassified.Single(u => u.Label.Contains("Stack Overflow", StringComparison.Ordinal));
        // Stack Overflow araması, PAY-1042 üzerinde çalışırken araya girmişti.
        Assert.Equal("PAY-1042", so.SuggestedKey);
        Assert.True(so.SuggestionConfidence > 0.5);
        Assert.Equal(new GroupRef("chrome.exe", "Stack Overflow"), so.Group);
    }

    [Fact]
    public void Manual_assignment_moves_time_to_issue()
    {
        var before = Analyzer().Analyze(Day, Segments(), Commits());
        var assignments = new Dictionary<GroupRef, string> { [new GroupRef("chrome.exe", "Stack Overflow")] = "PAY-1042" };
        var after = Analyzer().Analyze(Day, Segments(), Commits(), assignments);

        var pay = after.Issues.Single(i => i.Key == "PAY-1042");
        Assert.Equal(TimeSpan.FromMinutes(95 + 35), pay.Duration);
        Assert.Equal(before.Classified + TimeSpan.FromMinutes(35), after.Classified);
        Assert.DoesNotContain(after.Unclassified, u => u.Label.Contains("Stack Overflow", StringComparison.Ordinal));
        // Atanan arama, önceki PAY-1042 bloğuyla birleşir: 09:00–10:40 tek blok.
        Assert.Contains(after.Timeline, b => b.IssueKey == "PAY-1042" && b.Start <= At(10, 5) && b.End >= At(10, 40));
    }

    [Fact]
    public void Other_group_cannot_be_assigned()
    {
        var segs = new List<ActivitySegment>
        {
            new(1, At(9), At(9, 1), "notepad.exe", "a.txt - Notepad", false),
            new(2, At(9, 1), At(9, 2), "calc.exe", "Hesap Makinesi", false),
        };
        var a = Analyzer().Analyze(Day, segs, []);
        var other = a.Unclassified.Single(u => u.Label == DayAnalyzer.OtherLabel);
        Assert.Null(other.Group);
    }

    [Fact]
    public void Range_analysis_clips_to_window()
    {
        var a = Analyzer().AnalyzeRange(Day, At(9, 30), At(10, 30), Segments(), Commits());
        Assert.Equal(TimeSpan.FromHours(1), a.Active);
        Assert.Equal(new[] { "PAY-1042" }, a.Issues.Where(i => i.Duration > TimeSpan.Zero).Select(i => i.Key));
    }

    [Fact]
    public void Group_rule_pattern_matches_its_segments_and_preview_counts_them()
    {
        var group = new GroupRef("chrome.exe", "Stack Overflow");
        var classifier = new IssueClassifier(new ClassificationSettings());
        var preview = RulePreview.Measure(group.ToRulePattern(), Segments(), classifier);
        Assert.Equal(TimeSpan.FromMinutes(35), preview.Captured);
        Assert.Equal(1, preview.Days);

        // Kural olarak eklenince aynı segment sınıflandırılır.
        var settings = new ClassificationSettings { Rules = [new ClassificationRule { Pattern = group.ToRulePattern(), IssueKey = "PAY-1042" }] };
        Assert.Equal(ClassificationSource.Rule, new IssueClassifier(settings).Classify("chrome.exe", SoTitle).Source);
        Assert.Null(new IssueClassifier(settings).Classify("firefox.exe", SoTitle).IssueKey);
    }

    [Fact]
    public void Preview_ignores_already_classified_and_invalid_patterns()
    {
        var classifier = new IssueClassifier(new ClassificationSettings());
        Assert.Equal(TimeSpan.Zero, RulePreview.Measure("PAY-1042", Segments(), classifier).Captured);
        Assert.Equal(0, RulePreview.Measure("([", Segments(), classifier).Segments);
    }

    [Theory]
    [InlineData(" pay-12 ", "PAY-12")]
    [InlineData("PAY12", null)]
    [InlineData("PAY-12 ek", null)]
    [InlineData("", null)]
    public void Normalizes_user_typed_keys(string input, string? expected) =>
        Assert.Equal(expected, new IssueClassifier(new ClassificationSettings()).NormalizeKey(input));
}
