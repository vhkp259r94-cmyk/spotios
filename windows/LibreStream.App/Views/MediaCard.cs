using LibreStream.Core;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace LibreStream.App;

/// <summary>
/// Datos de presentación de una tarjeta. Puede venir del catálogo libre (<see cref="Item"/>)
/// o de YouTube (<see cref="YouTubeId"/>).
/// </summary>
public sealed class MediaCard
{
    public CatalogItem? Item { get; }
    public string? YouTubeId { get; }
    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public double DurationSec { get; }
    public Uri? ArtworkUrl { get; }
    public string? Published { get; private set; }

    public string Source => YouTubeId is null ? "catalog" : "youtube";
    public bool IsTrack => Item?.Kind == ItemKind.Track;
    public string Initials { get; }
    public string DurationText => DurationSec > 0 ? Format(TimeSpan.FromSeconds(DurationSec)) : "EN DIRECTO";
    public ImageSource? Artwork { get; }
    public Brush Tile { get; }

    public MediaCard(CatalogItem item)
        : this(item.Id, item.Title,
               item.Featuring is { Count: > 0 } f ? $"{item.Artist} · con {string.Join(", ", f)}" : item.Artist,
               item.DurationSec, item.ArtworkUrl)
    {
        Item = item;
    }

    public MediaCard(YouTubeVideo v) : this(v.Id, v.Title, v.Channel, v.DurationSec, v.Thumbnail)
    {
        YouTubeId = v.Id;
        Published = v.Published;
    }

    /// <summary>Tarjeta desde la biblioteca guardada; <paramref name="catalogItem"/> se resuelve por id si es del catálogo.</summary>
    public MediaCard(SavedItem s, CatalogItem? catalogItem)
        : this(s.Id, s.Title, s.Subtitle, s.DurationSec, Uri.TryCreate(s.ArtworkUrl, UriKind.Absolute, out var u) ? u : null)
    {
        if (s.Source == "youtube") YouTubeId = s.Id;
        else Item = catalogItem;
    }

    private MediaCard(string id, string title, string subtitle, double durationSec, Uri? artwork)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        DurationSec = durationSec;
        ArtworkUrl = artwork;
        Initials = string.Concat(title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        Artwork = artwork is null ? null : Sharp(artwork);
        Tile = Gradient(id);
    }

    /// <summary>
    /// Las miniaturas de YouTube de 320x180 se ven borrosas en pantallas con escala 125-200%: se pide la de 1280x720 y, si ese vídeo
    /// no la tiene, se vuelve a la pequeña. Las carátulas cuadradas (YouTube Music) ya vienen en buena resolución.
    /// </summary>
    private static BitmapImage Sharp(Uri uri)
    {
        var m = System.Text.RegularExpressions.Regex.Match(uri.AbsoluteUri, @"^https://i\.ytimg\.com/vi/([\w-]{11})/(mq|hq)default\.jpg$");
        if (!m.Success) return new BitmapImage(uri);
        var small = uri;
        var image = new BitmapImage(new Uri($"https://i.ytimg.com/vi/{m.Groups[1].Value}/hq720.jpg"));
        var retried = false;
        image.ImageFailed += (_, _) => { if (!retried) { retried = true; image.UriSource = small; } };
        return image;
    }

    public bool IsPlayable => YouTubeId is not null || Item is not null;

    public SavedItem ToSaved() => new(Source, Id, Title, Subtitle, DurationSec, ArtworkUrl?.AbsoluteUri);

    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>Degradado estable derivado del id (cada elemento tiene siempre el mismo color).</summary>
    private static LinearGradientBrush Gradient(string id)
    {
        var seed = 17;
        foreach (var ch in id) seed = unchecked(seed * 31 + ch);
        double hue = Math.Abs(seed % 360);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop { Color = Hsl(hue, 0.65, 0.42), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Hsl((hue + 50) % 360, 0.70, 0.22), Offset = 1 });
        return brush;
    }

    /// <summary>Tono (0-359) estable derivado del id, el mismo que usa el degradado de la tarjeta.</summary>
    public static double HueOf(string id)
    {
        var seed = 17;
        foreach (var ch in id) seed = unchecked(seed * 31 + ch);
        return Math.Abs(seed % 360);
    }

    internal static Color Hsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromArgb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
