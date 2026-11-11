using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibreStream.Core;

/// <summary>Una línea de letra. <see cref="Time"/> en segundos; negativo si la letra no está sincronizada.</summary>
public sealed record LyricLine(double Time, string Text);

public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, bool Synced);

/// <summary>Letras desde LRCLIB (https://lrclib.net), base de datos abierta y sin clave.</summary>
public static partial class LyricsService
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"\s*(-\s*Topic|VEVO|Official|Oficial)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelNoise();

    /// <summary>Convierte texto LRC ("[01:02.50] línea") en líneas ordenadas por tiempo.</summary>
    public static List<LyricLine> ParseLrc(string? lrc)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return lines;
        foreach (var raw in lrc.Split('\n'))
        {
            var tags = TimeTag().Matches(raw);
            if (tags.Count == 0) continue;
            var text = TimeTag().Replace(raw, "").Trim();
            foreach (Match t in tags) // una línea puede llevar varias marcas (estribillos)
            {
                var frac = t.Groups[3].Success ? double.Parse("0." + t.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                lines.Add(new LyricLine(int.Parse(t.Groups[1].Value) * 60 + int.Parse(t.Groups[2].Value) + frac, text));
            }
        }
        return lines.OrderBy(l => l.Time).ToList();
    }

    /// <summary>Índice de la línea que suena en <paramref name="position"/> (−1 si aún no ha empezado).</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, double position)
    {
        var index = -1;
        for (var i = 0; i < lines.Count && lines[i].Time <= position; i++) index = i;
        return index;
    }

    /// <summary>
    /// Deduce artista y título de un vídeo de YouTube ("Artista - Canción (Official Video)", canal "ArtistaVEVO").
    /// </summary>
    public static (string Artist, string Title) GuessTrack(string videoTitle, string channel)
    {
        var clean = MetadataMatcher.Clean(videoTitle).Title;
        var artist = ChannelNoise().Replace(channel, "").Trim();
        foreach (var sep in new[] { " - ", " – ", " — " })
        {
            var i = clean.IndexOf(sep, StringComparison.Ordinal);
            if (i <= 0) continue;
            return (clean[..i].Trim(), clean[(i + sep.Length)..].Trim());
        }
        return (artist, clean);
    }

    public static async Task<Lyrics?> FindAsync(HttpClient http, string artist, string title, double durationSec, CancellationToken ct = default)
    {
        var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var best = Pick(await GetAsync(http, url, ct), durationSec);
        if (best is null) // segundo intento: búsqueda libre
            best = Pick(await GetAsync(http, $"https://lrclib.net/api/search?q={Uri.EscapeDataString($"{artist} {title}")}", ct), durationSec);
        return best;
    }

    private static async Task<string> GetAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "LibreStream (reproductor de codigo abierto)");
        using var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct) : "[]";
    }

    /// <summary>Elige el mejor resultado: primero sincronizado, después duración más cercana.</summary>
    public static Lyrics? Pick(string json, double durationSec)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            Lyrics? best = null;
            var bestScore = double.MaxValue;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var synced = ParseLrc(Str(e, "syncedLyrics"));
                var plain = Str(e, "plainLyrics");
                if (synced.Count == 0 && string.IsNullOrWhiteSpace(plain)) continue;
                var duration = e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
                var diff = durationSec > 0 && duration > 0 ? Math.Abs(duration - durationSec) : 30;
                var score = diff + (synced.Count == 0 ? 1000 : 0);
                if (score >= bestScore) continue;
                bestScore = score;
                best = synced.Count > 0
                    ? new Lyrics(synced, true)
                    : new Lyrics(plain!.Split('\n').Select(l => new LyricLine(-1, l.Trim())).ToList(), false);
            }
            return best;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
