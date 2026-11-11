using Microsoft.UI.Xaml;

namespace LibreStream.App;

public partial class App : Application
{
    private Window? _window;

    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreStream");

    public App()
    {
        // Datos de WebView2 (caché del reproductor embebido) en LocalAppData, no junto al .exe
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(DataDir, "WebView2"));
        // Permite que el reproductor embebido arranque al pulsar una tarjeta de la app
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--autoplay-policy=no-user-gesture-required");

        // Un fallo suelto no debe cerrar la app: se anota en crash.log y se sigue
        UnhandledException += (_, e) => { Log("UI", e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("Dominio", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log("Tarea", e.Exception); e.SetObserved(); };

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static readonly HashSet<string> Seen = [];

    /// <summary>Como <see cref="Log"/>, pero solo la primera vez que aparece cada error (para bucles y temporizadores).</summary>
    public static void LogOnce(string origin, Exception ex)
    {
        if (Seen.Add(origin + ex.GetType().Name + ex.Message)) Log(origin, ex);
    }

    /// <summary>Anota un error en %LocalAppData%\LibreStream\crash.log (máx. ~200 KB).</summary>
    public static void Log(string origin, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var file = Path.Combine(DataDir, "crash.log");
            if (File.Exists(file) && new FileInfo(file).Length > 200_000) File.Delete(file);
            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {origin}: {ex}\n\n");
        }
        catch
        {
            // el registro nunca debe provocar otro fallo
        }
    }
}
