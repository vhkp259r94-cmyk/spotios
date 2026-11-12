using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibreStream.Core;

public sealed record ImportLink(string Service, string Kind, string Id, string FetchUrl);
public sealed record ImportTrack(string Title, string Artist);
public sealed record ImportInfo(string Name, List<ImportTrack> Tracks);

/// <summary>
/// Importar una canción, una lista pública o un artista desde un enlace de YouTube, SoundCloud, Spotify, Deezer o Apple Music.
/// Solo se leen datos públicos (título y artista); cada canción se busca después en YouTube Music. Sin cuentas ni contraseñas.
/// Mismas reglas que la web y los móviles (app/src/js/00-import.js).
/// </summary>
public static partial class LinkImport
{
    public const int Max = 500;
    public const string Hint = "Pega un enlace de YouTube, SoundCloud, Spotify, Deezer o Apple Music";
    public const string Notice = "Solo se pueden importar canciones, listas públicas o artistas. No accedemos a tu cuenta ni copiamos contenido: " +
                                 "solo leemos los títulos públicos y los buscamos en YouTube. Puede fallar si el servicio cambia su página.";

    private static readonly string[] AllowedHosts = ["www.youtube.com", "open.spotify.com", "api.deezer.com", "music.apple.com", "soundcloud.com"];

    [GeneratedRegex(@"^[A-Za-z0-9_-]{6,64}$")] private static partial Regex ListId();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")] private static partial Regex VideoId();
    [GeneratedRegex(@"^[A-Za-z0-9]{10,32}$")] private static partial Regex SpotifyId();
    [GeneratedRegex(@"^\d+$")] private static partial Regex Digits();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"^(www|m)\.")] private static partial Regex WwwPrefix();
    [GeneratedRegex(@"var ytInitialData = (\{.*?\});</script>", RegexOptions.Singleline)] private static partial Regex YtData();
    [GeneratedRegex(@"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline)] private static partial Regex NextData();
    [GeneratedRegex(@"<script[^>]*ld\+json[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex LdJson();
    [GeneratedRegex(@"^(.*) by (.+)$", RegexOptions.Singleline)] private static partial Regex ByArtist();
    [GeneratedRegex(@"\b(feat|ft|with)\b.*$")] private static partial Regex FeatTail();

    private static string Clean(string? s) => Spaces().Replace(s ?? "", " ").Trim();
    private static (ImportLink?, string?) Ok(string service, string kind, string id, string url) => (new ImportLink(service, kind, id, url), null);
    private static (ImportLink?, string?) No(string error) => (null, error);

    /// <summary>Enlace pegado → servicio, tipo (track / playlist / artist) y dirección a leer; o un mensaje de error claro.</summary>
    public static (ImportLink? Link, string? Error) ParseLink(string? text)
    {
        if (!Uri.TryCreate(Clean(text), UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http"))
            return No("Eso no parece un enlace. Pega la dirección completa (https://…).");
        var host = WwwPrefix().Replace(u.Host.ToLowerInvariant(), "");
        var seg = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var query = System.Web.HttpUtility.ParseQueryString(u.Query);

        if (host is "youtube.com" or "music.youtube.com" or "youtu.be")
        {
            var list = query["list"];
            if (host != "youtu.be" && seg.Length > 0 && seg[0] == "playlist" && list is not null && ListId().IsMatch(list))
                return Ok("youtube", "playlist", list, "https://www.youtube.com/playlist?list=" + list);
            var v = host == "youtu.be" ? seg.ElementAtOrDefault(0)
                : seg.ElementAtOrDefault(0) == "watch" ? query["v"]
                : seg.ElementAtOrDefault(0) == "shorts" ? seg.ElementAtOrDefault(1) : null;
            if (v is not null && VideoId().IsMatch(v))
                return Ok("youtube", "track", v, "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=" + v));
            return No("De YouTube solo se pueden importar vídeos sueltos o listas de reproducción (no canales ni mixes).");
        }
        if (host == "open.spotify.com")
        {
            var s = seg.Length > 0 && seg[0].StartsWith("intl-", StringComparison.Ordinal) ? seg[1..] : seg;
            var type = s.ElementAtOrDefault(0);
            if (type is not ("track" or "playlist" or "artist"))
                return No("De Spotify solo se pueden importar canciones, listas y artistas (no álbumes ni podcasts).");
            var id = s.ElementAtOrDefault(1);
            if (id is null || !SpotifyId().IsMatch(id)) return No("No reconozco ese enlace de Spotify.");
            return Ok("spotify", type, id, $"https://open.spotify.com/embed/{type}/{id}");
        }
        if (host is "deezer.com" or "deezer.page.link")
        {
            if (host == "deezer.page.link") return No("Los enlaces cortos de Deezer no se pueden leer: abre la lista y copia la dirección completa.");
            var i = Array.FindIndex(seg, x => x is "track" or "playlist" or "artist");
            if (i < 0) return No("De Deezer solo se pueden importar canciones, listas y artistas (no álbumes).");
            var id = seg.ElementAtOrDefault(i + 1);
            if (id is null || !Digits().IsMatch(id)) return No("No reconozco ese enlace de Deezer.");
            var kind = seg[i];
            return Ok("deezer", kind, id, kind == "artist" ? $"https://api.deezer.com/artist/{id}/top?limit={Max}" : $"https://api.deezer.com/{kind}/{id}");
        }
        if (host == "music.apple.com")
        {
            var i = query["i"];
            var kind = seg.Contains("playlist") ? "playlist" : seg.Contains("artist") ? "artist" : (seg.Contains("song") || i is not null) ? "track" : null;
            if (kind is null) return No("De Apple Music solo se pueden importar canciones, listas y artistas (no álbumes).");
            return Ok("apple", kind, seg.LastOrDefault() ?? "", "https://music.apple.com" + u.AbsolutePath + (kind == "track" && i is not null ? "?i=" + Uri.EscapeDataString(i) : ""));
        }
        if (host == "soundcloud.com")
        {
            string[] reserved = ["sets", "tracks", "likes", "reposts", "albums", "popular-tracks"];
            if (seg.Length == 2 && !reserved.Contains(seg[1]))
                return Ok("soundcloud", "track", string.Join('/', seg), "https://soundcloud.com/oembed?format=json&url=" + Uri.EscapeDataString("https://soundcloud.com/" + string.Join('/', seg)));
            return No("De SoundCloud por ahora solo se pueden importar canciones sueltas (no listas ni artistas).");
        }
        if (host is "amazon.com" or "tidal.com") return No("Ese servicio no es compatible.");
        return No("Servicio no compatible. Usa YouTube, SoundCloud, Spotify, Deezer o Apple Music.");
    }

    /// <summary>Lee la dirección (solo servidores de la lista permitida, por https).</summary>
    public static async Task<string> FetchAsync(HttpClient http, string url, CancellationToken ct = default)
    {
        var u = new Uri(url);
        if (u.Scheme != "https" || !AllowedHosts.Contains(u.Host)) throw new InvalidOperationException("Dirección no permitida");
        using var req = new HttpRequestMessage(HttpMethod.Get, u);
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Accept-Language", "es-ES,es;q=0.9,en;q=0.8");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private static ImportTrack T(string? title, string? artist) => new(Clean(title), Clean(artist));
    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static JsonElement? Get(JsonElement e, params string[] path)
    {
        var cur = e;
        foreach (var p in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out cur)) return null;
        }
        return cur;
    }
    private static string RunsText(JsonElement? e)
    {
        if (e is not { } x || x.ValueKind != JsonValueKind.Object) return "";
        if (Str(x, "simpleText") is { } s) return s;
        return x.TryGetProperty("runs", out var r) && r.ValueKind == JsonValueKind.Array ? string.Concat(r.EnumerateArray().Select(a => Str(a, "text") ?? "")) : "";
    }

    /// <summary>Respuesta del servicio → nombre y canciones (título + artista), sin repetidas y con tope de <see cref="Max"/>.</summary>
    public static ImportInfo Parse(ImportLink link, string body)
    {
        var name = ""; var tracks = new List<ImportTrack>();
        try
        {
            switch (link.Service)
            {
                case "youtube" when link.Kind == "track":
                    using (var d = JsonDocument.Parse(body)) { name = Str(d.RootElement, "title") ?? ""; tracks.Add(T(name, (Str(d.RootElement, "author_name") ?? "").Replace(" - Topic", ""))); }
                    break;
                case "youtube":
                    ReadYoutubePlaylist(body, ref name, tracks);
                    break;
                case "spotify":
                    ReadSpotify(link, body, ref name, tracks);
                    break;
                case "deezer":
                    ReadDeezer(link, body, ref name, tracks);
                    break;
                case "apple":
                    ReadApple(body, ref name, tracks);
                    break;
                case "soundcloud":
                    using (var d = JsonDocument.Parse(body))
                    {
                        var title = Clean(Str(d.RootElement, "title")); name = title;
                        var m = ByArtist().Match(title);
                        tracks.Add(m.Success ? T(m.Groups[1].Value, m.Groups[2].Value) : T(title, Str(d.RootElement, "author_name")));
                    }
                    break;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { tracks.Clear(); }
        var seen = new HashSet<string>();
        var list = tracks.Where(t => t.Title.Length is > 0 and <= 200 && seen.Add((t.Title + "|" + t.Artist).ToLowerInvariant())).Take(Max).ToList();
        var n = Clean(name); if (n.Length > 60) n = n[..60];
        return new ImportInfo(n, list);
    }

    private static void ReadYoutubePlaylist(string body, ref string name, List<ImportTrack> tracks)
    {
        var m = YtData().Match(body);
        if (!m.Success) return;
        using var doc = JsonDocument.Parse(m.Groups[1].Value);
        string header = "";
        void Walk(JsonElement e, int depth)
        {
            if (depth > 40) return;
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals("playlistVideoRenderer"))
                        tracks.Add(T(RunsText(Get(p.Value, "title")), RunsText(Get(p.Value, "shortBylineText")).Replace(" - Topic", "")));
                    else if (p.NameEquals("playlistHeaderRenderer") && header.Length == 0) header = RunsText(Get(p.Value, "title"));
                    else Walk(p.Value, depth + 1);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x, depth + 1);
        }
        Walk(doc.RootElement, 0);
        name = header;
    }

    private static void ReadSpotify(ImportLink link, string body, ref string name, List<ImportTrack> tracks)
    {
        var m = NextData().Match(body);
        if (!m.Success) return;
        using var doc = JsonDocument.Parse(m.Groups[1].Value);
        if (Get(doc.RootElement, "props", "pageProps", "state", "data", "entity") is not { } e) return;
        if (link.Kind == "track")
        {
            name = Str(e, "name") ?? "";
            var artists = e.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array ? string.Join(", ", a.EnumerateArray().Select(x => Str(x, "name") ?? "")) : "";
            tracks.Add(T(Str(e, "title") ?? name, artists));
            return;
        }
        name = Str(e, "name") ?? Str(e, "title") ?? "";
        if (e.TryGetProperty("trackList", out var tl) && tl.ValueKind == JsonValueKind.Array)
            foreach (var t in tl.EnumerateArray()) tracks.Add(T(Str(t, "title"), Str(t, "subtitle")));
    }

    private static void ReadDeezer(ImportLink link, string body, ref string name, List<ImportTrack> tracks)
    {
        using var d = JsonDocument.Parse(body);
        var r = d.RootElement;
        string Artist(JsonElement t) => Get(t, "artist", "name") is { ValueKind: JsonValueKind.String } n ? n.GetString()! : "";
        if (link.Kind == "track") { name = Str(r, "title") ?? ""; tracks.Add(T(name, Artist(r))); }
        else if (link.Kind == "playlist")
        {
            name = Str(r, "title") ?? "";
            if (Get(r, "tracks", "data") is { ValueKind: JsonValueKind.Array } data) foreach (var t in data.EnumerateArray()) tracks.Add(T(Str(t, "title"), Artist(t)));
        }
        else if (r.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in data.EnumerateArray()) tracks.Add(T(Str(t, "title"), Artist(t)));
            name = tracks.Count > 0 ? tracks[0].Artist : "Artista";
        }
    }

    private static void ReadApple(string body, ref string name, List<ImportTrack> tracks)
    {
        foreach (Match m in LdJson().Matches(body))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(m.Groups[1].Value); } catch (JsonException) { continue; }
            using (doc)
            {
                var d = doc.RootElement;
                string By(JsonElement? x)
                {
                    if (x is not { } v) return "";
                    if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0) v = v[0];
                    return Str(v, "name") ?? "";
                }
                var type = Str(d, "@type");
                if (type == "MusicRecording") { tracks.Add(T(Str(d, "name"), By(Get(d, "byArtist")))); name = Str(d, "name") ?? name; }
                else if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("track", out var tr) && tr.ValueKind == JsonValueKind.Array)
                {
                    name = Str(d, "name") ?? name;
                    foreach (var t in tr.EnumerateArray()) tracks.Add(T(Str(t, "name"), By(Get(t, "byArtist")) is { Length: > 0 } a ? a : By(Get(d, "byArtist"))));
                }
                else if (type is "MusicGroup" or "Person") name = Str(d, "name") ?? name;
            }
        }
    }

    // ---------- Elegir la canción correcta entre los resultados de YouTube Music ----------

    private static string Norm(string? s)
    {
        var d = Clean(s).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d) { if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.IsLetterOrDigit(c) ? c : ' '); }
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>La primera canción cuyo título y artista encajan con la buscada, o null si ninguna.</summary>
    public static YouTubeVideo? PickMatch(ImportTrack wanted, IEnumerable<YouTubeVideo> results)
    {
        var wt = FeatTail().Replace(Norm(wanted.Title), "").Trim();
        var wa = Norm(wanted.Artist).Split([" and ", " y "], StringSplitOptions.None)[0].Trim();
        foreach (var r in results)
        {
            var t = Norm(r.Title);
            var a = Norm(r.Channel.Split(" · ")[0]);
            var titleOk = t == wt || t.Contains(wt) || wt.Contains(t);
            var artistOk = wa.Length == 0 || a.Contains(wa) || wa.Contains(a.Split(' ')[0]);
            if (titleOk && artistOk) return r;
        }
        return null;
    }
}
