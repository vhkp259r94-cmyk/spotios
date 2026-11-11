using LibreStream.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LibreStream.App;

public sealed partial class MainWindow : Window
{
    private const string CatalogUrl = "https://vhkp259r94-cmyk.github.io/spotios/catalog.json";

    private readonly AudioPlayerManager _player = AudioPlayerManager.Shared;
    private readonly CatalogService _catalog = new(
        new Uri(CatalogUrl),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreStream"));
    private List<CatalogItem> _filtered = [];

    public MainWindow()
    {
        InitializeComponent();
        Video.SetMediaPlayer(_player.Player);
        _player.StateChanged += () => DispatcherQueue.TryEnqueue(UpdateNowPlaying);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _catalog.RefreshAsync();
            Status.Text = "";
        }
        catch (Exception ex)
        {
            Status.Text = _catalog.Items.Count == 0 ? $"No se pudo cargar el catálogo: {ex.Message}" : "";
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = MetadataMatcher.Fold(Search.Text ?? "");
        _filtered = _catalog.Items
            .Where(i => q.Length == 0 || MetadataMatcher.Fold($"{i.Artist} {i.Title}").Contains(q))
            .ToList();
        Library.ItemsSource = _filtered;
    }

    private void UpdateNowPlaying()
    {
        var item = _player.Current;
        NowTitle.Text = item?.Title ?? "";
        NowArtist.Text = item?.Artist ?? "";
        PlayIcon.Symbol = _player.IsPlaying ? Symbol.Pause : Symbol.Play;
        Video.Visibility = item?.Kind == ItemKind.Video ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Search_TextChanged(AutoSuggestBox s, AutoSuggestBoxTextChangedEventArgs e) => ApplyFilter();

    private void Library_ItemClick(object sender, ItemClickEventArgs e) =>
        _player.Play(_filtered, _filtered.IndexOf((CatalogItem)e.ClickedItem));

    private void Play_Click(object sender, RoutedEventArgs e) => _player.TogglePlayPause();
    private void Prev_Click(object sender, RoutedEventArgs e) => _player.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => _player.Next();
}
