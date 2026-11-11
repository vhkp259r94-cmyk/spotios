using Microsoft.UI.Xaml;

namespace LibreStream.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Datos de WebView2 (caché del reproductor embebido) en LocalAppData, no junto al .exe
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreStream", "WebView2"));
        // Permite que el reproductor embebido arranque al pulsar una tarjeta de la app
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--autoplay-policy=no-user-gesture-required");
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
