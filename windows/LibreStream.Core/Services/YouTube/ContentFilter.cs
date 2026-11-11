using System.Text.RegularExpressions;

namespace LibreStream.Core;

/// <summary>
/// Separa lo que es «música» de lo que es «vídeo» entre los resultados de YouTube, para que el apartado
/// Música solo muestre canciones y el de Vídeos solo vídeos (sin pistas de solo audio ni letras).
/// </summary>
public static partial class ContentFilter
{
    [GeneratedRegex(@"\b(tutorial|lesson|lecci[oó]n|reaction|reacci[oó]n|karaoke|how to|c[oó]mo tocar|review|podcast|interview|entrevista|nightcore|slowed|sped up|8d audio|full album|[aá]lbum completo|playlist|mix|megamix|compilation|recopilaci[oó]n)\b|\d+\s*(hours?|horas?)", RegexOptions.IgnoreCase)]
    private static partial Regex NotASong();

    [GeneratedRegex(@"\b(audio|lyrics?|letra|visuali[sz]er|topic|full album|[aá]lbum completo)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AudioOnly();

    /// <summary>Canción suelta: entre 1 y 12 minutos, no en directo y sin pinta de tutorial, mezcla o recopilatorio.</summary>
    public static bool IsSong(YouTubeVideo v) =>
        !v.IsLive && v.DurationSec is > 60 and <= 720 && !NotASong().IsMatch(v.Title);

    /// <summary>Vídeo propiamente dicho: no en directo y que no sea una pista de solo audio o con letra fija.</summary>
    public static bool IsVideo(YouTubeVideo v) =>
        !v.IsLive && v.DurationSec > 0 && !AudioOnly().IsMatch(v.Title);
}
