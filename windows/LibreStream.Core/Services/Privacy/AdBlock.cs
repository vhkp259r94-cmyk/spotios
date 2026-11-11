namespace LibreStream.Core;

/// <summary>
/// Filtro de red para el reproductor embebido: corta peticiones a servidores de anuncios y
/// rastreo conocidos. No modifica el reproductor ni salta anuncios por script, así que los
/// anuncios que YouTube sirve desde los mismos servidores que el vídeo pueden seguir saliendo.
/// </summary>
public static class AdBlock
{
    /// <summary>Dominios bloqueados (incluye subdominios).</summary>
    public static readonly string[] Hosts =
    [
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "googletagservices.com",
        "googletagmanager.com", "google-analytics.com", "adservice.google.com", "2mdn.net",
        "imasdk.googleapis.com", "moatads.com", "scorecardresearch.com", "adsrvr.org", "amazon-adsystem.com",
    ];

    /// <summary>Rutas de anuncios dentro de los dominios de YouTube.</summary>
    public static readonly string[] YouTubePaths =
    [
        "/pagead/", "/api/stats/ads", "/get_midroll_info", "/ptracking", "/youtubei/v1/player/ad_break",
    ];

    /// <summary>Interruptor general (ajuste del usuario). Con false no se bloquea nada.</summary>
    public static bool Enabled { get; set; } = true;

    public static bool IsBlocked(Uri uri)
    {
        if (!Enabled) return false;
        var host = uri.Host.ToLowerInvariant();
        if (Hosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal))) return true;
        var youtube = host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal)
            || host.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal);
        return youtube && YouTubePaths.Any(p => uri.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsBlocked(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && IsBlocked(u);
}
