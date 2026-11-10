import Foundation
import AVFoundation
import MediaPlayer
import Combine
import UIKit

/// Reproductor único de la app: AVPlayer + AVAudioSession (segundo plano) +
/// MPNowPlayingInfoCenter / MPRemoteCommandCenter (pantalla de bloqueo, auriculares, CarPlay).
@MainActor
public final class AudioPlayerManager: ObservableObject {

    public static let shared = AudioPlayerManager()

    @Published public private(set) var queue: [CatalogItem] = []
    @Published public private(set) var currentIndex: Int?
    @Published public private(set) var isPlaying = false
    @Published public private(set) var elapsed: TimeInterval = 0
    @Published public private(set) var duration: TimeInterval = 0

    public var current: CatalogItem? { currentIndex.flatMap { queue.indices.contains($0) ? queue[$0] : nil } }

    public let player = AVPlayer()
    private var timeObserver: Any?
    private var cancellables = Set<AnyCancellable>()
    private var artworkTask: Task<Void, Never>?

    private init() {
        player.automaticallyWaitsToMinimizeStalling = true
        configureSession()
        configureRemoteCommands()
        observePlayer()
    }

    // MARK: - API pública

    public func play(_ items: [CatalogItem], startAt index: Int = 0) {
        queue = items
        load(index: index)
    }

    public func togglePlayPause() { isPlaying ? pause() : resume() }

    public func resume() {
        activateSession()
        player.play()
    }

    public func pause() { player.pause() }

    public func next() {
        guard let i = currentIndex, i + 1 < queue.count else { return pause() }
        load(index: i + 1)
    }

    public func previous() {
        if elapsed > 3 || (currentIndex ?? 0) == 0 { return seek(to: 0) }
        load(index: currentIndex! - 1)
    }

    public func seek(to seconds: TimeInterval) {
        let t = CMTime(seconds: max(0, seconds), preferredTimescale: 600)
        player.seek(to: t, toleranceBefore: .zero, toleranceAfter: .zero) { [weak self] _ in
            Task { @MainActor in self?.updateNowPlaying() }
        }
    }

    // MARK: - Carga

    private func load(index: Int) {
        guard queue.indices.contains(index),
              let source = queue[index].sources.first(where: { $0.provider.isDirectlyPlayable }) else { return }
        currentIndex = index
        elapsed = 0
        duration = queue[index].durationSec
        let item = AVPlayerItem(asset: AVURLAsset(url: source.url))
        item.preferredForwardBufferDuration = 10
        player.replaceCurrentItem(with: item)
        resume()
        loadArtwork(for: queue[index])
    }

    // MARK: - AVAudioSession

    private func configureSession() {
        let s = AVAudioSession.sharedInstance()
        do {
            try s.setCategory(.playback, mode: .default, policy: .longFormAudio)
        } catch {
            print("AudioSession category error: \(error)")
        }
        let nc = NotificationCenter.default
        nc.publisher(for: AVAudioSession.interruptionNotification)
            .sink { [weak self] n in Task { @MainActor in self?.handleInterruption(n) } }
            .store(in: &cancellables)
        nc.publisher(for: AVAudioSession.routeChangeNotification)
            .sink { [weak self] n in Task { @MainActor in self?.handleRouteChange(n) } }
            .store(in: &cancellables)
        nc.publisher(for: AVPlayerItem.didPlayToEndTimeNotification)
            .sink { [weak self] _ in Task { @MainActor in self?.next() } }
            .store(in: &cancellables)
    }

    private func activateSession() {
        try? AVAudioSession.sharedInstance().setActive(true)
    }

    private func handleInterruption(_ n: Notification) {
        guard let raw = n.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
              let type = AVAudioSession.InterruptionType(rawValue: raw) else { return }
        switch type {
        case .began: pause()
        case .ended:
            let opts = (n.userInfo?[AVAudioSessionInterruptionOptionKey] as? UInt)
                .map(AVAudioSession.InterruptionOptions.init(rawValue:)) ?? []
            if opts.contains(.shouldResume) { resume() }
        @unknown default: break
        }
    }

    private func handleRouteChange(_ n: Notification) {
        guard let raw = n.userInfo?[AVAudioSessionRouteChangeReasonKey] as? UInt,
              AVAudioSession.RouteChangeReason(rawValue: raw) == .oldDeviceUnavailable else { return }
        pause() // auriculares desconectados
    }

    // MARK: - Observación del player

    private func observePlayer() {
        timeObserver = player.addPeriodicTimeObserver(
            forInterval: CMTime(seconds: 0.5, preferredTimescale: 600), queue: .main
        ) { [weak self] t in
            Task { @MainActor in
                guard let self else { return }
                self.elapsed = t.seconds.isFinite ? t.seconds : 0
                if let d = self.player.currentItem?.duration.seconds, d.isFinite, d > 0 { self.duration = d }
            }
        }
        player.publisher(for: \.timeControlStatus)
            .receive(on: DispatchQueue.main)
            .sink { [weak self] status in
                Task { @MainActor in
                    self?.isPlaying = status != .paused
                    self?.updateNowPlaying()
                }
            }
            .store(in: &cancellables)
    }

    // MARK: - Remote commands

    private func configureRemoteCommands() {
        let c = MPRemoteCommandCenter.shared()
        c.playCommand.addTarget { [weak self] _ in Task { @MainActor in self?.resume() }; return .success }
        c.pauseCommand.addTarget { [weak self] _ in Task { @MainActor in self?.pause() }; return .success }
        c.togglePlayPauseCommand.addTarget { [weak self] _ in Task { @MainActor in self?.togglePlayPause() }; return .success }
        c.nextTrackCommand.addTarget { [weak self] _ in Task { @MainActor in self?.next() }; return .success }
        c.previousTrackCommand.addTarget { [weak self] _ in Task { @MainActor in self?.previous() }; return .success }
        c.changePlaybackPositionCommand.addTarget { [weak self] e in
            guard let e = e as? MPChangePlaybackPositionCommandEvent else { return .commandFailed }
            let pos = e.positionTime
            Task { @MainActor in self?.seek(to: pos) }
            return .success
        }
        c.skipForwardCommand.isEnabled = false
        c.skipBackwardCommand.isEnabled = false
    }

    // MARK: - Now Playing

    private func updateNowPlaying() {
        guard let item = current else {
            MPNowPlayingInfoCenter.default().nowPlayingInfo = nil
            return
        }
        var info = MPNowPlayingInfoCenter.default().nowPlayingInfo ?? [:]
        info[MPMediaItemPropertyTitle] = item.title
        info[MPMediaItemPropertyArtist] = item.artist
        info[MPMediaItemPropertyAlbumTitle] = item.album
        info[MPMediaItemPropertyPlaybackDuration] = duration
        info[MPNowPlayingInfoPropertyElapsedPlaybackTime] = player.currentTime().seconds
        info[MPNowPlayingInfoPropertyPlaybackRate] = isPlaying ? 1.0 : 0.0
        info[MPNowPlayingInfoPropertyMediaType] = item.kind == .video
            ? MPNowPlayingInfoMediaType.video.rawValue : MPNowPlayingInfoMediaType.audio.rawValue
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
    }

    private func loadArtwork(for item: CatalogItem) {
        artworkTask?.cancel()
        MPNowPlayingInfoCenter.default().nowPlayingInfo?[MPMediaItemPropertyArtwork] = nil
        guard let url = item.artworkUrl else { return }
        artworkTask = Task { [weak self] in
            guard let (data, _) = try? await URLSession.shared.data(from: url),
                  let image = UIImage(data: data), !Task.isCancelled else { return }
            let art = MPMediaItemArtwork(boundsSize: image.size) { _ in image }
            await MainActor.run {
                guard self?.current?.id == item.id else { return }
                MPNowPlayingInfoCenter.default().nowPlayingInfo?[MPMediaItemPropertyArtwork] = art
            }
        }
    }
}
