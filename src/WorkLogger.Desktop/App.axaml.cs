using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using WorkLogger.Agent;
using WorkLogger.Desktop.Services;
using WorkLogger.Desktop.ViewModels;
using WorkLogger.Desktop.Views;
using WorkLogger.Infrastructure.Data;

namespace WorkLogger.Desktop;

public partial class App : Application
{
    private AppServices? _services;
    private MainWindow? _window;
    private MainWindowViewModel? _viewModel;
    private NativeMenuItem? _pauseItem;
    private RangeSummaryWindow? _rangeWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = new AppServices();
            _services.EnsureAgentRunning();

            _viewModel = new MainWindowViewModel(_services);
            _viewModel.DayEndReached += (_, _) => ShowMainWindow();
            _viewModel.Day.RangeSummaryRequested += (_, _) => ShowRangeSummary();
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsPaused) && _pauseItem is not null)
                    _pauseItem.Header = _viewModel.IsPaused ? "İzlemeyi sürdür" : "İzlemeyi duraklat";
            };

            _window = new MainWindow { DataContext = _viewModel };
            UiServices.Attach(_window);

            CreateTrayIcon(desktop);
            ListenForShowSignal();

            if (!Program.StartMinimized)
                ShowMainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var open = new NativeMenuItem("WorkLogger'ı aç");
        open.Click += (_, _) => ShowMainWindow();

        var today = new NativeMenuItem("Bugünün özeti");
        today.Click += (_, _) =>
        {
            ShowMainWindow();
            _viewModel?.ShowTodayCommand.Execute(null);
        };

        var range = new NativeMenuItem("Son 2 saatte ne yaptım?");
        range.Click += (_, _) => ShowRangeSummary();

        _pauseItem = new NativeMenuItem("İzlemeyi duraklat");
        _pauseItem.Click += (_, _) => _viewModel?.TogglePauseCommand.Execute(null);

        var exit = new NativeMenuItem("Pencereyi kapat (izleme sürer)");
        exit.Click += (_, _) => Exit(desktop, stopAgent: false);

        var exitAll = new NativeMenuItem("İzlemeyi durdur ve çık");
        exitAll.Click += (_, _) => Exit(desktop, stopAgent: true);

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(today);
        menu.Items.Add(range);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);
        menu.Items.Add(exitAll);

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://WorkLogger/Assets/worklogger.ico"))),
            ToolTipText = "WorkLogger — çalışma izleyici",
            Menu = menu,
        };
        tray.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, [tray]);
    }

    private void ShowMainWindow()
    {
        if (_window is null)
            return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ShowRangeSummary()
    {
        if (_services is null)
            return;
        if (_rangeWindow is null)
        {
            var vm = new RangeSummaryViewModel(_services);
            _rangeWindow = new RangeSummaryWindow { DataContext = vm };
            _rangeWindow.Closed += (_, _) => _rangeWindow = null;
            _ = vm.LoadAsync();
        }
        else if (_rangeWindow.DataContext is RangeSummaryViewModel vm)
        {
            _ = vm.LoadAsync();
        }
        _rangeWindow.Show();
        _rangeWindow.Activate();
    }

    private void ListenForShowSignal()
    {
        // İkinci kez başlatılan örnek bu olayı tetikler; dinleyici süreç boyunca yaşar.
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSignalName);
        var thread = new Thread(() =>
        {
            using (signal)
            {
                while (signal.WaitOne())
                    Dispatcher.UIThread.Post(ShowMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "show-signal",
        };
        thread.Start();
    }

    private void Exit(IClassicDesktopStyleApplicationLifetime desktop, bool stopAgent)
    {
        if (stopAgent && _services is not null)
        {
            try
            {
                // Önce kibarca iste: Agent açık segmenti kapatıp kendisi çıkar.
                _services.AgentState.Set(AgentStateRepository.ShutdownRequested, "1");
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (AgentHost.IsRunning() && DateTime.UtcNow < deadline)
                    Thread.Sleep(200);

                // Yanıt vermediyse son çare olarak sonlandır.
                if (AgentHost.IsRunning() && int.TryParse(_services.AgentState.Get(AgentStateRepository.ProcessId), out var pid))
                {
                    using var process = Process.GetProcessById(pid);
                    if (process.ProcessName.Equals(Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
                        process.Kill();
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
        _window?.AllowClose();
        desktop.Shutdown();
    }
}
