using System.Globalization;
using System.Text.Json;
using LibreStream.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Web.WebView2.Core;

namespace LibreStream.App;

public sealed partial class MainWindow : Window
{
    private const string CatalogUrl = "https://vhkp259r94-cmyk.github.io/spotios/catalog.json";
    private const string PlayerHost = "app.librestream";

    private enum Mode { None, Native, YouTube }

    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreStream");

    private readonly AudioPlayerManager _player = AudioPlayerManager.Shared;
    private readonly CatalogService _catalog = new(new Uri(CatalogUrl), DataDir);
    private readonly LibraryStore _library = new(DataDir);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(700) };

    // Listas visibles
    private List<MediaCard> _videos = [];
    private List<MediaCard> _tracks = [];
    private List<MediaCard> _youtube = [];
    private List<MediaCard> _saved = [];
    private MediaCard? _featured;
    private string _kind = "all";
    private string _youtubeQuery = "";
    private CancellationTokenSource? _searchCts;

    // Reproducción
    private Mode _mode = Mode.None;
    private List<MediaCard> _queue = [];
    private int _index = -1;
    private MediaCard? _now;
    private bool _updatingProgress;

    // Estado del reproductor de YouTube embebido
    private bool _ytReady, _ytPlaying;
    private string? _ytPending;
    private double _ytTime, _ytDuration;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        // Tamaño inicial: 85 % del área de trabajo, centrada (nunca detrás de la barra de tareas)
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        int w = (int)(area.Width * 0.85), h = (int)(area.Height * 0.88);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));

        Video.SetMediaPlayer(_player.Player);
        _player.Volume = Volume.Value / 100;
        _player.StateChanged += () => DispatcherQueue.TryEnqueue(OnNativeStateChanged);
        _timer.Tick += (_, _) => UpdateProgress();
        _timer.Start();
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); _ = SearchYouTubeAsync(Search.Text); };

        _ = LoadAsync();
        _ = InitYouTubePlayerAsync();
    }

    // ===================== Catálogo y listas =====================

    private async Task LoadAsync()
    {
        try
        {
            await _catalog.RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowStatus($"No se pudo actualizar el catálogo: {ex.Message}");
        }
        ApplyFilter();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Browse.ChangeView(null, 0, null, true));
    }

    private void ShowStatus(string? text)
    {
        Status.Text = text ?? "";
        Status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyFilter()
    {
        var q = MetadataMatcher.Fold(Search.Text ?? "");
        bool Match(string text) => q.Length == 0 || MetadataMatcher.Fold(text).Contains(q);
        var library = _kind == "library";

        var items = library ? [] : _catalog.Items
            .Where(i => Match($"{i.Artist} {i.Title} {string.Join(' ', i.Genres ?? [])}")).ToList();
        _videos = _kind == "track" ? [] : items.Where(i => i.Kind != ItemKind.Track).Select(i => new MediaCard(i)).ToList();
        _tracks = _kind == "video" ? [] : items.Where(i => i.Kind == ItemKind.Track).Select(i => new MediaCard(i)).ToList();
        _saved = !library ? [] : _library.Items
            .Where(s => Match($"{s.Title} {s.Subtitle}"))
            .Select(s => new MediaCard(s, _catalog.Items.FirstOrDefault(i => i.Id == s.Id)))
            .Where(c => c.IsPlayable).ToList();

        var showYouTube = !library && q.Length > 0;
        if (!showYouTube) { _youtube = []; _youtubeQuery = ""; }

        VideosRepeater.ItemsSource = _videos;
        TracksRepeater.ItemsSource = _tracks;
        SavedRepeater.ItemsSource = _saved;
        YouTubeRepeater.ItemsSource = _youtube;
        VideosSection.Visibility = Vis(_videos.Count > 0);
        TracksSection.Visibility = Vis(_tracks.Count > 0);
        SavedSection.Visibility = Vis(library);
        SavedEmpty.Visibility = Vis(library && _saved.Count == 0);
        YouTubeSection.Visibility = Vis(showYouTube);

        // Destacado: solo en la vista sin búsqueda
        _featured = q.Length == 0 && !library ? _videos.FirstOrDefault() ?? _tracks.FirstOrDefault() : null;
        Hero.Visibility = Vis(_featured is not null);
        if (_featured is not null)
        {
            HeroTile.Background = _featured.Tile;
            HeroImage.Source = _featured.Artwork;
            HeroTitle.Text = _featured.Title;
            HeroSubtitle.Text = $"{_featured.Subtitle} · {_featured.DurationText} · {_featured.Item?.License.Spdx}";
        }
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // ===================== Búsqueda en YouTube =====================

    private async Task SearchYouTubeAsync(string? text)
    {
        var query = (text ?? "").Trim();
        if (query.Length < 2 || _kind == "library" || query == _youtubeQuery) return;

        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        YouTubeBusy.IsActive = true;
        YouTubeInfo.Text = "";
        try
        {
            var results = await YouTubeSearch.SearchAsync(_http, query, cts.Token);
            if (cts.IsCancellationRequested) return;
            _youtubeQuery = query;
            _youtube = results.Select(v => new MediaCard(v)).ToList();
            YouTubeRepeater.ItemsSource = _youtube;
            YouTubeInfo.Text = _youtube.Count == 0 ? "Sin resultados en YouTube." : "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            YouTubeInfo.Text = $"No se pudo buscar en YouTube: {ex.Message}";
        }
        finally
        {
            if (_searchCts == cts) YouTubeBusy.IsActive = false;
        }
    }

    // ===================== Reproducción (cola unificada) =====================

    private void Play(MediaCard card, List<MediaCard> list)
    {
        if (!card.IsPlayable) return;
        _queue = list.Where(c => c.IsPlayable).ToList();
        _index = _queue.FindIndex(c => c.Source == card.Source && c.Id == card.Id);
        if (_index < 0) { _queue = [card]; _index = 0; }
        StartCurrent();
    }

    private void StartCurrent()
    {
        var card = _queue[_index];
        _now = card;
        ShowStatus(null);

        if (card.YouTubeId is { } id)
        {
            _mode = Mode.YouTube;
            _player.Player.Pause();
            VideoLayer.Visibility = Visibility.Collapsed;
            _ytTime = 0; _ytDuration = card.DurationSec; _ytPlaying = true;
            if (_ytReady) RunPlayerScript($"load('{id}')"); else _ytPending = id;
            YouTubeLayer.Visibility = Visibility.Visible;
        }
        else
        {
            _mode = Mode.Native;
            RunPlayerScript("cmd('stop')");
            YouTubeLayer.Visibility = Visibility.Collapsed;
            var natives = _queue.Where(c => c.Item is not null).ToList();
            _player.Play(natives.Select(c => c.Item!), natives.IndexOf(card));
        }
        UpdateNowPlayingBar();
    }

    private void Next()
    {
        if (_mode == Mode.YouTube || _queue.Any(c => c.YouTubeId is not null))
        {
            if (_index + 1 < _queue.Count) { _index++; StartCurrent(); }
            else if (_mode == Mode.YouTube) RunPlayerScript("cmd('pause')");
        }
        else _player.Next();
    }

    private void Previous()
    {
        if (_mode == Mode.YouTube)
        {
            if (_ytTime > 3 || _index <= 0) RunPlayerScript("cmd('seek', 0)");
            else { _index--; StartCurrent(); }
        }
        else _player.Previous();
    }

    private void OnNativeStateChanged()
    {
        if (_mode != Mode.Native) return;
        if (_player.Current is { } item)
        {
            var i = _queue.FindIndex(c => c.Item?.Id == item.Id);
            if (i >= 0) { _index = i; _now = _queue[i]; }
            if (item.Kind == ItemKind.Track) VideoLayer.Visibility = Visibility.Collapsed;
            else if (_player.IsPlaying && _now?.Item?.Id == item.Id) VideoLayer.Visibility = Visibility.Visible;
        }
        UpdateNowPlayingBar();
    }

    private bool IsPlaying => _mode == Mode.YouTube ? _ytPlaying : _player.IsPlaying;

    private void UpdateNowPlayingBar()
    {
        PlayIcon.Symbol = IsPlaying ? Symbol.Pause : Symbol.Play;
        if (_now is null) return;
        NowTitle.Text = _now.Title;
        NowArtist.Text = _now.YouTubeId is null ? _now.Subtitle : $"{_now.Subtitle} · YouTube";
        NowTile.Background = _now.Tile;
        NowImage.Source = _now.Artwork;
        SaveIcon.Glyph = _library.Contains(_now.Source, _now.Id) ? "" : "";
    }

    private void UpdateProgress()
    {
        TimeSpan pos, total;
        if (_mode == Mode.YouTube)
        {
            pos = TimeSpan.FromSeconds(_ytTime);
            total = TimeSpan.FromSeconds(_ytDuration);
        }
        else
        {
            pos = _player.Position;
            total = _player.Duration;
            if (total <= TimeSpan.Zero && _now is not null) total = TimeSpan.FromSeconds(_now.DurationSec);
        }

        _updatingProgress = true;
        Progress.Maximum = Math.Max(1, total.TotalSeconds);
        Progress.Value = Math.Min(pos.TotalSeconds, Progress.Maximum);
        _updatingProgress = false;

        Elapsed.Text = MediaCard.Format(pos);
        Total.Text = MediaCard.Format(total);
    }

    // ===================== Reproductor de YouTube embebido =====================

    private async Task InitYouTubePlayerAsync()
    {
        try
        {
            await YouTubePlayer.EnsureCoreWebView2Async();
            var core = YouTubePlayer.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(PlayerHost, Path.Combine(AppContext.BaseDirectory, "Assets"),
                CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.WebMessageReceived += OnPlayerMessage;
            // Bloqueo de anuncios y rastreadores a nivel de red (lista en Core/Services/Privacy/AdBlock.cs)
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (AdBlock.IsBlocked(e.Request.Uri))
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            core.NewWindowRequested += (_, e) => e.Handled = true; // sin ventanas emergentes
            YouTubePlayer.Source = new Uri($"https://{PlayerHost}/player.html");
        }
        catch (Exception ex)
        {
            ShowStatus($"No se pudo iniciar el reproductor de YouTube (¿falta WebView2 Runtime?): {ex.Message}");
        }
    }

    private void RunPlayerScript(string script)
    {
        if (_ytReady && YouTubePlayer.CoreWebView2 is not null) _ = YouTubePlayer.ExecuteScriptAsync(script);
    }

    private void OnPlayerMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _ytReady = true;
                    RunPlayerScript($"cmd('volume', {Volume.Value.ToString("0", CultureInfo.InvariantCulture)})");
                    if (_ytPending is { } id) { _ytPending = null; RunPlayerScript($"load('{id}')"); }
                    break;
                case "state" when _mode == Mode.YouTube:
                    var state = root.GetProperty("state").GetInt32(); // 1 = reproduciendo, 3 = cargando
                    _ytPlaying = state is 1 or 3;
                    _ytTime = root.GetProperty("time").GetDouble();
                    var d = root.GetProperty("duration").GetDouble();
                    if (d > 0) _ytDuration = d;
                    PlayIcon.Symbol = _ytPlaying ? Symbol.Pause : Symbol.Play;
                    break;
                case "ended" when _mode == Mode.YouTube:
                    Next();
                    break;
                case "error" when _mode == Mode.YouTube:
                    var code = root.GetProperty("code").GetInt32();
                    ShowStatus(code is 101 or 150
                        ? $"«{_now?.Title}» no permite reproducirse fuera de youtube.com (lo decide quien lo subió)."
                        : $"YouTube no pudo reproducir «{_now?.Title}» (error {code}).");
                    YouTubeLayer.Visibility = Visibility.Collapsed;
                    _ytPlaying = false;
                    if (_index + 1 < _queue.Count) { _index++; StartCurrent(); }
                    else UpdateNowPlayingBar();
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            // mensaje con formato inesperado: se ignora
        }
    }

    // ===================== Eventos de la interfaz =====================

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        _kind = (e.SelectedItem as NavigationViewItem)?.Tag as string ?? "all";
        VideoLayer.Visibility = Visibility.Collapsed;
        YouTubeLayer.Visibility = Visibility.Collapsed;
        if (_catalog is null) return;
        ApplyFilter();
        _ = SearchYouTubeAsync(Search.Text);
    }

    private void Search_TextChanged(AutoSuggestBox s, AutoSuggestBoxTextChangedEventArgs e)
    {
        VideoLayer.Visibility = Visibility.Collapsed;
        YouTubeLayer.Visibility = Visibility.Collapsed;
        ApplyFilter();
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void Search_QuerySubmitted(AutoSuggestBox s, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        _searchDelay.Stop();
        _ = SearchYouTubeAsync(s.Text);
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MediaCard card) return;
        var list = _saved.Contains(card) ? _saved
            : _youtube.Contains(card) ? _youtube
            : card.IsTrack ? _tracks : _videos;
        Play(card, list);
    }

    private void Hero_Click(object sender, RoutedEventArgs e)
    {
        if (_featured is not null) Play(_featured, _featured.IsTrack ? _tracks : _videos);
    }

    private void CloseLayer_Click(object sender, RoutedEventArgs e)
    {
        // Oculta el vídeo; el sonido sigue (modo música)
        VideoLayer.Visibility = Visibility.Collapsed;
        YouTubeLayer.Visibility = Visibility.Collapsed;
    }

    private void ShowVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == Mode.YouTube) YouTubeLayer.Visibility = Vis(YouTubeLayer.Visibility != Visibility.Visible);
        else if (_mode == Mode.Native && _now is { IsTrack: false }) VideoLayer.Visibility = Vis(VideoLayer.Visibility != Visibility.Visible);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_now is null) return;
        _library.Toggle(_now.ToSaved());
        UpdateNowPlayingBar();
        if (_kind == "library") ApplyFilter();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == Mode.None)
        {
            if ((_featured ?? _tracks.FirstOrDefault() ?? _videos.FirstOrDefault() ?? _saved.FirstOrDefault()) is { } first)
                Play(first, _saved.Contains(first) ? _saved : first.IsTrack ? _tracks : _videos);
        }
        else if (_mode == Mode.YouTube)
        {
            RunPlayerScript(_ytPlaying ? "cmd('pause')" : "cmd('play')");
        }
        else _player.TogglePlayPause();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => Next();

    private void Progress_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingProgress || _now is null) return;
        if (_mode == Mode.YouTube)
        {
            _ytTime = e.NewValue;
            RunPlayerScript($"cmd('seek', {e.NewValue.ToString("0.##", CultureInfo.InvariantCulture)})");
        }
        else _player.Seek(TimeSpan.FromSeconds(e.NewValue));
    }

    private void Volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_player is null) return;
        _player.Volume = e.NewValue / 100;
        RunPlayerScript($"cmd('volume', {e.NewValue.ToString("0", CultureInfo.InvariantCulture)})");
    }
}
