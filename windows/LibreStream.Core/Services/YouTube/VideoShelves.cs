namespace LibreStream.Core;

/// <summary>Una estantería del apartado Vídeos: un título y las búsquedas de YouTube que la llenan.</summary>
public sealed record VideoShelf(string Title, string[] Queries);

public static class VideoShelves
{
    public static readonly VideoShelf[] Default =
    [
        new("Eventos del año", ["La Velada del Año", "Kings League resumen"]),
        new("Series y tráilers", ["tráiler oficial serie 2026", "series estreno tráiler"]),
        new("YouTubers", ["Ibai", "AuronPlay", "TheGrefg"]),
        new("Deporte", ["resumen partido goles"]),
        new("Documentales", ["documental completo en español"]),
        new("Humor y entretenimiento", ["monólogo humor", "programa entretenimiento"]),
    ];

    /// <summary>
    /// Mezcla los resultados de varias búsquedas alternándolos (para que la fila sea variada), deja solo vídeos
    /// de verdad (ni directos, ni pistas de solo audio, ni «shorts» de menos de 45 s) y sin repetidos.
    /// </summary>
    public static List<YouTubeVideo> Merge(IEnumerable<IReadOnlyList<YouTubeVideo>> results, int take)
    {
        var lists = results.ToList();
        var merged = new List<YouTubeVideo>();
        for (var i = 0; i < lists.Select(l => l.Count).DefaultIfEmpty(0).Max(); i++)
            foreach (var list in lists)
                if (i < list.Count) merged.Add(list[i]);
        return merged.Where(v => ContentFilter.IsVideo(v) && v.DurationSec >= 45).DistinctBy(v => v.Id).Take(take).ToList();
    }
}
