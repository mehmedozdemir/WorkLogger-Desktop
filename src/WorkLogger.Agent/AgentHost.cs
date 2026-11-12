using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using WorkLogger.Infrastructure;
using WorkLogger.Infrastructure.Data;

namespace WorkLogger.Agent;

/// <summary>
/// Arka plan izleyici sürecinin giriş noktası. Desktop uygulaması kendisini "agent" argümanıyla
/// ayrı bir süreç olarak başlatır (bkz. docs/adr/0002-surec-modeli.md).
/// </summary>
public static class AgentHost
{
    public const string MutexName = @"Local\WorkLogger.Agent";
    public const string Argument = "agent";

    /// <summary>Agent çalışıyor mu (tek örnek kilidi alınmış mı)?</summary>
    public static bool IsRunning()
    {
        if (Mutex.TryOpenExisting(MutexName, out var mutex))
        {
            mutex.Dispose();
            return true;
        }
        return false;
    }

    public static int Run(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        if (!created)
            return 0; // zaten çalışıyor

        var paths = new AppPaths();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "agent-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();

        try
        {
            var database = new Database(paths.DatabaseFile);
            database.Migrate();

            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog();
            builder.Services.AddSingleton(paths);
            builder.Services.AddSingleton(database);
            builder.Services.AddSingleton<ActivityRepository>();
            builder.Services.AddSingleton<AgentStateRepository>();
            builder.Services.AddSingleton<SettingsStore>();
            builder.Services.AddHostedService<TrackerService>();
            builder.Services.AddHostedService<GitScanService>();
            builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

            using var host = builder.Build();
            host.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Agent beklenmedik şekilde sonlandı");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
            mutex.ReleaseMutex();
        }
    }
}
