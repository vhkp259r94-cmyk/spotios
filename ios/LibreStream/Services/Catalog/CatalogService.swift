import Foundation

/// Descarga el catálogo de GitHub Pages con caché por ETag y lo deduplica.
@MainActor
public final class CatalogService: ObservableObject {
    @Published public private(set) var items: [CatalogItem] = []
    @Published public private(set) var error: String?

    private let url: URL
    private let cacheURL = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("catalog.json")
    private let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }()

    public init(url: URL) { self.url = url }

    public func refresh() async {
        if items.isEmpty, let cached = try? Data(contentsOf: cacheURL) { apply(cached) }
        var req = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData)
        if let etag = UserDefaults.standard.string(forKey: "catalog.etag"), !items.isEmpty {
            req.setValue(etag, forHTTPHeaderField: "If-None-Match")
        }
        do {
            let (data, resp) = try await URLSession.shared.data(for: req)
            guard let http = resp as? HTTPURLResponse else { return }
            if http.statusCode == 304 { return }
            guard http.statusCode == 200 else { error = "HTTP \(http.statusCode)"; return }
            if apply(data) {
                try? data.write(to: cacheURL, options: .atomic)
                UserDefaults.standard.set(http.value(forHTTPHeaderField: "ETag"), forKey: "catalog.etag")
            }
        } catch {
            self.error = error.localizedDescription
        }
    }

    @discardableResult
    private func apply(_ data: Data) -> Bool {
        guard let catalog = try? decoder.decode(Catalog.self, from: data) else {
            error = "Catálogo inválido"
            return false
        }
        items = MetadataMatcher.deduplicate(catalog.items, key: \.id, duration: \.durationSec)
        error = nil
        return true
    }
}
