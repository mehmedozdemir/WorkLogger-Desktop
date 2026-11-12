using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkLogger.Infrastructure;
using WorkLogger.Infrastructure.Data;
using WorkLogger.Infrastructure.Git;

namespace WorkLogger.Agent;

/// <summary>Ayarlanan aralıkla dünkü ve bugünkü commit'leri tarar.</summary>
public sealed class GitScanService(
    ActivityRepository activity,
    AgentStateRepository state,
    SettingsStore settingsStore,
    ILogger<GitScanService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Açılışta diğer işleri bekletmemek için kısa bir gecikme.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                var (settings, _) = settingsStore.Load();
                if (settings.Git.RepoRoots.Count > 0)
                    await ScanAsync(settings.Git, stoppingToken).ConfigureAwait(false);
                var interval = TimeSpan.FromMinutes(Math.Clamp(settings.Git.ScanIntervalMinutes, 5, 240));
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ScanAsync(Core.Settings.GitSettings git, CancellationToken ct)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var commits = await GitScanner.ScanAsync(git, today.AddDays(-1), today.AddDays(1), ct).ConfigureAwait(false);
            var added = commits.Count(activity.UpsertCommit);
            state.Set(AgentStateRepository.LastGitScan, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            logger.LogInformation("Git taraması: {Total} commit, {Added} yeni", commits.Count, added);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Git taraması başarısız");
        }
    }
}
