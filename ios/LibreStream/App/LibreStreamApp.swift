import SwiftUI
import AVKit

@main
struct LibreStreamApp: App {
    @StateObject private var catalog = CatalogService(
        url: URL(string: Bundle.main.object(forInfoDictionaryKey: "LSCatalogURL") as? String
                 ?? "https://vhkp259r94-cmyk.github.io/spotios/catalog.json")!)
    @StateObject private var player = AudioPlayerManager.shared

    var body: some Scene {
        WindowGroup {
            LibraryView()
                .environmentObject(catalog)
                .environmentObject(player)
                .task { await catalog.refresh() }
        }
    }
}

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

struct MiniPlayer: View {
    @EnvironmentObject var player: AudioPlayerManager

    var body: some View {
        if let item = player.current {
            VStack(spacing: 6) {
                if item.kind == .video {
                    VideoPlayer(player: player.player).frame(height: 200)
                }
                HStack {
                    VStack(alignment: .leading) {
                        Text(item.title).bold().lineLimit(1)
                        Text(item.artist).font(.caption).lineLimit(1)
                    }
                    Spacer()
                    Button(action: player.previous) { Image(systemName: "backward.fill") }
                    Button(action: player.togglePlayPause) {
                        Image(systemName: player.isPlaying ? "pause.fill" : "play.fill")
                    }
                    Button(action: player.next) { Image(systemName: "forward.fill") }
                }
                ProgressView(value: player.elapsed, total: max(player.duration, 1))
            }
            .padding()
            .background(.ultraThinMaterial)
        }
    }
}
