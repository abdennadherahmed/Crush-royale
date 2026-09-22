"""Turns a sprite downloaded from the Gemini web app into a usable game asset.

Gemini renders sprites on a transparent background, but its "download full size" button hands back a JPEG, and a JPEG
has no alpha channel: what arrives is the checkerboard baked into the pixels. This strips it back out, keeps the
largest connected island so compression dust cannot survive, squares the result and writes a 256 px PNG.

    python tools/art/degemini.py ~/Downloads/Gemini_Generated_Image_xxx.jpeg unity/.../Art/Gems/cursed.png [--size 256]

The checkerboard is neutral grey at two tones, so anything neutral inside that band is background. Art that is itself
mid-grey and unsaturated would be eaten by this -- check the result before shipping it.
"""
import argparse
import os
from collections import deque

import numpy as np
from PIL import Image, ImageFilter


def largest_island(mask):
    """The biggest connected run of True, so stray JPEG speckles are dropped."""
    h, w = mask.shape
    seen = np.zeros_like(mask)
    best_cells = []
    for y in range(0, h, 4):
        for x in range(0, w, 4):
            if not mask[y, x] or seen[y, x]:
                continue
            queue = deque([(y, x)])
            seen[y, x] = True
            cells = []
            while queue:
                cy, cx = queue.popleft()
                cells.append((cy, cx))
                for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1)):
                    if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        queue.append((ny, nx))
            if len(cells) > len(best_cells):
                best_cells = cells
    keep = np.zeros_like(mask)
    for cy, cx in best_cells:
        keep[cy, cx] = True
    return keep


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source')
    parser.add_argument('output')
    parser.add_argument('--size', type=int, default=256)
    parser.add_argument('--tolerance', type=int, default=26, help='how neutral a pixel must be to count as backdrop')
    args = parser.parse_args()

    src = Image.open(args.source).convert('RGB')
    a = np.asarray(src).astype(np.int16)
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    t = args.tolerance
    neutral = (np.abs(r - g) < t) & (np.abs(g - b) < t) & (np.abs(r - b) < t)
    backdrop = neutral & (r > 60) & (r < 150)

    alpha = Image.fromarray(np.where(backdrop, 0, 255).astype(np.uint8))
    # Median removes the speckles, Min eats the grey fringe compression left along the outline.
    alpha = alpha.filter(ImageFilter.MedianFilter(7)).filter(ImageFilter.MinFilter(5))
    alpha = Image.fromarray(np.where(largest_island(np.asarray(alpha) > 128), 255, 0).astype(np.uint8))
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.8))

    out = Image.fromarray(np.dstack([np.asarray(src), np.asarray(alpha)]), 'RGBA')
    box = out.getbbox()
    if box is None:
        raise SystemExit('Nothing left after removing the backdrop: check --tolerance.')
    out = out.crop(box)
    side = max(out.size)
    square = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    square.paste(out, ((side - out.width) // 2, (side - out.height) // 2), out)
    square = square.resize((args.size, args.size), Image.LANCZOS)

    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    square.save(args.output)
    print('%s -> %s (%dx%d)' % (os.path.basename(args.source), args.output, args.size, args.size))


if __name__ == '__main__':
    main()
