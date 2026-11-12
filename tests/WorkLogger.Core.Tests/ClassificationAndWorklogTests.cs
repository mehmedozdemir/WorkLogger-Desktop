using WorkLogger.Core.Analysis;
using WorkLogger.Core.Classification;
using WorkLogger.Core.Settings;

namespace WorkLogger.Core.Tests;

public class ClassificationTests
{
    [Theory]
    [InlineData("PAY-1042 kart - Odeme.sln", "PAY-1042")]
    [InlineData("[crm-880] lisans", null)] // küçük harf key kabul edilmez (desen büyük harf)
    [InlineData("UTF-8 encoding sorunu", null)]
    [InlineData("SHA-256 hash PAY-7 fix", "PAY-7")]
    [InlineData("", null)]
    public void Extracts_first_valid_key(string title, string? expected)
    {
        var c = new IssueClassifier(new ClassificationSettings());
        Assert.Equal(expected, c.ExtractKey(title));
    }

    [Fact]
    public void Allowed_projects_filter_keys()
    {
        var c = new IssueClassifier(new ClassificationSettings { AllowedProjects = ["PAY"] });
        Assert.Null(c.ExtractKey("ABC-12 bir şey"));
        Assert.Equal("PAY-3", c.ExtractKey("ABC-12 ve PAY-3"));
    }

    [Fact]
    public void Rules_apply_when_title_has_no_key()
    {
        var settings = new ClassificationSettings
        {
            Rules = [new ClassificationRule { Pattern = @"Odeme\.sln", IssueKey = "pay-1" }],
        };
        var c = new IssueClassifier(settings);
        Assert.Equal(new IssueMatch("PAY-1", ClassificationSource.Rule), c.Classify("devenv.exe", "Program.cs - Odeme.sln - Microsoft Visual Studio"));
        Assert.Equal(ClassificationSource.Title, c.Classify("devenv.exe", "PAY-9 x - Odeme.sln").Source);
    }

    [Theory]
    [InlineData("PAY-1042 kart dogrulama - Odeme.sln - Microsoft Visual Studio", "kart dogrulama", "Odeme.sln")]
    [InlineData("[PAY-1042] Sanal kart - Jira - Google Chrome", "Sanal kart", "Jira")]
    [InlineData("● Program.cs - WorkLogger - Visual Studio Code", "Program.cs", "WorkLogger")]
    public void Title_normalizer(string title, string topic, string group)
    {
        Assert.Equal(topic, TitleNormalizer.Topic(title));
        Assert.Equal(group, TitleNormalizer.GroupKey(title));
    }

    [Fact]
    public void Settings_validation_reports_bad_regex()
    {
        var s = new WorkLoggerSettings();
        s.Classification.Rules.Add(new ClassificationRule { Pattern = "([", IssueKey = "X-1" });
        Assert.NotEmpty(s.Validate());
        Assert.Empty(new WorkLoggerSettings().Validate());
    }
}

public class WorklogPlannerTests
{
    [Theory]
    [InlineData(38, 15, 15, 45)]
    [InlineData(4, 15, 15, 15)]
    [InlineData(52, 15, 15, 45)]
    [InlineData(53, 15, 15, 60)]
    [InlineData(53, 0, 0, 53)]
    public void Rounds_to_step(int minutes, int step, int min, int expected) =>
        Assert.Equal(expected, WorklogPlanner.RoundMinutes(TimeSpan.FromMinutes(minutes), step, min));

    [Fact]
    public void Balance_scales_to_target_in_steps()
    {
        var items = new List<WorklogSuggestion>
        {
            new("A-1", TimeSpan.FromMinutes(120), 120, new TimeOnly(9, 0), "", false),
            new("A-2", TimeSpan.FromMinutes(60), 60, new TimeOnly(11, 0), "", false),
            new("A-3", TimeSpan.FromMinutes(30), 30, new TimeOnly(13, 0), "", false),
            new("A-4", TimeSpan.Zero, 0, new TimeOnly(13, 0), "", true),
        };
        var balanced = WorklogPlanner.BalanceTo(items, 480, 15);
        Assert.Equal(480, balanced.Sum(i => i.SuggestedMinutes));
        Assert.All(balanced, i => Assert.Equal(0, i.SuggestedMinutes % 15));
        Assert.Equal(0, balanced.Single(i => i.IssueKey == "A-4").SuggestedMinutes);
        Assert.True(balanced[0].SuggestedMinutes > balanced[1].SuggestedMinutes);
    }

    [Fact]
    public void Description_prefers_commit_messages_without_key()
    {
        var a = DayAnalyzerTests.Analyzer().Analyze(DayAnalyzerTests.Day, DayAnalyzerTests.Segments(), DayAnalyzerTests.Commits());
        var pay = a.Issues.Single(i => i.Key == "PAY-1042");
        Assert.Equal("kart dogrulama eklendi", WorklogPlanner.Describe(pay));
    }
}
