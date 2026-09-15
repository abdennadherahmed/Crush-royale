"""Turns AI-generated source art into Unity-ready files (Assets/Resources/Art).

Sprite sheets are generated on a flat magenta background (#FF00FF) and laid out on a regular grid:
the key color is removed with soft edges and spill suppression, each cell is trimmed to its content,
centered on a transparent square and resized. Backgrounds are copied as-is.

Usage (from the repository root):
    python tools/art/process_art.py sheet <source> <out_dir> <columns> <rows> <name1,name2,...> [size]
    python tools/art/process_art.py background <source> <out_file>

Empty names ("") skip a cell. Requires Pillow and numpy.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

KEY_OPAQUE = 70     # below: fully opaque
KEY_CLEAR = 170     # above: fully transparent
PADDING = 0.06      # empty margin around each trimmed item, relative to the square side


def remove_magenta(image: Image.Image) -> Image.Image:
    rgb = np.asarray(image.convert("RGB")).astype(np.float32)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # How "magenta" a pixel is: red and blue high, green low.
    key = np.minimum(r, b) - g
    alpha = 1.0 - np.clip((key - KEY_OPAQUE) / (KEY_CLEAR - KEY_OPAQUE), 0.0, 1.0)

    # Spill suppression: edge pixels keep a pink cast from the background.
    spill = np.clip(np.minimum(r, b) - g, 0, None) * (1.0 - alpha)
    r = np.clip(r - spill, 0, 255)
    b = np.clip(b - spill, 0, 255)

    rgba = np.dstack([r, g, b, alpha * 255.0]).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def trim_to_square(cell: Image.Image, size: int) -> Image.Image | None:
    alpha = np.asarray(cell)[..., 3]
    ys, xs = np.nonzero(alpha > 24)
    if len(xs) == 0:
        return None
    left, right, top, bottom = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    item = cell.crop((left, top, right, bottom))
    side = int(max(item.width, item.height) * (1 + 2 * PADDING))
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(item, ((side - item.width) // 2, (side - item.height) // 2), item)
    return square.resize((size, size), Image.LANCZOS)


def process_sheet(source: Path, out_dir: Path, columns: int, rows: int, names: list[str], size: int) -> None:
    sheet = remove_magenta(Image.open(source))
    cell_w, cell_h = sheet.width / columns, sheet.height / rows
    out_dir.mkdir(parents=True, exist_ok=True)
    for index, name in enumerate(names):
        if not name:
            continue
        col, row = index % columns, index // columns
        box = (round(col * cell_w), round(row * cell_h), round((col + 1) * cell_w), round((row + 1) * cell_h))
        sprite = trim_to_square(sheet.crop(box), size)
        if sprite is None:
            print(f"  skipped {name}: empty cell")
            continue
        target = out_dir / f"{name}.png"
        sprite.save(target, optimize=True)
        print(f"  {target} ({size}x{size})")


def process_boxes(source: Path, out_dir: Path, boxes: dict[str, tuple[int, int, int, int]], size: int) -> None:
    """Irregular sheets (e.g. character rows with different widths): one explicit (left, top, right, bottom) box per sprite."""
    sheet = remove_magenta(Image.open(source))
    out_dir.mkdir(parents=True, exist_ok=True)
    for name, box in boxes.items():
        sprite = trim_to_square(sheet.crop(box), size)
        if sprite is None:
            print(f"  skipped {name}: empty box")
            continue
        target = out_dir / f"{name}.png"
        sprite.save(target, optimize=True)
        print(f"  {target} ({size}x{size})")


def process_background(source: Path, out_file: Path) -> None:
    out_file.parent.mkdir(parents=True, exist_ok=True)
    image = Image.open(source).convert("RGB")
    image.save(out_file, quality=92)
    print(f"  {out_file} ({image.width}x{image.height})")


def main(argv: list[str]) -> int:
    if len(argv) >= 6 and argv[1] == "sheet":
        size = int(argv[7]) if len(argv) > 7 else 256
        process_sheet(Path(argv[2]), Path(argv[3]), int(argv[4]), int(argv[5]), argv[6].split(","), size)
        return 0
    if len(argv) == 4 and argv[1] == "background":
        process_background(Path(argv[2]), Path(argv[3]))
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
