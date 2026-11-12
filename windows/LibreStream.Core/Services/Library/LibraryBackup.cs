using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LibreStream.Core;

/// <summary>
/// Copia de la biblioteca y las listas en un archivo JSON con el MISMO formato que exporta la web y las apps de móvil
/// (<c>{"nsound":1,"library":[…],"playlists":[…]}</c>), así que se puede restaurar en cualquiera de ellas.
/// Solo lleva lo que el usuario ha guardado: sin cuentas, sin ajustes, sin datos personales.
/// </summary>
public static partial class LibraryBackup
{
    [GeneratedRegex(@"^[\w.\-:]{1,80}$")]
    private static partial Regex IdPattern();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string FileName(DateTimeOffset when) => $"nsound-biblioteca-{when.UtcDateTime:yyyy-MM-dd}.json";

    public static string Build(IEnumerable<SavedItem> library, IEnumerable<Playlist> playlists, DateTimeOffset when)
    {
        var root = new JsonObject
        {
            ["nsound"] = 1,
            ["kind"] = "biblioteca",
            ["exportedAt"] = when.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["library"] = new JsonArray(library.Select(Item).Where(x => x is not null).ToArray()),
            ["playlists"] = new JsonArray(playlists.Select(p => (JsonNode)new JsonObject
            {
                ["name"] = Trim(p.Name, 60) is { Length: > 0 } n ? n : "Lista",
                ["items"] = new JsonArray(p.Items.Select(Item).Where(x => x is not null).ToArray()),
            }).ToArray()),
        };
        return root.ToJsonString(Options);
    }

    private static string Trim(string? s, int max)
    {
        var t = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return t.Length > max ? t[..max] : t;
    }

    private static JsonNode? Item(SavedItem i)
    {
        var src = i.Source == "youtube" ? "yt" : i.Source == "catalog" ? "cat" : null;
        if (src is null || !IdPattern().IsMatch(i.Id ?? "") || Trim(i.Title, 200).Length == 0) return null;
        var sub = Trim(i.Subtitle, 200);
        var artist = Trim(sub.Split(" · ")[0].Replace(" - Topic", ""), 120);
        var thumb = i.ArtworkUrl is { } u && u.StartsWith("https://", StringComparison.Ordinal) ? (u.Length > 400 ? u[..400] : u) : "";
        return new JsonObject
        {
            ["src"] = src, ["id"] = i.Id, ["title"] = Trim(i.Title, 200), ["sub"] = sub,
            ["duration"] = Math.Clamp(double.IsFinite(i.DurationSec) ? i.DurationSec : 0, 0, 86400),
            ["thumb"] = thumb, ["artist"] = artist, ["url"] = "", ["kind"] = "", ["published"] = null,
        };
    }
}
