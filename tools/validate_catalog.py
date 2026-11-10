"""Valida catálogos contra el JSON Schema, verifica ids MD5 y duplicados.
Uso: python tools/validate_catalog.py catalog/catalog.json [...]"""
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

sys.path.insert(0, str(Path(__file__).parent))
from title_normalizer import dedupe_key, durations_match  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
schema = json.loads((ROOT / "schemas/catalog.schema.json").read_text(encoding="utf-8"))
validator = Draft202012Validator(schema, format_checker=FormatChecker())

errors = 0
for path in sys.argv[1:] or [str(ROOT / "catalog/catalog.json")]:
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    for e in validator.iter_errors(data):
        errors += 1
        print(f"{path}: {'/'.join(map(str, e.path))}: {e.message}")
    seen = {}
    for item in data.get("items", []):
        key = dedupe_key(item["artist"], item["title"], item.get("version", "original"))
        if item["id"] != key:
            errors += 1
            print(f"{path}: id {item['id']} != MD5 esperado {key} ({item['title']})")
        if key in seen and durations_match(seen[key], item["durationSec"]):
            errors += 1
            print(f"{path}: duplicado {item['title']} ({key})")
        seen[key] = item["durationSec"]
        for s in item["sources"]:
            if "durationSec" in s and not durations_match(s["durationSec"], item["durationSec"]):
                errors += 1
                print(f"{path}: fuente fuera de tolerancia ±4s en {item['title']}")
print("OK" if not errors else f"{errors} error(es)")
sys.exit(1 if errors else 0)
