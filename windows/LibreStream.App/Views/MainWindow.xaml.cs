using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using LibreStream.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace LibreStream.App;

public sealed partial class MainWindow : Window
{
    private const string CatalogUrl = "https://vhkp259r94-cmyk.github.io/spotios/catalog.json";
    private const string PlayerHost = "app.librestream";

    private enum Mode { None, Native, YouTube }

    private sealed record Shelf(string Title, List<MediaCard> Cards);

    private static readonly string DataDir = App.DataDir;

    private readonly AudioPlayerManager _player = AudioPlayerManager.Shared;
    private readonly CatalogService _catalog = new(new Uri(CatalogUrl), DataDir);
    private readonly LibraryStore _library = new(DataDir);
    private readonly PlaylistStore _playlists = new(DataDir);
    private readonly SettingsStore _settings = new(DataDir);
    private readonly DownloadStore _downloads = new(DataDir);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _dl = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _heroTimer = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    // Listas visibles
    private List<MediaCard> _youtube = [];
    private List<MediaCard> _saved = [];
    private List<MediaCard> _releases = [];
    private List<Shelf> _shelves = [];
    private bool _shelvesLoading, _shelvesLoaded;
    private MediaCard? _featured;
    private int _heroIndex;
    private string _kind = "all";
    private string _scope = "all";            // filtro de Inicio: all | track | video
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

    // Reproductor de YouTube embebido y ecualizador
    private bool _ytReady, _ytPlaying;
    private string? _ytPending;
    private double _ytTime, _ytDuration;
    private int _errorStreak;   // saltos automáticos seguidos por error (máximo 3)
    private readonly List<CoreWebView2Frame> _ytFrames = [];
    private string _eqScript = "";
    private bool _loadingUi;
    private int _eqTick;

    /// <summary>Qué se muestra: en Música solo música, en Vídeos solo vídeos; en Inicio lo decide el filtro.</summary>
    private string Scope => _kind switch { "track" => "track", "video" => "video", "all" => _scope, _ => "all" };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico")); } catch (Exception ex) { App.Log("Icono de la ventana", ex); }
        SetTitleBar(TitleBar);

        // Tamaño inicial: 85 % del área de trabajo, centrada (nunca detrás de la barra de tareas)
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        int w = (int)(area.Width * 0.85), h = (int)(area.Height * 0.88);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));

        AdBlock.Enabled = _settings.Current.AdBlock;
        Volume.Value = _settings.Current.Volume;
        Video.SetMediaPlayer(_player.Player);
        _player.Volume = Volume.Value / 100;
        _player.StateChanged += () => DispatcherQueue.TryEnqueue(OnNativeStateChanged);

        _timer.Tick += (_, _) => Safe("Reloj", () =>
        {
            UpdateProgress();
            if (_kind == "settings" && ++_eqTick % 4 == 0) _ = RefreshEqStatusAsync();
        });
        _timer.Start();
        _heroTimer.Tick += (_, _) => Safe("Destacado", () =>
        {
            if (_releases.Count > 1 && Hero.Visibility == Visibility.Visible)
            {
                _heroIndex = (_heroIndex + 1) % _releases.Count;
                _featured = _releases[_heroIndex];
                UpdateHero();
            }
        });
        _heroTimer.Start();
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); _ = SearchYouTubeAsync(Search.Text); };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Safe("Guardar ajustes", _settings.Save); };
        Closed += (_, _) => Safe("Cierre", () => { _settings.Save(); _timer.Stop(); _heroTimer.Stop(); });

        ShowYouTubeLayer(false);
        RebuildPlaylistNav();
        BuildChips(Genres, GenreNames, Genre_Click);
        BuildChips(ScopeChips, ["Todo", "Música", "Vídeos"], Scope_Click, ["all", "track", "video"]);
        BuildEqUi();
        LookBox.ItemsSource = Themes.All.Select(t => t.Name).ToList();
        ApplyLook(_settings.Current.Look);
        StartKenBurns();
        _ = LoadAsync();
        _ = InitYouTubePlayerAsync();
    }

    /// <summary>Ejecuta una acción de interfaz anotando cualquier fallo en vez de cerrar la app.</summary>
    private static void Safe(string origin, Action action)
    {
        try { action(); }
        catch (Exception ex) { App.LogOnce(origin, ex); }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static void BuildChips(StackPanel host, string[] labels, RoutedEventHandler click, string[]? tags = null)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            var chip = new Button { Content = labels[i], Tag = tags?[i] ?? labels[i], CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 6) };
            chip.Click += click;
            host.Children.Add(chip);
        }
    }

    private static void Highlight(StackPanel host, string selected)
    {
        foreach (var chip in host.Children.OfType<Button>())
            chip.Style = (Style)Application.Current.Resources[(string)chip.Tag == selected ? "AccentButtonStyle" : "DefaultButtonStyle"];
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
            App.LogOnce("Catálogo", ex); // sin conexión: se usa la copia local si la hay
        }
        ApplyFilter();
        _ = LoadReleasesAsync();
        _ = SearchYouTubeAsync(Search.Text);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Browse.ChangeView(null, 0, null, true));
    }

    private void ShowStatus(string? text)
    {
        Status.Text = text ?? "";
        Status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyFilter()
    {
        var q = MetadataMatcher.Fold(Search.Text ?? "");
        var raw = Search.Text?.Trim() ?? "";
        bool Match(string text) => q.Length == 0 || MetadataMatcher.Fold(text).Contains(q);
        var playlist = _kind.StartsWith("pl:", StringComparison.Ordinal) ? _playlists.Find(_kind[3..]) : null;
        var library = _kind == "library" || playlist is not null;
        var special = _kind is "settings" or "downloads";
        var scope = Scope;

        SettingsSection.Visibility = Vis(_kind == "settings");
        DownloadsSection.Visibility = Vis(_kind == "downloads");
        ScopeChips.Visibility = Vis(_kind == "all");
        Highlight(ScopeChips, _scope);
        Highlight(Genres, _genre);
        Genres.Visibility = Vis(_kind == "track" && q.Length == 0);
        PlaylistActions.Visibility = Vis(playlist is not null);
        LibraryActions.Visibility = Vis(_kind == "library");
        ShelvesHost.Visibility = Vis(_kind == "video" && q.Length == 0);

        // Título y ayuda de cada apartado
        (PageTitle.Text, var hint) = _kind switch
        {
            "track" => ("Música", q.Length > 0 ? $"Canciones para «{raw}». Solo música, sin vídeo." : "Elige un género o busca un artista. Aquí solo hay música y suena sin vídeo."),
            "video" => ("Vídeos", q.Length > 0 ? $"Vídeos para «{raw}». Solo vídeos." : "Eventos, series, YouTubers y más. Busca para encontrar cualquier vídeo."),
            "library" => ("Mi biblioteca", q.Length > 0 ? $"Guardados que coinciden con «{raw}»." : "Todo lo que has guardado con el corazón."),
            "downloads" => ("Descargas", "Contenido libre que puedes ver sin conexión."),
            "settings" => ("Ajustes", "Ecualizador, descargas y privacidad."),
            _ => ("Inicio", q.Length > 0 ? $"{(scope == "track" ? "Canciones" : scope == "video" ? "Vídeos" : "Resultados")} para «{raw}»." : ""),
        };
        if (playlist is not null)
        {
            PageTitle.Text = playlist.Name;
            hint = playlist.Items.Count == 1 ? "Lista · 1 elemento" : $"Lista · {playlist.Items.Count} elementos";
        }
        PageHint.Text = hint;
        PageHint.Visibility = Vis(hint.Length > 0);

        if (special)
        {
            SavedSection.Visibility = YouTubeSection.Visibility = Visibility.Collapsed;
            SavedEmpty.Visibility = Visibility.Collapsed;
            Hero.Visibility = Visibility.Collapsed;
            _featured = null;
            if (_kind == "downloads") BuildDownloads(); else RefreshSettingsUi();
            return;
        }

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
        if (!showYouTube || !_youtubeQuery.StartsWith($"{_kind}|{scope}|", StringComparison.Ordinal)) { _youtube = []; _youtubeQuery = ""; }

        YouTubeTitle.Text = (_kind, q.Length > 0) switch
        {
            ("track", false) => _genre,
            ("track", true) => "Música en YouTube",
            ("all", false) => scope switch { "track" => "Canciones del momento", "video" => "Videoclips del momento", _ => "Éxitos del momento" },
            _ => scope == "track" ? "Música en YouTube" : scope == "video" ? "Vídeos de YouTube" : "Resultados en YouTube",
        };
        SavedTitle.Text = playlist is not null ? "En esta lista" : library ? (q.Length > 0 ? "Resultados" : "Guardado") : "De tu biblioteca";
        var template = (DataTemplate)Root.Resources[scope == "track" ? "TrackCard" : "VideoCard"];
        if (!ReferenceEquals(YouTubeRepeater.ItemTemplate, template)) YouTubeRepeater.ItemTemplate = template;

        SavedRepeater.ItemsSource = _saved;
        YouTubeRepeater.ItemsSource = _youtube;
        SavedSection.Visibility = Vis(library || (home && _saved.Count > 0));
        SavedEmpty.Text = q.Length > 0 ? "Nada guardado coincide con la búsqueda."
            : playlist is not null ? "Lista vacía. Reproduce algo y pulsa + en la barra inferior para añadirlo."
            : "Aún no has guardado nada. Reproduce algo y pulsa el corazón de la barra inferior.";
        SavedEmpty.Visibility = Vis(library && _saved.Count == 0);
        YouTubeSection.Visibility = Vis(showYouTube);

        // Anuncio de lanzamientos nuevos: solo en Inicio y sin búsqueda
        _featured = home && _releases.Count > 0 ? _releases[_heroIndex % _releases.Count] : null;
        Hero.Visibility = Vis(_featured is not null);
        UpdateHero();
    }

    // ===================== Anuncio de lanzamientos =====================

    /// <summary>Canciones publicadas esta semana: mezcla búsqueda en español y en inglés.</summary>
    private async Task LoadReleasesAsync()
    {
        if (_releases.Count > 0) return;
        try
        {
            var latin = await YouTubeSearch.SearchAsync(_http, "nuevo lanzamiento oficial video musical", default, thisWeek: true);
            var world = await YouTubeSearch.SearchAsync(_http, "new music official video", default, thisWeek: true);
            var mixed = new List<YouTubeVideo>();
            for (var i = 0; i < Math.Max(latin.Count, world.Count); i++)
            {
                if (i < world.Count) mixed.Add(world[i]);
                if (i < latin.Count) mixed.Add(latin[i]);
            }
            _releases = mixed.Where(v => ContentFilter.IsSong(v) && v.DurationSec <= 480).DistinctBy(v => v.Id).Take(6).Select(v => new MediaCard(v)).ToList();
            _heroIndex = 0;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            App.LogOnce("Lanzamientos", ex); // sin conexión: simplemente no se muestra el anuncio
        }
    }

    private void UpdateHero()
    {
        if (_featured is not { } card) return;
        HeroTile.Background = card.Tile;
        var image = new BitmapImage(new Uri($"https://i.ytimg.com/vi/{card.Id}/maxresdefault.jpg"));
        image.ImageFailed += (_, _) => HeroImage.Source = new BitmapImage(new Uri($"https://i.ytimg.com/vi/{card.Id}/hqdefault.jpg"));
        HeroImage.Source = image;
        HeroLabel.Text = string.IsNullOrEmpty(card.Published) ? "NUEVO LANZAMIENTO" : $"NUEVO LANZAMIENTO · {card.Published.ToUpperInvariant()}";
        var (_, title) = LyricsService.GuessTrack(card.Title, card.Subtitle);
        HeroTitle.Text = title;
        HeroSubtitle.Text = $"{card.Subtitle} · {card.DurationText}";
        HeroDots.Children.Clear();
        for (var i = 0; i < _releases.Count; i++)
            HeroDots.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(i == _heroIndex % _releases.Count ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(90, 255, 255, 255)),
            });
    }

    // ===================== Búsqueda en YouTube =====================

    private async Task SearchYouTubeAsync(string? text)
    {
        var typed = (text ?? "").Trim();
        var kind = _kind;
        var scope = Scope;
        if (kind is "library" or "settings" or "downloads" || kind.StartsWith("pl:", StringComparison.Ordinal) || typed.Length == 1) return;
        if (kind == "video" && typed.Length == 0) { _ = LoadShelvesAsync(); return; }

        // Sin texto: Música muestra el género elegido e Inicio los éxitos del momento
        var hits = $"official music video {DateTime.Now.Year}";
        var query = typed.Length > 0 ? (scope == "track" ? typed + " audio" : typed)
            : kind == "track" && _genre != "Éxitos" ? $"{_genre} official music video"
            : hits;
        var key = $"{kind}|{scope}|{query}";
        if (key == _youtubeQuery) return;

        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        YouTubeBusy.IsActive = true;
        YouTubeInfo.Text = "";
        try
        {
            List<YouTubeVideo>? results = null;
            if (scope == "track")
            {
                // Música: solo canciones de YouTube Music (versiones de audio: sin intros ni charla)
                var musicQuery = typed.Length > 0 ? typed : YouTubeMusic.GenreQuery(kind == "track" ? _genre : "Éxitos", DateTime.Now.Year);
                try { results = (await YouTubeMusic.SearchSongsAsync(_http, musicQuery, cts.Token)).Where(ContentFilter.IsSong).ToList(); }
                catch (Exception ex) when (ex is not OperationCanceledException) { App.LogOnce("YouTube Music", ex); }
                if (results is { Count: 0 }) results = null;   // si YouTube Music no da nada, se usa la búsqueda normal
            }
            results ??= await YouTubeSearch.SearchAsync(_http, query, cts.Token, videosOnly: true);
            if (cts.IsCancellationRequested || kind != _kind || scope != Scope) return;
            results = scope == "track" ? results.Where(ContentFilter.IsSong).ToList()
                : scope == "video" ? results.Where(ContentFilter.IsVideo).ToList()
                : typed.Length == 0 ? results.Where(ContentFilter.IsSong).ToList()
                : results.Where(v => !v.IsLive).ToList();
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

    // ===================== Estanterías de Vídeos =====================

    private async Task LoadShelvesAsync()
    {
        if (_shelvesLoaded) { BuildShelves(); return; }
        if (_shelvesLoading) return;
        _shelvesLoading = true;
        ShelvesBusy.IsActive = true;
        ShelvesInfo.Text = "Cargando vídeos…";
        try
        {
            using var gate = new SemaphoreSlim(3); // como mucho 3 búsquedas a la vez
            async Task<IReadOnlyList<YouTubeVideo>> One(string query)
            {
                await gate.WaitAsync();
                try { return await YouTubeSearch.SearchAsync(_http, query, default, videosOnly: true); }
                catch (Exception ex) { App.LogOnce("Estantería " + query, ex); return []; }
                finally { gate.Release(); }
            }

            var built = await Task.WhenAll(VideoShelves.Default.Select(async shelf =>
            {
                var results = await Task.WhenAll(shelf.Queries.Select(One));
                return new Shelf(shelf.Title, VideoShelves.Merge(results, 10).Select(v => new MediaCard(v)).ToList());
            }));
            _shelves = built.Where(s => s.Cards.Count > 0).ToList();
            _shelvesLoaded = _shelves.Count > 0;
            ShelvesInfo.Text = _shelves.Count == 0 ? "No se pudieron cargar los vídeos. Comprueba la conexión." : "";
        }
        finally
        {
            _shelvesLoading = false;
            ShelvesBusy.IsActive = false;
        }
        BuildShelves();
    }

    private void BuildShelves()
    {
        while (ShelvesHost.Children.Count > 2) ShelvesHost.Children.RemoveAt(2);
        var template = (DataTemplate)Root.Resources["VideoCard"];
        foreach (var shelf in _shelves)
        {
            var row = new ItemsRepeater
            {
                ItemTemplate = template,
                ItemsSource = shelf.Cards,
                Layout = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8 },
            };
            var scroller = new ScrollViewer
            {
                Content = row,
                HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(new TextBlock { Text = shelf.Title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(8, 8, 0, 0) });
            panel.Children.Add(scroller);
            ShelvesHost.Children.Add(panel);
        }
    }


    // ===================== Cola de reproducción =====================

    private static bool SameCard(MediaCard a, MediaCard b) => a.Source == b.Source && a.Id == b.Id;

    private void QueueCard(MediaCard card, bool next)
    {
        if (!card.IsPlayable) return;
        if (_now is null || _queue.Count == 0) { Play(card, [card]); ShowStatus(null); return; }
        if (_mode != Mode.YouTube || card.YouTubeId is null)
        {
            ShowStatus("La cola solo admite canciones y vídeos de YouTube mientras suena YouTube.");
            return;
        }
        QueueOps.Add(_queue, ref _index, card, next, SameCard);
        ShowStatus(next ? $"Sonará a continuación: {card.Title}" : $"Añadida a la cola: {card.Title}");
    }

    private void Card_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MediaCard card) return;
        var menu = new MenuFlyout();
        var next = new MenuFlyoutItem { Text = "Reproducir a continuación", Icon = new SymbolIcon(Symbol.Play) };
        next.Click += (_, _) => QueueCard(card, true);
        var end = new MenuFlyoutItem { Text = "Añadir al final de la cola", Icon = new SymbolIcon(Symbol.Add) };
        end.Click += (_, _) => QueueCard(card, false);
        menu.Items.Add(next);
        menu.Items.Add(end);
        menu.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        e.Handled = true;
    }

    private Flyout? _queueFlyout;

    private void Queue_Click(object sender, RoutedEventArgs e)
    {
        _queueFlyout = new Flyout();
        BuildQueueFlyout();
        _queueFlyout.ShowAt((FrameworkElement)sender);
    }

    private void BuildQueueFlyout()
    {
        if (_queueFlyout is null) return;
        var panel = new StackPanel { Spacing = 6, MinWidth = 340, MaxWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = _index >= 0 && _index < _queue.Count ? $"Suena ahora: {_queue[_index].Title}" : "No hay nada sonando.",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var list = new StackPanel { Spacing = 2 };
        for (var i = _index + 1; i < _queue.Count; i++)
        {
            var pos = i;
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var c = 0; c < 3; c++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var jump = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            jump.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = _queue[pos].Title, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = _queue[pos].Subtitle, FontSize = 12, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            };
            jump.Click += (_, _) => { _index = pos; StartCurrent(); BuildQueueFlyout(); };
            Button Small(string glyph, string name, bool enabled, Action act)
            {
                var b = new Button { Content = glyph, MinWidth = 36, Padding = new Thickness(4), IsEnabled = enabled };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, name);
                b.Click += (_, _) => { act(); BuildQueueFlyout(); };
                return b;
            }
            var up = Small("▲", "Subir", pos > _index + 1, () => QueueOps.Move(_queue, _index, pos, -1));
            var down = Small("▼", "Bajar", pos < _queue.Count - 1, () => QueueOps.Move(_queue, _index, pos, 1));
            var del = Small("✕", "Quitar", true, () => QueueOps.Remove(_queue, ref _index, pos));
            Grid.SetColumn(jump, 0); Grid.SetColumn(up, 1); Grid.SetColumn(down, 2); Grid.SetColumn(del, 3);
            row.Children.Add(jump); row.Children.Add(up); row.Children.Add(down); row.Children.Add(del);
            list.Children.Add(row);
        }
        if (list.Children.Count == 0)
            panel.Children.Add(new TextBlock { Text = "No hay más canciones en la cola. Clic derecho en una tarjeta para añadirla.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65 });
        else
        {
            panel.Children.Add(new ScrollViewer { MaxHeight = 320, Content = list });
            var clear = new Button { Content = "Vaciar" };
            clear.Click += (_, _) => { QueueOps.ClearUpcoming(_queue, _index); BuildQueueFlyout(); };
            panel.Children.Add(clear);
        }
        _queueFlyout.Content = panel;
    }

    // ===================== Reproducción (cola unificada) =====================

    private void Play(MediaCard card, List<MediaCard> list)
    {
        if (!card.IsPlayable) return;
        _audioOnly = Scope == "track";
        _errorStreak = 0;
        _queue = list.Where(c => c.IsPlayable).ToList();
        _index = _queue.FindIndex(c => c.Source == card.Source && c.Id == card.Id);
        if (_index < 0) { _queue = [card]; _index = 0; }
        StartCurrent();
    }

    /// <summary>Si el elemento del catálogo está descargado, se reproduce desde el disco.</summary>
    private CatalogItem Resolve(CatalogItem item) =>
        _downloads.Find(item.Id) is { } d && File.Exists(d.FilePath) && item.Sources.Count > 0
            ? item with { Sources = [item.Sources[0] with { Url = new Uri(d.FilePath), Provider = Provider.Direct }] }
            : item;

    private void StartCurrent()
    {
        if (_index < 0 || _index >= _queue.Count) return;
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
            _player.Play(natives.Select(c => Resolve(c.Item!)), Math.Max(0, natives.IndexOf(card)));
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
        UpdateAmbient();
        UpdateNowSheet();
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
            : LyricsService.GuessTrack(card.Title, card.Subtitle.Split(" · ")[0]);
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
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (i.Tag as string) == tag);
        if (item is not null) Nav.SelectedItem = item;
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
        var menu = sender as MenuFlyout ?? PlaylistMenu;
        menu.Items.Clear();
        if (_now is not { } card)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "Reproduce algo para añadirlo a una lista", IsEnabled = false });
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
            menu.Items.Add(entry);
        }
        if (_playlists.All.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = "Nueva lista con esta canción…", Icon = new SymbolIcon(Symbol.Add) };
        create.Click += (_, _) => _ = CreatePlaylistAsync(addCurrent: true);
        menu.Items.Add(create);
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

    private void Scope_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((Button)sender).Tag;
        // En Inicio, «Música» y «Vídeos» llevan directamente a su apartado
        if (_kind == "all" && tag is "track" or "video"
            && Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag) is { } target)
        {
            Nav.SelectedItem = target;
            return;
        }
        _scope = tag;
        ApplyFilter();
        _ = SearchYouTubeAsync(Search.Text);
    }

    private void UpdateProgress()
    {
        TimeSpan pos, total;
        if (_mode == Mode.YouTube)
        {
            pos = TimeSpan.FromSeconds(Clean(_ytTime));
            total = TimeSpan.FromSeconds(Clean(_ytDuration));
        }
        else
        {
            pos = _player.Position;
            total = _player.Duration;
            if (total <= TimeSpan.Zero && _now is not null) total = TimeSpan.FromSeconds(Clean(_now.DurationSec));
        }

        _updatingProgress = true;
        Progress.Maximum = Math.Max(1, total.TotalSeconds);
        Progress.Value = Math.Min(pos.TotalSeconds, Progress.Maximum);
        _updatingProgress = false;

        FollowLyrics(pos.TotalSeconds);
        SyncSheetAndBars(pos, total);
        Elapsed.Text = MediaCard.Format(pos);
        Total.Text = MediaCard.Format(total);

        static double Clean(double seconds) => double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 360_000) : 0;
    }

    // ===================== Reproductor de YouTube embebido =====================

    private async Task InitYouTubePlayerAsync()
    {
        try
        {
            _eqScript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "eq.js"));
            await YouTubePlayer.EnsureCoreWebView2Async();
            var core = YouTubePlayer.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(PlayerHost, Path.Combine(AppContext.BaseDirectory, "Assets"),
                CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.WebMessageReceived += OnPlayerMessage;
            // Bloqueo de anuncios y rastreadores a nivel de red (lista en Core/Services/Privacy/AdBlock.cs)
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => Safe("Bloqueo de anuncios", () =>
            {
                if (AdBlock.IsBlocked(e.Request.Uri))
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            });
            core.NewWindowRequested += (_, e) => e.Handled = true; // sin ventanas emergentes
            core.ProcessFailed += (_, e) => Safe("WebView2", () => OnPlayerProcessFailed(e));
            // Ecualizador: se inyecta en el marco (iframe) del reproductor de YouTube
            core.FrameCreated += (_, e) => Safe("Marco", () =>
            {
                var frame = e.Frame;
                _ytFrames.Add(frame);
                frame.Destroyed += (_, _) => _ytFrames.Remove(frame);
                frame.NavigationCompleted += async (_, _) =>
                {
                    try { await frame.ExecuteScriptAsync(_eqScript); PushEq(); }
                    catch (Exception ex) { App.LogOnce("Ecualizador (marco)", ex); }
                };
            });
            YouTubePlayer.Source = new Uri($"https://{PlayerHost}/player.html");
        }
        catch (Exception ex)
        {
            App.Log("Reproductor de YouTube", ex);
            ShowStatus($"No se pudo iniciar el reproductor de YouTube (¿falta WebView2 Runtime?): {ex.Message}");
        }
    }

    private void OnPlayerProcessFailed(CoreWebView2ProcessFailedEventArgs e)
    {
        App.Log("WebView2 " + e.ProcessFailedKind, null);
        _ytReady = false;
        _ytFrames.Clear();
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            if (_mode == Mode.YouTube && _now?.YouTubeId is { } id) _ytPending = id; // al reiniciarse, retoma lo que sonaba
            YouTubePlayer.CoreWebView2?.Reload();
        }
        else ShowStatus("El reproductor de YouTube se detuvo. Reinicia la aplicación para recuperarlo.");
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
        try
        {
            if (_ytReady && YouTubePlayer.CoreWebView2 is not null) _ = YouTubePlayer.ExecuteScriptAsync(script);
        }
        catch (Exception ex) { App.LogOnce("Script del reproductor", ex); }
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
                    if (_ytPlaying && _ytTime > 1) _errorStreak = 0;   // ya suena: se acaba la racha de errores
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
                    _errorStreak++;
                    if (_errorStreak <= 3 && _index + 1 < _queue.Count) { _index++; StartCurrent(); }
                    else
                    {
                        if (_errorStreak > 3) ShowStatus("YouTube no deja reproducir estos vídeos ahora mismo. Prueba con otro o inténtalo más tarde.");
                        UpdateNowPlayingBar();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            App.LogOnce("Mensaje del reproductor", ex); // mensaje con formato inesperado: se ignora
        }
    }

    // ===================== Ecualizador =====================

    private void BuildEqUi()
    {
        EqPresetBox.ItemsSource = EqPresets.All.Keys.Append(EqPresets.Custom).ToList();
        for (var i = 0; i < AppSettings.BandCount; i++)
        {
            var slider = new Slider
            {
                Orientation = Orientation.Vertical, Height = 150, Minimum = -AppSettings.MaxGainDb, Maximum = AppSettings.MaxGainDb,
                StepFrequency = 1, Tag = i, HorizontalAlignment = HorizontalAlignment.Center, IsThumbToolTipEnabled = false,
            };
            slider.ValueChanged += EqBand_Changed;
            var value = new TextBlock { Text = "0", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.8 };
            var label = new TextBlock { Text = EqPresets.BandLabels[i], FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.65 };
            var column = new StackPanel { Spacing = 6, Width = 44 };
            column.Children.Add(value);
            column.Children.Add(slider);
            column.Children.Add(label);
            EqBandsHost.Children.Add(column);
        }
    }

    private Slider EqSlider(int i) => (Slider)((StackPanel)EqBandsHost.Children[i]).Children[1];
    private TextBlock EqValue(int i) => (TextBlock)((StackPanel)EqBandsHost.Children[i]).Children[0];

    private void RefreshSettingsUi()
    {
        _loadingUi = true;
        var s = _settings.Current;
        EqToggle.IsOn = s.EqEnabled;
        EqPresetBox.SelectedItem = s.EqPreset;
        for (var i = 0; i < AppSettings.BandCount; i++)
        {
            EqSlider(i).Value = s.EqBands[i];
            EqValue(i).Text = s.EqBands[i].ToString("+0;-0;0", CultureInfo.InvariantCulture);
        }
        DownloadFolderText.Text = "Carpeta: " + _settings.DownloadFolder;
        AdBlockToggle.IsOn = s.AdBlock;
        LookBox.SelectedIndex = Math.Max(0, Array.FindIndex(Themes.All, t => t.Id == s.Look));
        _loadingUi = false;
        _ = RefreshEqStatusAsync();
    }

    private void EqToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        _settings.Current.EqEnabled = EqToggle.IsOn;
        ScheduleSave();
        PushEq();
        _ = RefreshEqStatusAsync();
    }

    private void EqPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || EqPresetBox.SelectedItem is not string name) return;
        _settings.Current.EqPreset = name;
        if (EqPresets.All.TryGetValue(name, out var bands)) _settings.Current.EqBands = (double[])bands.Clone();
        ScheduleSave();
        RefreshSettingsUi();
        PushEq();
    }

    private void EqBand_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loadingUi || sender is not Slider { Tag: int i }) return;
        _settings.Current.EqBands[i] = e.NewValue;
        _settings.Current.EqPreset = EqPresets.Match(_settings.Current.EqBands);
        EqValue(i).Text = e.NewValue.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        _loadingUi = true;
        EqPresetBox.SelectedItem = _settings.Current.EqPreset;
        _loadingUi = false;
        ScheduleSave();
        PushEq();
    }

    /// <summary>Envía las ganancias actuales a todos los marcos de YouTube abiertos.</summary>
    private void PushEq()
    {
        var s = _settings.Current;
        var script = $"window.__lsEq && window.__lsEq({JsonSerializer.Serialize(s.EqBands)}, {(s.EqEnabled ? "true" : "false")})";
        foreach (var frame in _ytFrames.ToList())
        {
            try { _ = frame.ExecuteScriptAsync(script); }
            catch (Exception ex) { App.LogOnce("Ecualizador", ex); }
        }
    }

    private async Task RefreshEqStatusAsync()
    {
        if (_kind != "settings") return;
        if (!_settings.Current.EqEnabled) { EqStatus.Text = "Ecualizador desactivado."; return; }
        var frames = _ytFrames.ToList();
        if (frames.Count == 0) { EqStatus.Text = "Se aplicará cuando reproduzcas algo de YouTube."; return; }
        foreach (var frame in frames)
        {
            try
            {
                var raw = await frame.ExecuteScriptAsync("window.__lsEqStatus ? window.__lsEqStatus() : null");
                if (raw is null or "null") continue;
                using var doc = JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)!);
                var r = doc.RootElement;
                var level = r.GetProperty("level").GetDouble();
                if (r.GetProperty("attached").GetBoolean())
                    EqStatus.Text = level > 0 ? $"Activo: el sonido de YouTube pasa por el ecualizador (nivel {level:0})."
                        : "Conectado al reproductor; esperando sonido.";
                else
                    EqStatus.Text = r.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                        ? $"No se pudo conectar: {err.GetString()}" : "Esperando a que empiece el sonido de YouTube…";
                return;
            }
            catch (Exception ex) { App.LogOnce("Estado del ecualizador", ex); }
        }
        EqStatus.Text = "Esperando a que empiece el sonido de YouTube…";
    }

    // ===================== Ajustes de descargas y privacidad =====================

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            _settings.Current.DownloadFolder = folder.Path;
            _settings.Save();
            RefreshSettingsUi();
        }
        catch (Exception ex) { App.Log("Carpeta de descargas", ex); }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_settings.DownloadFolder);
            Process.Start(new ProcessStartInfo { FileName = _settings.DownloadFolder, UseShellExecute = true });
        }
        catch (Exception ex) { App.Log("Abrir carpeta", ex); }
    }

    private void AdBlock_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        _settings.Current.AdBlock = AdBlockToggle.IsOn;
        AdBlock.Enabled = AdBlockToggle.IsOn;
        ScheduleSave();
    }

    // ===================== Descargas =====================

    private static string FormatBytes(long bytes) =>
        bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.0} GB" : bytes >= 1_000_000 ? $"{bytes / 1e6:0} MB" : $"{bytes / 1e3:0} KB";

    private static Grid MakeRow(string title, string detail, string? image, out StackPanel actions)
    {
        var row = new Grid
        {
            ColumnSpacing = 12, Padding = new Thickness(8), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 255, 255, 255)),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var picture = new Image { Width = 96, Height = 54, Stretch = Stretch.UniformToFill };
        if (Uri.TryCreate(image, UriKind.Absolute, out var uri)) picture.Source = new BitmapImage(uri);
        row.Children.Add(picture);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 12, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return row;
    }

    private void BuildDownloads()
    {
        DownloadedHost.Children.Clear();
        AvailableHost.Children.Clear();
        foreach (var d in _downloads.Items)
        {
            var row = MakeRow(d.Title, $"{d.Artist} · {MediaCard.Format(TimeSpan.FromSeconds(d.DurationSec))} · {FormatBytes(d.Bytes)}", d.ArtworkUrl, out var actions);
            var play = new Button { Content = "Reproducir" };
            var done = d;
            play.Click += (_, _) =>
            {
                if (_catalog.Items.FirstOrDefault(i => i.Id == done.Id) is { } item) { var card = new MediaCard(item); _scope = "video"; Play(card, [card]); }
            };
            var remove = new Button { Content = "Eliminar" };
            remove.Click += (_, _) => { _downloads.Remove(done.Id); BuildDownloads(); };
            actions.Children.Add(play);
            actions.Children.Add(remove);
            DownloadedHost.Children.Add(row);
        }
        DownloadsEmpty.Visibility = Vis(_downloads.Items.Count == 0);

        foreach (var item in _catalog.Items.Where(i => _downloads.Find(i.Id) is null))
        {
            var row = MakeRow(item.Title, $"{item.Artist} · {MediaCard.Format(TimeSpan.FromSeconds(item.DurationSec))} · {item.License.Spdx}", item.ArtworkUrl?.AbsoluteUri, out var actions);
            var bar = new ProgressBar { Width = 130, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
            var button = new Button { Content = "Descargar" };
            var target = item;
            button.Click += async (_, _) => await DownloadAsync(target, button, bar);
            actions.Children.Add(bar);
            actions.Children.Add(button);
            AvailableHost.Children.Add(row);
        }
        if (AvailableHost.Children.Count == 0)
            AvailableHost.Children.Add(new TextBlock { Text = "Ya has descargado todo el contenido libre disponible.", Opacity = 0.7, Margin = new Thickness(8, 0, 0, 0) });
    }

    private async Task DownloadAsync(CatalogItem item, Button button, ProgressBar bar)
    {
        var source = item.Sources.FirstOrDefault(s => s.IsDirectlyPlayable);
        if (source is null) return;
        button.IsEnabled = false;
        bar.Value = 0;
        bar.Visibility = Visibility.Visible;
        try
        {
            var ext = Path.GetExtension(source.Url.AbsolutePath);
            var destination = Path.Combine(_settings.DownloadFolder, FileDownloader.SafeFileName($"{item.Artist} - {item.Title}", string.IsNullOrEmpty(ext) ? ".mp4" : ext));
            var bytes = await FileDownloader.DownloadAsync(_dl, source.Url, destination, new Progress<double>(p => bar.Value = p * 100));
            _downloads.Add(new DownloadedItem(item.Id, item.Title, item.Artist, item.Kind.ToString().ToLowerInvariant(), destination, bytes, item.DurationSec, item.ArtworkUrl?.AbsoluteUri));
            if (_kind == "downloads") BuildDownloads();
            ShowStatus(null);
        }
        catch (Exception ex)
        {
            App.Log("Descarga " + item.Title, ex);
            button.IsEnabled = true;
            bar.Visibility = Visibility.Collapsed;
            button.Content = "Reintentar";
            ShowStatus($"No se pudo descargar «{item.Title}»: {ex.Message}");
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        SelectNav("downloads");
        ShowStatus(_now?.YouTubeId is not null
            ? "YouTube no permite descargar sus vídeos. En Descargas solo hay contenido libre (Creative Commons)."
            : null);
    }

    // ===================== Estilo: paleta, brillo ambiental y movimiento =====================

    private ThemePalette _palette = Themes.Find(null);
    private Storyboard? _bars;
    private bool _barsOn;

    private static Windows.UI.Color Hex(string hex, byte alpha = 255)
    {
        var v = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return Windows.UI.Color.FromArgb(alpha, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private static void SetBrush(string key, Windows.UI.Color color)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush) brush.Color = color;
    }

    /// <summary>Aplica un estilo: cambia los pinceles compartidos en caliente (todo lo que los usa se actualiza).</summary>
    private void ApplyLook(string id)
    {
        var t = _palette = Themes.Find(id);
        var classic = t.Id == "clasico";
        // Los botones de acento de Fluent toman su color de SystemAccentColor*: se cambian y se fuerza a releer el tema
        var res = Application.Current.Resources;
        res["SystemAccentColor"] = Hex(t.Accent);
        res["SystemAccentColorLight1"] = Hex(t.Accent);
        res["SystemAccentColorLight2"] = Hex(t.Accent);
        res["SystemAccentColorLight3"] = Hex(t.Accent);
        res["SystemAccentColorDark1"] = Hex(t.Accent2);
        res["SystemAccentColorDark2"] = Hex(t.Accent2);
        res["SystemAccentColorDark3"] = Hex(t.Accent2);
        var wanted = t.Dark ? ElementTheme.Dark : ElementTheme.Light;
        Root.RequestedTheme = wanted == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Root.RequestedTheme = wanted;
        SetBrush("AccentFillColorDefaultBrush", Hex(t.Accent));
        SetBrush("AccentFillColorSecondaryBrush", Hex(t.Accent, 0xE6));
        SetBrush("AccentFillColorTertiaryBrush", Hex(t.Accent, 0xCC));
        SetBrush("TextOnAccentFillColorPrimaryBrush", Hex(t.On));
        SetBrush("NsAccentBrush", Hex(t.Accent));
        SetBrush("NsBgBrush", Hex(t.Bg));
        var pane = t.Dark ? Hex("#FFFFFF", 0x0A) : Hex("#000000", 0x08);
        SetBrush("NsPaneBrush", pane);
        SetBrush("NavigationViewDefaultPaneBackground", pane);       // fondo del menú lateral
        SetBrush("NavigationViewExpandedPaneBackground", pane);
        SetBrush("NsBarBrush", Hex(t.Surface, 0xF2));
        SetBrush("NsLineBrush", t.Dark ? Hex("#FFFFFF", 0x16) : Hex("#000000", 0x1A));
        SetBrush("NsPanelBrush", t.Dark ? Hex("#000000", 0x33) : Hex("#000000", 0x0D));

        Ambient.Visibility = AmbientBlue.Visibility = Vis(!classic);
        AmbientBlueStop.Color = Hex(t.Blue, t.Dark ? (byte)0x33 : (byte)0x22);
        AmbientBlueEnd.Color = Hex(t.Blue, 0);
        SlashA.Color = Hex(t.Accent, 0); SlashB.Color = Hex(t.Accent, 0x66); SlashC.Color = Hex(t.Accent, 0);
        HeroSlash.Visibility = Vis(!classic);
        Hero.CornerRadius = new CornerRadius(classic ? 14 : 26);
        Bar.Margin = classic ? new Thickness(0) : new Thickness(12, 4, 12, 12);
        Bar.CornerRadius = new CornerRadius(classic ? 0 : 22);
        Bar.BorderThickness = classic ? new Thickness(0, 1, 0, 0) : new Thickness(1);
        UpdateAmbient();
        if (_featured is not null) UpdateHero();
        if (classic) CloseNowSheet();
        SetBars(IsPlaying);
    }

    /// <summary>El brillo de arriba a la derecha toma el tono de la canción (o el acento si no suena nada).</summary>
    private void UpdateAmbient()
    {
        var color = _now is { } n ? MediaCard.Hsl(MediaCard.HueOf(n.Id), 0.8, 0.48) : Hex(_palette.Accent);
        var alpha = _palette.Dark ? (byte)0x4D : (byte)0x30;
        AmbientStop.Color = Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);
        AmbientEnd.Color = Windows.UI.Color.FromArgb(0, color.R, color.G, color.B);
        NpGlowStop.Color = Windows.UI.Color.FromArgb(0x70, color.R, color.G, color.B);
        NpGlowEnd.Color = Windows.UI.Color.FromArgb(0, color.R, color.G, color.B);
    }

    private void StartKenBurns()
    {
        var sb = new Storyboard();
        foreach (var prop in new[] { "ScaleX", "ScaleY" })
        {
            var a = new DoubleAnimation
            {
                From = 1.02, To = 1.13, Duration = TimeSpan.FromSeconds(26), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(a, HeroScale);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void FadeIn()
    {
        Browse.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(260) };
        Browse.Opacity = 0;
        DispatcherQueue.TryEnqueue(() => Browse.Opacity = 1);
    }

    private void SetBars(bool playing)
    {
        var show = playing && _palette.Id != "clasico";
        PlayingBars.Visibility = Vis(show);
        if (show == _barsOn) return;
        _barsOn = show;
        if (!show) { _bars?.Stop(); return; }
        if (_bars is null)
        {
            _bars = new Storyboard();
            foreach (var (scale, secs) in new[] { (Bar1, 0.9), (Bar2, 0.7), (Bar3, 1.1) })
            {
                var a = new DoubleAnimation
                {
                    From = 0.25, To = 1, Duration = TimeSpan.FromSeconds(secs), AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever, EnableDependentAnimation = true,
                };
                Storyboard.SetTarget(a, scale);
                Storyboard.SetTargetProperty(a, "ScaleY");
                _bars.Children.Add(a);
            }
        }
        _bars.Begin();
    }

    private void SyncSheetAndBars(TimeSpan pos, TimeSpan total)
    {
        SetBars(IsPlaying);
        if (NowSheet.Visibility != Visibility.Visible) return;
        _updatingProgress = true;
        NpProgress.Maximum = Math.Max(1, total.TotalSeconds);
        NpProgress.Value = Math.Min(pos.TotalSeconds, NpProgress.Maximum);
        _updatingProgress = false;
        NpElapsed.Text = MediaCard.Format(pos);
        NpTotal.Text = MediaCard.Format(total);
        NpPlayIcon.Symbol = IsPlaying ? Symbol.Pause : Symbol.Play;
    }

    // Pantalla «Reproduciendo»
    private void Now_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_now is not null && _palette.Id != "clasico") OpenNowSheet();
    }

    private void OpenNowSheet()
    {
        UpdateNowSheet();
        ShowYouTubeLayer(false);
        VideoLayer.Visibility = Visibility.Collapsed;
        NowSheet.Visibility = Visibility.Visible;
    }

    private void CloseNowSheet() => NowSheet.Visibility = Visibility.Collapsed;

    private void NowSheetClose_Click(object sender, RoutedEventArgs e) => CloseNowSheet();

    private void NowSheetLyrics_Click(object sender, RoutedEventArgs e)
    {
        CloseNowSheet();
        Lyrics_Click(sender, e);
    }

    private void NowSheetVideo_Click(object sender, RoutedEventArgs e)
    {
        CloseNowSheet();
        if (_mode == Mode.YouTube) ShowYouTubeLayer(true);
        else if (_mode == Mode.Native && _now is { IsTrack: false }) VideoLayer.Visibility = Visibility.Visible;
    }

    private void UpdateNowSheet()
    {
        if (_now is not { } card) return;
        NpTitle.Text = card.Title;
        NpArtist.Text = card.YouTubeId is null ? card.Subtitle : $"{card.Subtitle} · YouTube";
        NpImage.Source = card.YouTubeId is { } id ? new BitmapImage(new Uri($"https://i.ytimg.com/vi/{id}/hq720.jpg")) : card.Artwork;
        NpHeartIcon.Glyph = _library.Contains(card.Source, card.Id) ? "\uEB52" : "\uEB51";
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_palette.Id == "clasico" || sender is not UIElement ui) return;
        ui.TranslationTransition ??= new Vector3Transition { Duration = TimeSpan.FromMilliseconds(220) };
        ui.Translation = new Vector3(0, -6, 0);
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement ui) ui.Translation = Vector3.Zero;
    }

    // ===================== Importar una lista desde un enlace =====================

    /// <summary>Pide un enlace (YouTube, Spotify, Deezer, Apple Music o SoundCloud) y crea una lista con esas canciones buscadas en YouTube Music.</summary>
    private async void ImportLink_Click(object sender, RoutedEventArgs e)
    {
        var url = new TextBox { PlaceholderText = LinkImport.Hint + "…" };
        var note = new TextBlock
        {
            Text = LinkImport.Notice + " Por enlace, Spotify solo entrega las primeras canciones (unas 50–100).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 12,
        };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 22 };
        var bar = new ProgressBar { Visibility = Visibility.Collapsed, Minimum = 0, Maximum = 1 };
        var panel = new StackPanel { Spacing = 10, Width = 460, Children = { url, note, bar, status } };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Importar lista de reproducción", Content = panel,
            PrimaryButtonText = "Importar", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ElementTheme.Dark,
        };
        var cts = new CancellationTokenSource();
        string? openListId = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;                                         // el diálogo sigue abierto mientras se importa
            var (link, error) = LinkImport.ParseLink(url.Text);
            if (link is null) { status.Text = error ?? "Enlace no válido."; return; }
            dialog.IsPrimaryButtonEnabled = false; url.IsEnabled = false; bar.Visibility = Visibility.Visible;
            try { openListId = await RunImportAsync(link, status, bar, cts.Token); }
            catch (OperationCanceledException) { /* cerrado a mitad: lo ya importado queda en la lista */ }
            catch (Exception ex) { App.LogOnce("Importar lista", ex); status.Text = "No se pudo importar la lista."; }
            finally { dialog.CloseButtonText = "Cerrar"; bar.Visibility = Visibility.Collapsed; }
        };
        dialog.Closing += (_, _) => cts.Cancel();
        await dialog.ShowAsync();
        RebuildPlaylistNav();
        if (openListId is not null) SelectNav("pl:" + openListId);
    }

    /// <summary>Lee el enlace, crea la lista y busca cada canción en YouTube Music por lotes de 24 (3 a la vez). Devuelve el id de la lista creada.</summary>
    private async Task<string?> RunImportAsync(ImportLink link, TextBlock status, ProgressBar bar, CancellationToken ct)
    {
        status.Text = "Leyendo el enlace…";
        string body;
        try { body = await LinkImport.FetchAsync(_http, link.FetchUrl, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            status.Text = "No se pudo abrir el enlace (¿es público? ¿hay conexión?).";
            return null;
        }
        var info = LinkImport.Parse(link, body);
        if (info.Tracks.Count == 0)
        {
            status.Text = "No se encontraron canciones. La lista debe ser pública, y el servicio puede haber cambiado su página.";
            return null;
        }
        var list = _playlists.Create(string.IsNullOrWhiteSpace(info.Name) ? "Lista importada" : info.Name);
        var total = info.Tracks.Count;
        int done = 0, found = 0;
        var missing = new List<string>();
        for (var start = 0; start < total; start += 24)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = info.Tracks.Skip(start).Take(24).ToList();
            var results = new SavedItem?[chunk.Count];
            var failed = new bool[chunk.Count];
            using var gate = new SemaphoreSlim(3);
            await Task.WhenAll(chunk.Select(async (t, i) =>
            {
                await gate.WaitAsync(ct);
                try { (results[i], failed[i]) = await SearchSongAsync(t, ct); }
                finally
                {
                    gate.Release();
                    done++;
                    bar.Value = (double)done / total;
                    status.Text = $"Buscando en YouTube Music… {done} de {total}";
                }
            }));
            found += _playlists.AddMany(list.Id, results.OfType<SavedItem>());
            for (var i = 0; i < chunk.Count; i++)
                if (results[i] is null) missing.Add((chunk[i].Artist.Length > 0 ? chunk[i].Artist + " — " : "") + chunk[i].Title + (failed[i] ? " (YouTube no respondió)" : ""));
        }
        if (found == 0) { _playlists.Delete(list.Id); status.Text = "No se encontró ninguna de las canciones en YouTube Music."; return null; }
        status.Text = $"{found} de {total} canciones añadidas a «{list.Name}»."
            + (missing.Count > 0 ? $" {missing.Count} sin encontrar (por ejemplo: {string.Join("; ", missing.Take(3))})." : "");
        return list.Id;
    }

    /// <summary>Busca una canción; si YouTube pide calma (error de red o límite) espera y reintenta una vez.</summary>
    private async Task<(SavedItem? Item, bool Failed)> SearchSongAsync(ImportTrack t, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var results = (await YouTubeMusic.SearchSongsAsync(_http, $"{t.Artist} {t.Title}".Trim(), ct)).Where(ContentFilter.IsSong);
                var m = LinkImport.PickMatch(t, results);
                return (m is null ? null : new SavedItem("youtube", m.Id, m.Title, m.Channel, m.DurationSec, m.Thumbnail.AbsoluteUri), false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                await Task.Delay(8000 * (attempt + 1), ct);
            }
        }
        return (null, true);
    }

    /// <summary>Guarda la biblioteca y las listas en un archivo (el mismo formato que la web y el móvil) donde elija el usuario.</summary>
    private async void ExportLibrary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_library.Items.Count == 0 && _playlists.All.Count == 0)
            {
                ShowStatus("Aún no hay nada que exportar: guarda canciones o crea listas.");
                return;
            }
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = Path.GetFileNameWithoutExtension(LibraryBackup.FileName(DateTimeOffset.Now)),
            };
            picker.FileTypeChoices.Add("Copia de Nsound", [".json"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;                                   // cancelado
            await Windows.Storage.FileIO.WriteTextAsync(file, LibraryBackup.Build(_library.Items, _playlists.All, DateTimeOffset.Now));
            ShowStatus($"Biblioteca exportada: {_library.Items.Count} guardadas y {_playlists.All.Count} listas.");
        }
        catch (Exception ex)
        {
            App.LogOnce("Exportar biblioteca", ex);
            ShowStatus("No se pudo guardar el archivo.");
        }
    }

    private void Look_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || LookBox.SelectedIndex < 0) return;
        var id = Themes.All[LookBox.SelectedIndex].Id;
        _settings.Current.Look = id;
        ScheduleSave();
        ApplyLook(id);
    }

    // ===================== Eventos de la interfaz =====================

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        _kind = (e.SelectedItem as NavigationViewItem)?.Tag as string ?? "all";
        VideoLayer.Visibility = Visibility.Collapsed;
        ShowYouTubeLayer(false);
        FadeIn();
        if (_catalog is null) return;
        ApplyFilter();
        _ = SearchYouTubeAsync(Search.Text);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Browse.ChangeView(null, 0, null, true));
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
            : _shelves.FirstOrDefault(s => s.Cards.Contains(card))?.Cards ?? [card];
        Play(card, list);
    }

    private void Hero_Click(object sender, RoutedEventArgs e)
    {
        if (_featured is not null) Play(_featured, _releases);
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
            if ((_featured ?? _youtube.FirstOrDefault() ?? _saved.FirstOrDefault()) is { } first)
                Play(first, first == _featured ? _releases : _youtube.Contains(first) ? _youtube : _saved);
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
        _settings.Current.Volume = e.NewValue;
        ScheduleSave();
    }
}
