"""What a Musicnotes PDF draws: the glyphs of its music font and the paths around them, grouped into staves and systems.

A Musicnotes score is not a picture. Every notehead, rest, clef, accidental and flag is a glyph of the font "Doremi" at an
exact place, and every staff line, stem, beam, bar line, tie and bracket is a vector path. Reading those gives each note's
pitch from geometry (how high its head sits on its staff), with nothing recognised by eye.
"""
import sys, site
site.addsitedir(site.getusersitepackages())          # python -I leaves the user's packages out; PyMuPDF lives there
import fitz  # PyMuPDF

FONT = "Doremi"
# ---- the font's glyphs, by character code --------------------------------------------------------------------------
HEAD_FILLED, HEAD_OPEN, HEAD_WHOLE = 0xCF, 0xFA, 0x77
REST_QUARTER, REST_EIGHTH, REST_16TH, REST_HALF, REST_BAR = 0xCE, 0xAB, 0xEA, 0xEE, 0xB7
SHARP, FLAT, NATURAL = 0x23, 0x62, 0x6E
CLEF_F, CLEF_G = 0x46, 0x47
FLAG_UP, FLAG_DOWN = 0x6A, 0x4A
DOT, REPEAT_DOTS, ACCENT, TENUTO, FERMATA, ARPEGGIO, GRACE_SLASH, TEMPO_QUARTER = 0x2E, 0x3A, 0x3E, 0x5F, 0x3B, 0xD0, 0xC8, 0x71
BRACE = (0x7B, 0x7D)
DYNAMIC = {0x70: "p", 0x6D: "m", 0x66: "f"}
DIGITS = {0x30 + d: d for d in range(10)}             # a time signature's figures
KNOWN = {HEAD_FILLED, HEAD_OPEN, HEAD_WHOLE, REST_QUARTER, REST_EIGHTH, REST_16TH, REST_HALF, REST_BAR, SHARP, FLAT, NATURAL, CLEF_F, CLEF_G,
         FLAG_UP, FLAG_DOWN, DOT, REPEAT_DOTS, ACCENT, TENUTO, FERMATA, ARPEGGIO, GRACE_SLASH, TEMPO_QUARTER, *BRACE, *DYNAMIC, *DIGITS}
NAMES = {HEAD_FILLED: "notehead", HEAD_OPEN: "open notehead", HEAD_WHOLE: "whole note", REST_QUARTER: "quarter rest", REST_EIGHTH: "eighth rest",
         REST_16TH: "sixteenth rest", REST_HALF: "half rest", REST_BAR: "bar rest", SHARP: "sharp", FLAT: "flat", NATURAL: "natural", CLEF_F: "bass clef",
         CLEF_G: "treble clef", FLAG_UP: "flag up", FLAG_DOWN: "flag down", DOT: "dot", REPEAT_DOTS: "repeat dots", ACCENT: "accent", TENUTO: "tenuto",
         FERMATA: "fermata", ARPEGGIO: "rolled-chord line", GRACE_SLASH: "grace slash", TEMPO_QUARTER: "tempo's quarter note"}

STEP_NAMES = ["C", "D", "E", "F", "G", "A", "B"]
STEP_SEMITONES = [0, 2, 4, 5, 7, 9, 11]


def midi_of(dia, alter):
    """The MIDI key of a note given as a count of scale steps (C4 = 28) and a sharp or flat."""
    return 12 * (dia // 7 + 1) + STEP_SEMITONES[dia % 7] + alter


def name_of(dia, alter):
    return STEP_NAMES[dia % 7] + {0: "", 1: "#", -1: "b"}[alter] + str(dia // 7)


def page_shapes(page):
    """Everything one page draws, sorted into the few kinds a score is made of."""
    glyphs, texts = [], []
    for b in page.get_text("rawdict")["blocks"]:
        if b["type"] != 0:
            continue
        for l in b["lines"]:
            for s in l["spans"]:
                if s["font"].startswith(FONT):
                    for c in s["chars"]:
                        if ord(c["c"]) != 0x20:
                            glyphs.append(dict(code=ord(c["c"]), size=round(s["size"], 1), x=c["origin"][0], y=c["origin"][1], bbox=tuple(c["bbox"])))
                else:
                    text = "".join(c["c"] for c in s["chars"])
                    if text.strip():
                        boxes = [c["bbox"] for c in s["chars"] if c["c"].strip()]
                        texts.append(dict(text=text.strip(), font=s["font"], size=round(s["size"], 1), x=boxes[0][0], y=s["chars"][0]["origin"][1],
                                          bbox=(min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))))
    lines, beams, curves = [], [], []
    for index, d in enumerate(page.get_drawings()):
        items = d["items"]
        sig = "".join(it[0][0] for it in items)
        if d.get("type") == "s":
            dashed = d.get("dashes") not in (None, "[] 0")
            for it in items:
                if it[0] != "l":
                    continue
                x0, y0, x1, y1 = it[1].x, it[1].y, it[2].x, it[2].y
                if x0 > x1:
                    x0, y0, x1, y1 = x1, y1, x0, y0
                o = "H" if abs(y1 - y0) < 0.05 else "V" if abs(x1 - x0) < 0.05 else "S"
                if o == "V":
                    y0, y1 = min(y0, y1), max(y0, y1)
                lines.append(dict(x0=x0, y0=y0, x1=x1, y1=y1, w=round(d.get("width") or 0, 2), o=o, dashed=dashed, path=index,
                                  len=((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5))
        elif d.get("type") == "f":
            if sig == "llll":                          # a beam: a parallelogram two points high
                pts = [it[1] for it in items] + [items[-1][2]]
                xa, xb = min(p.x for p in pts), max(p.x for p in pts)
                ya = [p.y for p in pts if abs(p.x - xa) < 0.05]; yb = [p.y for p in pts if abs(p.x - xb) < 0.05]
                if ya and yb and max(ya) - min(ya) < 3.5:
                    beams.append(dict(x0=xa, x1=xb, y0=sum(ya) / len(ya), y1=sum(yb) / len(yb)))
            elif sig == "r":
                r = items[0][1]
                if 1.5 < r.height < 2.6 and r.width < 150:     # a level beam
                    beams.append(dict(x0=r.x0, x1=r.x1, y0=(r.y0 + r.y1) / 2, y1=(r.y0 + r.y1) / 2))
            elif "c" in sig and len(sig) <= 6:         # a tie or slur: a thin crescent
                pts = []
                for it in items:
                    pts += list(it[1:]) if it[0] == "c" else [it[1], it[2]]
                left, right = min(pts, key=lambda p: p.x), max(pts, key=lambda p: p.x)
                ys = [p.y for p in pts]
                ends = (left.y + right.y) / 2
                curves.append(dict(x0=left.x, y0=left.y, x1=right.x, y1=right.y, bow="down" if max(ys) - ends > ends - min(ys) else "up"))
    return dict(glyphs=glyphs, texts=texts, lines=lines, beams=beams, curves=curves)


def find_systems(shapes):
    """The page's systems, top to bottom: an upper and a lower staff of five lines each, and the bar lines joining them."""
    staff_lines = sorted((l for l in shapes["lines"] if l["o"] == "H" and not l["dashed"] and l["len"] > 250 and 0.5 < l["w"] < 0.7), key=lambda l: l["y0"])
    if not staff_lines:
        return []
    if len(staff_lines) % 10:
        raise ValueError(f"{len(staff_lines)} staff lines on a page don't make whole systems of two staves")
    staves = []
    for i in range(0, len(staff_lines), 5):
        five = staff_lines[i:i + 5]
        if not all(3.5 < five[k + 1]["y0"] - five[k]["y0"] < 5 for k in range(4)):
            raise ValueError("staff lines don't group in fives: " + str([round(l["y0"], 1) for l in five]))
        staves.append(dict(top=five[0]["y0"], bottom=five[4]["y0"], sp=(five[4]["y0"] - five[0]["y0"]) / 4,
                           x0=min(l["x0"] for l in five), x1=max(l["x1"] for l in five)))
    systems = []
    for k in range(0, len(staves), 2):
        up, low = staves[k], staves[k + 1]
        bar_lines = sorted((l for l in shapes["lines"] if l["o"] == "V" and l["w"] > 0.8 and abs(l["y0"] - up["top"]) < 3 and abs(l["y1"] - low["bottom"]) < 3), key=lambda l: l["x0"])
        bounds = []
        for l in bar_lines:                           # a double bar or a repeat sign is two lines close together: one boundary
            if bounds and l["x0"] - bounds[-1]["x1"] < 7:
                bounds[-1]["x1"] = l["x0"]; bounds[-1]["thick"] |= l["w"] > 1.5; bounds[-1]["lines"] += 1
            else:
                bounds.append(dict(x0=l["x0"], x1=l["x0"], thick=l["w"] > 1.5, lines=1))
        systems.append(dict(upper=up, lower=low, bounds=bounds, top=up["top"], bottom=low["bottom"]))
    return systems


def staff_pos(staff, y):
    """How many lines-and-spaces above the staff's bottom line: 0 is the bottom line, 8 the top."""
    return (staff["bottom"] - y) / (staff["sp"] / 2)


def nearest_staff(system, y):
    up, low = system["upper"], system["lower"]
    du = 0 if up["top"] <= y <= up["bottom"] else min(abs(y - up["top"]), abs(y - up["bottom"]))
    dl = 0 if low["top"] <= y <= low["bottom"] else min(abs(y - low["top"]), abs(y - low["bottom"]))
    return "U" if du <= dl else "L"


def open_score(path):
    return fitz.open(path)


def zones(systems, page_height):
    """Each system's band of the page: halfway to its neighbours, so a mark between two systems goes to the nearer."""
    out = []
    for i, s in enumerate(systems):
        top = 0 if i == 0 else (systems[i - 1]["bottom"] + s["top"]) / 2
        bottom = page_height if i == len(systems) - 1 else (s["bottom"] + systems[i + 1]["top"]) / 2
        out.append((top, bottom))
    return out
