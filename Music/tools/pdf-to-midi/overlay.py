"""Draws each line of the score with the reading written over it, to check against the page.

Above the upper staff and below the lower one, at each chord's place: "start/length" in twelfths of a quarter note (a
quarter is 12, an eighth 6, a sixteenth 3, a triplet eighth 4) with the key speed after a v, then the note names from
the top down. ~ marks a tie, an arrow a note moved by an octave line, > an accent, ^ a fermata. Red is stems up, blue
stems down, black a whole note; green marks the bars with the page's own numbers.

usage: python -I overlay.py score.pdf reading.json folder [zoom]
"""
import sys, os, json, collections, site
from fractions import Fraction as F
site.addsitedir(site.getusersitepackages())
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fitz
from PIL import Image, ImageDraw, ImageFont

BAND = 108


def font(size):
    for name in (r"C:\Windows\Fonts\arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf"):
        if os.path.exists(name):
            return ImageFont.truetype(name, size)
    return ImageFont.load_default()


def draw(pdf, flat, out, zoom=3):
    os.makedirs(out, exist_ok=True)
    doc = fitz.open(pdf)
    big, small = font(12), font(11)
    u = lambda s: str(F(s) * 12)
    lines = collections.OrderedDict()
    for b in flat["bars"]:
        lines.setdefault((b["page"], b["line"]), []).append(b)
    by_bar = collections.defaultdict(list)
    for n in flat["notes"]:
        by_bar[n["bar"]].append(n)
    names = []
    for (page, line), bars in lines.items():
        x0, x1 = min(b["x0"] for b in bars) - 16, max(b["x1"] for b in bars) + 4
        clip = fitz.Rect(x0, bars[0]["top"] - 34, x1, bars[0]["bottom"] + 30)
        pix = doc[page - 1].get_pixmap(matrix=fitz.Matrix(zoom, zoom), clip=clip, alpha=False)
        crop = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
        img = Image.new("RGB", (crop.width, crop.height + 2 * BAND), "white")
        img.paste(crop, (0, BAND))
        d = ImageDraw.Draw(img)
        for b in bars:
            bx = (b["x0"] - x0) * zoom
            d.text((bx + 3, 2), f"bar {b['number']}  {b['time'][0]}/{b['time'][1]}" + ("  mended" if b["mended"] else ""), fill=(0, 110, 0), font=big)
            d.line((bx, 0, bx, BAND - 2), fill=(170, 220, 170))
            d.line((bx, img.height - BAND + 2, bx, img.height), fill=(170, 220, 170))
            for staff in "UL":
                last_vel = None
                chords = collections.OrderedDict()
                for n in by_bar[b["index"]]:
                    if n["staff"] == staff and not n.get("grace"):
                        chords.setdefault((n["onset"], n["dur"], n["stem"]), []).append(n)
                for (onset, dur, stem), ns in chords.items():
                    ns = sorted(ns, key=lambda n: -n["midi"])
                    cx = (min(n["x"] for n in ns) - x0) * zoom
                    marks = (">" if ns[0]["accent"] else "") + ("^" if ns[0]["fermata"] else "")
                    vel = "" if ns[0]["vel"] == last_vel else f" v{ns[0]['vel']}"         # the key speed, where it changes
                    last_vel = ns[0]["vel"]
                    rows = [f"{u(onset)}/{u(dur)}{marks}{vel}"] +[("~" if n["tie_in"] else "") + n["name"] + ("~" if n["tie_out"] else "") +
                                                                           ("\u2191" if n["shift"] > 0 else "\u2193" if n["shift"] < 0 else "") for n in ns]
                    colour = {"up": (255, 0, 0), "down": (0, 0, 220)}.get(stem, (0, 0, 0))
                    y = 16 if staff == "U" else img.height - BAND + 4
                    if any(k != (onset, dur, stem) and k[0] == onset and (k[2], stem) in (("up", "down"), ("none", "up"), ("none", "down")) for k in chords):
                        y += 13 * (1 + len(ns))            # a second voice in the same column goes underneath the first
                    for i, text in enumerate(rows[:7]):
                        d.text((cx, y + i * 13), text, fill=colour if i else (90, 90, 90), font=small if i else small)
                for n in by_bar[b["index"]]:
                    if n["staff"] == staff and n.get("grace"):
                        d.text(((n["x"] - x0) * zoom - 14, 16 if staff == "U" else img.height - BAND + 4), f"({n['name']})", fill=(160, 0, 160), font=small)
        name = f"p{page}-line{line}-bars-{bars[0]['number']}-{bars[-1]['number']}.png"
        img.save(os.path.join(out, name))
        names.append(name)
    return names


if __name__ == "__main__":
    for name in draw(sys.argv[1], json.load(open(sys.argv[2])), sys.argv[3], float(sys.argv[4]) if len(sys.argv) > 4 else 3):
        print(name)
