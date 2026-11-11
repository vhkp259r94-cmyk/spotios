using LibreStream.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace LibreStream.App;

public sealed partial class MainWindow : Window
{
    private const string CatalogUrl = "https://vhkp259r94-cmyk.github.io/spotios/catalog.json";

    private readonly AudioPlayerManager _player = AudioPlayerManager.Shared;
    private readonly CatalogService _catalog = new(
        new Uri(CatalogUrl),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreStream"));
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private List<MediaCard> _videos = [];
    private List<MediaCard> _tracks = [];
    private MediaCard? _featured;
    private string _kind = "all";
    private bool _updatingProgress;

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
        _player.StateChanged += () => DispatcherQueue.TryEnqueue(UpdateNowPlaying);
        _timer.Tick += (_, _) => UpdateProgress();
        _timer.Start();
        _ = LoadAsync();
    }

    // ----- Catálogo -----

    private async Task LoadAsync()
    {
        try
        {
            await _catalog.RefreshAsync();
            Status.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Status.Text = $"No se pudo actualizar el catálogo: {ex.Message}";
            Status.Visibility = Visibility.Visible;
        }
        ApplyFilter();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Browse.ChangeView(null, 0, null, true));
    }

    private void ApplyFilter()
    {
        var q = MetadataMatcher.Fold(Search.Text ?? "");
        var items = _catalog.Items
            .Where(i => q.Length == 0 || MetadataMatcher.Fold($"{i.Artist} {i.Title} {string.Join(' ', i.Genres ?? [])}").Contains(q))
            .ToList();

        _videos = _kind == "track" ? [] : items.Where(i => i.Kind != ItemKind.Track).Select(i => new MediaCard(i)).ToList();
        _tracks = _kind == "video" ? [] : items.Where(i => i.Kind == ItemKind.Track).Select(i => new MediaCard(i)).ToList();

        VideosRepeater.ItemsSource = _videos;
        TracksRepeater.ItemsSource = _tracks;
        VideosSection.Visibility = _videos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TracksSection.Visibility = _tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = _videos.Count + _tracks.Count == 0 && Status.Visibility == Visibility.Collapsed
            ? Visibility.Visible : Visibility.Collapsed;

        YouTubeSearchButton.Visibility = q.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        YouTubeSearchText.Text = $"Buscar «{Search.Text?.Trim()}» en YouTube";

        // Destacado: solo en la vista sin búsqueda
        _featured = q.Length == 0 ? _videos.FirstOrDefault() ?? _tracks.FirstOrDefault() : null;
        Hero.Visibility = _featured is null ? Visibility.Collapsed : Visibility.Visible;
        if (_featured is not null)
        {
            HeroTile.Background = _featured.Tile;
            HeroImage.Source = _featured.Artwork;
            HeroTitle.Text = _featured.Title;
            HeroSubtitle.Text = $"{_featured.Subtitle} · {_featured.DurationText} · {_featured.Item.License.Spdx}";
        }
    }

    private void Play(MediaCard card)
    {
        var list = card.Item.Kind == ItemKind.Track ? _tracks : _videos;
        var index = list.FindIndex(c => c.Item.Id == card.Item.Id);
        if (index < 0) { list = [card]; index = 0; }
        PauseYouTube();
        _player.Play(list.Select(c => c.Item), index);
    }

    // ----- Estado de reproducción -----

    private void UpdateNowPlaying()
    {
        var item = _player.Current;
        PlayIcon.Symbol = _player.IsPlaying ? Symbol.Pause : Symbol.Play;
        if (item is null) return;

        var card = new MediaCard(item);
        NowTitle.Text = card.Title;
        NowArtist.Text = card.Subtitle;
        NowTile.Background = card.Tile;
        NowImage.Source = card.Artwork;
        if (item.Kind == ItemKind.Track) VideoLayer.Visibility = Visibility.Collapsed;
        else if (_player.IsPlaying) VideoLayer.Visibility = Visibility.Visible;
    }

    private void UpdateProgress()
    {
        var total = _player.Duration;
        if (total <= TimeSpan.Zero && _player.Current is { } c) total = TimeSpan.FromSeconds(c.DurationSec);
        var pos = _player.Position;

        _updatingProgress = true;
        Progress.Maximum = Math.Max(1, total.TotalSeconds);
        Progress.Value = Math.Min(pos.TotalSeconds, Progress.Maximum);
        _updatingProgress = false;

        Elapsed.Text = MediaCard.Format(pos);
        Total.Text = MediaCard.Format(total);
    }

    // ----- Eventos -----

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        _kind = (e.SelectedItem as NavigationViewItem)?.Tag as string ?? "all";
        VideoLayer.Visibility = Visibility.Collapsed;
        if (_kind == "youtube") { ShowYouTube(Search.Text); return; }
        YouTubeLayer.Visibility = Visibility.Collapsed;
        if (_catalog is not null) ApplyFilter();
    }

    // ----- YouTube (sitio oficial embebido en WebView2) -----

    private void ShowYouTube(string? query)
    {
        _player.Player.Pause();
        YouTubeLayer.Visibility = Visibility.Visible;
        var target = string.IsNullOrWhiteSpace(query)
            ? "https://www.youtube.com/"
            : "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query.Trim());
        if (Web.Source?.AbsoluteUri != target) Web.Source = new Uri(target);
    }

    private void PauseYouTube()
    {
        if (Web.CoreWebView2 is not null)
            _ = Web.ExecuteScriptAsync("document.querySelectorAll('video').forEach(v => v.pause())");
    }

    private void Search_QuerySubmitted(AutoSuggestBox s, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        if (_kind == "youtube") ShowYouTube(s.Text);
    }

    private void YouTubeSearch_Click(object sender, RoutedEventArgs e) => Nav.SelectedItem = YouTubeItem;

    private void Search_TextChanged(AutoSuggestBox s, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (_kind != "youtube") ApplyFilter();
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaCard card) Play(card);
    }

    private void Hero_Click(object sender, RoutedEventArgs e)
    {
        if (_featured is not null) Play(_featured);
    }

    private void CloseVideo_Click(object sender, RoutedEventArgs e)
    {
        _player.Player.Pause();
        VideoLayer.Visibility = Visibility.Collapsed;
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_player.Current is null && (_featured ?? _tracks.FirstOrDefault() ?? _videos.FirstOrDefault()) is { } first) Play(first);
        else _player.TogglePlayPause();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _player.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => _player.Next();

    private void Progress_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingProgress && _player.Current is not null) _player.Seek(TimeSpan.FromSeconds(e.NewValue));
    }

    private void Volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_player is not null) _player.Volume = e.NewValue / 100;
    }
}
