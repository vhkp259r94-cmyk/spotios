import XCTest

/// Prueba en el simulador de iPhone: reproduce una canción real de YouTube, manda la app a segundo plano y mide si sigue sonando.
/// El resultado se imprime con el prefijo RESULTADO para leerlo en el registro de GitHub.
final class PlaybackUITests: XCTestCase {
    private func elapsed(_ app: XCUIApplication) -> Int? {
        let q = app.staticTexts.matching(NSPredicate(format: "label MATCHES %@", "^[0-9]+:[0-9]{2}$"))
        for el in q.allElementsBoundByIndex {
            let p = el.label.split(separator: ":").compactMap { Int($0) }
            if p.count == 2 { return p[0] * 60 + p[1] }
        }
        return nil
    }

    private func shot(_ name: String) {
        let a = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        a.name = name; a.lifetime = .keepAlways; add(a)
    }

    func testReproduceYSigueEnSegundoPlano() throws {
        let app = XCUIApplication()
        app.launch()
        shot("01-inicio")

        let play = app.buttons["Reproducir"].firstMatch
        if !play.waitForExistence(timeout: 60) { print("RESULTADO arbol:\n" + app.debugDescription); XCTFail("No aparece el botón Reproducir"); return }
        play.tap()
        sleep(14)
        shot("02-reproduciendo")
        let t1 = elapsed(app) ?? -1
        sleep(6)
        let t2 = elapsed(app) ?? -1
        print("RESULTADO primer_plano t1=\(t1) t2=\(t2) avanza=\(t2 > t1 && t1 >= 0)")

        XCUIDevice.shared.press(.home)
        sleep(25)
        app.activate()
        sleep(3)
        shot("03-vuelta")
        let t3 = elapsed(app) ?? -1
        print("RESULTADO segundo_plano t2=\(t2) t3=\(t3) delta=\(t3 - t2) (≈28 si sonó todo el rato; ≈0 si se pausó)")
        XCTAssertTrue(t2 > t1 && t1 >= 0, "En primer plano no avanza el tiempo: no está reproduciendo")
    }
}
