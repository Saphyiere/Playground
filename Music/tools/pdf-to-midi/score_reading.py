"""Reads a score's shapes into bars of notes: exact pitches, lengths and start times, with every doubt written down.

Pitch is geometry: a notehead's height on its staff, under the clef, accidentals and octave lines in force. Length is the
head's kind, its beams or flag, its dot and any triplet mark. Start times come from the print's own layout: things printed
in one column start together, and the next column starts when the soonest thing still sounding ends. The reading then has to
prove itself: every bar must add up to its time signature in both staves, the two staves must agree on when each column
sounds, every notehead must hang on a stem (or be a whole note), and each head must have the ledger lines its place needs.
Whatever fails is a doubt, listed for a person to look at; nothing is guessed silently.
"""
import sys, os, re, collections
from fractions import Fraction as F
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score_shapes as S

COLUMN = 3.4                        # things on one staff closer than this across are one column: they start together (the print
                                    # nudges some notes half a notehead, 2.8 points, to make room for a tie or a neighbour)
SAME = 2.2                          # across the two staves, closer than this is surely one moment; up to COLUMN it may be a nudge
                                    # or two moments a twelfth of a beat apart (a triplet against a sixteenth)
REST_LENGTH = {S.REST_QUARTER: F(1), S.REST_EIGHTH: F(1, 2), S.REST_16TH: F(1, 4), S.REST_HALF: F(2)}
ALTER = {S.SHARP: 1, S.FLAT: -1, S.NATURAL: 0}
CLEF_BASE = {"G": 30, "F": 18}      # the scale-step number (C4 = 28) of the staff's bottom line: E4 in treble, G2 in bass
LEVELS = {"ppp": 20, "pp": 33, "p": 49, "mp": 64, "mf": 80, "f": 96, "ff": 112, "fff": 124}
HAIRPIN_STEP = 16                   # a hairpin with no mark at its far end moves the level by one step, mp to mf
QUOTES = chr(0x201C) + chr(0x201D) + '"'


def units(q):
    """A length in twelfths of a quarter note, as a whole number when it is one (a sixteenth is 3, a triplet eighth 4)."""
    u = F(q) * 12
    return str(u.numerator) if u.denominator == 1 else f"{u.numerator}/{u.denominator}"


def beam_y(b, x):
    return b["y0"] + (b["y1"] - b["y0"]) * ((x - b["x0"]) / max(b["x1"] - b["x0"], 0.01))


def wanted_ledgers(pos):
    return (pos - 8) // 2 if pos >= 10 else (-pos) // 2 if pos <= -2 else 0


def read_system(shapes, system, zone, where, doubt):
    top, bottom = zone
    inzone = lambda y: top <= y < bottom
    glyphs = [g for g in shapes["glyphs"] if inzone(g["y"])]
    lines = [l for l in shapes["lines"] if inzone((l["y0"] + l["y1"]) / 2)]
    beams = [b for b in shapes["beams"] if inzone((b["y0"] + b["y1"]) / 2)]
    curves = [c for c in shapes["curves"] if inzone((c["y0"] + c["y1"]) / 2)]
    texts = [t for t in shapes["texts"] if inzone(t["y"])]
    staves = {"U": system["upper"], "L": system["lower"]}
    staff_of = lambda y: S.nearest_staff(system, y)
    for code in sorted({g["code"] for g in glyphs} - S.KNOWN):
        doubt(f"{where}: a shape of the music font this reader doesn't know (U+{code:04X}); it was left out")

    def pos_in(staff, y):
        p = S.staff_pos(staves[staff], y)
        if abs(p - round(p)) > 0.25:
            doubt(f"{where}: a glyph at y={y:.1f} sits between positions on the {'upper' if staff == 'U' else 'lower'} staff ({p:.2f})")
        return round(p)

    ledgers = [l for l in lines if l["o"] == "H" and 0.6 < l["w"] < 0.7 and l["len"] < 22 and not l["dashed"]]

    def ledger_count(staff, x, y):
        """Ledger lines drawn through or beside this notehead between it and the staff."""
        st = staves[staff]
        lo, hi = (y - 1, st["top"] - 1) if y < st["top"] else (st["bottom"] + 1, y + 1)
        return len({round(l["y0"] * 2) for l in ledgers if l["x0"] - 1 <= x + 2 <= l["x1"] and lo <= l["y0"] <= hi})    # two voices draw theirs twice

    def staff_for(heads):
        """The staff a chord is on: the nearer one, unless the ledger lines drawn say it hangs from the other."""
        fits = {s: sum(1 for h in heads if wanted_ledgers(round(S.staff_pos(staves[s], h["y"]))) == ledger_count(s, h["x"], h["y"])) for s in "UL"}
        nearest = min(heads, key=lambda h: min(0 if st["top"] <= h["y"] <= st["bottom"] else min(abs(h["y"] - st["top"]), abs(h["y"] - st["bottom"])) for st in staves.values()))
        near = staff_of(nearest["y"])
        other = "L" if near == "U" else "U"
        if fits[near] == len(heads):
            return near
        if fits[other] == len(heads):
            return other
        doubt(f"{where}: the noteheads at x={heads[0]['x']:.1f} don't have the ledger lines either staff would give them ({fits['U']} of {len(heads)} fit the upper, {fits['L']} the lower)")
        return near

    # ---- chords: the noteheads on each stem, and whole notes, which have none ---------------------------------
    heads = [g for g in glyphs if g["code"] in (S.HEAD_FILLED, S.HEAD_OPEN) and g["size"] > 19]
    wholes = sorted((g for g in glyphs if g["code"] == S.HEAD_WHOLE and g["size"] > 19), key=lambda g: (staff_of(g["y"]), g["x"]))
    dots = [g for g in glyphs if g["code"] == S.DOT]
    used_dots = set()
    stems = sorted((l for l in lines if l["o"] == "V" and 0.3 < l["w"] < 0.45 and l["len"] > 4.5), key=lambda l: l["x0"])
    hooks = [l for l in lines if l["o"] == "V" and 0.3 < l["w"] < 0.45 and l["len"] <= 4.5]
    owner = {}
    for si, s in enumerate(stems):
        for hi, h in enumerate(heads):
            if not (s["y0"] - 1.6 <= h["y"] <= s["y1"] + 1.6):
                continue
            dx = s["x0"] - h["x"]
            side = "left" if abs(dx - (5.4 if h["code"] == S.HEAD_FILLED else 5.8)) < 0.6 else "right" if abs(dx - 0.2) < 0.6 else None
            if side:
                owner.setdefault(hi, []).append((si, side))
    chords = {}
    for hi, h in enumerate(heads):
        cands = owner.get(hi, [])
        if not cands:
            doubt(f"{where}: a notehead at x={h['x']:.1f} y={h['y']:.1f} has no stem; it was left out")
            continue
        if len(cands) > 1:      # two voices share the column: the stem it sits at the end of
            cands = sorted(cands, key=lambda c: min(abs(h["y"] - stems[c[0]]["y0"]), abs(h["y"] - stems[c[0]]["y1"])))
        chords.setdefault(cands[0][0], []).append(h)

    def dotted(hs, width):
        """One and a half times as long for a dot after the chord, one and three quarters for two."""
        right = max(h["x"] for h in hs)
        found = [i for i, d in enumerate(dots) if 5.5 <= d["x"] - right - (width - 5.6) <= 10.5 and any(abs(d["y"] - h["y"]) <= 2.6 for h in hs)]
        used_dots.update(found)
        xs = sorted({round(dots[i]["x"]) for i in found})
        return F(7, 4) if len(xs) > 1 and xs[-1] - xs[0] >= 2 else F(3, 2) if found else F(1)

    events = []
    for si, hs in chords.items():
        s = stems[si]
        ys = [h["y"] for h in hs]
        if abs(max(ys) - s["y1"]) < 1.7 and s["y0"] < min(ys) - 2:
            direction = "up"
        elif abs(min(ys) - s["y0"]) < 1.7 and s["y1"] > max(ys) + 2:
            direction = "down"
        else:
            direction = "up" if (s["y0"] + s["y1"]) / 2 < sum(ys) / len(ys) else "down"
            doubt(f"{where}: the stem at x={s['x0']:.1f} has no head at either end; took it as stem {direction}")
        nbeams = sum(1 for b in beams if b["x0"] - 0.7 <= s["x0"] <= b["x1"] + 0.7 and s["y0"] - 1.6 <= beam_y(b, s["x0"]) <= s["y1"] + 1.6)
        flag = any(abs(g["x"] - s["x0"]) < 0.5 and ((g["code"] == S.FLAG_UP and abs(g["y"] - (s["y0"] + 4.1)) < 1.2) or (g["code"] == S.FLAG_DOWN and abs(g["y"] - s["y1"]) < 1.2))
                   for g in glyphs if g["code"] in (S.FLAG_UP, S.FLAG_DOWN) and g["size"] > 19)
        is_open = any(h["code"] == S.HEAD_OPEN for h in hs)
        if is_open:
            written = F(2)
            if nbeams or flag or not all(h["code"] == S.HEAD_OPEN for h in hs):
                doubt(f"{where}: an open notehead with a beam, a flag or a filled head on its stem at x={s['x0']:.1f}")
        else:
            written = [F(1), F(1, 2), F(1, 4), F(1, 8), F(1, 16)][min(nbeams + (1 if flag else 0), 4)]
            if nbeams and flag:
                doubt(f"{where}: a stem with both a beam and a flag at x={s['x0']:.1f}")
        written *= dotted(hs, 6.0 if is_open else 5.6)
        events.append(dict(kind="chord", x=s["x0"] - (5.4 if direction == "up" else 0.2), stem_x=s["x0"], y0=min(s["y0"], min(ys)), y1=max(s["y1"], max(ys)), stem=direction,
                           staff=staff_for(hs), heads=sorted(hs, key=lambda h: -h["y"]), written=written, dur=written, tuplet=None))
    group = []
    for h in wholes + [None]:                           # whole notes side by side are one chord
        if group and (h is None or h["x"] - group[0]["x"] > 9 or staff_of(h["y"]) != staff_of(group[0]["y"])):
            ys = [g["y"] for g in group]
            written = F(4) * dotted(group, 7.5)
            events.append(dict(kind="chord", x=min(g["x"] for g in group), stem_x=None, y0=min(ys) - 3, y1=max(ys) + 3, stem="none", staff=staff_for(group),
                               heads=sorted(group, key=lambda g: -g["y"]), written=written, dur=written, tuplet=None))
            group = []
        if h is not None:
            group.append(h)
    for g in glyphs:
        if g["code"] in REST_LENGTH:
            found = [i for i, d in enumerate(dots) if 4 <= d["x"] - g["x"] <= 11 and abs(d["y"] - g["y"]) <= 5 and i not in used_dots]
            used_dots.update(found)
            written = REST_LENGTH[g["code"]] * (F(3, 2) if found else 1)
            events.append(dict(kind="rest", x=g["x"], y0=g["y"] - 6, y1=g["y"] + 6, staff=staff_of(g["y"]), written=written, dur=written, tuplet=None))
        elif g["code"] == S.REST_BAR:
            events.append(dict(kind="barrest", x=g["x"], y0=g["y"], y1=g["y"], staff=staff_of(g["y"]), written=None, dur=None, tuplet=None))
    for i, d in enumerate(dots):
        if i not in used_dots:
            doubt(f"{where}: a dot at x={d['x']:.1f} y={d['y']:.1f} belongs to no note I can find")

    # ---- triplets: a 3 over a bracket, or a 3 on a beam -------------------------------------------------------
    numerals = [t for t in texts if t["font"].startswith("Tinos,Italic") and 7.9 < t["size"] < 8.5 and t["text"].isdigit()]
    short_h = [l for l in lines if l["o"] == "H" and 0.3 < l["w"] < 0.45 and l["len"] < 60]
    by_beam = collections.defaultdict(list)
    for t in numerals:
        if t["text"] != "3":
            doubt(f"{where}: a tuplet marked {t['text']} at x={t['x']:.1f}; this reader only knows triplets")
            continue
        gx0, gx1 = t["bbox"][0], t["bbox"][2]
        cx, cy = (gx0 + gx1) / 2, t["y"] - 2.7
        left = [l for l in short_h if abs(l["y0"] - cy) < 3 and -5.5 < l["x1"] - gx0 < 1.5]
        right = [l for l in short_h if abs(l["y0"] - cy) < 3 and -1.5 < l["x0"] - gx1 < 5.5]
        if left and right:
            x0, x1, by = left[0]["x0"], right[0]["x1"], left[0]["y0"]
            hook = [h for h in hooks if (abs(h["x0"] - x0) < 0.6 or abs(h["x0"] - x1) < 0.6) and (abs(h["y0"] - by) < 0.6 or abs(h["y1"] - by) < 0.6)]
            if not hook:
                doubt(f"{where}: a triplet bracket at x={x0:.1f}-{x1:.1f} has no hooks")
                continue
            below = abs(hook[0]["y0"] - by) < 0.6          # hooks point down: its notes are below it
            cands = [e for e in events if e["kind"] != "barrest" and x0 - 2.5 <= e["x"] <= x1 + 0.5 and ((below and e["y1"] > by) or (not below and e["y0"] < by))]
            if not cands:
                doubt(f"{where}: the triplet bracket at x={x0:.1f}-{x1:.1f} covers nothing")
                continue
            staff = min(cands, key=lambda e: (e["y0"] - by) if below else (by - e["y1"]))["staff"]
            cands = sorted((e for e in cands if e["staff"] == staff), key=lambda e: e["x"])
            if any(b["x"] - a["x"] < COLUMN for a, b in zip(cands, cands[1:])):
                # two things in one column are two voices: the bracket is the voice's whose stems reach toward it
                toward = "up" if below else "down"
                cands = [e for e in cands if e["kind"] == "rest" or e["stem"] == toward]
            for e in cands:
                if e["tuplet"]:
                    doubt(f"{where}: an event at x={e['x']:.1f} sits under two triplet marks")
                e["tuplet"] = "bracket"
        elif left or right:
            doubt(f"{where}: a 3 at x={cx:.1f} has half a bracket")
        else:
            near = [(abs(beam_y(b, cx) - cy), i) for i, b in enumerate(beams) if b["x0"] - 2 <= cx <= b["x1"] + 2]
            if not near or min(near)[0] > 16:
                doubt(f"{where}: a 3 at x={cx:.1f} has neither bracket nor beam")
                continue
            by_beam[min(near)[1]].append(cx)

    def on_beam(b):
        return [e for e in events if e["kind"] == "chord" and e["stem_x"] is not None and b["x0"] - 0.7 <= e["stem_x"] <= b["x1"] + 0.7 and e["y0"] - 1.6 <= beam_y(b, e["stem_x"]) <= e["y1"] + 1.6]

    for bi in by_beam:                                  # a 3 on a beam: the stems that beam joins
        for e in on_beam(beams[bi]):
            if e["tuplet"]:
                doubt(f"{where}: an event at x={e['x']:.1f} sits under two triplet marks")
            e["tuplet"] = "beam"
    for e in events:
        if e["tuplet"]:
            e["dur"] = e["written"] * F(2, 3)
    groups = []                                         # beamed groups, for mending a triplet the print forgot to mark
    for b in beams:
        members = on_beam(b)
        if len(members) >= 2 and abs((b["x1"] - b["x0"]) - (max(m["stem_x"] for m in members) - min(m["stem_x"] for m in members))) < 1.5:
            groups.append(members)

    # ---- clefs, accidentals, time signatures, octave lines -----------------------------------------------------
    clefs = collections.defaultdict(list)
    for g in glyphs:
        if g["code"] in (S.CLEF_F, S.CLEF_G):
            clefs[staff_of(g["y"])].append((g["x"], "F" if g["code"] == S.CLEF_F else "G"))
    for k in clefs:
        clefs[k].sort()
    accidentals = [dict(x=g["x"], staff=staff_of(g["y"]), y=g["y"], alter=ALTER[g["code"]]) for g in glyphs if g["code"] in ALTER]
    times = []
    figures = sorted((g for g in glyphs if g["code"] in S.DIGITS and g["size"] > 19 and system["upper"]["top"] - 2 <= g["y"] <= system["upper"]["bottom"] + 2), key=lambda g: g["x"])
    while figures:
        here = [g for g in figures if g["x"] - figures[0]["x"] < 12]
        figures = [g for g in figures if g not in here]
        num = [S.DIGITS[g["code"]] for g in here if abs(S.staff_pos(system["upper"], g["y"]) - 4) < 0.6]
        den = [S.DIGITS[g["code"]] for g in here if abs(S.staff_pos(system["upper"], g["y"])) < 0.6]
        if num and den:
            times.append((here[0]["x"], int("".join(map(str, num))), int("".join(map(str, den)))))
        else:
            doubt(f"{where}: figures in the staff at x={here[0]['x']:.1f} don't read as a time signature")
    ottava = []
    labels = [t for t in texts if 8.3 < t["size"] < 9.1 and t["font"].startswith("Tinos,Italic")]
    used_labels = set()
    for l in lines:
        if not (l["dashed"] and l["o"] == "H"):
            continue
        mine = [i for i, t in enumerate(labels) if abs(t["y"] - l["y0"]) < 6 and t["bbox"][0] < l["x0"] + 2 and l["x0"] - t["bbox"][2] < 12]
        used_labels.update(mine)
        text = "".join(labels[i]["text"] for i in sorted(mine, key=lambda i: labels[i]["bbox"][0]))
        if "8" not in text:
            doubt(f"{where}: a dashed line at y={l['y0']:.1f} with no 8va or 8vb in front of it")
            continue
        hook = [h for h in lines if h["o"] == "V" and 0.5 < h["w"] < 0.7 and h["len"] < 5 and abs(h["x0"] - l["x1"]) < 0.8 and (abs(h["y0"] - l["y0"]) < 0.8 or abs(h["y1"] - l["y0"]) < 0.8)]
        if l["y0"] < system["upper"]["top"]:
            staff = "U"
        elif l["y0"] > system["lower"]["bottom"]:
            staff = "L"
        elif hook:
            staff = "L" if abs(hook[0]["y0"] - l["y0"]) < 0.8 else "U"       # the hook points at its notes
        else:
            staff = staff_of(l["y0"])
        ottava.append(dict(x0=min(labels[i]["bbox"][0] for i in mine) - 1, x1=l["x1"] + 1, staff=staff, shift=-7 if "vb" in text else 7,
                           short=l["len"] < 30, between=system["upper"]["bottom"] < l["y0"] < system["lower"]["top"]))
    for i, t in enumerate(labels):                      # an 8va or 8vb with no line: just the notes at its place
        if i not in used_labels and re.fullmatch(r"8v[ab]", t["text"]):
            staff = "U" if t["y"] < system["upper"]["top"] else "L" if t["y"] > system["lower"]["bottom"] else staff_of(t["y"])
            ottava.append(dict(x0=t["bbox"][0] - 6, x1=t["bbox"][2] + 4, staff=staff, shift=-7 if t["text"].endswith("b") else 7, short=True, between=False))

    # ---- marks: tempo, words, loud and soft, hairpins, accents, fermatas ---------------------------------------
    tempos = []
    for g in glyphs:
        if g["code"] == S.TEMPO_QUARTER:
            t = [t for t in texts if "=" in t["text"] and abs(t["y"] - g["y"]) < 4 and 0 < t["x"] - g["x"] < 40]
            m = re.search(r"=\s*(\d+)", t[0]["text"]) if t else None
            if m:
                lead = [w for w in texts if w["font"].startswith("Tinos,Italic") and abs(w["y"] - g["y"]) < 4 and 0 < g["x"] - w["bbox"][2] < 14]
                tempos.append((min([g["x"]] + [w["x"] for w in lead]), int(m.group(1))))     # words before the number start it: "piu lento,"
            else:
                doubt(f"{where}: a tempo's note at x={g['x']:.1f} with no number after it")
    words = [(t["x"], t["text"].lower()) for t in texts if t["font"].startswith("Tinos,Italic") and 9.5 < t["size"] < 10.8]
    for t in texts:
        if t["font"].startswith("Tinos") and t["size"] < 13 and re.search(r"^[123]\.$|D\.\s?[SC]\.|Coda|Fine|Segno", t["text"]):
            doubt(f"{where}: \"{t['text']}\": first and second endings and jumps (D.S., D.C., Coda, Fine) aren't known to this reader; its bars are read in printed order")
    dynamics = []
    for g in sorted((g for g in glyphs if g["code"] in S.DYNAMIC), key=lambda g: g["x"]):
        mark = next((d for d in dynamics if abs(d["y"] - g["y"]) < 1.5 and 0 < g["x"] - d["end"] < 9), None)
        if mark:
            mark["text"] += S.DYNAMIC[g["code"]]; mark["end"] = g["x"]
        else:
            dynamics.append(dict(x=g["x"], end=g["x"], y=g["y"], text=S.DYNAMIC[g["code"]]))
    slants = [l for l in lines if l["o"] == "S" and 0.5 < l["w"] < 0.7 and l["len"] > 15 and not l["dashed"]]
    hairpins = []
    for i, a in enumerate(slants):
        for b in slants[i + 1:]:
            if abs(a["x0"] - b["x0"]) < 0.6 and abs(a["x1"] - b["x1"]) < 0.6 and abs((a["y0"] + a["y1"]) - (b["y0"] + b["y1"])) < 12:
                gap0, gap1 = abs(a["y0"] - b["y0"]), abs(a["y1"] - b["y1"])
                hairpins.append(dict(x0=a["x0"], x1=a["x1"], louder=gap1 > gap0, opens=gap0 < 0.3, closes=gap1 < 0.3))
    return dict(events=events, clefs=clefs, accidentals=accidentals, ottava=ottava, curves=curves, glyphs=glyphs, staves=staves, pos_in=pos_in, staff_of=staff_of,
                groups=groups, times=times, tempos=tempos, words=words, dynamics=dynamics, hairpins=hairpins,
                grace=[g for g in glyphs if g["code"] == S.HEAD_FILLED and g["size"] < 14],
                accents=[g for g in glyphs if g["code"] == S.ACCENT], fermatas=[g for g in glyphs if g["code"] == S.FERMATA],
                rolled=[g for g in glyphs if g["code"] == S.ARPEGGIO])


def sweep(events, length):
    """Start times for one staff's events in one bar. Things in one column start together; the next column starts when
    the soonest-ending thing already sounding ends. Returns (what's wrong or None, where the last thing ends)."""
    for e in events:
        if e["kind"] == "barrest":
            e["onset"], e["dur"] = F(0), length
    seq = sorted((e for e in events if e["kind"] != "barrest"), key=lambda e: e["x"])
    if not seq:
        return None, (length if events else F(0))
    cols = [[seq[0]]]
    for e in seq[1:]:
        if e["x"] - cols[-1][0]["x"] < COLUMN:
            cols[-1].append(e)
        else:
            cols.append([e])
    ends, t = [], F(0)
    for k, col in enumerate(cols):
        if k:
            later = [x for x in ends if x > t]
            if not later:
                for e in seq:
                    e.setdefault("onset", t)
                return "a column with nothing ending before it", max(ends)
            t = min(later)
        for e in col:
            e["onset"] = t
            ends.append(t + e["dur"])
    if max(ends) > length:
        return f"runs past the bar line ({units(max(ends))} twelfths of a quarter in a bar of {units(length)})", max(ends)
    if max(ends) < length and any(e["kind"] == "chord" for e in seq):
        return f"stops short ({units(max(ends))} twelfths of a quarter in a bar of {units(length)})", max(ends)
    return None, max(ends)


def column_problems(bar):
    """Notes printed in one column sound together: both staves must agree on when each column is."""
    out = []
    cols = sorted((e["x"], e["onset"], staff) for staff in "UL" for e in bar["staves"][staff] if e["kind"] == "chord")
    for (xa, ta, sa), (xb, tb, sb) in zip(cols, cols[1:]):
        gap = xb - xa
        if gap < SAME and ta != tb:
            out.append(f"one column (x={xa:.1f}) but two times: {units(ta)} and {units(tb)} twelfths in")
        elif gap >= COLUMN and tb <= ta:
            out.append(f"further right but not later: x={xa:.1f} at {units(ta)}, x={xb:.1f} at {units(tb)}")
        elif SAME <= gap < COLUMN and tb < ta:      # in between, either is printed: a nudged note of the same moment, or three against four
            out.append(f"a little further right but earlier: x={xa:.1f} at {units(ta)}, x={xb:.1f} at {units(tb)}")
    return out


def mend_short(bar):
    """One staff adds up and the other stops short: a note printed too short (a flag that shouldn't be there) or a rest left
    out. If giving the missing time to one note makes the bar add up and both staves agree on every column, that note is
    the one, and the bar says so; if no note or more than one would do, the doubt stands."""
    for staff, other in (("U", "L"), ("L", "U")):
        wrong = bar["wrong"][staff]
        if not (wrong and "short" in wrong and bar["wrong"][other] is None and any(e["kind"] == "chord" for e in bar["staves"][other])):
            continue
        evs = bar["staves"][staff]
        missing = bar["length"] - bar["ends"][staff]
        fits = []
        for e in sorted((e for e in evs if e["kind"] != "barrest"), key=lambda e: e["x"]):
            e["dur"] += missing
            again, end = sweep(evs, bar["length"])
            if again is None and not column_problems(bar):
                fits.append(e)
            e["dur"] -= missing
        sweep(evs, bar["length"])
        if len(fits) == 1:
            e = fits[0]
            e["dur"] += missing
            _, bar["ends"][staff] = sweep(evs, bar["length"])
            bar["wrong"][staff] = None
            bar["staves"][staff] = sorted(evs, key=lambda e: (e["onset"], e["x"]))
            what = "rest" if e["kind"] == "rest" else "note"
            bar["mended"].append(f"the {'upper' if staff == 'U' else 'lower'} staff is printed {units(missing)} twelfths of a quarter short; its {what} at {units(e['onset'])} twelfths in "
                                 f"is spaced as longer than it is printed and was read as {units(e['dur'])} long, which is the only way both staves agree")


def read(path):
    """The whole score: {"bars": [...], "doubts": [...], "notes": [...]}; see the module's words for what each check means."""
    doc = S.open_score(path)
    doubts, bars, systems_read, pending, late = [], [], [], {}, []
    doubt = doubts.append
    later = lambda bar, text: late.append((bar, text))      # a doubt about a bar, worded once the bars have their numbers
    time_now = None
    info = dict(title=None, piece=None, copyright=[])
    for n, page in enumerate(doc, 1):
        shapes = S.page_shapes(page)
        for t in shapes["texts"]:               # the print's own title and copyright lines, to carry into the file made from it
            if t["font"].startswith("Arimo,Bold") and t["size"] > 14 and not info["title"]:
                info["title"] = t["text"]
            elif t["font"].startswith("Tinos,Bold") and 8.5 < t["size"] < 9.8 and t["text"][0] in QUOTES and not info["piece"]:
                info["piece"] = t["text"].strip(QUOTES)
            elif t["font"].startswith("Arimo") and t["size"] < 8 and t["y"] > page.rect.height * 0.85 and not bars:
                info["copyright"].append(t["text"])
        try:
            systems = S.find_systems(shapes)
        except ValueError as e:
            doubt(f"page {n}: {e}; the page was left out")
            continue
        printed = [t for t in shapes["texts"] if t["font"].startswith("Tinos,Italic") and t["size"] < 7.7 and t["text"].isdigit()]
        for si, (system, zone) in enumerate(zip(systems, S.zones(systems, page.rect.height))):
            where = f"page {n} line {si + 1}"
            bounds = system["bounds"]
            if len(bounds) < 2:
                doubt(f"{where}: a line of music with no bar lines; it was left out")
                continue
            sysdata = read_system(shapes, system, zone, where, doubt)
            number = [int(t["text"]) for t in printed if system["top"] - 26 < t["y"] < system["top"] + 2 and t["x"] < bounds[0]["x0"] + 25]
            dots = [g for g in sysdata["glyphs"] if g["code"] == S.REPEAT_DOTS]
            first = len(bars)
            taken = set()
            for bi in range(len(bounds) - 1):
                x0, x1 = bounds[bi]["x1"], bounds[bi + 1]["x0"]
                if bi == 0 and len(bounds) > 2 and not any(x0 < e["x"] < x1 for e in sysdata["events"]):
                    continue        # the clefs' and time signature's space before a repeat sign that opens the line: not a bar
                for k, (tx, num, den) in enumerate(sysdata["times"]):
                    if k not in taken and tx < x1:      # one printed after the line's last bar line announces the next line's
                        time_now = (num, den)
                        taken.add(k)
                if time_now is None:
                    doubt(f"{where}: no time signature before the first bar; took 4/4")
                    time_now = (4, 4)
                bar = dict(index=len(bars), page=n, line=si + 1, x0=x0, x1=x1, top=system["top"], bottom=system["bottom"], time=time_now,
                           length=F(time_now[0] * 4, time_now[1]), repeat_start=any(x0 < d["x"] < x0 + 8 for d in dots), repeat_end=any(x1 - 8 < d["x"] < x1 for d in dots),
                           final=bounds[bi + 1]["thick"] and not any(x1 - 8 < d["x"] < x1 + 8 for d in dots), double=bounds[bi + 1]["lines"] > 1,
                           staves={}, mended=[], ends={}, wrong={})
                for staff in "UL":
                    evs = [e for e in sysdata["events"] if e["staff"] == staff and x0 < e["x"] < x1]
                    acc = sorted((a for a in sysdata["accidentals"] if a["staff"] == staff and x0 < a["x"] < x1), key=lambda a: a["x"])
                    for e in evs:
                        if e["kind"] != "chord":
                            continue
                        e["notes"] = []
                        for h in e["heads"]:
                            before = [c for cx, c in sysdata["clefs"][staff] if cx < h["x"]]
                            if not before:
                                doubt(f"{where}: a note with no clef before it on its staff; took treble")
                            clef = before[-1] if before else "G"
                            pos = sysdata["pos_in"](staff, h["y"])
                            dia, alter = CLEF_BASE[clef] + pos, 0
                            for a in acc:
                                if a["x"] < h["x"] + 0.5 and sysdata["pos_in"](staff, a["y"]) == pos:
                                    alter = a["alter"]
                            shift = sum(o["shift"] for o in sysdata["ottava"] if o["staff"] == staff and o["x0"] <= h["x"] <= o["x1"])
                            e["notes"].append(dict(dia=dia + shift, alter=alter, written_dia=dia, clef=clef, x=h["x"], y=h["y"], staff=staff, bar=bar["index"], shift=shift,
                                                   tie_in=False, tie_out=False, event=e))
                    for a in acc:
                        place = sysdata["pos_in"](staff, a["y"])
                        if not any(0 < nt["x"] - a["x"] < 22 and nt["written_dia"] - CLEF_BASE[nt["clef"]] == place for e in evs if e["kind"] == "chord" for nt in e["notes"]):
                            later(bar, f"a sharp, flat or natural in the {'upper' if staff == 'U' else 'lower'} staff with no note after it on its line: a key signature, "
                                       "which this reader doesn't know, so notes it governs would come out wrong")
                    wrong, end = sweep(evs, bar["length"])
                    if wrong and "past" in wrong:
                        # three beamed eighths with no 3 over them, in a bar that overflows: a triplet the print forgot to mark
                        for group in sysdata["groups"]:
                            if len(group) == 3 and all(e in evs and not e["tuplet"] and e["written"] == F(1, 2) for e in group):
                                for e in group:
                                    e["tuplet"] = "unmarked"; e["dur"] = e["written"] * F(2, 3)
                                again, end2 = sweep(evs, bar["length"])
                                if again is None:
                                    bar["mended"].append(f"three beamed eighths in the {'upper' if staff == 'U' else 'lower'} staff read as a triplet though the page prints no 3")
                                    wrong, end = None, end2
                                    break
                                for e in group:
                                    e["tuplet"] = None; e["dur"] = e["written"]
                                sweep(evs, bar["length"])
                    bar["staves"][staff] = sorted(evs, key=lambda e: (e.get("onset", F(0)), e["x"]))
                    bar["ends"][staff], bar["wrong"][staff] = end, wrong
                mend_short(bar)
                bars.append(bar)
            systems_read.append(dict(first=first, count=len(bars) - first, printed=number[0] if number else None, where=where, data=sysdata, system=system))
            attach(sysdata, bars[first:], system, later)
            tie(sysdata, bars[first:], pending, later)
    while bars and not any(bars[-1]["staves"][s] for s in "UL"):      # staff left empty after the piece's last bar line
        bars.pop()
    for n in pending.values():
        if n["bar"] < len(bars):
            later(bars[n["bar"]], "a tie leaves the line's end and reaches nothing on the next")
    if not bars:
        return dict(bars=[], doubts=doubts + ["no bars of music found"], remarks=[], tempo=[], systems=[], title=None, copyright=None)
    number_bars(bars, systems_read, doubt)
    for bar, text in late:
        doubt(f"bar {bar['number']}: {text}" if "number" in bar else text)
    settle_lengths(bars, doubt)
    check_columns(bars, doubt)
    start = F(0)
    for bar in bars:
        bar["start"] = start
        start += bar["length"]
    tempo = tempo_marks(bars, systems_read, doubt)
    loudness(bars, systems_read, doubt)
    return dict(bars=bars, doubts=doubts, remarks=remarks_on(bars, systems_read), tempo=tempo, systems=systems_read, title=": ".join(t for t in (info["title"], info["piece"]) if t) or None,
                copyright=" ".join(info["copyright"]) or None)


def attach(sysdata, bars, system, later):
    """Accents, fermatas, rolled-chord lines and small notes go to the chord they sit by."""
    chords = [e for bar in bars for s in "UL" for e in bar["staves"][s] if e["kind"] == "chord"]
    for kind, glyphs, offset in (("accent", sysdata["accents"], 2.8), ("fermata", sysdata["fermatas"], 3.2)):
        for g in glyphs:
            near = [e for e in chords if abs(e["x"] + offset - g["x"]) < 6.5]
            if not near:
                later(next((b for b in bars if b["x0"] < g["x"] < b["x1"]), bars[0]), f"a{'n accent' if kind == 'accent' else ' fermata'} at x={g['x']:.1f} sits by no chord")
                continue
            best = min(near, key=lambda e: 0 if e["y0"] <= g["y"] <= e["y1"] else min(abs(g["y"] - e["y0"]), abs(g["y"] - e["y1"])))
            best[kind] = True
    for g in sysdata["rolled"]:
        near = [e for e in chords if 0 < e["x"] - g["x"] < 16]
        for e in near:
            e["rolled"] = True
    for g in sysdata["grace"]:
        staff = sysdata["staff_of"](g["y"])
        for bar in bars:
            if bar["x0"] < g["x"] < bar["x1"]:
                before = [c for cx, c in sysdata["clefs"][staff] if cx < g["x"]]
                dia = CLEF_BASE[before[-1] if before else "G"] + sysdata["pos_in"](staff, g["y"])
                after = [e for e in bar["staves"][staff] if e["kind"] == "chord" and e["x"] > g["x"]]
                if not after:
                    later(bar, "a small note with nothing after it")
                    continue
                min(after, key=lambda e: e["x"]).setdefault("grace", []).append(dict(dia=dia, alter=0, x=g["x"], y=g["y"]))


def tie(sysdata, bars, pending, later):
    """A thin crescent from one notehead to the next on the same line or space is a tie; between different ones, a slur."""
    notes = [n for bar in bars for s in "UL" for e in bar["staves"][s] if e["kind"] == "chord" for n in e["notes"]]
    if not notes:
        return
    first_x, last_x = min(n["x"] for n in notes), max(n["x"] for n in notes)
    bar_of = {b["index"]: b for b in bars}
    for c in sorted(sysdata["curves"], key=lambda c: c["x0"]):
        up = c["bow"] == "up"

        def near(x, y, starting):
            found = []
            for n in notes:
                dx = x - n["x"]
                if not (-1.5 <= dx <= 10.5 if starting else -5.5 <= dx <= 8):
                    continue
                dy = n["y"] - y if up else y - n["y"]      # the head is on the inside of the bow
                if -1.5 <= dy <= 6.5:
                    found.append((abs(dy - 3) + abs(dx - 3) * 0.2, id(n), n))
            return [n for _, _, n in sorted(found)]

        starts, ends = near(c["x0"], c["y0"], True), near(c["x1"], c["y1"], False)
        pair = next(((a, b) for a in starts for b in ends if a is not b and a["staff"] == b["staff"] and a["written_dia"] == b["written_dia"] and b["x"] > a["x"]), None)
        if pair:
            a, b = pair
            a["tie_out"] = True; b["tie_in"] = True
            b["alter"] = a["alter"]                       # a tied note keeps the sharp or flat it was struck with
        elif starts and not [b for b in ends if b["x"] > starts[0]["x"]] and c["x1"] > last_x + 4:
            starts[0]["tie_out"] = True
            pending[(starts[0]["staff"], starts[0]["written_dia"])] = starts[0]
        elif ends and not [a for a in starts if a["x"] < ends[0]["x"]] and c["x0"] < first_x:
            b = next((b for b in ends if (b["staff"], b["written_dia"]) in pending), None)
            if b is None:
                later(bar_of[ends[0]["bar"]], "a tie comes in from the line before, but none went out for that note")
            else:
                a = pending.pop((b["staff"], b["written_dia"]))
                b["tie_in"] = True; b["alter"] = a["alter"]
        elif starts and ends:                             # between different notes: a slur, which changes no sound
            for bar in bars:
                if bar["index"] == starts[0]["bar"]:
                    bar["slurs"] = bar.get("slurs", 0) + 1
        else:
            later(next((b for b in bars if b["x0"] - 12 < c["x0"] < b["x1"]), bars[0]), f"a curve at x={c['x0']:.1f}-{c['x1']:.1f} joins nothing I can find")


def number_bars(bars, systems, doubt):
    """Bars take the numbers the page prints at the start of each line; the first line's are counted back from the second's."""
    systems = [s for s in systems if s["first"] < len(bars)]
    for s in systems:
        s["count"] = min(s["count"], len(bars) - s["first"])
    anchor = next((s for s in systems if s["printed"] is not None), None)
    number = (anchor["printed"] - sum(s["count"] for s in systems if s["first"] < anchor["first"])) if anchor else 1
    for s in systems:
        if s["printed"] is not None and s["printed"] != number:
            doubt(f"{s['where']}: the page numbers its first bar {s['printed']}, but counting bar lines makes it {number}")
            number = s["printed"]
        for bar in bars[s["first"]:s["first"] + s["count"]]:
            bar["number"] = number
            number += 1


def settle_lengths(bars, doubt):
    """A first or last bar may be short (a pickup and its other half); every other bar must fill its time signature."""
    for i, bar in enumerate(bars):
        ends = [bar["ends"][s] for s in "UL" if any(e["kind"] == "chord" for e in bar["staves"][s])] or [bar["ends"][s] for s in "UL"]
        short = max(ends, default=F(0))
        edge = i == 0 or i == len(bars) - 1
        if edge and 0 < short < bar["length"] and not any(w and "past" in w for w in bar["wrong"].values()):
            bar["mended"].append(f"a short {'first' if i == 0 else 'last'} bar: {units(short)} twelfths of a quarter where the time signature has {units(bar['length'])}")
            bar["length"] = short
            for s in "UL":
                for e in bar["staves"][s]:
                    if e["kind"] == "barrest":
                        e["dur"] = short
                if bar["ends"][s] in (short, F(0)) or not any(e["kind"] == "chord" for e in bar["staves"][s]):
                    bar["wrong"][s] = None
        for s in "UL":
            if bar["wrong"][s]:
                doubt(f"bar {bar['number']} {'upper' if s == 'U' else 'lower'} staff: {bar['wrong'][s]}")
        if not any(bar["staves"][s] for s in "UL"):
            doubt(f"bar {bar['number']}: nothing in it at all")


def check_columns(bars, doubt):
    for bar in bars:
        for problem in column_problems(bar):
            doubt(f"bar {bar['number']}: {problem}")


def when(bar, x, exact=False):
    """How far into a bar the thing printed at x comes: the start of the first column at or after it."""
    cols = sorted({(e["x"], e["onset"]) for s in "UL" for e in bar["staves"][s] if e["kind"] != "barrest"})
    if exact:           # between columns, in proportion
        pts = cols + [(bar["x1"], bar["length"])]
        prev = (bar["x0"], F(0))
        for cx, t in pts:
            if x <= cx:
                return prev[1] + (t - prev[1]) * F(max(x - prev[0], 0) / max(cx - prev[0], 0.01)).limit_denominator(1000)
            prev = (cx, t)
        return bar["length"]
    for cx, t in cols:
        if cx >= x - 4:
            return t
    return bar["length"]


def bar_at(bars, system, x):
    inside = [b for b in bars[system["first"]:system["first"] + system["count"]] if b["x0"] - 2 <= x <= b["x1"]]
    return inside[0] if inside else None


def tempo_marks(bars, systems, doubt):
    """Tempo numbers, rit. and a tempo, and fermatas, each at its written time (in quarters from the start)."""
    out = []
    for s in systems:
        for x, bpm in s["data"]["tempos"]:
            bar = bar_at(bars, s, x)
            if bar is None:      # over staff with no bars of this piece: it stands for what follows
                later = [b for b in bars[s["first"]:s["first"] + s["count"]] if b["x0"] > x]
                out.append(dict(at=later[0]["start"] if later else bars[min(s["first"] + s["count"], len(bars) - 1)]["start"], kind="tempo", bpm=bpm))
            else:
                out.append(dict(at=bar["start"] + when(bar, x), kind="tempo", bpm=bpm))
        for x, text in s["data"]["words"]:
            bar = bar_at(bars, s, x)
            if bar is None:
                continue
            if re.match(r"(poco )?(rit|rall)", text):
                out.append(dict(at=bar["start"] + when(bar, x), kind="slower", to=0.875 if text.startswith("poco") else 0.75))
            elif text.startswith("accel"):
                out.append(dict(at=bar["start"] + when(bar, x), kind="slower", to=1.15))
            elif text.startswith("a tempo"):
                out.append(dict(at=bar["start"] + when(bar, x), kind="a tempo"))
    for bar in bars:
        held = [e for st in "UL" for e in bar["staves"][st] if e.get("fermata")]
        if held:                 # the pause is on the last of the bar's notes that carry one
            e = max(held, key=lambda e: e["onset"])
            out.append(dict(at=bar["start"] + e["onset"], kind="fermata", until=bar["start"] + e["onset"] + e["dur"]))
    return sorted(out, key=lambda t: (t["at"], t["kind"] != "tempo"))


def loudness(bars, systems, doubt):
    """How hard each key goes down, from the page's marks: pp to ff, hairpins, cresc. and dim., accents."""
    marks, ramps = [], []
    open_ramp = None
    for s in systems:
        for d in s["data"]["dynamics"]:
            bar = bar_at(bars, s, d["x"])
            if bar is None:
                continue
            if d["text"] not in LEVELS:
                doubt(f"bar {bar['number']}: a loudness mark \"{d['text']}\" this reader doesn't know")
                continue
            marks.append((bar["start"] + when(bar, d["x"] + 2), LEVELS[d["text"]]))
        for h in sorted(s["data"]["hairpins"], key=lambda h: h["x0"]):
            a, b = bar_at(bars, s, h["x0"] + 1), bar_at(bars, s, h["x1"] - 1)
            if a is None or b is None:
                continue
            t0, t1 = a["start"] + when(a, h["x0"], True), b["start"] + when(b, h["x1"], True)
            # a hairpin carried over a line break comes in two pieces: a crescendo's second piece doesn't start at a point,
            # and a diminuendo's first piece doesn't end at one
            carried = open_ramp is not None and open_ramp["louder"] == h["louder"] and ((h["louder"] and not h["opens"]) or (not h["louder"] and not open_ramp["closed"]))
            if carried:
                open_ramp["t1"] = t1; open_ramp["closed"] = h["closes"]
            else:
                open_ramp = dict(t0=t0, t1=t1, louder=h["louder"], closed=h["closes"])
                ramps.append(open_ramp)
        for x, text in s["data"]["words"]:
            bar = bar_at(bars, s, x)
            if bar is not None and re.match(r"(cresc|dim|decresc)", text):
                ramps.append(dict(t0=bar["start"] + when(bar, x), t1=None, louder=text.startswith("cresc")))
    marks.sort()
    ramps.sort(key=lambda r: r["t0"])
    for r in ramps:
        after = [m for m in marks if m[0] >= (r["t1"] if r["t1"] is not None else r["t0"]) - F(1, 2)]
        if r["t1"] is None:      # words, not a wedge: it runs to the next mark
            r["t1"] = after[0][0] if after else r["t0"] + 8
    events = sorted([(t, 0, "mark", v) for t, v in marks] + [(r["t0"], 1, "ramp", r) for r in ramps], key=lambda e: (e[0], e[1]))
    curve, level = [], LEVELS["mp"]       # pieces of (from, to, level at from, level at to); before any mark, mp
    for t, _, kind, v in events:
        if kind == "mark":
            level = v
            curve.append((t, None, v, v))
        else:
            ahead = next((m[1] for m in marks if v["t1"] - F(1, 2) <= m[0] <= v["t1"] + 3), None)
            step = HAIRPIN_STEP if v["louder"] else -HAIRPIN_STEP
            if ahead is None:                           # no mark where it ends: one step louder or softer
                target = max(20, min(124, level + step))
            elif (ahead > level) == v["louder"] and ahead != level:
                target = ahead                          # it leads to the mark at its end
            else:                                       # the mark at its end is no louder than where it began: the page means it
                target, level = ahead, max(20, min(124, ahead - step))     # arrives there, so it starts a step away
            curve.append((v["t0"], v["t1"], level, target))
            level = target

    def at(t):
        value = LEVELS["mp"]
        for t0, t1, v0, v1 in curve:
            if t < t0:
                break
            value = v1 if t1 is None or t >= t1 else v0 + (v1 - v0) * float((t - t0) / (t1 - t0))
        return value

    for bar in bars:
        for s in "UL":
            for e in bar["staves"][s]:
                if e["kind"] == "chord":
                    e["vel"] = max(1, min(127, round(at(bar["start"] + e["onset"])) + (14 if e.get("accent") else 0)))


def runs(numbers):
    """Bar numbers as a short list: 81-86, 90."""
    out = []
    for n in sorted(set(numbers)):
        if out and n == out[-1][1] + 1:
            out[-1][1] = n
        else:
            out.append([n, n])
    return ", ".join(str(a) if a == b else f"{a}-{b}" for a, b in out)


def remarks_on(bars, systems):
    """Things read as printed that a person may still want to look at or listen to; none of them is a doubt."""
    out = [f"bar {b['number']}: {m}" for b in bars for m in b["mended"]]
    between = collections.defaultdict(list)
    for s in systems:
        for o in s["data"]["ottava"]:
            bar = bar_at(bars, s, o["x0"] + 3)
            if o["between"] and bar is not None:
                between[(o["staff"], o["shift"])].append(bar["number"])
    for (staff, shift), numbers in between.items():
        out.append(f"bars {runs(numbers)}: an octave mark printed between the two staves; read as printed, the {'upper' if staff == 'U' else 'lower'} staff's notes under it "
                   f"an octave {'higher' if shift > 0 else 'lower'}. Worth checking by ear.")
    words = sorted({text for s in systems for x, text in s["data"]["words"] if not re.match(r"(poco )?(rit|rall)|accel|a tempo|cresc|dim|decresc", text)})
    if words:
        out.append("words on the page that change no sound here: " + ", ".join(words))
    for what, key in (("rolled chords are written as plain chords", "rolled"), ("small notes sound just before the beat", "grace")):
        numbers = [b["number"] for b in bars for st in "UL" for e in b["staves"][st] if e.get(key)]
        if numbers:
            out.append(f"{what} (bars {runs(numbers)})")
    slurs = [b["number"] for b in bars if b.get("slurs")]
    if slurs:
        out.append(f"curves between different notes were taken as slurs, which change no sound (bars {runs(slurs)})")
    return out


def describe(e):
    """One event on one line: start/length in twelfths of a quarter, names from the bottom up, ~ for ties."""
    t = {"bracket": "t", "beam": "t", "unmarked": "T", None: ""}[e["tuplet"]]
    if e["kind"] == "rest":
        return f"{units(e['onset'])}:r/{units(e['dur'])}{t}"
    if e["kind"] == "barrest":
        return "bar rest"
    names = "+".join(("~" if n["tie_in"] else "") + S.name_of(n["dia"], n["alter"]) + ("~" if n["tie_out"] else "") for n in e["notes"])
    g = "".join(f"({S.name_of(x['dia'], x['alter'])})" for x in e.get("grace", []))
    marks = (">" if e.get("accent") else "") + ("^" if e.get("fermata") else "")
    return f"{units(e['onset'])}:{g}{names}/{units(e['dur'])}{t}{marks}"


def flatten(reading):
    """The reading as plain data: bars, and every note with its bar, hand, start, length, key and marks."""
    bars = [dict(index=b["index"], number=b["number"], page=b["page"], line=b["line"], x0=round(b["x0"], 1), x1=round(b["x1"], 1), top=round(b["top"], 1), bottom=round(b["bottom"], 1),
                 time=list(b["time"]), length=str(b["length"]), start=str(b["start"]), repeat_start=b["repeat_start"], repeat_end=b["repeat_end"], final=b["final"],
                 mended=b["mended"], slurs=b.get("slurs", 0)) for b in reading["bars"]]
    notes = []
    for b in reading["bars"]:
        for s in "UL":
            for e in b["staves"][s]:
                if e["kind"] != "chord":
                    continue
                for g in e.get("grace", []):
                    notes.append(dict(bar=b["index"], number=b["number"], staff=s, onset=str(e["onset"]), dur="0", midi=S.midi_of(g["dia"], g["alter"]), name=S.name_of(g["dia"], g["alter"]),
                                      grace=True, vel=max(1, e["vel"] - 8), page=b["page"], x=round(g["x"], 1), y=round(g["y"], 1)))
                for n in e["notes"]:
                    notes.append(dict(bar=b["index"], number=b["number"], staff=s, onset=str(e["onset"]), dur=str(e["dur"]), midi=S.midi_of(n["dia"], n["alter"]), name=S.name_of(n["dia"], n["alter"]),
                                      tie_in=n["tie_in"], tie_out=n["tie_out"], vel=e["vel"], accent=bool(e.get("accent")), fermata=bool(e.get("fermata")), rolled=bool(e.get("rolled")),
                                      tuplet=e["tuplet"], stem=e["stem"], shift=n["shift"], page=b["page"], x=round(n["x"], 1), y=round(n["y"], 1)))
    tempo = [dict(t, at=str(t["at"]), **({"until": str(t["until"])} if "until" in t else {})) for t in reading["tempo"]]
    return dict(bars=bars, notes=notes, tempo=tempo, doubts=reading["doubts"], remarks=reading["remarks"], title=reading["title"], copyright=reading["copyright"])


def print_reading(reading, out=sys.stdout):
    line = None
    for bar in reading["bars"]:
        if (bar["page"], bar["line"]) != line:
            line = (bar["page"], bar["line"])
            print(f"\n=== page {bar['page']} line {bar['line']}", file=out)
        signs = f"{bar['time'][0]}/{bar['time'][1]} " + ("|: " if bar["repeat_start"] else "") + (":| " if bar["repeat_end"] else "") + ("END " if bar["final"] else "")
        for s in "UL":
            print(f"bar {bar['number']:3} {s} {'??' if bar['wrong'][s] else '  '} {signs if s == 'U' else ''}" + "  ".join(describe(e) for e in bar["staves"][s]), file=out)
        for m in bar["mended"]:
            print(f"        mended: {m}", file=out)
    print(f"\ntempo: " + "; ".join(f"{t['kind']} at {float(t['at']):.2f}" + (f" = {t['bpm']}" if "bpm" in t else "") for t in reading["tempo"]), file=out)
    print(f"\n{len(reading['doubts'])} doubts", file=out)
    for d in reading["doubts"]:
        print("  ?", d, file=out)
    if reading["remarks"]:
        print("\nread as printed, but worth a look:", file=out)
        for r in reading["remarks"]:
            print("  -", r, file=out)
