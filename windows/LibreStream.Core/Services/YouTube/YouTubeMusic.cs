using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibreStream.Core;

/// <summary>
/// Búsqueda de canciones en YouTube Music con el filtro «Canciones»: versiones de audio de estudio, sin intros,
/// charla ni vídeos con efectos. Solo se leen los metadatos; la reproducción sigue siendo el reproductor oficial
/// de YouTube incrustado (no se extraen ni descargan streams).
/// </summary>
public static partial class YouTubeMusic
{
    private const string SearchUrl = "https://music.youtube.com/youtubei/v1/search?prettyPrint=false";
    private const string SongsFilter = "EgWKAQIIAWoMEA4QChADEAQQCRAF";

    [GeneratedRegex(@"^\d+(:\d{2})+$")]
    private static partial Regex Duration();

    [GeneratedRegex(@"=w\d+-h\d+.*$")]
    private static partial Regex ArtSize();

    /// <summary>Búsqueda de YouTube Music por género (las canciones salen mejor con palabras sueltas).</summary>
    public static string GenreQuery(string genre, int year) => genre switch
    {
        "Éxitos" => $"éxitos {year}",
        "Reggaetón" => "reggaeton",
        "Pop" => "pop hits",
        "Rock" => "rock",
        "Electrónica" => "electronica",
        "Hip hop" => "hip hop",
        "Latino" => "latino",
        "Lo-fi" => "lofi",
        _ => genre,
    };

    public static string SearchBody(string query) => JsonSerializer.Serialize(new
    {
        context = new { client = new { clientName = "WEB_REMIX", clientVersion = "1.20241106.01.00", hl = "es", gl = "ES" } },
        query = query.Trim(),
        @params = SongsFilter,
    });

    public static async Task<List<YouTubeVideo>> SearchSongsAsync(HttpClient http, string query, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, SearchUrl)
        {
            Content = new StringContent(SearchBody(query), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Origin", "https://music.youtube.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://music.youtube.com/");
        req.Headers.TryAddWithoutValidation("Accept-Language", "es-ES,es;q=0.9");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Respuesta de YouTube Music → canciones. Channel = «Artista · Álbum»; Art = carátula cuadrada.</summary>
    public static List<YouTubeVideo> Parse(string json)
    {
        var result = new List<YouTubeVideo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, result, 0);
        }
        catch (JsonException) { /* formato inesperado: sin resultados */ }
        return result.DistinctBy(v => v.Id).ToList();
    }

    private static void Walk(JsonElement e, List<YouTubeVideo> acc, int depth)
    {
        if (depth > 40) return;
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.NameEquals("musicResponsiveListItemRenderer") && TryReadSong(p.Value) is { } song) acc.Add(song);
                else Walk(p.Value, acc, depth + 1);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray()) Walk(x, acc, depth + 1);
        }
    }

    private static string[] Runs(JsonElement columns, int index)
    {
        if (index >= columns.GetArrayLength()) return [];
        var col = columns[index];
        if (!col.TryGetProperty("musicResponsiveListItemFlexColumnRenderer", out var r)
            || !r.TryGetProperty("text", out var text) || !text.TryGetProperty("runs", out var runs)
            || runs.ValueKind != JsonValueKind.Array) return [];
        return runs.EnumerateArray().Select(x => x.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "").ToArray();
    }

    private static YouTubeVideo? TryReadSong(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object) return null;
        var id = r.TryGetProperty("playlistItemData", out var pd) && pd.TryGetProperty("videoId", out var v) ? v.GetString() : null;
        if (!YouTubeSearch.IsValidId(id)) return null;
        if (!r.TryGetProperty("flexColumns", out var cols) || cols.ValueKind != JsonValueKind.Array) return null;

        var title = Runs(cols, 0).FirstOrDefault()?.Trim() ?? "";
        var parts = Runs(cols, 1).Select(t => t.Trim()).Where(t => t.Length > 0 && t != "•" && t != "·").ToList();
        if (title.Length == 0 || parts.Count == 0) return null;

        var duration = Duration().IsMatch(parts[^1]) ? YouTubeSearch.ParseLength(parts[^1]) : 0;
        var artist = parts[0];
        var album = parts.Count >= 3 ? parts[1] : "";

        string? art = null;
        if (r.TryGetProperty("thumbnail", out var th) && th.TryGetProperty("musicThumbnailRenderer", out var mt)
            && mt.TryGetProperty("thumbnail", out var t2) && t2.TryGetProperty("thumbnails", out var thumbs)
            && thumbs.ValueKind == JsonValueKind.Array && thumbs.GetArrayLength() > 0
            && thumbs[thumbs.GetArrayLength() - 1].TryGetProperty("url", out var u) && u.GetString() is { } url
            && url.StartsWith("https://", StringComparison.Ordinal))
            art = ArtSize().Replace(url, "=w544-h544");

        return new YouTubeVideo(id!, title, album.Length > 0 ? $"{artist} · {album}" : artist, duration, null, art);
    }
}
