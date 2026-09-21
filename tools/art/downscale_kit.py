"""Halves the 2x Blender kit renders into the Unity kit sizes (companion of tools/art/blender_kit.py).

    python tools/art/downscale_kit.py <render_dir> <unity_kit_dir>

Rendering at 2x and downscaling with Lanczos is what gives the pieces clean anti-aliased edges and a crisp gold
hairline; rendering straight at the target size leaves the thin rims aliased. Sizes match the existing kit family
(button_stone lines up with button_gold at 468x160 so the two never look like different games).
"""
import sys
from pathlib import Path

from PIL import Image

# name: final size. Everything here is rendered at exactly twice this.
SIZES = {
    "glass_card": (320, 320),
    "header_banner": (660, 170),
    "button_stone": (468, 160),
    "icon_button": (160, 160),
    "tab_active": (200, 100),
    "divider": (640, 80),
}


def main(argv: list[str]) -> int:
    if len(argv) < 3:
        print(__doc__)
        return 2
    source = Path(argv[1])
    target = Path(argv[2])
    target.mkdir(parents=True, exist_ok=True)
    for name, size in SIZES.items():
        path = source / (name + ".png")
        if not path.exists():
            print("missing", path)
            continue
        image = Image.open(path).convert("RGBA").resize(size, Image.LANCZOS)
        image.save(target / (name + ".png"))
        print("wrote", name, size)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
