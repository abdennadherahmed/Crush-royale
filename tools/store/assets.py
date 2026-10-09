"""
Builds the graphic assets Google Play asks for, from the art already in the game.

    python tools/store/assets.py <screenshots-folder>

Writes into store/:
  icon_512.png        the app icon at the size the listing wants
  feature_1024x500.png the banner at the top of the store page
  screenshot_1..8.png  phone screenshots, 1080x1920, taken from a capture run

Play rejects a feature graphic with important content near the edges (it is cropped on some surfaces), so the
logo sits in the middle third, and it rejects screenshots with device frames or added marketing text, so these
are the captures untouched.
"""
import os
import sys

from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ART = os.path.join(ROOT, "unity", "CrushRoyale", "Assets", "Resources", "Art")
OUT = os.path.join(ROOT, "store")

# The eight screens that tell the story of the game, in the order a stranger should meet them.
SHOTS = [
    "08-Gameplay",
    "06-WorldMap",
    "11-Pvp",
    "22-Kingdom",
    "21-Pets",
    "13-Shop",
    "15-Guild",
    "19-BattlePass",
]


def icon():
    source = os.path.join(ROOT, "unity", "CrushRoyale", "Assets", "Art", "AppIcon", "app_icon.png")
    image = Image.open(source).convert("RGB").resize((512, 512), Image.LANCZOS)
    image.save(os.path.join(OUT, "icon_512.png"), "PNG")
    print("icon_512.png")


def feature():
    """Crystalheim behind, darkened and blurred, with the game's own logo centred."""
    back = Image.open(os.path.join(ART, "Backgrounds", "crystalheim.jpg")).convert("RGB")
    w, h = back.size
    # A tall painting cropped to a wide banner: take the middle band, where the citadel is.
    band = back.crop((0, int(h * 0.26), w, int(h * 0.26) + int(w * 500 / 1024)))
    band = band.resize((1024, 500), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.6))
    band = Image.blend(band, Image.new("RGB", (1024, 500), (14, 10, 30)), 0.34)

    logo = Image.open(os.path.join(ART, "logo.png")).convert("RGBA")
    scale = min(560 / logo.width, 300 / logo.height)
    logo = logo.resize((int(logo.width * scale), int(logo.height * scale)), Image.LANCZOS)
    band.paste(logo, ((1024 - logo.width) // 2, (500 - logo.height) // 2), logo)
    band.save(os.path.join(OUT, "feature_1024x500.png"), "PNG")
    print("feature_1024x500.png")


def screenshots(folder):
    kept = 0
    for name in SHOTS:
        path = os.path.join(folder, name + ".png")
        if not os.path.exists(path):
            print("missing capture:", name)
            continue
        kept += 1
        image = Image.open(path).convert("RGB")
        image.save(os.path.join(OUT, "screenshot_%d.png" % kept), "PNG")
    print("%d screenshots" % kept)


if __name__ == "__main__":
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    icon()
    feature()
    screenshots(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "screenshots"))
