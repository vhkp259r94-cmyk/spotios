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
    private readonly PlaylistStore _playlists = new(DataDir);
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
    private bool _audioOnly;
    private bool _ytLayerShown;

    // Géneros (apartado Música) y letra
    private static readonly string[] GenreNames = ["Éxitos", "Reggaetón", "Pop", "Rock", "Electrónica", "Hip hop", "Latino", "Lo-fi"];
    private string _genre = GenreNames[0];
    private Lyrics? _lyrics;
    private string _lyricsFor = "";
    private int _lyricIndex = -1;
    private CancellationTokenSource? _lyricsCts;

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

        ShowYouTubeLayer(false);
        RebuildPlaylistNav();
        foreach (var name in GenreNames)
        {
            var chip = new Button { Content = name, Tag = name, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 6) };
            chip.Click += Genre_Click;
            Genres.Children.Add(chip);
        }
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
        _ = SearchYouTubeAsync(Search.Text);
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
        var playlist = _kind.StartsWith("pl:", StringComparison.Ordinal) ? _playlists.Find(_kind[3..]) : null;
        var library = _kind == "library" || playlist is not null;

        var items = library ? [] : _catalog.Items
            .Where(i => Match($"{i.Artist} {i.Title} {string.Join(' ', i.Genres ?? [])}")).ToList();
        _videos = _kind == "track" ? [] : items.Where(i => i.Kind != ItemKind.Track).Select(i => new MediaCard(i)).ToList();
        _tracks = _kind == "video" ? [] : items.Where(i => i.Kind == ItemKind.Track).Select(i => new MediaCard(i)).ToList();
        _saved = !library ? [] : ((IEnumerable<SavedItem>?)playlist?.Items ?? _library.Items)
            .Where(s => Match($"{s.Title} {s.Subtitle}"))
            .Select(s => new MediaCard(s, _catalog.Items.FirstOrDefault(i => i.Id == s.Id)))
            .Where(c => c.IsPlayable).ToList();

        // Inicio sin búsqueda: fila con lo último guardado
        var home = _kind == "all" && q.Length == 0;
        if (home)
            _saved = _library.Items.Take(8)
                .Select(x => new MediaCard(x, _catalog.Items.FirstOrDefault(i => i.Id == x.Id)))
                .Where(c => c.IsPlayable).ToList();

        var showYouTube = !library && (q.Length > 0 || _kind is "track" or "all");
        if (!showYouTube || !_youtubeQuery.StartsWith(_kind + ":", StringComparison.Ordinal)) { _youtube = []; _youtubeQuery = ""; }
        Genres.Visibility = Vis(_kind == "track" && q.Length == 0);
        foreach (var child in Genres.Children.OfType<Button>())
            child.Style = (Style)Application.Current.Resources[(string)child.Tag == _genre ? "AccentButtonStyle" : "DefaultButtonStyle"];

        // Título y ayuda de cada apartado
        var raw = Search.Text?.Trim() ?? "";
        (PageTitle.Text, var hint) = _kind switch
        {
            "track" => ("Música", q.Length > 0 ? $"Canciones para «{raw}». En este apartado YouTube suena sin vídeo." : "Elige un género o busca un artista. Aquí YouTube suena sin vídeo."),
            "video" => ("Vídeos", q.Length > 0 ? $"Vídeos para «{raw}»." : "Películas y cortos del catálogo libre. Busca para ver vídeos de YouTube."),
            "library" => ("Mi biblioteca", q.Length > 0 ? $"Guardados que coinciden con «{raw}»." : "Todo lo que has guardado con el corazón."),
            _ => ("Inicio", q.Length > 0 ? $"Todo lo encontrado para «{raw}»." : ""),
        };
        PageHint.Text = hint;
        PageHint.Visibility = Vis(hint.Length > 0);
        if (playlist is not null)
        {
            PageTitle.Text = playlist.Name;
            PageHint.Text = playlist.Items.Count == 1 ? "Lista · 1 elemento" : $"Lista · {playlist.Items.Count} elementos";
            PageHint.Visibility = Visibility.Visible;
        }
        PlaylistActions.Visibility = Vis(playlist is not null);
        VideosTitle.Text = q.Length > 0 ? "Vídeos del catálogo libre" : "Películas y cortos";
        TracksTitle.Text = q.Length > 0 ? "Música del catálogo libre" : _kind == "track" ? "Canciones" : "Música";
        YouTubeTitle.Text = _kind switch
        {
            "track" => q.Length > 0 ? "Música en YouTube" : _genre,
            "all" when q.Length == 0 => "Éxitos del momento",
            _ => "Vídeos de YouTube",
        };
        SavedTitle.Text = library ? (q.Length > 0 ? "Resultados" : "Guardado") : "De tu biblioteca";
        var template = (DataTemplate)Root.Resources[_kind == "track" ? "TrackCard" : "VideoCard"];
        if (!ReferenceEquals(YouTubeRepeater.ItemTemplate, template)) YouTubeRepeater.ItemTemplate = template;

        VideosRepeater.ItemsSource = _videos;
        TracksRepeater.ItemsSource = _tracks;
        SavedRepeater.ItemsSource = _saved;
        YouTubeRepeater.ItemsSource = _youtube;
        VideosSection.Visibility = Vis(_videos.Count > 0);
        TracksSection.Visibility = Vis(_tracks.Count > 0);
        SavedSection.Visibility = Vis(library || (home && _saved.Count > 0));
        SavedEmpty.Text = q.Length > 0 ? "Nada guardado coincide con la búsqueda." : "Aún no has guardado nada. Reproduce algo y pulsa el corazón de la barra inferior.";
        if (playlist is not null)
        {
            SavedTitle.Text = "En esta lista";
            if (q.Length == 0) SavedEmpty.Text = "Lista vacía. Reproduce algo y pulsa + en la barra inferior para añadirlo.";
        }
        SavedEmpty.Visibility = Vis(library && _saved.Count == 0);
        YouTubeSection.Visibility = Vis(showYouTube);

        // Destacado: solo en la vista sin búsqueda
        _featured = q.Length == 0 && _kind == "all" ? _videos.FirstOrDefault() ?? _tracks.FirstOrDefault() : null;
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
        var typed = (text ?? "").Trim();
        var kind = _kind;
        if (kind == "library" || kind.StartsWith("pl:", StringComparison.Ordinal) || typed.Length == 1) return;
        // Sin texto: Música muestra el género elegido e Inicio los éxitos del momento
        var query = typed.Length > 0 ? (kind == "track" ? typed + " audio" : typed)
            : kind == "track" ? (_genre == "Éxitos" ? $"official music video {DateTime.Now.Year}" : $"{_genre} official music video")
            : kind == "all" ? $"official music video {DateTime.Now.Year}"
            : "";
        var musicOnly = kind == "track" || typed.Length == 0;
        var key = $"{kind}:{query}";
        if (query.Length == 0 || key == _youtubeQuery) return;

        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        YouTubeBusy.IsActive = true;
        YouTubeInfo.Text = "";
        try
        {
            // En Música se buscan pistas de audio; en Vídeos se excluyen los directos
            var results = await YouTubeSearch.SearchAsync(_http, query, cts.Token, videosOnly: musicOnly);
            if (cts.IsCancellationRequested || kind != _kind) return;
            if (musicOnly) results = results.Where(v => v.DurationSec is > 60 and <= 900).ToList();
            _youtubeQuery = key;
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
        _audioOnly = _kind == "track";
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
            ShowYouTubeLayer(!_audioOnly);
        }
        else
        {
            _mode = Mode.Native;
            RunPlayerScript("cmd('stop')");
            ShowYouTubeLayer(false);
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
        if (LyricsPanel.Visibility == Visibility.Visible) _ = LoadLyricsAsync();
    }

    // ===================== Letra sincronizada =====================

    private async Task LoadLyricsAsync()
    {
        if (_now is not { } card) return;
        var key = $"{card.Source}:{card.Id}";
        if (key == _lyricsFor) return;
        _lyricsFor = key;
        _lyricsCts?.Cancel();
        var cts = _lyricsCts = new CancellationTokenSource();
        _lyrics = null; _lyricIndex = -1;
        LyricsList.ItemsSource = null;
        LyricsInfo.Text = "Buscando la letra…";

        var (artist, title) = card.Item is { } item
            ? (item.Artist, item.Title)
            : LyricsService.GuessTrack(card.Title, card.Subtitle);
        try
        {
            var found = await LyricsService.FindAsync(_http, artist, title, card.DurationSec, cts.Token);
            if (cts.IsCancellationRequested) return;
            _lyrics = found;
            LyricsList.ItemsSource = found?.Lines;
            LyricsInfo.Text = found is null ? $"No hay letra para «{title}»."
                : found.Synced ? $"{artist} — {title}" : $"{artist} — {title} (sin sincronizar)";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LyricsInfo.Text = $"No se pudo cargar la letra: {ex.Message}";
        }
    }

    private void FollowLyrics(double position)
    {
        if (_lyrics is not { Synced: true } lyrics || LyricsPanel.Visibility != Visibility.Visible) return;
        var index = LyricsService.IndexAt(lyrics.Lines, position + 0.3);
        if (index == _lyricIndex) return;
        _lyricIndex = index;
        LyricsList.SelectedIndex = index;
        if (index >= 0) LyricsList.ScrollIntoView(lyrics.Lines[Math.Min(index + 3, lyrics.Lines.Count - 1)]);
    }

    private void Lyrics_Click(object sender, RoutedEventArgs e)
    {
        LyricsPanel.Visibility = Vis(LyricsPanel.Visibility != Visibility.Visible);
        if (LyricsPanel.Visibility != Visibility.Visible) return;
        if (_now is null) LyricsInfo.Text = "Reproduce una canción para ver su letra.";
        else _ = LoadLyricsAsync();
    }

    private void Lyrics_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LyricLine { Time: >= 0 } line) return;
        if (_mode == Mode.YouTube)
        {
            _ytTime = line.Time;
            RunPlayerScript($"cmd('seek', {line.Time.ToString("0.##", CultureInfo.InvariantCulture)})");
        }
        else if (_mode == Mode.Native) _player.Seek(TimeSpan.FromSeconds(line.Time));
    }

    // ===================== Listas de reproducción =====================

    private void RebuildPlaylistNav()
    {
        foreach (var old in Nav.MenuItems.OfType<NavigationViewItem>()
                     .Where(i => (i.Tag as string)?.StartsWith("pl:", StringComparison.Ordinal) == true).ToList())
            Nav.MenuItems.Remove(old);
        var at = Nav.MenuItems.IndexOf(NewListItem);
        foreach (var list in _playlists.All)
            Nav.MenuItems.Insert(at++, new NavigationViewItem { Content = list.Name, Tag = "pl:" + list.Id, Icon = new SymbolIcon(Symbol.MusicInfo) });
    }

    private void SelectNav(string tag)
    {
        if (Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (i.Tag as string) == tag) is { } item)
            Nav.SelectedItem = item;
    }

    private async Task CreatePlaylistAsync(bool addCurrent)
    {
        var box = new TextBox { PlaceholderText = "Nombre de la lista", MaxLength = 60 };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Nueva lista", Content = box,
            PrimaryButtonText = "Crear", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ElementTheme.Dark,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(box.Text)) return;
        var list = _playlists.Create(box.Text);
        if (addCurrent && _now is not null) _playlists.Toggle(list.Id, _now.ToSaved());
        RebuildPlaylistNav();
        SelectNav("pl:" + list.Id);
    }

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer?.Tag as string == "newlist") _ = CreatePlaylistAsync(addCurrent: false);
    }

    private void PlaylistMenu_Opening(object? sender, object e)
    {
        PlaylistMenu.Items.Clear();
        if (_now is not { } card)
        {
            PlaylistMenu.Items.Add(new MenuFlyoutItem { Text = "Reproduce algo para añadirlo a una lista", IsEnabled = false });
            return;
        }
        var saved = card.ToSaved();
        foreach (var list in _playlists.All)
        {
            var entry = new ToggleMenuFlyoutItem { Text = list.Name, IsChecked = _playlists.Contains(list.Id, saved) };
            var id = list.Id;
            entry.Click += (_, _) =>
            {
                _playlists.Toggle(id, saved);
                if (_kind == "pl:" + id) ApplyFilter();
            };
            PlaylistMenu.Items.Add(entry);
        }
        if (_playlists.All.Count > 0) PlaylistMenu.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = "Nueva lista con esta canción…", Icon = new SymbolIcon(Symbol.Add) };
        create.Click += (_, _) => _ = CreatePlaylistAsync(addCurrent: true);
        PlaylistMenu.Items.Add(create);
    }

    private void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        if (_saved.Count > 0) Play(_saved[0], _saved);
    }

    private async void DeleteList_Click(object sender, RoutedEventArgs e)
    {
        if (!_kind.StartsWith("pl:", StringComparison.Ordinal) || _playlists.Find(_kind[3..]) is not { } list) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = $"¿Eliminar «{list.Name}»?",
            Content = "Se borra la lista, no las canciones guardadas en Mi biblioteca.",
            PrimaryButtonText = "Eliminar", CloseButtonText = "Cancelar", RequestedTheme = ElementTheme.Dark,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _playlists.Delete(list.Id);
        RebuildPlaylistNav();
        SelectNav("all");
    }

    private void Genre_Click(object sender, RoutedEventArgs e)
    {
        _genre = (string)((Button)sender).Tag;
        ApplyFilter();
        _ = SearchYouTubeAsync(Search.Text);
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

        FollowLyrics(pos.TotalSeconds);
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

    /// <summary>
    /// Muestra u oculta el vídeo de YouTube. Al ocultarlo NO se colapsa (WebView2 suspendería la
    /// reproducción): se deja vivo en 2×2 px y transparente para que el sonido siga o pueda empezar.
    /// </summary>
    private void ShowYouTubeLayer(bool show)
    {
        _ytLayerShown = show;
        YouTubeLayer.Width = show ? double.NaN : 2;
        YouTubeLayer.Height = show ? double.NaN : 2;
        YouTubeLayer.Opacity = show ? 1 : 0;
        YouTubeLayer.IsHitTestVisible = show;
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
                    ShowYouTubeLayer(false);
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
        ShowYouTubeLayer(false);
        if (_catalog is null) return;
        ApplyFilter();
        _ = SearchYouTubeAsync(Search.Text);
    }

    private void Search_TextChanged(AutoSuggestBox s, AutoSuggestBoxTextChangedEventArgs e)
    {
        VideoLayer.Visibility = Visibility.Collapsed;
        ShowYouTubeLayer(false);
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
        ShowYouTubeLayer(false);
    }

    private void ShowVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == Mode.YouTube) ShowYouTubeLayer(!_ytLayerShown);
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
