"""Cuts every sprite out of an AI sheet whose items are not on a regular grid.

The magenta background connected to the image border is removed (pink or purple parts inside sprites survive),
then each large connected shape becomes one sprite, named in reading order (top to bottom, left to right).
Small fragments close to a sprite (sparkles, gems) are merged into it.

Usage (from the repository root):
    python tools/art/extract_sprites.py <source> <out_dir> <name1,name2,...> [--pad 6] [--min-area 2000]
Empty names ("") skip a sprite. Output keeps the native size (no square padding): good for 9-sliced UI frames.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
from process_art import _components, key_cell  # noqa: E402


def extract(source: Path, out_dir: Path, names: list[str], pad: int, min_area: int) -> None:
    sheet = Image.open(source).convert("RGB")
    keyed = np.array(key_cell(sheet, main_only=False))
    alpha = keyed[..., 3] > 30
    labels, comps = _components(alpha)
    big = [i for i, c in enumerate(comps) if c[0] >= min_area]
    small = [i for i, c in enumerate(comps) if c[0] < min_area]

    boxes = []
    for i in big:
        _, _, _, top, bottom, left, right = comps[i]
        boxes.append([top, bottom, left, right, [i + 1]])
    for i in small:
        _, cy, cx, top, bottom, left, right = comps[i]
        for box in boxes:
            margin = 24
            if box[0] - margin <= cy <= box[1] + margin and box[2] - margin <= cx <= box[3] + margin:
                box[0], box[1] = min(box[0], top), max(box[1], bottom)
                box[2], box[3] = min(box[2], left), max(box[3], right)
                box[4].append(i + 1)
                break

    # Reading order: a box starts a new row when its centre is below the current row's bottom edge.
    boxes.sort(key=lambda b: (b[0] + b[1]) / 2)
    rows: list[list] = []
    for box in boxes:
        centre = (box[0] + box[1]) / 2
        if rows and centre <= max(x[1] for x in rows[-1]):
            rows[-1].append(box)
        else:
            rows.append([box])
    ordered = [b for row in rows for b in sorted(row, key=lambda b: b[2])]

    out_dir.mkdir(parents=True, exist_ok=True)
    h, w = alpha.shape
    print(f"{len(ordered)} sprites found")
    for index, box in enumerate(ordered):
        name = names[index] if index < len(names) else ""
        top, bottom, left, right, ids = box
        if not name:
            print(f"  skipped sprite {index} ({right - left + 1}x{bottom - top + 1})")
            continue
        mask = np.isin(labels, ids)
        grown = mask | np.roll(mask, 1, 0) | np.roll(mask, -1, 0) | np.roll(mask, 1, 1) | np.roll(mask, -1, 1)
        rgba = keyed.copy()
        rgba[..., 3] = np.where(grown, rgba[..., 3], 0)
        t, b = max(0, top - pad), min(h, bottom + pad + 1)
        l, r = max(0, left - pad), min(w, right + pad + 1)
        sprite = Image.fromarray(rgba[t:b, l:r], "RGBA")
        target = out_dir / f"{name}.png"
        sprite.save(target, optimize=True)
        print(f"  {target} ({sprite.width}x{sprite.height})")


def main(argv: list[str]) -> int:
    if len(argv) < 4:
        print(__doc__)
        return 2
    pad = int(argv[argv.index("--pad") + 1]) if "--pad" in argv else 6
    min_area = int(argv[argv.index("--min-area") + 1]) if "--min-area" in argv else 2000
    extract(Path(argv[1]), Path(argv[2]), argv[3].split(","), pad, min_area)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
