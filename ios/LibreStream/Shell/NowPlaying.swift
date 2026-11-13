import Foundation
import MediaPlayer
import UIKit

/// Pantalla de bloqueo y Centro de control de iOS: título, artista, carátula cuadrada, barra de tiempo y los botones
/// anterior / siguiente (sin «±10 s»). La interfaz web envía los datos; los botones vuelven a ella como eventos.
final class NowPlayingBridge {
    /// Se llama con {"cmd": "play" | "pause" | "next" | "prev" | "seek", "position": segundos}.
    var onCommand: (([String: Any]) -> Void)?

    private var info: [String: Any] = [:]
    private var configured = false
    private var holding = false   // true solo mientras está en pausa: sonando manda el reproductor de YouTube
    private var playing = false
    private var artworkURL = ""
    private var artworkObject: MPMediaItemArtwork?
    private var artworkTask: URLSessionDataTask?

    private func configureCommands() {
        guard !configured else { return }
        configured = true
        let c = MPRemoteCommandCenter.shared()
        c.playCommand.isEnabled = true
        c.playCommand.addTarget { [weak self] _ in if self?.holding == true { self?.onCommand?(["cmd": "play"]) }; return .success }
        c.pauseCommand.isEnabled = true
        c.pauseCommand.addTarget { [weak self] _ in if self?.holding == true { self?.onCommand?(["cmd": "pause"]) }; return .success }
        c.togglePlayPauseCommand.isEnabled = true
        c.togglePlayPauseCommand.addTarget { [weak self] _ in
            if self?.holding == true { self?.onCommand?(["cmd": (self?.playing ?? false) ? "pause" : "play"]) }; return .success
        }
        c.nextTrackCommand.isEnabled = true
        c.nextTrackCommand.addTarget { [weak self] _ in if self?.holding == true { self?.onCommand?(["cmd": "next"]) }; return .success }
        c.previousTrackCommand.isEnabled = true
        c.previousTrackCommand.addTarget { [weak self] _ in if self?.holding == true { self?.onCommand?(["cmd": "prev"]) }; return .success }
        c.skipForwardCommand.isEnabled = false
        c.skipBackwardCommand.isEnabled = false
        c.changePlaybackPositionCommand.isEnabled = true
        c.changePlaybackPositionCommand.addTarget { [weak self] event in
            guard let e = event as? MPChangePlaybackPositionCommandEvent else { return .commandFailed }
            if self?.holding == true { self?.onCommand?(["cmd": "seek", "position": e.positionTime]) }
            return .success
        }
    }

    func update(title: String, artist: String, album: String, artwork: String, duration: Double, position: Double, playing: Bool) {
        configureCommands()
        self.playing = playing
        var next: [String: Any] = [
            MPMediaItemPropertyTitle: title,
            MPMediaItemPropertyArtist: artist,
            MPNowPlayingInfoPropertyElapsedPlaybackTime: position,
            MPNowPlayingInfoPropertyPlaybackRate: playing ? 1.0 : 0.0,
        ]
        if !album.isEmpty { next[MPMediaItemPropertyAlbumTitle] = album }
        if duration > 0 { next[MPMediaItemPropertyPlaybackDuration] = duration }
        if artwork == artworkURL, let art = artworkObject { next[MPMediaItemPropertyArtwork] = art }
        info = next
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
        MPNowPlayingInfoCenter.default().playbackState = playing ? .playing : .paused
        loadArtwork(artwork)
    }

    /// En pausa YouTube suelta su sesión y iOS quita el reproductor al cabo de unos segundos: la app lo sostiene con los datos de Nsound.
    func hold(title: String, artist: String, album: String, artwork: String, duration: Double, position: Double) {
        holding = true
        update(title: title, artist: artist, album: album, artwork: artwork, duration: duration, position: position, playing: false)
    }

    /// Vuelve a sonar: manda otra vez el reproductor de YouTube (no se borra nada: lo reescribe él).
    func release() { holding = false }

    func updateState(position: Double, duration: Double, playing: Bool) {
        guard !info.isEmpty else { return }
        self.playing = playing
        info[MPNowPlayingInfoPropertyElapsedPlaybackTime] = position
        info[MPNowPlayingInfoPropertyPlaybackRate] = playing ? 1.0 : 0.0
        if duration > 0 { info[MPMediaItemPropertyPlaybackDuration] = duration }
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
        MPNowPlayingInfoCenter.default().playbackState = playing ? .playing : .paused
    }

    /// Solo se descargan carátulas de los servidores de imágenes de YouTube.
    private func loadArtwork(_ s: String) {
        if s == artworkURL { return }
        artworkURL = s
        artworkObject = nil
        artworkTask?.cancel()
        guard let url = URL(string: s), url.scheme == "https", let host = url.host,
              host.hasSuffix("googleusercontent.com") || host.hasSuffix("ggpht.com") || host.hasSuffix("ytimg.com") else { return }
        artworkTask = URLSession.shared.dataTask(with: url) { [weak self] data, _, _ in
            guard let self = self, let data = data, data.count < 3_000_000, let image = UIImage(data: data) else { return }
            let art = MPMediaItemArtwork(boundsSize: image.size) { _ in image }
            DispatchQueue.main.async {
                guard self.artworkURL == s else { return }
                self.artworkObject = art
                self.info[MPMediaItemPropertyArtwork] = art
                MPNowPlayingInfoCenter.default().nowPlayingInfo = self.info
            }
        }
        artworkTask?.resume()
    }
}
