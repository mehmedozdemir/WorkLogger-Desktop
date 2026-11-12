using System.Text.RegularExpressions;
using WorkLogger.Core.Settings;

namespace WorkLogger.Core.Classification;

public enum ClassificationSource
{
    None,
    Title,
    Rule,
    Git,
}

public readonly record struct IssueMatch(string? IssueKey, ClassificationSource Source)
{
    public static readonly IssueMatch Unclassified = new(null, ClassificationSource.None);
    public bool IsClassified => IssueKey is not null;
}

/// <summary>Pencere başlığından veya kullanıcı kurallarından issue key çıkarır.</summary>
public sealed class IssueClassifier
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private readonly Regex _keyPattern;
    private readonly HashSet<string> _ignored;
    private readonly HashSet<string> _allowedProjects;
    private readonly List<(Regex Pattern, string Key)> _rules;
    private readonly Dictionary<string, string> _aliases;
    private readonly Dictionary<(string, string), IssueMatch> _cache = [];

    public IssueClassifier(ClassificationSettings settings)
    {
        _keyPattern = new Regex(settings.IssueKeyPattern, RegexOptions.Compiled, RegexTimeout);
        _ignored = new HashSet<string>(settings.IgnoredIssueKeys.Select(k => k.ToUpperInvariant()), StringComparer.Ordinal);
        _allowedProjects = new HashSet<string>(settings.AllowedProjects.Select(k => k.ToUpperInvariant()), StringComparer.Ordinal);
        _rules = settings.Rules
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Pattern) && !string.IsNullOrWhiteSpace(r.IssueKey))
            .Select(r => (new Regex(r.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout), r.IssueKey.Trim().ToUpperInvariant()))
            .ToList();
        _aliases = new Dictionary<string, string>(settings.AppAliases, StringComparer.OrdinalIgnoreCase);
    }

    public IssueMatch Classify(string app, string title)
    {
        if (_cache.TryGetValue((app, title), out var cached))
            return cached;

        IssueMatch result;
        if (ExtractKey(title) is { } key)
            result = new IssueMatch(key, ClassificationSource.Title);
        else if (MatchRule(app, title) is { } ruleKey)
            result = new IssueMatch(ruleKey, ClassificationSource.Rule);
        else
            result = IssueMatch.Unclassified;

        if (_cache.Count > 20_000)
            _cache.Clear();
        _cache[(app, title)] = result;
        return result;
    }

    /// <summary>Metindeki ilk geçerli issue key; yoksa null.</summary>
    public string? ExtractKey(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        try
        {
            foreach (Match match in _keyPattern.Matches(text))
            {
                var key = match.Value.ToUpperInvariant();
                var project = ProjectOf(key);
                if (_ignored.Contains(key) || _ignored.Contains(project))
                    continue;
                if (_allowedProjects.Count > 0 && !_allowedProjects.Contains(project))
                    continue;
                return key;
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return null;
    }

    public string AppLabel(string app) =>
        _aliases.TryGetValue(app, out var alias)
            ? alias
            : app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app[..^4] : app;

    public static string ProjectOf(string issueKey)
    {
        var dash = issueKey.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 ? issueKey[..dash] : issueKey;
    }

    private string? MatchRule(string app, string title)
    {
        var haystack = $"{app} {title}";
        foreach (var (pattern, key) in _rules)
        {
            try
            {
                if (pattern.IsMatch(haystack))
                    return key;
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }
        return null;
    }
}
