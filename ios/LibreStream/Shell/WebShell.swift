import AVFoundation
import SwiftUI
import UIKit
import WebKit

/// Envoltorio nativo de la interfaz compartida (app/dist/index.html) dentro de un WKWebView.
/// Aporta lo que una página web no puede: leer YouTube/LRCLIB sin CORS, descargar ficheros,
/// bloquear anuncios y servir las descargas al reproductor.
struct WebShell: UIViewRepresentable {
    func makeCoordinator() -> ShellCoordinator { ShellCoordinator() }
    func makeUIView(context: Context) -> WKWebView { context.coordinator.makeWebView() }
    func updateUIView(_ webView: WKWebView, context: Context) {}
}

final class ShellCoordinator: NSObject, WKScriptMessageHandlerWithReply, WKURLSchemeHandler,
                              WKNavigationDelegate, WKUIDelegate, URLSessionDownloadDelegate {

    /// Origen https ficticio: el reproductor de YouTube exige un origen https para incrustarse.
    private let pageOrigin = URL(string: "https://app.librestream/")!
    private let fetchHosts: Set<String> = ["www.youtube.com", "lrclib.net", "vhkp259r94-cmyk.github.io",
                                                 "open.spotify.com", "api.deezer.com", "music.apple.com", "soundcloud.com"]
    private let userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36"

    private weak var webView: WKWebView?
    private let nowPlaying = NowPlayingBridge()
    private var adList: WKContentRuleList?
    private var adEnabled = true
    private lazy var session = URLSession(configuration: .default, delegate: self, delegateQueue: .main)

    private struct Pending { let id: String; let filename: String; let reply: (Any?, String?) -> Void }
    private var downloads: [Int: Pending] = [:]

    private var downloadsDir: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("downloads")
    }

    // MARK: - Creación

    func makeWebView() -> WKWebView {
        let config = WKWebViewConfiguration()
        config.allowsInlineMediaPlayback = true
        config.mediaTypesRequiringUserActionForPlayback = []
        config.allowsPictureInPictureMediaPlayback = true
        config.setURLSchemeHandler(self, forURLScheme: "lsmedia")
        nowPlaying.onCommand = { [weak self] command in self?.sendRemote(command) }
        observeAudioSession()

        let controller = config.userContentController
        controller.addScriptMessageHandler(self, contentWorld: .page, name: "native")
        // Ecualizador: se inyecta en todos los marcos (el reproductor de YouTube vive en un iframe)
        if let url = Bundle.main.url(forResource: "eq", withExtension: "js", subdirectory: "dist"),
           let js = try? String(contentsOf: url, encoding: .utf8) {
            controller.addUserScript(WKUserScript(source: js, injectionTime: .atDocumentStart, forMainFrameOnly: false))
        }

        let web = WKWebView(frame: .zero, configuration: config)
        web.navigationDelegate = self
        web.uiDelegate = self
        web.isOpaque = false
        web.backgroundColor = UIColor(red: 0.07, green: 0.07, blue: 0.08, alpha: 1)
        web.scrollView.backgroundColor = web.backgroundColor
        web.scrollView.bounces = false
        webView = web

        // Primero se compila la lista de bloqueo y después se carga la página
        WKContentRuleListStore.default().compileContentRuleList(forIdentifier: "librestream-ads", encodedContentRuleList: adRulesJSON()) { [weak self] list, _ in
            DispatchQueue.main.async {
                guard let self else { return }
                self.adList = list
                if let list, self.adEnabled { controller.add(list) }
                self.loadPage()
            }
        }
        return web
    }

    private func loadPage() {
        guard let url = Bundle.main.url(forResource: "index", withExtension: "html", subdirectory: "dist"),
              let html = try? String(contentsOf: url, encoding: .utf8) else {
            webView?.loadHTMLString("<body style='background:#121214;color:#fff;font:16px sans-serif;padding:24px'>Falta la interfaz (dist/index.html).</body>", baseURL: nil)
            return
        }
        webView?.loadHTMLString(html, baseURL: pageOrigin)
    }

    // MARK: - Bloqueo de anuncios (mismas reglas que app/src/js/00-core.js)

    private func adRulesJSON() -> String {
        let hosts = ["doubleclick.net", "googlesyndication.com", "googleadservices.com", "googletagservices.com",
                     "googletagmanager.com", "google-analytics.com", "adservice.google.com", "2mdn.net",
                     "imasdk.googleapis.com", "moatads.com", "scorecardresearch.com", "adsrvr.org", "amazon-adsystem.com"]
        let paths = ["pagead/", "api/stats/ads", "get_midroll_info", "ptracking", "youtubei/v1/player/ad_break"]
        var rules: [[String: Any]] = hosts.map { h in
            ["trigger": ["url-filter": "^https?://([a-z0-9-]+\\.)*" + h.replacingOccurrences(of: ".", with: "\\.") + "/"],
             "action": ["type": "block"]]
        }
        rules += paths.map { p in
            ["trigger": ["url-filter": "^https?://([a-z0-9-]+\\.)*youtube\\.com/" + p.replacingOccurrences(of: ".", with: "\\.")],
             "action": ["type": "block"]]
        }
        let data = (try? JSONSerialization.data(withJSONObject: rules)) ?? Data("[]".utf8)
        return String(decoding: data, as: UTF8.self)
    }

    private func setAdBlock(_ enabled: Bool) {
        adEnabled = enabled
        guard let controller = webView?.configuration.userContentController, let list = adList else { return }
        if enabled { controller.add(list) } else { controller.remove(list) }
    }

    // MARK: - Órdenes desde la interfaz (JavaScript → Swift)

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage,
                               replyHandler: @escaping (Any?, String?) -> Void) {
        guard let body = message.body as? [String: Any], let name = body["name"] as? String else {
            replyHandler(nil, "Mensaje no válido"); return
        }
        let payload = body["payload"] as? [String: Any] ?? [:]
        switch name {
        case "fetchText":
            guard let s = payload["url"] as? String else { replyHandler(nil, "Falta la dirección"); return }
            fetchText(s, reply: replyHandler)
        case "postJson":
            guard let s = payload["url"] as? String, let body = payload["body"] as? String else { replyHandler(nil, "Falta la dirección"); return }
            postJson(s, body: body, reply: replyHandler)
        case "download":
            download(payload, reply: replyHandler)
        case "deleteDownload":
            if let file = payload["file"] as? String { try? FileManager.default.removeItem(at: downloadsDir.appendingPathComponent(safeName(file))) }
            replyHandler("ok", nil)
        case "nowPlaying":
            nowPlaying.update(title: payload["title"] as? String ?? "", artist: payload["artist"] as? String ?? "",
                              album: payload["album"] as? String ?? "", artwork: payload["artwork"] as? String ?? "",
                              duration: num(payload["duration"]), position: num(payload["position"]), playing: payload["playing"] as? Bool ?? true)
            replyHandler("ok", nil)
        case "nowPlayingState":
            nowPlaying.updateState(position: num(payload["position"]), duration: num(payload["duration"]), playing: payload["playing"] as? Bool ?? false)
            replyHandler("ok", nil)
        case "setAdBlock":
            setAdBlock(payload["enabled"] as? Bool ?? true)
            replyHandler("ok", nil)
        default:
            replyHandler(nil, "Orden desconocida: \(name)")
        }
    }

    /// Mantiene viva la sesión de audio: si el sistema la interrumpe (llamada, Siri, bloqueo) la música se reanuda al terminar.
    private var audioObservers: [NSObjectProtocol] = []
    private func observeAudioSession() {
        guard audioObservers.isEmpty else { return }
        let nc = NotificationCenter.default
        audioObservers.append(nc.addObserver(forName: AVAudioSession.interruptionNotification, object: nil, queue: .main) { [weak self] note in
            guard let raw = note.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
                  AVAudioSession.InterruptionType(rawValue: raw) == .ended else { return }
            try? AVAudioSession.sharedInstance().setActive(true)
            let opts = (note.userInfo?[AVAudioSessionInterruptionOptionKey] as? UInt).map { AVAudioSession.InterruptionOptions(rawValue: $0) }
            if opts?.contains(.shouldResume) == true { self?.sendRemote(["cmd": "play"]) }
        })
        audioObservers.append(nc.addObserver(forName: UIApplication.didEnterBackgroundNotification, object: nil, queue: .main) { _ in
            try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
            try? AVAudioSession.sharedInstance().setActive(true)
        })
        audioObservers.append(nc.addObserver(forName: UIApplication.willEnterForegroundNotification, object: nil, queue: .main) { _ in
            try? AVAudioSession.sharedInstance().setActive(true)
        })
    }

    private func num(_ value: Any?) -> Double { (value as? NSNumber)?.doubleValue ?? 0 }

    /// Los botones de la pantalla de bloqueo vuelven a la interfaz como eventos nativos.
    private func sendRemote(_ command: [String: Any]) {
        var event = command
        event["type"] = "remote"
        guard JSONSerialization.isValidJSONObject(event),
              let data = try? JSONSerialization.data(withJSONObject: event),
              let json = String(data: data, encoding: .utf8) else { return }
        DispatchQueue.main.async { [weak self] in
            self?.webView?.evaluateJavaScript("window.__nativeEvent && window.__nativeEvent(\(json))")
        }
    }

    private func fetchText(_ urlString: String, reply: @escaping (Any?, String?) -> Void) {
        guard let url = URL(string: urlString), url.scheme == "https", let host = url.host, fetchHosts.contains(host) else {
            reply(nil, "Dirección no permitida"); return
        }
        var request = URLRequest(url: url, timeoutInterval: 20)
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        request.setValue("es-ES,es;q=0.9,en;q=0.8", forHTTPHeaderField: "Accept-Language")
        URLSession.shared.dataTask(with: request) { data, response, error in
            DispatchQueue.main.async {
                if let error { reply(nil, error.localizedDescription); return }
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                guard (200..<300).contains(status), let data else { reply(nil, "HTTP \(status)"); return }
                reply(String(decoding: data, as: UTF8.self), nil)
            }
        }.resume()
    }

    /// POST de JSON solo a YouTube Music (búsqueda de canciones).
    private func postJson(_ urlString: String, body: String, reply: @escaping (Any?, String?) -> Void) {
        guard let url = URL(string: urlString), url.scheme == "https", url.host == "music.youtube.com", body.utf8.count < 20_000 else {
            reply(nil, "Dirección no permitida"); return
        }
        var request = URLRequest(url: url, timeoutInterval: 20)
        request.httpMethod = "POST"
        request.httpBody = Data(body.utf8)
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("https://music.youtube.com", forHTTPHeaderField: "Origin")
        request.setValue("https://music.youtube.com/", forHTTPHeaderField: "Referer")
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        request.setValue("es-ES,es;q=0.9,en;q=0.8", forHTTPHeaderField: "Accept-Language")
        URLSession.shared.dataTask(with: request) { data, response, error in
            DispatchQueue.main.async {
                if let error { reply(nil, error.localizedDescription); return }
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                guard (200..<300).contains(status), let data else { reply(nil, "HTTP \(status)"); return }
                reply(String(decoding: data, as: UTF8.self), nil)
            }
        }.resume()
    }

    private func safeName(_ name: String) -> String {
        let last = URL(fileURLWithPath: name).lastPathComponent
        let cleaned = last.unicodeScalars.map { CharacterSet.alphanumerics.contains($0) || "._-".unicodeScalars.contains($0) ? Character($0) : "_" }
        return String(cleaned).isEmpty ? "descarga" : String(cleaned)
    }

    // MARK: - Descargas del contenido libre

    private func download(_ payload: [String: Any], reply: @escaping (Any?, String?) -> Void) {
        guard let s = payload["url"] as? String, let url = URL(string: s), url.scheme == "https",
              let id = payload["id"] as? String, let file = payload["filename"] as? String else {
            reply(nil, "Descarga no válida"); return
        }
        let task = session.downloadTask(with: url)
        downloads[task.taskIdentifier] = Pending(id: id, filename: safeName(file), reply: reply)
        task.resume()
    }

    private func jsLiteral(_ value: String) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: [value]), let s = String(data: data, encoding: .utf8) else { return "\"\"" }
        return String(s.dropFirst().dropLast())
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        guard totalBytesExpectedToWrite > 0, let d = downloads[downloadTask.taskIdentifier] else { return }
        let p = Double(totalBytesWritten) / Double(totalBytesExpectedToWrite)
        webView?.evaluateJavaScript("window.__nativeEvent && window.__nativeEvent({type:'progress',id:\(jsLiteral(d.id)),p:\(p)})")
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        guard let d = downloads.removeValue(forKey: downloadTask.taskIdentifier) else { return }
        let status = (downloadTask.response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else { d.reply(nil, "HTTP \(status)"); return }
        do {
            try FileManager.default.createDirectory(at: downloadsDir, withIntermediateDirectories: true)
            let dest = downloadsDir.appendingPathComponent(d.filename)
            try? FileManager.default.removeItem(at: dest)
            try FileManager.default.moveItem(at: location, to: dest)
            let size = ((try? FileManager.default.attributesOfItem(atPath: dest.path))?[.size] as? NSNumber)?.int64Value ?? 0
            d.reply(["url": "lsmedia://file/\(d.filename)", "bytes": size, "file": d.filename], nil)
        } catch {
            d.reply(nil, error.localizedDescription)
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        guard let error, let d = downloads.removeValue(forKey: task.taskIdentifier) else { return }
        d.reply(nil, error.localizedDescription)
    }

    // MARK: - Servir las descargas (lsmedia://file/<nombre>) con soporte de rangos

    func webView(_ webView: WKWebView, start urlSchemeTask: WKURLSchemeTask) {
        guard let url = urlSchemeTask.request.url, url.scheme == "lsmedia" else {
            urlSchemeTask.didFailWithError(URLError(.badURL)); return
        }
        let fileURL = downloadsDir.appendingPathComponent(safeName(url.lastPathComponent))
        guard let size = (try? FileManager.default.attributesOfItem(atPath: fileURL.path))?[.size] as? NSNumber,
              let handle = try? FileHandle(forReadingFrom: fileURL) else {
            urlSchemeTask.didFailWithError(URLError(.fileDoesNotExist)); return
        }
        defer { try? handle.close() }
        let total = size.int64Value
        var start: Int64 = 0, end = total - 1, status = 200
        if let range = urlSchemeTask.request.value(forHTTPHeaderField: "Range"), range.hasPrefix("bytes=") {
            let parts = range.dropFirst(6).split(separator: "-", omittingEmptySubsequences: false)
            if let a = parts.first, let s = Int64(a) { start = s }
            if parts.count > 1, let e = Int64(parts[1]) { end = min(e, total - 1) }
            end = min(end, start + 8 * 1024 * 1024 - 1)       // trozos de 8 MB como máximo
            status = 206
        }
        guard start >= 0, start <= end else { urlSchemeTask.didFailWithError(URLError(.badServerResponse)); return }
        let length = end - start + 1
        do { try handle.seek(toOffset: UInt64(start)) } catch { urlSchemeTask.didFailWithError(error); return }
        let data = handle.readData(ofLength: Int(length))

        var headers = ["Content-Type": mime(for: fileURL.pathExtension), "Content-Length": "\(data.count)",
                       "Accept-Ranges": "bytes", "Access-Control-Allow-Origin": "*"]
        if status == 206 { headers["Content-Range"] = "bytes \(start)-\(end)/\(total)" }
        if let response = HTTPURLResponse(url: url, statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers) {
            urlSchemeTask.didReceive(response)
            urlSchemeTask.didReceive(data)
            urlSchemeTask.didFinish()
        } else {
            urlSchemeTask.didFailWithError(URLError(.cannotParseResponse))
        }
    }

    func webView(_ webView: WKWebView, stop urlSchemeTask: WKURLSchemeTask) {}

    private func mime(for ext: String) -> String {
        switch ext.lowercased() {
        case "mp4", "m4v": return "video/mp4"
        case "webm": return "video/webm"
        case "mp3": return "audio/mpeg"
        case "m4a": return "audio/mp4"
        default: return "application/octet-stream"
        }
    }

    // MARK: - Ayuda de pruebas (simulador): -lsRoute home|music|videos|library|settings  -lsAction play

    private var debugHookDone = false

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard !debugHookDone else { return }
        debugHookDone = true
        let args = CommandLine.arguments
        func value(_ key: String) -> String? {
            guard let i = args.firstIndex(of: key), i + 1 < args.count else { return nil }
            return args[i + 1]
        }
        if let route = value("-lsRoute"), ["home", "music", "videos", "library", "settings"].contains(route) {
            webView.evaluateJavaScript("location.hash='#/\(route)'")
        }
        if value("-lsAction") == "play" {
            DispatchQueue.main.asyncAfter(deadline: .now() + 8) {
                webView.evaluateJavaScript("var c=document.querySelector('.card'); if(c) c.click();")
            }
        }
    }

    // MARK: - Navegación: los enlaces externos se abren en Safari; sin ventanas emergentes

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if navigationAction.targetFrame?.isMainFrame ?? true,
           navigationAction.navigationType == .linkActivated,
           let url = navigationAction.request.url, url.host != pageOrigin.host {
            UIApplication.shared.open(url)
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? { nil }
}
