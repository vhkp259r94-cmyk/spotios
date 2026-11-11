using LibreStream.Core;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace LibreStream.App;

/// <summary>
/// Equivalente Windows de AudioPlayerManager.swift: MediaPlayer + cola (MediaPlaybackList) +
/// SystemMediaTransportControls (overlay de volumen, teclas multimedia, pantalla de bloqueo).
/// </summary>
public sealed class AudioPlayerManager : IDisposable
{
    public static AudioPlayerManager Shared { get; } = new();

    public MediaPlayer Player { get; } = new() { AudioCategory = MediaPlayerAudioCategory.Media };
    private readonly MediaPlaybackList _list = new() { MaxPrefetchTime = TimeSpan.FromSeconds(10) };
    private List<CatalogItem> _queue = [];

    public event Action? StateChanged;
    public CatalogItem? Current =>
        _list.CurrentItemIndex < _queue.Count ? _queue[(int)_list.CurrentItemIndex] : null;
    public bool IsPlaying => Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    public TimeSpan Position => Player.PlaybackSession.Position;
    public TimeSpan Duration => Player.PlaybackSession.NaturalDuration;
    public double Volume { get => Player.Volume; set => Player.Volume = Math.Clamp(value, 0, 1); }

    private AudioPlayerManager()
    {
        // MediaPlayer integra SMTC automáticamente (CommandManager habilitado por defecto).
        Player.CommandManager.IsEnabled = true;
        Player.Source = _list;
        _list.CurrentItemChanged += (_, _) => StateChanged?.Invoke();
        Player.PlaybackSession.PlaybackStateChanged += (_, _) => StateChanged?.Invoke();
        Player.MediaFailed += (_, e) => System.Diagnostics.Debug.WriteLine($"MediaFailed: {e.ErrorMessage}");
    }

    public void Play(IEnumerable<CatalogItem> items, int startAt = 0)
    {
        Player.Pause();
        _list.Items.Clear();
        _queue = [];
        foreach (var item in items)
        {
            var src = item.Sources.FirstOrDefault(s => s.IsDirectlyPlayable);
            if (src is null) continue;
            _queue.Add(item);
            _list.Items.Add(BuildPlaybackItem(item, src));
        }
        if (_queue.Count == 0) return;
        _list.MoveTo((uint)Math.Clamp(startAt, 0, _queue.Count - 1));
        Player.Play();
    }

    public void TogglePlayPause() { if (IsPlaying) Player.Pause(); else Player.Play(); }
    public void Next() => _list.MoveNext();

    public void Previous()
    {
        if (Position > TimeSpan.FromSeconds(3) || _list.CurrentItemIndex == 0) Seek(TimeSpan.Zero);
        else _list.MovePrevious();
    }

    public void Seek(TimeSpan to) => Player.PlaybackSession.Position = to < TimeSpan.Zero ? TimeSpan.Zero : to;

    private static MediaPlaybackItem BuildPlaybackItem(CatalogItem item, Source src)
    {
        var playback = new MediaPlaybackItem(MediaSource.CreateFromUri(src.Url));
        var props = playback.GetDisplayProperties();
        if (item.Kind == ItemKind.Video)
        {
            props.Type = MediaPlaybackType.Video;
            props.VideoProperties.Title = item.Title;
            props.VideoProperties.Subtitle = item.Artist;
        }
        else
        {
            props.Type = MediaPlaybackType.Music;
            props.MusicProperties.Title = item.Title;
            props.MusicProperties.Artist = item.Artist;
            props.MusicProperties.AlbumTitle = item.Album ?? "";
        }
        if (item.ArtworkUrl is not null)
            props.Thumbnail = RandomAccessStreamReference.CreateFromUri(item.ArtworkUrl);
        playback.ApplyDisplayProperties(props);
        return playback;
    }

    public void Dispose() => Player.Dispose();
}
