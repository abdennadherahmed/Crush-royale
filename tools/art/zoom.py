"""Decoupe une capture en vignettes agrandies, pour regarder une interface de pres.

Un ecran de 1080 sur 1920 regarde en entier ne montre pas qu'une icone deborde de son cadre, qu'une
pastille est coupee par un bord ou qu'un texte touche sa bordure. Ce sont pourtant ces details qui
font qu'un jeu a l'air fini ou bricole, et ils ne se voient qu'en zoomant.

    python tools/art/zoom.py shots/03-MainMenu.png --grid 3x5
    python tools/art/zoom.py shots/15-Guild.png --box 0,0,0.3,0.12

Les vignettes sont ecrites a cote, dans un dossier au nom de la capture.
"""
import argparse
import os
import sys

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow est requis : py -m pip install pillow")


def crop(image, box, scale, path):
    left, top, right, bottom = box
    piece = image.crop((int(left), int(top), int(right), int(bottom)))
    if piece.width == 0 or piece.height == 0:
        return False
    piece = piece.resize((piece.width * scale, piece.height * scale), Image.LANCZOS)
    piece.save(path)
    return True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("image")
    parser.add_argument("--grid", help="colonnes x lignes, par exemple 3x5")
    parser.add_argument("--box", help="gauche,haut,droite,bas en fractions de 0 a 1")
    parser.add_argument("--scale", type=int, default=3)
    parser.add_argument("--out", default=None)
    args = parser.parse_args()

    image = Image.open(args.image)
    width, height = image.size
    folder = args.out or os.path.splitext(args.image)[0] + "_zoom"
    os.makedirs(folder, exist_ok=True)

    if args.box:
        left, top, right, bottom = [float(v) for v in args.box.split(",")]
        name = os.path.join(folder, "box.png")
        crop(image, (left * width, top * height, right * width, bottom * height), args.scale, name)
        print(name)
        return

    columns, rows = (int(v) for v in (args.grid or "3x5").lower().split("x"))
    # Les vignettes se chevauchent legerement : un defaut pile sur une decoupe passerait inapercu.
    overlap = 0.04
    written = []
    for row in range(rows):
        for column in range(columns):
            left = max(0, (column / columns - overlap)) * width
            right = min(1, ((column + 1) / columns + overlap)) * width
            top = max(0, (row / rows - overlap)) * height
            bottom = min(1, ((row + 1) / rows + overlap)) * height
            name = os.path.join(folder, "r%dc%d.png" % (row + 1, column + 1))
            if crop(image, (left, top, right, bottom), args.scale, name):
                written.append(name)
    print("\n".join(written))


if __name__ == "__main__":
    main()
