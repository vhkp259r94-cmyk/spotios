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

    /// <summary>Portada de un disco de cada artista de la lista, ya resuelta (MusicBrainz) para que salga al instante.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownCovers = new Dictionary<string, string>
    {
        ["Bad Bunny"] = "b3073a10-2d7c-484c-b387-e49ae629da3d",
        ["Taylor Swift"] = "9ae7e7e9-ac40-4874-bd43-d77a9bb9b617",
        ["Karol G"] = "2928f830-b86c-457f-9684-95f5bc7f4c04",
        ["The Weeknd"] = "14f83b04-2b9e-4974-9169-476f26475be3",
        ["Shakira"] = "f04254be-6a7b-4137-bffa-a2236ab8433e",
        ["Feid"] = "3a1a8d6e-85d3-4f75-8d73-827dc590a9b4",
        ["Drake"] = "34009a25-def9-4e37-b3be-803b154902c0",
        ["Rosalía"] = "bb3ec118-57df-4eb9-a8fe-a26a70a7dbe0",
        ["Peso Pluma"] = "fc474abc-71f9-487b-a3b5-7162b1136a26",
        ["Dua Lipa"] = "c358634d-b58d-4635-aca3-246cee2577f0",
        ["Billie Eilish"] = "02a544b3-0459-42c7-bd9c-047162e7b67a",
        ["Quevedo"] = "87635467-6804-404a-b95f-a862a07a5d27",
        ["Ed Sheeran"] = "2068661d-b968-402f-adc9-bc42dda441a9",
        ["Bruno Mars"] = "a868464a-2e60-484b-bacd-aaeaee71284e",
        ["Anuel AA"] = "ddbbc5f3-5393-4e1d-b513-e7c6f3b8271d",
        ["Coldplay"] = "3988c875-b35f-4c3e-b143-ccb484d45347",
        ["Mora"] = "130d3966-a94b-437f-b921-1706e0ffafff",
        ["Olivia Rodrigo"] = "4c4eee61-da1e-4115-8eb9-07e5b671fefe",
        ["J Balvin"] = "9cd0edd6-f719-4d83-86cb-a01594885b4f",
        ["Daddy Yankee"] = "45fbc014-1184-43ad-a907-94b73af33374",
        ["Ariana Grande"] = "2b8f453f-ffd6-4860-8742-7cff1c2feae3",
        ["Myke Towers"] = "e3b39cf3-52c0-4e7a-8e78-830f75ff7fc5",
        ["Maluma"] = "7a4f6e7c-f016-4513-89d6-2bd692fdeb2b",
        ["Harry Styles"] = "8676b232-108c-4229-be09-f1b09b0ec5f6",
        ["Aitana"] = "9b76aa9f-d6cb-4f4c-a037-ed7dcbeeafa3",
        ["Lola Índigo"] = "cbba1116-d884-4654-b0ca-232e4d4b57ba",
        ["Morad"] = "dcb511dc-e78a-4677-8d2d-1262cef381d8",
        ["Eminem"] = "2133f99e-22e7-48c9-a818-914265089e51",
        ["Rauw Alejandro"] = "786f5aec-d59c-491c-aee6-baaff09a81c8",
    };

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
