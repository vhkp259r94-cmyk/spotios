using LibreStream.Core;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace LibreStream.App;

/// <summary>Datos de presentación de un elemento del catálogo (tarjeta).</summary>
public sealed class MediaCard
{
    public CatalogItem Item { get; }
    public string Title => Item.Title;
    public string Subtitle { get; }
    public string Initials { get; }
    public string DurationText { get; }
    public ImageSource? Artwork { get; }
    public Brush Tile { get; }

    public MediaCard(CatalogItem item)
    {
        Item = item;
        Subtitle = item.Featuring is { Count: > 0 } f ? $"{item.Artist} · con {string.Join(", ", f)}" : item.Artist;
        Initials = string.Concat(item.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        DurationText = Format(TimeSpan.FromSeconds(item.DurationSec));
        Artwork = item.ArtworkUrl is null ? null : new BitmapImage(item.ArtworkUrl);
        Tile = Gradient(item.Id);
    }

    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>Degradado estable derivado del id (cada elemento tiene siempre el mismo color).</summary>
    private static LinearGradientBrush Gradient(string id)
    {
        var seed = id.Length >= 4 && int.TryParse(id[..4], System.Globalization.NumberStyles.HexNumber, null, out var n) ? n : id.GetHashCode();
        double hue = Math.Abs(seed) % 360;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop { Color = Hsl(hue, 0.65, 0.42), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Hsl((hue + 50) % 360, 0.70, 0.22), Offset = 1 });
        return brush;
    }

    private static Color Hsl(double h, double s, double l)
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
