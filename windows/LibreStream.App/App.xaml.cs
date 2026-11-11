using Microsoft.UI.Xaml;

namespace LibreStream.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Datos de WebView2 (cookies de YouTube) en LocalAppData, no junto al .exe
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreStream", "WebView2"));
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
