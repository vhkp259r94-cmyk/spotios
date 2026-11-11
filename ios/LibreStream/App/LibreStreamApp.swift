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
