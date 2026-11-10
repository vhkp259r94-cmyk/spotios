"""Casos borde del normalizador. Ejecutar: python tools/test_title_regex.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from title_normalizer import clean_title, dedupe_key, durations_match  # noqa: E402

CASES = [
    ("Song Name (Official Video)", ("Song Name", [], "original")),
    ("Song Name [Official Music Video] [4K]", ("Song Name", [], "original")),
    ("Song Name (Lyrics)", ("Song Name", [], "original")),
    ("Song Name - Official Audio", ("Song Name", [], "original")),
    ("Song Name (2011 Remaster)", ("Song Name", [], "original")),
    ("Song Name - Remastered 2009", ("Song Name", [], "original")),
    ("Song Name - 2015 Remaster", ("Song Name", [], "original")),
    ("Song Name (feat. Artist B)", ("Song Name", ["Artist B"], "original")),
    ("Song Name ft. Artist B & Artist C", ("Song Name", ["Artist B", "Artist C"], "original")),
    ("Song Name (feat. Artist B) [Official Video]", ("Song Name", ["Artist B"], "original")),
    ("Song Name (Artist X Remix)", ("Song Name", [], "remix")),
    ("Song Name - Club Mix", ("Song Name", [], "remix")),
    ("Song Name (Live at Wembley)", ("Song Name", [], "live")),
    ("Song Name - En Vivo", ("Song Name", [], "live")),
    ("Song Name (Acoustic Version)", ("Song Name", [], "acoustic")),
    ("Song Name (Radio Edit)", ("Song Name", [], "edit")),
    ("Song Name (feat. B) (Live) [HD]", ("Song Name", ["B"], "live")),
    ("Livewire", ("Livewire", [], "original")),          # 'live' dentro de palabra
    ("Remix Culture", ("Remix Culture", [], "original")),  # sin paréntesis/guion
    ("Bailando con Lobos", ("Bailando con Lobos", [], "original")),
    ("Señorita (Video Oficial)", ("Señorita", [], "original")),
]

fails = 0
for raw, expected in CASES:
    got = clean_title(raw)
    ok = got == expected
    fails += not ok
    print(f"{'OK ' if ok else 'FAIL'} {raw!r:50} -> {got}")

assert dedupe_key("Beyoncé", "Halo") == dedupe_key("beyonce", "HALO!"), "fold"
assert dedupe_key("A", "Song", "live") != dedupe_key("A", "Song"), "version en hash"
assert durations_match(200, 204) and not durations_match(200, 204.1), "tolerancia ±4s"
print(f"\n{len(CASES) - fails}/{len(CASES)} casos OK")
sys.exit(1 if fails else 0)
