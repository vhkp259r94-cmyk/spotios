"""Espejo en Python de MetadataMatcher.swift (mismos patrones) para CI y generación del catálogo."""
import hashlib
import re
import unicodedata

NOISE = re.compile(
    r"\s*[\(\[\{][^\)\]\}]*?\b(official|oficial|video|vídeo|audio|lyrics?|letra|visualizer|"
    r"hd|hq|4k|1080p|720p|explicit|clean|mv|m/v|remaster(?:ed)?|\d{4}\s+remaster(?:ed)?)\b[^\)\]\}]*[\)\]\}]",
    re.I,
)
NOISE_TAIL = re.compile(r"\s+[-–|]\s+(official\s+)?(music\s+)?(video|audio|lyrics?|visualizer)\s*$", re.I)
REMASTER_TAIL = re.compile(r"\s+[-–]\s+(\d{4}\s+)?remaster(ed)?(\s+\d{4})?(\s+version)?\s*$", re.I)
FEAT = re.compile(r"\s*[\(\[]?\b(?:feat\.?|ft\.?|featuring)\s+([^\)\]]+?)[\)\]]?(?=\s*[\(\[\-–]|$)", re.I)
VERSIONS = [
    ("remix", re.compile(r"\b(remix|rmx|mix|bootleg|rework)\b", re.I)),
    ("live", re.compile(r"\b(live|en vivo|en directo|unplugged)\b", re.I)),
    ("acoustic", re.compile(r"\b(acoustic|acústic[oa])\b", re.I)),
    ("instrumental", re.compile(r"\b(instrumental|karaoke)\b", re.I)),
    ("cover", re.compile(r"\bcover\b", re.I)),
    ("edit", re.compile(r"\b(radio edit|edit|extended)\b", re.I)),
]
VERSION_GROUP = re.compile(r"\s*[\(\[]([^\)\]]*)[\)\]]|\s+[-–]\s+([^\-–]+)$")
DURATION_TOLERANCE = 4.0


def _fold(s: str) -> str:
    s = unicodedata.normalize("NFKD", s.lower())
    s = "".join(c for c in s if not unicodedata.combining(c))
    s = re.sub(r"[^\w\s]", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def clean_title(raw: str):
    t = NOISE.sub("", raw)
    t = NOISE_TAIL.sub("", t)
    t = REMASTER_TAIL.sub("", t)
    feats = []
    m = FEAT.search(t)
    if m:
        feats = [p.strip() for p in re.split(r",|&| y | and ", m.group(1)) if p.strip()]
        t = t[: m.start()] + t[m.end():]
    version = "original"
    for g in VERSION_GROUP.finditer(t):
        chunk = g.group(1) or g.group(2) or ""
        for name, rx in VERSIONS:
            if rx.search(chunk):
                version = name
                t = t.replace(g.group(0), "")
                break
        if version != "original":
            break
    t = re.sub(r"\s+", " ", t).strip(" -–|")
    return t, feats, version


def dedupe_key(artist: str, title: str, version: str = "original") -> str:
    base = f"{_fold(artist)}|{_fold(title)}|{version}"
    return hashlib.md5(base.encode("utf-8")).hexdigest()


def durations_match(a: float, b: float) -> bool:
    return abs(a - b) <= DURATION_TOLERANCE
