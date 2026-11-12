using Avalonia.Controls;

namespace WorkLogger.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Uygulamadan gerçekten çıkılırken pencerenin kapanmasına izin verir.</summary>
    public void AllowClose() => _allowClose = true;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Pencereyi kapatmak uygulamayı kapatmaz; tepsiye küçültür. İzleme ayrı süreçte sürer.
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
