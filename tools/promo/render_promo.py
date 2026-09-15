"""Renders the Crush Royale promo video from a real engine recording (tools/PromoRecorder) and the game's art/audio.

    dotnet run --project tools/PromoRecorder -- art/promo/promo_match.json
    python tools/promo/render_promo.py --lang fr            # full video, 9:16 + 16:9
    python tools/promo/render_promo.py --lang fr --preview 1.0,6.5,12,18   # PNG frames only

Outputs art/promo/crushroyale_promo_<lang>_9x16.mp4 (TikTok, Reels, Shorts) and _16x9.mp4 (YouTube / Google Play).
Scenes: hook (Fire Storm) -> PvP duel -> power-ups -> guild boss -> kingdoms -> leagues -> end card.
The match itself is exact engine output; guild/league screens use sample names and numbers.
"""
import argparse
import copy
import json
import math
import random
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "unity/CrushRoyale/Assets/Resources/Art"
AUDIO = ROOT / "unity/CrushRoyale/Assets/Resources/Audio"
OUT = ROOT / "art/promo"
W, H, FPS = 1080, 1920, 30
COLS = ROWS = 8
CELL = 116
BOARD = CELL * COLS
BOARD_X = (W - BOARD) // 2
BOARD_Y = 650
COLOR_NAMES = ["red", "blue", "green", "yellow", "purple", "orange"]
GEM_RGB = [(255, 70, 90), (70, 150, 255), (70, 220, 120), (255, 210, 60), (190, 90, 255), (255, 140, 40)]
GOLD = (255, 205, 90)
CRYSTAL = (120, 230, 255)
POWER_FILES = {"Multiplier2x": "multiplier", "NuclearBomb": "bomb", "FireStorm": "tornado"}

TEXT = {
    "fr": {
        "hook": "UN COUP… TOUT EXPLOSE !", "pvp": "AFFRONTE DE VRAIS JOUEURS", "powerups": "9 BONUS EXPLOSIFS",
        "guild": "BATS DES BOSS AVEC TA GUILDE", "story": "1000 NIVEAUX · 5 ROYAUMES", "league": "GRIMPE AU SOMMET DES LIGUES",
        "end": "JOUE GRATUITEMENT", "you": "TOI", "rival": "RIVAL", "defeated": "BOSS VAINCU !", "guildname": "LES CRISTAUX NOIRS",
        "guildlevel": "Guilde · Niveau 12", "leaguename": "LIGUE MAÎTRE", "android": "Disponible sur Android",
        "kingdoms": ["FROSTREACH", "SUNSPIRE", "TERRES SAUVAGES", "EMBERFALL", "CRYSTALHEIM"], "rank": "CLASSEMENT",
    },
    "en": {
        "hook": "ONE MOVE… EVERYTHING BLOWS UP!", "pvp": "BEAT REAL PLAYERS", "powerups": "9 EXPLOSIVE POWER-UPS",
        "guild": "DEFEAT BOSSES WITH YOUR GUILD", "story": "1000 LEVELS · 5 KINGDOMS", "league": "CLIMB TO THE TOP LEAGUE",
        "end": "PLAY FOR FREE", "you": "YOU", "rival": "RIVAL", "defeated": "BOSS DEFEATED!", "guildname": "BLACK CRYSTALS",
        "guildlevel": "Guild · Level 12", "leaguename": "MASTER LEAGUE", "android": "Available on Android",
        "kingdoms": ["FROSTREACH", "SUNSPIRE", "VERDANT WILDS", "EMBERFALL", "CRYSTALHEIM"], "rank": "LEADERBOARD",
    },
}

# ----------------------------------------------------------------------------------------------------------- helpers

_fonts = {}


def font(size, title=False):
    key = (size, title)
    if key not in _fonts:
        path = ROOT / "unity/CrushRoyale/Assets/Resources/Fonts/CinzelDecorative-Bold.ttf" if title else Path("C:/Windows/Fonts/seguibl.ttf")
        if not path.exists():
            path = Path("C:/Windows/Fonts/arialbd.ttf")
        _fonts[key] = ImageFont.truetype(str(path), size)
    return _fonts[key]


def clamp(v, a=0.0, b=1.0):
    return max(a, min(b, v))


def ease_out_cubic(k):
    k = clamp(k)
    return 1 - (1 - k) ** 3


def ease_in_quad(k):
    k = clamp(k)
    return k * k


def ease_out_back(k):
    k = clamp(k)
    c1, c3 = 1.70158, 2.70158
    return 1 + c3 * (k - 1) ** 3 + c1 * (k - 1) ** 2


def load(path, size=None):
    im = Image.open(path).convert("RGBA")
    if size:
        im = im.resize(size if isinstance(size, tuple) else (size, size), Image.LANCZOS)
    return im


def cover(path, w=W, h=H, brightness=1.0, blur=0, zoom=1.0):
    im = Image.open(path).convert("RGB")
    s = max(w / im.width, h / im.height) * zoom
    im = im.resize((math.ceil(im.width * s), math.ceil(im.height * s)), Image.LANCZOS)
    x, y = (im.width - w) // 2, (im.height - h) // 2
    im = im.crop((x, y, x + w, y + h))
    if blur:
        im = im.filter(ImageFilter.GaussianBlur(blur))
    if brightness != 1.0:
        im = ImageEnhance.Brightness(im).enhance(brightness)
    return im.convert("RGBA")


def scaled(im, scale):
    size = (max(1, int(im.width * scale)), max(1, int(im.height * scale)))
    return im.resize(size, Image.LANCZOS)


def paste_center(canvas, im, cx, cy, alpha=1.0):
    if alpha < 1.0:
        im = im.copy()
        a = im.getchannel("A").point(lambda v: int(v * clamp(alpha)))
        im.putalpha(a)
    canvas.paste(im, (int(cx - im.width / 2), int(cy - im.height / 2)), im)


def text(draw, xy, s, size, fill=(255, 255, 255, 255), stroke=6, stroke_fill=(30, 12, 60, 255), anchor="mm", title=False):
    draw.text(xy, s, font=font(size, title), fill=fill, stroke_width=stroke, stroke_fill=stroke_fill, anchor=anchor)


def fit_size(s, max_width, size, title=False):
    while size > 20 and font(size, title).getlength(s) > max_width:
        size -= 4
    return size


def circle_portrait(path, size, ring=GOLD):
    src = load(path, size)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, size - 1, size - 1), fill=255)
    out = Image.new("RGBA", (size + 12, size + 12), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)
    d.ellipse((0, 0, size + 11, size + 11), fill=ring + (255,))
    inner = Image.new("RGBA", (size, size), (25, 14, 55, 255))
    inner.paste(src, (0, 0), src)
    out.paste(inner, (6, 6), mask)
    return out


# ----------------------------------------------------------------------------------------------------------- assets

class Assets:
    def __init__(self):
        base = int(CELL * 0.94)
        self.gems = [load(ART / f"Gems/{n}.png", base) for n in COLOR_NAMES]
        self.stone = load(ART / "Gems/stone.png", base)
        self.cache = {}
        self.powers = {k: load(ART / f"PowerUps/{v}.png") for k, v in POWER_FILES.items()}
        self.panel = self._panel()

    def _panel(self):
        panel = Image.new("RGBA", (BOARD, BOARD), (0, 0, 0, 0))
        d = ImageDraw.Draw(panel)
        d.rounded_rectangle((0, 0, BOARD - 1, BOARD - 1), radius=34, fill=(14, 9, 34, 215), outline=(150, 120, 255, 180), width=4)
        for x in range(COLS):
            for y in range(ROWS):
                if (x + y) % 2 == 0:
                    d.rounded_rectangle((x * CELL + 4, y * CELL + 4, (x + 1) * CELL - 4, (y + 1) * CELL - 4), radius=14, fill=(255, 255, 255, 16))
        return panel

    def piece(self, c, t, scale=1.0):
        step = max(1, int(round(scale * 20)))
        key = (c, t, step)
        im = self.cache.get(key)
        if im is None:
            im = (self.stone if t == 4 else self.gems[c]).copy()
            if t in (1, 2, 3):
                overlay = Image.new("RGBA", im.size, (0, 0, 0, 0))
                d = ImageDraw.Draw(overlay)
                s = im.width
                if t == 1:
                    d.rounded_rectangle((4, s * 0.42, s - 4, s * 0.58), radius=8, fill=(255, 255, 255, 200))
                elif t == 2:
                    d.rounded_rectangle((s * 0.42, 4, s * 0.58, s - 4), radius=8, fill=(255, 255, 255, 200))
                else:
                    d.ellipse((6, 6, s - 6, s - 6), outline=GOLD + (255,), width=9)
                glow = overlay.filter(ImageFilter.GaussianBlur(5))
                im.alpha_composite(glow)
                im.alpha_composite(overlay)
            if step != 20:
                im = scaled(im, step / 20)
            self.cache[key] = im
        return im


A = None


# ----------------------------------------------------------------------------------------------------------- board

def cell_center(x, y):
    """Board-layer coordinates of a cell centre (y = 0 is the bottom row, like the engine)."""
    return (x + 0.5) * CELL, (ROWS - 1 - y + 0.5) * CELL


def screen_center(x, y):
    lx, ly = cell_center(x, y)
    return BOARD_X + lx, BOARD_Y + ly


def draw_board(canvas, spec, k, shake=(0, 0)):
    layer = A.panel.copy()
    moves = spec.get("moves", {})
    vanish = spec.get("vanish", set())
    pop = spec.get("pop", set())
    for pid, p in spec["pieces"].items():
        x, y = p["x"], p["y"]
        scale = 1.0
        if pid in moves:
            (fx, fy), (tx, ty), kind = moves[pid]
            e = ease_in_quad(k) if kind == "fall" else ease_out_cubic(k)
            x, y = fx + (tx - fx) * e, fy + (ty - fy) * e
        if pid in vanish:
            scale = 1.0 - ease_in_quad(k)
        if pid in pop:
            scale = max(0.05, ease_out_back(k))
        if scale <= 0.04:
            continue
        im = A.piece(p["c"], p["t"], scale)
        lx, ly = cell_center(x, y)
        layer.paste(im, (int(lx - im.width / 2), int(ly - im.height / 2)), im)
    canvas.alpha_composite(layer, (BOARD_X + int(shake[0]), BOARD_Y + int(shake[1])))


def draw_bursts(fx, bursts, lt):
    d = ImageDraw.Draw(fx)
    for (cx, cy, rgb, seed) in bursts:
        rnd = random.Random(seed)
        if lt > 0.6:
            continue
        ring = CELL * (0.35 + lt * 2.2)
        a = int(220 * clamp(1 - lt * 3.2))
        if a > 0:
            d.ellipse((cx - ring, cy - ring, cx + ring, cy + ring), outline=(255, 255, 255, a), width=5)
        for _ in range(7):
            ang = rnd.uniform(0, math.tau)
            spd = rnd.uniform(260, 520)
            px = cx + math.cos(ang) * spd * lt
            py = cy + (math.sin(ang) * spd - 300) * lt + 900 * lt * lt
            r = rnd.uniform(6, 13) * (1 - lt / 0.6)
            alpha = int(255 * clamp(1 - lt / 0.6))
            light = tuple(min(255, v + 90) for v in rgb)
            d.ellipse((px - r, py - r, px + r, py + r), fill=light + (alpha,))


def board_state(pieces):
    return {p["id"]: {"x": p["x"], "y": p["y"], "c": p["c"], "t": p["t"], "hp": p.get("hp", 0)} for p in pieces}


def piece_at(state, x, y):
    for pid, p in state.items():
        if p["x"] == x and p["y"] == y:
            return pid
    return None


# ----------------------------------------------------------------------------------------------------------- timeline

class Seg:
    """A slice of video: duration, a draw callback (canvas, fx overlay, k 0..1, local seconds) and sound cues."""

    def __init__(self, dur, draw, sounds=None, hud=None):
        self.dur = dur
        self.draw = draw
        self.sounds = sounds or []
        self.hud = hud or {}


def match_segments(state, action, speed, hud_base, rival_at):
    """Animation segments of one engine action; mutates state to the post-action board."""
    segs = []
    t_ms = action["t"]
    prev_score = hud_base["score"]
    total_base = sum(max(1, s["base"]) for s in action["steps"]) or 1

    def hud(score, extra=None):
        h = dict(hud_base)
        h.update({"score": score, "rival": rival_at(t_ms), "time_ms": t_ms})
        if extra:
            h.update(extra)
        return h

    if action["kind"] == "swap":
        a = piece_at(state, *action["from"])
        b = piece_at(state, *action["to"])
        snap = copy.deepcopy(state)
        moves = {}
        if a is not None and b is not None:
            moves[a] = (tuple(action["from"]), tuple(action["to"]), "swap")
            moves[b] = (tuple(action["to"]), tuple(action["from"]), "swap")
            state[a]["x"], state[a]["y"], state[b]["x"], state[b]["y"] = state[b]["x"], state[b]["y"], state[a]["x"], state[a]["y"]
        segs.append(Seg(0.17 / speed, lambda c, f, k, lt, snap=snap, moves=moves: draw_board(c, {"pieces": snap, "moves": moves}, k), [("click", 0.0, 0.5)], hud(prev_score)))
    else:
        power = action["power"]
        snap = copy.deepcopy(state)
        target = action.get("target")
        cx, cy = screen_center(*target) if target else (BOARD_X + BOARD / 2, BOARD_Y + BOARD / 2)
        icon = A.powers[power]

        def intro(c, f, k, lt, snap=snap, power=power, cx=cx, cy=cy, icon=icon):
            shake = (math.sin(lt * 70) * 14 * (1 - k), math.cos(lt * 55) * 10 * (1 - k)) if power != "Multiplier2x" else (0, 0)
            draw_board(c, {"pieces": snap}, 1.0, shake)
            d = ImageDraw.Draw(f)
            if power == "FireStorm":
                d.rectangle((0, 0, W, H), fill=(255, 110, 20, int(90 * math.sin(k * math.pi))))
            ring = 60 + ease_out_cubic(k) * 900
            d.ellipse((cx - ring, cy - ring, cx + ring, cy + ring), outline=CRYSTAL + (int(255 * (1 - k)),), width=14)
            s = max(0.05, ease_out_back(min(1.0, k * 3.0)))
            halo = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            ImageDraw.Draw(halo).ellipse((W / 2 - 330, BOARD_Y + BOARD / 2 - 330, W / 2 + 330, BOARD_Y + BOARD / 2 + 330), fill=(255, 220, 140, int(170 * (1 - k))))
            f.alpha_composite(halo.filter(ImageFilter.GaussianBlur(40)))
            paste_center(f, scaled(icon, s * 1.9), W / 2, BOARD_Y + BOARD / 2, alpha=1.0 if k < 0.75 else (1 - k) / 0.25)
            if power == "Multiplier2x":
                text(d, (W / 2, BOARD_Y + BOARD / 2 + 330), "x2", int(150 + 60 * s), fill=GOLD + (int(255 * clamp(2 - k * 2)),), stroke=10)

        sound = "explosion" if power in ("NuclearBomb", "FireStorm") else "powerup"
        segs.append(Seg(0.75 / speed, intro, [("powerup", 0.0, 0.9), (sound, 0.25, 1.0)], hud(prev_score, {"used": power})))

    running_base = 0
    for i, step in enumerate(action["steps"]):
        running_base += max(1, step["base"])
        step_score = prev_score + action["points"] * running_base // total_base
        snap = copy.deepcopy(state)
        vanish = {c["id"] for c in step["cleared"]} | {s["id"] for s in step["stones"] if s["hp"] <= 0}
        bursts = []
        for c in step["cleared"]:
            sx, sy = screen_center(c["x"], c["y"])
            rgb = GEM_RGB[c["c"]] if 0 <= c["c"] < 6 else (200, 200, 200)
            bursts.append((sx, sy, rgb, c["id"]))
        if step["cleared"]:
            mx = sum(screen_center(c["x"], c["y"])[0] for c in step["cleared"]) / len(step["cleared"])
            my = sum(screen_center(c["x"], c["y"])[1] for c in step["cleared"]) / len(step["cleared"])
        else:
            mx, my = W / 2, BOARD_Y + BOARD / 2
        gained = step_score - prev_score if i == 0 else action["points"] * max(1, step["base"]) // total_base
        big = len(step["cleared"]) >= 10 or any(c["cause"] != "Match" for c in step["cleared"])

        for pid in vanish:
            state.pop(pid, None)
        for s in step["stones"]:
            if s["hp"] > 0 and s["id"] in state:
                state[s["id"]]["hp"] = s["hp"]
        pop = set()
        for sp in step["specials"]:
            state[sp["id"]] = {"x": sp["x"], "y": sp["y"], "c": sp["c"], "t": sp["t"], "hp": sp.get("hp", 0)}
            pop.add(sp["id"])
        moves = {}
        for fl in step["falls"]:
            if fl["id"] in state:
                moves[fl["id"]] = ((fl["fx"], fl["fy"]), (fl["tx"], fl["ty"]), "fall")
                state[fl["id"]]["x"], state[fl["id"]]["y"] = fl["tx"], fl["ty"]
        for rf in step["refills"]:
            state[rf["id"]] = {"x": rf["x"], "y": rf["y"], "c": rf["c"], "t": rf["t"], "hp": rf.get("hp", 0)}
            moves[rf["id"]] = ((rf["x"], rf["row"]), (rf["x"], rf["y"]), "fall")
        after = copy.deepcopy(state)
        clear_d, fall_d = 0.16 / speed, 0.24 / speed
        label = ("+" + format(gained, ",").replace(",", " ")) if gained > 0 else ""
        level = step["level"]

        def clear_draw(c, f, k, lt, snap=snap, vanish=vanish, bursts=bursts, big=big):
            shake = (math.sin(lt * 90) * 12, math.cos(lt * 70) * 8) if big else (0, 0)
            draw_board(c, {"pieces": snap, "vanish": vanish}, k, shake)
            draw_bursts(f, bursts, lt)

        def fall_draw(c, f, k, lt, after=after, moves=moves, pop=pop, bursts=bursts, label=label, mx=mx, my=my, level=level, clear_d=clear_d):
            draw_board(c, {"pieces": after, "moves": moves, "pop": pop}, k)
            draw_bursts(f, bursts, lt + clear_d)
            d = ImageDraw.Draw(f)
            if label:
                alpha = int(255 * clamp(1.4 - k))
                text(d, (mx, my - 40 - 90 * k), label, 64, fill=GOLD + (alpha,), stroke=6, stroke_fill=(60, 20, 0, alpha))
            if level >= 1:
                s = ease_out_back(min(1, k * 1.8))
                text(d, (W / 2, BOARD_Y - 10), ("CASCADE x" + str(level + 1)), int(58 * s) + 1, fill=CRYSTAL + (int(255 * clamp(1.6 - k)),), stroke=6)

        count = len(step["cleared"])
        match_sound = "match3" if count <= 3 else "match4" if count <= 5 else "match5"
        sounds = [(match_sound, 0.0, 0.8)]
        if level >= 1:
            sounds.append(("cascade", 0.0, 0.6))
        if big:
            sounds.append(("explosion", 0.0, 0.45))
        segs.append(Seg(clear_d, clear_draw, sounds, hud(prev_score if i == 0 else step_score)))
        segs.append(Seg(fall_d, fall_draw, None, hud(step_score)))
        prev_score = step_score

    if action.get("shuffle"):
        state.clear()
        state.update(board_state(action["shuffle"]))
    hud_base["score"] = action["score"]
    if action["kind"] == "powerup":
        hud_base.setdefault("used_all", []).append(action["power"])
        if action["power"] == "Multiplier2x":
            hud_base["x2_until"] = action["t"] + 15000
    return segs


def pause(state, dur, hud):
    snap = copy.deepcopy(state)
    return Seg(dur, lambda c, f, k, lt, snap=snap: draw_board(c, {"pieces": snap}, 1.0), None, dict(hud))


# ----------------------------------------------------------------------------------------------------------- HUD & captions

def draw_caption(canvas, s, lt, y=250):
    d = ImageDraw.Draw(canvas)
    k = ease_out_back(min(1.0, lt / 0.28))
    band_h = 150
    d.rounded_rectangle((40, y - band_h / 2, W - 40, y + band_h / 2), radius=36, fill=(16, 8, 40, 200), outline=GOLD + (220,), width=4)
    size = fit_size(s, W - 140, 86)
    text(d, (W / 2, y), s, max(10, int(size * k)), fill=(255, 255, 255, 255), stroke=7)


def draw_pvp_hud(canvas, hud, T, portraits):
    d = ImageDraw.Draw(canvas)
    top = 380
    d.rounded_rectangle((30, top, W - 30, top + 220), radius=36, fill=(12, 8, 32, 210), outline=(140, 110, 255, 160), width=3)
    canvas.paste(portraits["you"], (60, top + 40), portraits["you"])
    canvas.paste(portraits["rival"], (W - 60 - portraits["rival"].width, top + 40), portraits["rival"])
    you, rival = hud.get("score", 0), hud.get("rival", 0)
    text(d, (205, top + 70), T["you"], 40, fill=CRYSTAL + (255,), stroke=4, anchor="lm")
    text(d, (205, top + 135), format(you, ",").replace(",", " "), 62, fill=(255, 255, 255, 255), stroke=5, anchor="lm")
    text(d, (W - 205, top + 70), T["rival"], 40, fill=(255, 120, 140, 255), stroke=4, anchor="rm")
    text(d, (W - 205, top + 135), format(rival, ",").replace(",", " "), 62, fill=(255, 255, 255, 255), stroke=5, anchor="rm")
    remaining = max(0, 90 - hud.get("time_ms", 0) / 1000)
    text(d, (W / 2, top + 95), f"{int(remaining // 60)}:{int(remaining % 60):02d}", 58, fill=GOLD + (255,), stroke=5)
    if hud.get("time_ms", 0) < hud.get("x2_until", -1):
        text(d, (W / 2, top + 165), "x2", 50, fill=(255, 230, 120, 255), stroke=5)
    ratio = you / max(1, you + rival)
    bx0, bx1, by = 70, W - 70, top + 245
    d.rounded_rectangle((bx0, by - 14, bx1, by + 14), radius=14, fill=(255, 90, 120, 255))
    d.rounded_rectangle((bx0, by - 14, bx0 + (bx1 - bx0) * ratio, by + 14), radius=14, fill=(90, 200, 255, 255))

    # Power-up bar.
    by = BOARD_Y + BOARD + 125
    used = set(hud.get("used_all", []))
    active = hud.get("used")
    for i, name in enumerate(["Multiplier2x", "NuclearBomb", "FireStorm"]):
        cx = W / 2 + (i - 1) * 250
        glow = name == active
        d.rounded_rectangle((cx - 105, by - 95, cx + 105, by + 95), radius=30, fill=(40, 25, 80, 230) if not glow else (120, 80, 20, 240),
                            outline=(GOLD + (255,)) if glow else (140, 110, 255, 180), width=6 if glow else 3)
        icon = scaled(A.powers[name], 0.62 if not glow else 0.72)
        if name in used and not glow:
            icon = icon.copy()
            icon.putalpha(icon.getchannel("A").point(lambda v: v // 3))
        paste_center(canvas, icon, cx, by)


# ----------------------------------------------------------------------------------------------------------- scenes

def flash_in(f, lt, dur=0.14):
    if lt < dur:
        ImageDraw.Draw(f).rectangle((0, 0, W, H), fill=(255, 255, 255, int(230 * (1 - lt / dur))))


def guild_scene(T):
    bg = cover(ART / "Backgrounds/emberfall.jpg", brightness=0.5)
    boss = load(ART / "Bosses/pyraxis.png", 860)
    shield = load(ART / "Icons/guild.png", 150)
    chest = load(ART / "Icons/chest.png", 260)
    coin = load(ART / "Icons/coin.png", 90)
    members = [("Lyra", "lyra", 184_300), ("Kael", "kael", 226_900), ("Mira", "mira", 197_400), ("Thorin", "thorin", 153_800), (T["you"], "hero", 237_600)]
    portraits = {m[1]: circle_portrait(ART / f"Characters/{m[1]}.png", 120) for m in members}
    max_hp = sum(m[2] for m in members)
    hits = [0.55 + i * 0.62 for i in range(len(members))]
    dur = 5.4

    def draw(c, f, k, lt):
        c.alpha_composite(bg)
        d = ImageDraw.Draw(c)
        fd = ImageDraw.Draw(f)
        draw_caption(c, T["guild"], lt)
        c.paste(shield, (70, 380), shield)
        text(d, (240, 430), T["guildname"], 56, fill=GOLD + (255,), stroke=5, anchor="lm")
        text(d, (240, 495), T["guildlevel"], 38, fill=(220, 210, 255, 255), stroke=4, anchor="lm")

        done = [h for h in hits if lt >= h]
        damage = sum(members[i][2] for i in range(len(done)))
        last_hit = max(done) if done else -9
        since = lt - last_hit
        shake = (math.sin(since * 80) * 22 * clamp(1 - since * 4), 0) if since < 0.25 else (0, 0)
        defeated = len(done) == len(members)
        bob = math.sin(lt * 2.2) * 12
        glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(glow).ellipse((W / 2 - 430, 560, W / 2 + 430, 1300), fill=(255, 90, 20, 90))
        c.alpha_composite(glow.filter(ImageFilter.GaussianBlur(60)))
        boss_alpha = 1.0 if not defeated else clamp(1 - (lt - hits[-1]) * 1.6)
        paste_center(c, boss, W / 2 + shake[0], 930 + bob, alpha=boss_alpha)
        if since < 0.18:
            fd.rectangle((0, 0, W, H), fill=(255, 40, 40, int(90 * (1 - since / 0.18))))

        hp = max(0, max_hp - damage)
        shown = hp + (members[len(done) - 1][2] * clamp(1 - since * 5) if done and since < 0.2 else 0)
        y = 1400
        d.rounded_rectangle((90, y - 34, W - 90, y + 34), radius=34, fill=(40, 10, 10, 230), outline=(255, 180, 90, 255), width=4)
        d.rounded_rectangle((96, y - 28, 96 + (W - 192) * shown / max_hp, y + 28), radius=28, fill=(235, 60, 40, 255))
        text(d, (W / 2, y - 80), "PYRAXIS", 58, fill=(255, 200, 150, 255), stroke=6, title=True)
        text(d, (W / 2, y), format(int(hp), ",").replace(",", " ") + " PV" if T is TEXT["fr"] else format(int(hp), ",") + " HP", 40, stroke=4)

        for i, h in enumerate(hits):
            if lt < h - 0.35:
                continue
            name, key, dmg = members[i]
            k_in = ease_out_cubic((lt - (h - 0.35)) / 0.3)
            cx = 150 + i * 195
            cy = 1640 + (1 - k_in) * 200
            c.paste(portraits[key], (int(cx - 66), int(cy - 66)), portraits[key])
            text(d, (cx, cy + 90), name, 34, stroke=4)
            if 0 <= lt - h < 0.9:
                p = (lt - h) / 0.9
                text(fd, (W / 2 + (i - 2) * 120, 1000 - 260 * p), "-" + format(dmg, ",").replace(",", " "), int(70 + 30 * (1 - p)),
                     fill=(255, 230, 120, int(255 * clamp(1.5 - p * 1.5))), stroke=7, stroke_fill=(120, 20, 0, 255))

        if defeated:
            p = lt - hits[-1] - 0.15
            if p > 0:
                s = ease_out_back(min(1, p / 0.35))
                paste_center(f, scaled(chest, s), W / 2, 900)
                size = fit_size(T["defeated"], W - 120, 104, title=True)
                text(fd, (W / 2, 1180), T["defeated"], max(10, int(size * s)), fill=GOLD + (255,), stroke=9, title=True)
                rnd = random.Random(7)
                for j in range(14):
                    ang = rnd.uniform(0, math.tau)
                    dist = 120 + 560 * ease_out_cubic(p / 0.8)
                    paste_center(f, coin, W / 2 + math.cos(ang) * dist, 900 + math.sin(ang) * dist * 0.8 + 400 * p * p, alpha=clamp(1.4 - p))
        flash_in(f, lt)

    sounds = [("powerup", h, 0.7) for h in hits] + [("explosion", h + 0.02, 0.5) for h in hits] + [("win", hits[-1] + 0.15, 1.0), ("coins", hits[-1] + 0.35, 0.8)]
    return [Seg(dur, draw, sounds)]


def story_scene(T):
    files = ["frostreach", "sunspire", "verdant", "emberfall", "crystalheim"]
    heroes = ["lyra", "mira", "zara", "thorin", "valdorax"]
    bgs = [cover(ART / f"Backgrounds/{n}.jpg", brightness=0.8, zoom=1.08) for n in files]
    faces = [load(ART / f"Characters/{n}.png", 620) for n in heroes]
    per = 0.78
    dur = per * len(files)

    def draw(c, f, k, lt):
        i = min(len(files) - 1, int(lt / per))
        p = (lt - i * per) / per
        offset = int((1 - ease_out_cubic(min(1, p * 2.5))) * W * 0.35)
        c.alpha_composite(bgs[i], (0, 0))
        if i > 0 and p < 0.25:
            prev = bgs[i - 1].copy()
            prev.putalpha(int(255 * (1 - p / 0.25)))
            c.alpha_composite(prev)
        d = ImageDraw.Draw(c)
        paste_center(c, faces[i], W / 2 + (offset if i % 2 == 0 else -offset), 1240)
        name = T["kingdoms"][i]
        text(d, (W / 2, 560), name, fit_size(name, W - 100, 110, title=True), fill=GOLD + (255,), stroke=9, title=True)
        draw_caption(c, T["story"], lt)
        for j in range(len(files)):
            cx = W / 2 + (j - 2) * 60
            d.ellipse((cx - 14, 1810 - 14, cx + 14, 1810 + 14), fill=(GOLD + (255,)) if j <= i else (255, 255, 255, 90))
        if p < 0.12:
            flash_in(f, p * per, 0.1)

    return [Seg(dur, draw, [("cascade", per * i, 0.6) for i in range(len(files))])]


def league_scene(T):
    bg = cover(ART / "Backgrounds/crystalheim.jpg", brightness=0.45, blur=3)
    crown = load(ART / "Icons/crown.png", 170)
    trophy = load(ART / "Icons/trophy.png", 64)
    star = load(ART / "Icons/star.png", 70)
    you_face = circle_portrait(ART / "Characters/hero.png", 92, ring=CRYSTAL)
    others = [("CrystalQueen", 3080), ("Dragonbane", 3010), ("NoxMaster", 2950), ("Aria", 2890), ("Kaito", 2820), ("Zed", 2760)]
    dur = 3.6

    def draw(c, f, k, lt):
        c.alpha_composite(bg)
        d = ImageDraw.Draw(c)
        fd = ImageDraw.Draw(f)
        draw_caption(c, T["league"], lt)
        paste_center(c, crown, W / 2, 470 + math.sin(lt * 3) * 8)
        text(d, (W / 2, 610), T["leaguename"], 60, fill=GOLD + (255,), stroke=6, title=True)
        rise = ease_out_cubic(clamp((lt - 0.5) / 1.8))
        trophies = int(2480 + (3150 - 2480) * rise)
        you_rank = 6 - rise * 6
        rows = []
        for idx, (name, value) in enumerate(others):
            slot = idx + (1 if idx >= you_rank - 0.5 else 0)
            rows.append((slot, name, value, False))
        rows.append((you_rank, T["you"], trophies, True))
        for slot, name, value, mine in rows:
            y = int(760 + slot * 150)
            fill = (70, 40, 130, 235) if not mine else (150, 100, 20, 245)
            d.rounded_rectangle((60, y, W - 60, y + 128), radius=30, fill=fill, outline=(GOLD + (255,)) if mine else (140, 110, 255, 150), width=5 if mine else 2)
            rank = int(round(slot)) + 1
            text(d, (130, y + 64), "#" + str(rank), 50, fill=(GOLD + (255,)) if rank == 1 else (255, 255, 255, 255), stroke=5)
            if mine:
                c.paste(you_face, (205, y + 12), you_face)
            text(d, (330 if mine else 220, y + 64), name, 50, stroke=5, anchor="lm")
            c.paste(trophy, (W - 330, y + 32), trophy)
            text(d, (W - 90, y + 64), format(value, ",").replace(",", " "), 50, fill=(255, 235, 170, 255), stroke=5, anchor="rm")
        if rise >= 1:
            p = lt - 2.3
            rnd = random.Random(3)
            for j in range(18):
                ang = rnd.uniform(0, math.tau)
                dist = 80 + 520 * ease_out_cubic(clamp(p / 0.9))
                paste_center(f, star, W / 2 + math.cos(ang) * dist, 830 + math.sin(ang) * dist * 0.7 + 300 * p * p, alpha=clamp(1.5 - p))
        flash_in(f, lt)

    return [Seg(dur, draw, [("trophy_gain", 0.5, 0.9), ("coins", 1.2, 0.6), ("win", 2.3, 0.8)])]


def end_scene(T):
    bg = Image.open(ART / "Backgrounds/crystalheim.jpg").convert("RGB")
    logo = load(ART / "logo.png")
    logo = scaled(logo, (W - 120) / logo.width)
    icon = load(ART / "Icons/crown.png", 90)
    dur = 3.2

    def draw(c, f, k, lt):
        z = 1.0 + 0.06 * (lt / dur)
        c.alpha_composite(cover(ART / "Backgrounds/crystalheim.jpg", brightness=0.6, zoom=z) if lt < 0.05 or int(lt * FPS) % 3 == 0 else draw.cache)
        draw.cache = c.copy()
        s = ease_out_back(min(1, lt / 0.45))
        paste_center(c, scaled(logo, max(0.05, s)), W / 2, 820)
        d = ImageDraw.Draw(c)
        pulse = 1 + 0.05 * math.sin(lt * 7)
        size = fit_size(T["end"], W - 140, 104)
        if lt > 0.4:
            d.rounded_rectangle((W / 2 - 440 * pulse, 1230 - 95 * pulse, W / 2 + 440 * pulse, 1230 + 95 * pulse), radius=90, fill=(60, 190, 110, 255), outline=(255, 255, 255, 255), width=6)
            text(d, (W / 2, 1230), T["end"], int(size * pulse), stroke=7, stroke_fill=(10, 70, 30, 255))
        text(d, (W / 2, 1420), T["android"], 46, fill=(230, 220, 255, 255), stroke=5)
        flash_in(f, lt)

    draw.cache = None
    return [Seg(dur, draw, [("win", 0.1, 0.7)])]


# ----------------------------------------------------------------------------------------------------------- build

def build_timeline(rec, lang):
    T = TEXT[lang]
    actions = rec["actions"]
    idx = {a.get("power"): i for i, a in enumerate(actions) if a["kind"] == "powerup"}
    nuke, fire = idx["NuclearBomb"], idx["FireStorm"]
    opponent = rec["opponent"]

    own = [(a["t"], a["score"]) for a in actions]

    def rival_at(t_ms):
        # The rival's pace comes from a second engine run; its level is pinned just behind the player
        # (about 2.5 s late, 9% lower) so the duel bar stays tense in every scene.
        base = 0
        for t, s in own:
            if t <= t_ms - 2500:
                base = s
        wobble = 0
        for t, s in opponent:
            if t <= t_ms:
                wobble = s
        return int(base * 0.91 + (wobble % 900))

    portraits = {"you": circle_portrait(ART / "Characters/hero.png", 130, ring=CRYSTAL), "rival": circle_portrait(ART / "Characters/kael.png", 130, ring=(255, 110, 130))}
    scenes = []

    def board_scene(name, bg_name, caption, first, last, speed, preroll_to=None, lead=0.35):
        state = board_state(rec["initial"])
        hud = {"score": 0, "used_all": []}
        start = first if preroll_to is None else preroll_to
        for a in actions[:start]:
            match_segments(state, a, 50.0, hud, rival_at)
        hud["used"] = None
        segs = [pause(state, lead, dict(hud, rival=rival_at(actions[start]["t"]), time_ms=actions[start]["t"]))]
        for a in actions[start:last + 1]:
            hud["used"] = None
            segs.extend(match_segments(state, a, speed, hud, rival_at))
        segs.append(pause(state, 0.25, dict(hud, rival=rival_at(actions[last]["t"]), time_ms=actions[last]["t"])))
        bg = cover(ART / f"Backgrounds/{bg_name}.jpg", brightness=0.55)
        scenes.append({"name": name, "bg": bg, "caption": caption, "segs": segs, "pvp": True})

    board_scene("hook", "emberfall", T["hook"], fire, fire, 1.0, preroll_to=fire, lead=0.25)
    board_scene("pvp", "crystalheim", T["pvp"], 0, nuke - 1, 1.7)
    board_scene("powerups", "frostreach", T["powerups"], nuke, min(len(actions) - 1, nuke + 2), 1.15, preroll_to=nuke)
    for name, maker in (("guild", guild_scene), ("story", story_scene), ("league", league_scene), ("end", end_scene)):
        scenes.append({"name": name, "bg": None, "caption": None, "segs": maker(T), "pvp": False})
    return scenes, portraits, T


def render(scenes, portraits, T, frames_out, preview_times=None):
    events = []
    frame_count = 0
    scene_starts = []
    t_global = 0.0
    for sc in scenes:
        scene_starts.append(t_global)
        for seg in sc["segs"]:
            for (snd, off, vol) in seg.sounds:
                events.append((t_global + off, snd, vol))
            t_global += seg.dur
    total = t_global
    n_frames = int(total * FPS)
    wanted = None if preview_times is None else {int(t * FPS): t for t in preview_times}

    si, seg_i, seg_start = 0, 0, 0.0
    scene_t0 = 0.0
    for frame in range(n_frames):
        t = frame / FPS
        while si < len(scenes) - 1 and t >= scene_starts[si + 1]:
            si += 1
            seg_i, seg_start = 0, scene_starts[si]
        sc = scenes[si]
        while seg_i < len(sc["segs"]) - 1 and t >= seg_start + sc["segs"][seg_i].dur:
            seg_start += sc["segs"][seg_i].dur
            seg_i += 1
        if wanted is not None and frame not in wanted:
            continue
        seg = sc["segs"][seg_i]
        k = clamp((t - seg_start) / seg.dur) if seg.dur > 0 else 1.0
        lt_scene = t - scene_starts[si]
        canvas = sc["bg"].copy() if sc["bg"] is not None else Image.new("RGBA", (W, H), (10, 6, 24, 255))
        fx = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        seg.draw(canvas, fx, k, t - seg_start)
        if sc["pvp"]:
            draw_pvp_hud(canvas, seg.hud, T, portraits)
            draw_caption(canvas, sc["caption"], lt_scene)
            flash_in(fx, lt_scene)
        canvas.alpha_composite(fx)
        if wanted is not None:
            path = OUT / f"preview_{wanted[frame]:05.2f}.png"
            canvas.convert("RGB").save(path)
            print("preview", path)
        else:
            frames_out.write(canvas.convert("RGB").tobytes())
        if frame % 60 == 0:
            print(f"  frame {frame}/{n_frames} ({sc['name']})", flush=True)
    return total, events


def mux_audio(video, events, total, output):
    sounds = sorted({e[1] for e in events})
    inputs = ["-i", str(video), "-i", str(AUDIO / "music_pvp.ogg")]
    for snd in sounds:
        inputs += ["-i", str(AUDIO / f"{snd}.ogg")]
    parts = [f"[1:a]atrim=0:{total:.2f},afade=t=in:d=0.3,afade=t=out:st={max(0, total - 1.4):.2f}:d=1.4,volume=0.55[m]"]
    labels = ["[m]"]
    counts = {}
    for snd in sounds:
        counts[snd] = sum(1 for e in events if e[1] == snd)
    splits = {}
    for i, snd in enumerate(sounds):
        n = counts[snd]
        outs = "".join(f"[{snd}{j}]" for j in range(n))
        parts.append(f"[{i + 2}:a]asplit={n}{outs}" if n > 1 else f"[{i + 2}:a]anull[{snd}0]")
        splits[snd] = 0
    for (t, snd, vol) in events:
        j = splits[snd]
        splits[snd] += 1
        ms = int(t * 1000)
        parts.append(f"[{snd}{j}]adelay={ms}|{ms},volume={vol}[e{len(labels)}]")
        labels.append(f"[e{len(labels)}]")
    parts.append("".join(labels) + f"amix=inputs={len(labels)}:normalize=0:duration=first,alimiter=limit=0.95[a]")
    cmd = ["ffmpeg", "-v", "error", "-y", *inputs, "-filter_complex", ";".join(parts), "-map", "0:v", "-map", "[a]",
           "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest", str(output)]
    subprocess.run(cmd, check=True)


def main():
    global A
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", default="fr", choices=sorted(TEXT))
    ap.add_argument("--recording", default=str(OUT / "promo_match.json"))
    ap.add_argument("--preview", default=None, help="comma-separated seconds: write PNG frames only")
    args = ap.parse_args()

    OUT.mkdir(parents=True, exist_ok=True)
    A = Assets()
    rec = json.loads(Path(args.recording).read_text(encoding="utf-8"))
    scenes, portraits, T = build_timeline(rec, args.lang)

    if args.preview:
        render(scenes, portraits, T, None, [float(v) for v in args.preview.split(",")])
        return 0

    silent = OUT / f"_silent_{args.lang}.mp4"
    ff = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
                           "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", str(silent)], stdin=subprocess.PIPE)
    total, events = render(scenes, portraits, T, ff.stdin)
    ff.stdin.close()
    if ff.wait() != 0:
        raise SystemExit("ffmpeg video encoding failed")

    vertical = OUT / f"crushroyale_promo_{args.lang}_9x16.mp4"
    mux_audio(silent, events, total, vertical)
    silent.unlink(missing_ok=True)

    wide = OUT / f"crushroyale_promo_{args.lang}_16x9.mp4"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(vertical), "-filter_complex",
                    "[0:v]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,boxblur=28:2,eq=brightness=-0.12[bg];"
                    "[0:v]scale=-2:1080[fg];[bg][fg]overlay=(W-w)/2:0,format=yuv420p[v]",
                    "-map", "[v]", "-map", "0:a", "-c:v", "libx264", "-crf", "19", "-preset", "medium", "-c:a", "copy", str(wide)], check=True)
    print(f"done: {vertical.name} and {wide.name} ({total:.1f} s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
