using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibreStream.Core;

/// <summary>Modelos que reflejan schemas/catalog.schema.json.</summary>
public sealed record Catalog(string SchemaVersion, DateTimeOffset GeneratedAt, List<CatalogItem> Items);

public enum ItemKind { Track, Video, Episode }
public enum Provider { Direct, ArchiveOrg, Jamendo, Fma, Youtube }

public sealed record License(string Spdx, string? Attribution);

public sealed record Source(Provider Provider, Uri Url, string? Mime, int? BitrateKbps, double? DurationSec)
{
    /// <summary>youtube solo es referencia (embebido oficial): no se extraen streams.</summary>
    public bool IsDirectlyPlayable => Provider != Provider.Youtube;
}

public sealed record CatalogItem(
    string Id, ItemKind Kind, string Title, string Artist, List<string>? Featuring,
    TrackVersion? Version, string? Album, double DurationSec, Uri? ArtworkUrl,
    List<string>? Genres, License License, List<Source> Sources);

/// <summary>Descarga el catálogo de GitHub Pages con caché en disco + ETag y lo deduplica.</summary>
public sealed class CatalogService(Uri url, string cacheDir, HttpClient? http = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly HttpClient _http = http ?? new HttpClient();
    private string CacheFile => Path.Combine(cacheDir, "catalog.json");
    private string EtagFile => Path.Combine(cacheDir, "catalog.etag");

    public IReadOnlyList<CatalogItem> Items { get; private set; } = [];

    public static IReadOnlyList<CatalogItem> Parse(string json) =>
        MetadataMatcher.Deduplicate(
            JsonSerializer.Deserialize<Catalog>(json, Json)?.Items ?? [], i => i.Id, i => i.DurationSec);

    public async Task<IReadOnlyList<CatalogItem>> RefreshAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        if (Items.Count == 0 && File.Exists(CacheFile)) Items = Parse(await File.ReadAllTextAsync(CacheFile, ct));

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (Items.Count > 0 && File.Exists(EtagFile))
            req.Headers.TryAddWithoutValidation("If-None-Match", await File.ReadAllTextAsync(EtagFile, ct));

        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotModified) return Items;
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        Items = Parse(body);
        await File.WriteAllTextAsync(CacheFile, body, ct);
        if (resp.Headers.ETag is { } etag) await File.WriteAllTextAsync(EtagFile, etag.ToString(), ct);
        return Items;
    }
}
