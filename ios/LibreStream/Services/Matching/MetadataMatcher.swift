import Foundation
import CryptoKit

/// Normaliza títulos, compara duraciones (±4 s) y genera la clave de deduplicación
/// MD5(artista|título|versión). Los patrones son idénticos a `tools/title_normalizer.py`.
public struct MetadataMatcher {

    public enum Version: String, Codable, CaseIterable {
        case original, remix, live, acoustic, instrumental, cover, edit
    }

    public struct CleanTitle: Equatable {
        public let title: String
        public let featuring: [String]
        public let version: Version
    }

    public static let durationTolerance: TimeInterval = 4.0

    // MARK: - Patrones

    private static func rx(_ p: String) -> NSRegularExpression {
        // Patrones constantes: si fallan es un error de programación.
        try! NSRegularExpression(pattern: p, options: [.caseInsensitive])
    }

    private static let noise = rx(
        #"\s*[\(\[\{][^\)\]\}]*?\b(official|oficial|video|vídeo|audio|lyrics?|letra|visualizer|"# +
        #"hd|hq|4k|1080p|720p|explicit|clean|mv|m/v|remaster(?:ed)?|\d{4}\s+remaster(?:ed)?)\b[^\)\]\}]*[\)\]\}]"#)
    private static let noiseTail = rx(#"\s+[-–|]\s+(official\s+)?(music\s+)?(video|audio|lyrics?|visualizer)\s*$"#)
    private static let remasterTail = rx(#"\s+[-–]\s+(\d{4}\s+)?remaster(ed)?(\s+\d{4})?(\s+version)?\s*$"#)
    private static let feat = rx(#"\s*[\(\[]?\b(?:feat\.?|ft\.?|featuring)\s+([^\)\]]+?)[\)\]]?(?=\s*[\(\[\-–]|$)"#)
    private static let versionGroup = rx(#"\s*[\(\[]([^\)\]]*)[\)\]]|\s+[-–]\s+([^\-–]+)$"#)
    private static let featSplit = rx(#",|&| y | and "#)
    private static let versions: [(Version, NSRegularExpression)] = [
        (.remix, rx(#"\b(remix|rmx|mix|bootleg|rework)\b"#)),
        (.live, rx(#"\b(live|en vivo|en directo|unplugged)\b"#)),
        (.acoustic, rx(#"\b(acoustic|acústic[oa])\b"#)),
        (.instrumental, rx(#"\b(instrumental|karaoke)\b"#)),
        (.cover, rx(#"\bcover\b"#)),
        (.edit, rx(#"\b(radio edit|edit|extended)\b"#)),
    ]

    // MARK: - API

    public static func clean(_ raw: String) -> CleanTitle {
        var t = replace(noise, in: raw)
        t = replace(noiseTail, in: t)
        t = replace(remasterTail, in: t)

        var featuring: [String] = []
        if let m = feat.firstMatch(in: t, range: ns(t)),
           let whole = Range(m.range, in: t), let names = Range(m.range(at: 1), in: t) {
            featuring = split(String(t[names]))
            t.removeSubrange(whole)
        }

        var version: Version = .original
        outer: for m in versionGroup.matches(in: t, range: ns(t)) {
            let idx = m.range(at: 1).location != NSNotFound ? 1 : 2
            guard let chunkR = Range(m.range(at: idx), in: t), let wholeR = Range(m.range, in: t) else { continue }
            let chunk = String(t[chunkR])
            for (v, r) in versions where r.firstMatch(in: chunk, range: ns(chunk)) != nil {
                version = v
                t.removeSubrange(wholeR)
                break outer
            }
        }

        t = t.replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression)
            .trimmingCharacters(in: CharacterSet(charactersIn: " -–|"))
        return CleanTitle(title: t, featuring: featuring, version: version)
    }

    /// Clave estable para deduplicar: MD5 en hex de `fold(artista)|fold(título)|versión`.
    public static func dedupeKey(artist: String, title: String, version: Version = .original) -> String {
        let base = "\(fold(artist))|\(fold(title))|\(version.rawValue)"
        return Insecure.MD5.hash(data: Data(base.utf8)).map { String(format: "%02x", $0) }.joined()
    }

    public static func durationsMatch(_ a: TimeInterval, _ b: TimeInterval) -> Bool {
        abs(a - b) <= durationTolerance
    }

    /// ¿Corresponde un candidato (p. ej. resultado de un proveedor) a un elemento del catálogo?
    public static func matches(catalogArtist: String, catalogTitle: String, catalogDuration: TimeInterval,
                               candidateArtist: String, candidateTitle: String, candidateDuration: TimeInterval) -> Bool {
        let a = clean(catalogTitle), b = clean(candidateTitle)
        return durationsMatch(catalogDuration, candidateDuration)
            && dedupeKey(artist: catalogArtist, title: a.title, version: a.version)
            == dedupeKey(artist: candidateArtist, title: b.title, version: b.version)
    }

    /// Elimina duplicados conservando el primero (misma clave y duración dentro de tolerancia).
    public static func deduplicate<T>(_ items: [T], key: (T) -> String, duration: (T) -> TimeInterval) -> [T] {
        var seen: [String: [TimeInterval]] = [:]
        return items.filter { item in
            let k = key(item), d = duration(item)
            if seen[k, default: []].contains(where: { durationsMatch($0, d) }) { return false }
            seen[k, default: []].append(d)
            return true
        }
    }

    // MARK: - Utilidades

    static func fold(_ s: String) -> String {
        s.folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: nil)
            .replacingOccurrences(of: #"[^\w\s]"#, with: " ", options: .regularExpression)
            .replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespaces)
    }

    private static func ns(_ s: String) -> NSRange { NSRange(s.startIndex..., in: s) }

    private static func replace(_ r: NSRegularExpression, in s: String) -> String {
        r.stringByReplacingMatches(in: s, range: ns(s), withTemplate: "")
    }

    private static func split(_ s: String) -> [String] {
        featSplit.stringByReplacingMatches(in: s, range: ns(s), withTemplate: "\u{0}")
            .split(separator: "\u{0}")
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
    }
}
