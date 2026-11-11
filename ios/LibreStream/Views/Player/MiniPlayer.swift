import SwiftUI
import AVKit

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
