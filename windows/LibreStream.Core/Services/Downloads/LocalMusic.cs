using System.Security.Cryptography;
using System.Text;

namespace LibreStream.Core;

/// <summary>Música que el usuario ya tiene en su equipo (por ejemplo MP3 propios): se reproduce sin conexión con su artista y su portada.</summary>
public static class LocalMusic
{
    public const string Prefix = "local:";
    public static readonly string[] Extensions = [".mp3", ".m4a", ".flac", ".wav", ".ogg", ".wma", ".aac"];

    public static bool IsLocal(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Identificador estable de un archivo: el mismo archivo no se añade dos veces.</summary>
    public static string IdFor(string path) =>
        Prefix + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..12].ToLowerInvariant();

    /// <summary>Título del archivo sin extensión y sin número de pista al principio, para cuando no tiene etiquetas.</summary>
    public static string TitleFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Trim();
        var i = 0;
        while (i < name.Length && (char.IsDigit(name[i]) || name[i] is '.' or '-' or ' ')) i++;
        return i > 0 && i < name.Length ? name[i..].Trim() : name;
    }

    public static CatalogItem ToCatalogItem(DownloadedItem d) => new(
        d.Id, ItemKind.Track, d.Title, d.Artist, null, null, null, d.DurationSec,
        Uri.TryCreate(d.ArtworkUrl, UriKind.Absolute, out var art) ? art : null, null,
        new License("LicenseRef-user-file", null),
        [new Source(Provider.Direct, new Uri(d.FilePath), null, null, d.DurationSec)]);
}
