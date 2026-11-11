using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibreStream.Core;

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
