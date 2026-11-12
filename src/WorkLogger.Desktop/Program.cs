using Avalonia;
using WorkLogger.Agent;

namespace WorkLogger.Desktop;

internal static class Program
{
    public const string DesktopMutexName = @"Local\WorkLogger.Desktop";
    public const string ShowSignalName = @"Local\WorkLogger.Desktop.Show";

    /// <summary>Tepsiye küçültülmüş başlat (oturum açılışı).</summary>
    public static bool StartMinimized { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Aynı exe iki rolde çalışır: "agent" argümanıyla arayüzsüz izleyici, aksi halde masaüstü uygulaması.
        if (args.Length > 0 && string.Equals(args[0], AgentHost.Argument, StringComparison.OrdinalIgnoreCase))
            return AgentHost.Run(args[1..]);

        using var mutex = new Mutex(initiallyOwned: true, DesktopMutexName, out var created);
        if (!created)
        {
            // Zaten açık: mevcut pencereyi öne getir.
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
            {
                signal.Set();
                signal.Dispose();
            }
            return 0;
        }

        StartMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
