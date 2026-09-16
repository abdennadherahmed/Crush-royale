"""Adds or replaces UI strings in every Source/ui.{lang}.json without reformatting the files.

Usage (from the repository root):
    python tools/LocalizationGen/add_keys.py <keys.json>
keys.json maps each key to 11 strings in this order: en, fr, ar, es, de, it, pt, ru, ja, ko, zh.
Existing keys are replaced in place; new keys are appended at the end of the file.
"""
import json
import sys
from pathlib import Path

LANGS = ["en", "fr", "ar", "es", "de", "it", "pt", "ru", "ja", "ko", "zh"]
SOURCE = Path(__file__).parent / "Source"


def line(key: str, value: str) -> str:
    return "  " + json.dumps(key, ensure_ascii=False) + ": " + json.dumps(value, ensure_ascii=False)


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2
    table = json.loads(Path(argv[1]).read_text(encoding="utf-8"))
    for key, values in table.items():
        if len(values) != len(LANGS):
            raise SystemExit(f"{key}: expected {len(LANGS)} strings, got {len(values)}")
    for index, lang in enumerate(LANGS):
        path = SOURCE / f"ui.{lang}.json"
        raw = path.read_text(encoding="utf-8")
        nl = "\r\n" if "\r\n" in raw else "\n"
        lines = raw.split(nl)
        pending = dict(table)
        for i, text in enumerate(lines):
            stripped = text.strip()
            for key in list(pending):
                if stripped.startswith(json.dumps(key, ensure_ascii=False) + ":"):
                    comma = "," if stripped.endswith(",") else ""
                    lines[i] = line(key, pending.pop(key)[index]) + comma
        if pending:
            close = max(i for i, text in enumerate(lines) if text.strip() == "}")
            last = max(i for i in range(close) if lines[i].strip())
            if not lines[last].rstrip().endswith(","):
                lines[last] = lines[last].rstrip() + ","
            new = [line(k, v[index]) + "," for k, v in pending.items()]
            new[-1] = new[-1][:-1]
            lines[close:close] = new
        out = nl.join(lines)
        json.loads(out)
        path.write_text(out, encoding="utf-8", newline="")
    print(f"{len(table)} key(s) written to {len(LANGS)} languages")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
