import SwiftUI
import AVFoundation

@main
struct LibreStreamApp: App {
    init() {
        // Audio incluso con el interruptor de silencio y con la pantalla apagada
        try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
        try? AVAudioSession.sharedInstance().setActive(true)
    }

    var body: some Scene {
        WindowGroup {
            WebShell()
                .background(Color(red: 0.07, green: 0.07, blue: 0.08).ignoresSafeArea())
        }
    }
}
