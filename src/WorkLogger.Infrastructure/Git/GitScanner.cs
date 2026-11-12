using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WorkLogger.Core.Model;
using WorkLogger.Core.Settings;

namespace WorkLogger.Infrastructure.Git;

/// <summary>Yapılandırılan kök klasörlerdeki git depolarından commit ve diff istatistiklerini toplar.</summary>
public sealed partial class GitScanner
{
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", ".vs", ".idea", "venv", ".venv", "dist", "build", "target",
    };

    private const char RecordSeparator = '\u001e';
    private const char FieldSeparator = '\u001f';

    [GeneratedRegex(@"(\d+) files? changed(?:, (\d+) insertions?\(\+\))?(?:, (\d+) deletions?\(-\))?")]
    private static partial Regex ShortStat();

    public static List<string> FindRepositories(IEnumerable<string> roots, int maxDepth)
    {
        var repos = new List<string>();
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var path = Environment.ExpandEnvironmentVariables(root.Trim());
            if (Directory.Exists(path))
                Walk(path, maxDepth, repos);
        }
        return repos;
    }

    private static void Walk(string path, int depthLeft, List<string> repos)
    {
        if (Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git")))
        {
            repos.Add(path);
            return;
        }
        if (depthLeft <= 0)
            return;
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }
        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || SkipDirectories.Contains(name))
                continue;
            Walk(child, depthLeft - 1, repos);
        }
    }

    /// <summary>[from, to) aralığındaki, tüm dallardaki merge olmayan commit'ler.</summary>
    public static async Task<List<CommitInfo>> CommitsAsync(string repo, DateOnly from, DateOnly to, string authorEmail, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-C", repo, "log", "--all", "--no-merges", "--source",
            $"--since={from:yyyy-MM-dd} 00:00", $"--until={to:yyyy-MM-dd} 00:00",
            "--pretty=format:%x1e%H%x1f%at%x1f%S%x1f%s", "--shortstat",
        };
        if (!string.IsNullOrWhiteSpace(authorEmail))
            args.Add($"--author={authorEmail.Trim()}");

        var output = await RunGitAsync(args, ct).ConfigureAwait(false);
        return output is null ? [] : Parse(Path.GetFileName(repo.TrimEnd('\\', '/')), output);
    }

    internal static List<CommitInfo> Parse(string repoName, string output)
    {
        var commits = new List<CommitInfo>();
        foreach (var record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = record.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
                continue;
            var fields = lines[0].Split(FieldSeparator);
            if (fields.Length < 4 || !long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
                continue;

            int files = 0, ins = 0, del = 0;
            foreach (var line in lines.Skip(1))
            {
                var m = ShortStat().Match(line);
                if (!m.Success)
                    continue;
                files = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                ins = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                del = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            }

            commits.Add(new CommitInfo(
                repoName,
                fields[0],
                NormalizeRef(fields[2]),
                string.Join(FieldSeparator, fields.Skip(3)),
                DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime(),
                files,
                ins,
                del));
        }
        return commits;
    }

    internal static string NormalizeRef(string reference)
    {
        foreach (var prefix in new[] { "refs/heads/", "refs/remotes/origin/", "refs/remotes/", "refs/tags/" })
        {
            if (reference.StartsWith(prefix, StringComparison.Ordinal))
                return reference[prefix.Length..];
        }
        return reference;
    }

    private static async Task<string?> RunGitAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 ? await stdout.ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Tüm depoları tarar ve yeni commit'leri döner.</summary>
    public static async Task<List<CommitInfo>> ScanAsync(GitSettings settings, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var all = new List<CommitInfo>();
        foreach (var repo in FindRepositories(settings.RepoRoots, settings.ScanDepth))
        {
            ct.ThrowIfCancellationRequested();
            all.AddRange(await CommitsAsync(repo, from, to, settings.AuthorEmail, ct).ConfigureAwait(false));
        }
        return all;
    }
}
