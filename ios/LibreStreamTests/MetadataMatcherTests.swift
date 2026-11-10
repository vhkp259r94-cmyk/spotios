import XCTest
@testable import LibreStream

final class MetadataMatcherTests: XCTestCase {
    typealias V = MetadataMatcher.Version

    // Mismos casos que tools/test_title_regex.py
    private let cases: [(String, String, [String], V)] = [
        ("Song Name (Official Video)", "Song Name", [], .original),
        ("Song Name [Official Music Video] [4K]", "Song Name", [], .original),
        ("Song Name (Lyrics)", "Song Name", [], .original),
        ("Song Name - Official Audio", "Song Name", [], .original),
        ("Song Name (2011 Remaster)", "Song Name", [], .original),
        ("Song Name - Remastered 2009", "Song Name", [], .original),
        ("Song Name - 2015 Remaster", "Song Name", [], .original),
        ("Song Name (feat. Artist B)", "Song Name", ["Artist B"], .original),
        ("Song Name ft. Artist B & Artist C", "Song Name", ["Artist B", "Artist C"], .original),
        ("Song Name (feat. Artist B) [Official Video]", "Song Name", ["Artist B"], .original),
        ("Song Name (Artist X Remix)", "Song Name", [], .remix),
        ("Song Name - Club Mix", "Song Name", [], .remix),
        ("Song Name (Live at Wembley)", "Song Name", [], .live),
        ("Song Name - En Vivo", "Song Name", [], .live),
        ("Song Name (Acoustic Version)", "Song Name", [], .acoustic),
        ("Song Name (Radio Edit)", "Song Name", [], .edit),
        ("Song Name (feat. B) (Live) [HD]", "Song Name", ["B"], .live),
        ("Livewire", "Livewire", [], .original),
        ("Remix Culture", "Remix Culture", [], .original),
        ("Bailando con Lobos", "Bailando con Lobos", [], .original),
        ("Señorita (Video Oficial)", "Señorita", [], .original),
    ]

    func testCleanEdgeCases() {
        for (raw, title, feat, version) in cases {
            XCTAssertEqual(MetadataMatcher.clean(raw),
                           .init(title: title, featuring: feat, version: version), raw)
        }
    }

    func testDedupeKeyMatchesPython() {
        // Valores calculados con tools/title_normalizer.py
        XCTAssertEqual(MetadataMatcher.dedupeKey(artist: "Kevin MacLeod", title: "Monkeys Spinning Monkeys"),
                       "4b498eb814fcbc96514e49ad2f236f1e")
        XCTAssertEqual(MetadataMatcher.dedupeKey(artist: "Beyoncé", title: "Halo"),
                       MetadataMatcher.dedupeKey(artist: "beyonce", title: "HALO!"))
        XCTAssertNotEqual(MetadataMatcher.dedupeKey(artist: "A", title: "S", version: .live),
                          MetadataMatcher.dedupeKey(artist: "A", title: "S"))
    }

    func testDurationTolerance() {
        XCTAssertTrue(MetadataMatcher.durationsMatch(200, 204))
        XCTAssertFalse(MetadataMatcher.durationsMatch(200, 204.1))
    }

    func testDeduplicate() {
        let items = [("a", 200.0), ("a", 203.0), ("a", 260.0), ("b", 200.0)]
        let out = MetadataMatcher.deduplicate(items, key: { $0.0 }, duration: { $0.1 })
        XCTAssertEqual(out.count, 3)
    }
}
