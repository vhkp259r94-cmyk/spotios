using System.Text.Json;

namespace LibreStream.Core;

/// <summary>Un disco de la discografía (MusicBrainz).</summary>
public sealed record AlbumInfo(string Id, string Title, string Year, string Type)
{
    public string CoverUrl => ArtistCatalog.CoverUrl(Id);
}

/// <summary>
/// Artistas de la semana y discografía. Los datos salen de MusicBrainz (ficha) y Cover Art Archive (portadas), ambos gratuitos y
/// sin cuenta. No se reproduce nada desde aquí: al pulsar un disco se buscan sus canciones en YouTube Music como siempre.
/// </summary>
public static class ArtistCatalog
{
    private const string Mb = "https://musicbrainz.org/ws/2";

    /// <summary>Lista fija (no es un ranking real de ventas); cada semana se muestra otro tramo.</summary>
    public static readonly string[] TopArtists =
    [
        "Bad Bunny", "Taylor Swift", "Karol G", "The Weeknd", "Shakira", "Feid", "Drake", "Rosalía", "Peso Pluma", "Dua Lipa",
        "Billie Eilish", "Quevedo", "Ed Sheeran", "Rauw Alejandro", "Bruno Mars", "Anuel AA", "Coldplay", "Mora", "Olivia Rodrigo", "J Balvin",
        "Daddy Yankee", "Ariana Grande", "Myke Towers", "Imagine Dragons", "Maluma", "Harry Styles", "Aitana", "Lola Índigo", "Morad", "Eminem",
    ];

    public static IReadOnlyList<string> WeeklyArtists(DateTime utcNow, int count = 12)
    {
        var week = (long)Math.Floor((utcNow - DateTime.UnixEpoch).TotalDays / 7);
        var n = TopArtists.Length;
        var start = (int)(week * 7 % n);
        return Enumerable.Range(0, Math.Min(count, n)).Select(i => TopArtists[(start + i) % n]).ToList();
    }

    public static string Fold(string s) => string.Concat((s ?? "").Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)).ToLowerInvariant().Trim();

    public static string ArtistSearchUrl(string name) =>
        $"{Mb}/artist/?query={Uri.EscapeDataString($"artist:\"{name.Replace('"', ' ').Replace('\\', ' ').Trim()}\"")}&fmt=json&limit=6";

    public static string ReleaseGroupsUrl(string mbid) => $"{Mb}/release-group?artist={Uri.EscapeDataString(mbid)}&type=album%7Cep&fmt=json&limit=100";

    public static string CoverUrl(string releaseGroupId) => $"https://coverartarchive.org/release-group/{Uri.EscapeDataString(releaseGroupId)}/front-250";

    /// <summary>Id del artista que coincide exactamente con el nombre; null si no hay coincidencia clara (artistas pequeños o nuevos).</summary>
    public static string? PickArtist(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("artists", out var list) || list.ValueKind != JsonValueKind.Array) return null;
            var want = Fold(name);
            if (want.Length == 0) return null;
            string? best = null; var bestScore = -1;
            foreach (var a in list.EnumerateArray())
            {
                if (!a.TryGetProperty("id", out var id) || !a.TryGetProperty("name", out var n)) continue;
                var sort = a.TryGetProperty("sort-name", out var s) ? s.GetString() ?? "" : "";
                if (Fold(n.GetString() ?? "") != want && Fold(sort) != want) continue;
                var score = a.TryGetProperty("score", out var sc) && sc.TryGetInt32(out var v) ? v : 0;
                if (score > bestScore) { bestScore = score; best = id.GetString(); }
            }
            return bestScore >= 80 ? best : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Discos de estudio y EP (sin directos, recopilatorios ni remixes), sin repetir título, los más recientes primero.</summary>
    public static IReadOnlyList<AlbumInfo> ParseReleaseGroups(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("release-groups", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            var seen = new HashSet<string>();
            var output = new List<AlbumInfo>();
            foreach (var r in list.EnumerateArray())
            {
                var id = r.TryGetProperty("id", out var i) ? i.GetString() : null;
                var title = r.TryGetProperty("title", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title)) continue;
                if (r.TryGetProperty("secondary-types", out var sec) && sec.ValueKind == JsonValueKind.Array && sec.GetArrayLength() > 0) continue;
                if (!seen.Add(Fold(title))) continue;
                var date = r.TryGetProperty("first-release-date", out var d) ? d.GetString() ?? "" : "";
                var type = r.TryGetProperty("primary-type", out var p) ? p.GetString() ?? "" : "";
                output.Add(new AlbumInfo(id, title, date.Length >= 4 ? date[..4] : "", type));
            }
            return output.OrderByDescending(a => a.Year, StringComparer.Ordinal).ToList();
        }
        catch (JsonException) { return []; }
    }

    /// <summary>Descarga la discografía de un artista (2 peticiones a MusicBrainz, con 1,1 s entre ellas).</summary>
    public static async Task<IReadOnlyList<AlbumInfo>> FetchDiscographyAsync(HttpClient http, string name, CancellationToken ct)
    {
        var mbid = PickArtist(await GetAsync(http, ArtistSearchUrl(name), ct), name);
        if (mbid is null) return [];
        await Task.Delay(1100, ct);
        return ParseReleaseGroups(await GetAsync(http, ReleaseGroupsUrl(mbid), ct)).Take(24).ToList();
    }

    private static async Task<string> GetAsync(HttpClient http, string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Nsound/0.8 (https://github.com/vhkp259r94-cmyk/spotios)");
            using var res = await http.SendAsync(req, ct);
            if ((int)res.StatusCode is 503 or 429 && attempt < 2) { await Task.Delay(1500, ct); continue; }
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync(ct);
        }
    }
}
