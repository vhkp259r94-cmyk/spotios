import Foundation

/// Modelos que reflejan schemas/catalog.schema.json.
public struct Catalog: Codable {
    public let schemaVersion: String
    public let generatedAt: Date
    public let items: [CatalogItem]
}

public struct CatalogItem: Codable, Identifiable, Hashable {
    public enum Kind: String, Codable { case track, video, episode }

    public let id: String
    public let kind: Kind
    public let title: String
    public let artist: String
    public let featuring: [String]?
    public let version: MetadataMatcher.Version?
    public let album: String?
    public let durationSec: TimeInterval
    public let artworkUrl: URL?
    public let genres: [String]?
    public let license: License
    public let sources: [Source]

    public struct License: Codable, Hashable {
        public let spdx: String
        public let attribution: String?
    }
}

public struct Source: Codable, Hashable {
    public enum Provider: String, Codable {
        case direct, archive_org, jamendo, fma, youtube

        /// Proveedores cuya URL es un fichero/HLS reproducible directamente por AVPlayer.
        /// `youtube` solo se admite como referencia (embebido oficial), no se extraen streams.
        public var isDirectlyPlayable: Bool { self != .youtube }
    }

    public let provider: Provider
    public let url: URL
    public let mime: String?
    public let bitrateKbps: Int?
    public let durationSec: TimeInterval?
}
