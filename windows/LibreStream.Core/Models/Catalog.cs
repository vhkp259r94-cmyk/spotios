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
