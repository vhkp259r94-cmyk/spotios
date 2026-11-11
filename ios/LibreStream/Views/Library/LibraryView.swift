import SwiftUI

struct LibraryView: View {
    @EnvironmentObject var catalog: CatalogService
    @EnvironmentObject var player: AudioPlayerManager
    @State private var query = ""

    private var filtered: [CatalogItem] {
        guard !query.isEmpty else { return catalog.items }
        let q = MetadataMatcher.fold(query)
        return catalog.items.filter {
            MetadataMatcher.fold("\($0.artist) \($0.title)").contains(q)
        }
    }

    var body: some View {
        NavigationStack {
            List(Array(filtered.enumerated()), id: \.element.id) { idx, item in
                Button {
                    player.play(filtered, startAt: idx)
                } label: {
                    VStack(alignment: .leading) {
                        Text(item.title).font(.headline)
                        Text(item.artist).font(.subheadline).foregroundStyle(.secondary)
                    }
                }
            }
            .searchable(text: $query)
            .refreshable { await catalog.refresh() }
            .navigationTitle("LibreStream")
            .overlay { if let e = catalog.error, catalog.items.isEmpty { Text(e).foregroundStyle(.red) } }
            .safeAreaInset(edge: .bottom) { MiniPlayer() }
        }
    }
}
