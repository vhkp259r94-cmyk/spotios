using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibreStream.Core;

public sealed record YouTubeVideo(string Id, string Title, string Channel, double DurationSec, string? Published = null)
{
    public bool IsLive => DurationSec <= 0;
    public Uri Thumbnail => new($"https://i.ytimg.com/vi/{Id}/mqdefault.jpg");
}

/// <summary>
/// Búsqueda de vídeos de YouTube leyendo los metadatos de la página pública de resultados.
/// Solo obtiene id, título, canal y duración: la reproducción se hace con el reproductor
/// oficial embebido (no se extraen ni descargan streams).
/// </summary>
public static partial class YouTubeSearch
{
    [GeneratedRegex(@"var ytInitialData = (\{.*?\});</script>", RegexOptions.Singleline)]
    private static partial Regex InitialData();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoId();

    public static bool IsValidId(string? id) => id is not null && VideoId().IsMatch(id);

    public static async Task<List<YouTubeVideo>> SearchAsync(HttpClient http, string query, CancellationToken ct = default, bool videosOnly = false, bool thisWeek = false)
    {
        // sp=EgIQAQ== es el filtro público "Tipo: vídeo" (excluye listas, canales y directos agrupados)
        var url = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query.Trim())
            + (thisWeek ? "&sp=EgQIAxAB" : videosOnly ? "&sp=EgIQAQ%3D%3D" : ""); // EgQIAxAB = "Esta semana" + "Vídeo"
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Accept-Language", "es-ES,es;q=0.9,en;q=0.8");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    public static List<YouTubeVideo> Parse(string html)
    {
        var result = new List<YouTubeVideo>();
        var m = InitialData().Match(html);
        if (!m.Success) return result;
        try
        {
            using var doc = JsonDocument.Parse(m.Groups[1].Value);
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
                if (p.NameEquals("videoRenderer") && TryRead(p.Value) is { } v) acc.Add(v);
                else Walk(p.Value, acc, depth + 1);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray()) Walk(x, acc, depth + 1);
        }
    }

    private static YouTubeVideo? TryRead(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;
        var id = v.TryGetProperty("videoId", out var i) ? i.GetString() : null;
        if (!IsValidId(id)) return null;
        var title = FirstRun(v, "title") ?? "";
        if (title.Length == 0) return null;
        var channel = FirstRun(v, "ownerText") ?? FirstRun(v, "longBylineText") ?? "";
        var length = v.TryGetProperty("lengthText", out var l) && l.TryGetProperty("simpleText", out var s) ? s.GetString() : null;
        var published = FirstRun(v, "publishedTimeText");
        return new YouTubeVideo(id!, title, channel, ParseLength(length), published);
    }

    private static string? FirstRun(JsonElement v, string name)
    {
        if (!v.TryGetProperty(name, out var t) || t.ValueKind != JsonValueKind.Object) return null;
        if (t.TryGetProperty("simpleText", out var st)) return st.GetString();
        return t.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array && runs.GetArrayLength() > 0
            && runs[0].TryGetProperty("text", out var text) ? text.GetString() : null;
    }

    /// <summary>"3:25" → 205, "1:02:03" → 3723; nulo o no numérico (directos) → 0.</summary>
    public static double ParseLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        double total = 0;
        foreach (var part in text.Split(':'))
        {
            if (!int.TryParse(part, out var n)) return 0;
            total = total * 60 + n;
        }
        return total;
    }
}
