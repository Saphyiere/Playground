"""Writes a reading as MusicXML, the score as it is printed, and reads such a file back to hold it against the reading.

A MIDI file holds only when each key goes down and up, so whatever reads it has to guess the writing back, and some of it
can't be guessed. This file carries the writing itself, from the same reading the MIDI is made from: each note's printed
value, dots, stem and beams, the voices on each staff, triplets, ties, accidentals where they're printed, clefs and clef
changes, 8va and 8vb lines (the notes at the pitch they sound, the line saying how they're printed), the pickup, time
signatures, repeat signs, double and final bar lines, the tempo, words, loud-and-soft marks and hairpins, accents, tenuto
lines, fermatas, rolled chords and small notes, and where each line and page of the print begins. The title, composer,
arranger and copyright line are the print's.
"""
import collections
import datetime
import itertools
import os
import sys
import zipfile
import xml.etree.ElementTree as ET
from fractions import Fraction as F

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score_reading as R  # noqa: E402
import score_shapes as S  # noqa: E402

DIVISIONS = 480                     # to the quarter note: whole numbers for sixteenths, thirty-seconds and triplets alike
TYPES = [(F(4), "whole"), (F(2), "half"), (F(1), "quarter"), (F(1, 2), "eighth"), (F(1, 4), "16th"), (F(1, 8), "32nd"), (F(1, 16), "64th")]
ACCIDENTALS = {1: "sharp", -1: "flat", 0: "natural", 2: "double-sharp", -2: "flat-flat"}
CLEFS = {"G": ("G", "2"), "F": ("F", "4")}
DYNAMICS = {"ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "fp", "sf", "sfz", "sfp", "fz", "rf", "rfz"}
NOTE = "Made from the purchaser's own copy of the score, for personal practice."
STEP = [0, 2, 4, 5, 7, 9, 11]


def ticks(q):
    t = F(q) * DIVISIONS
    if t.denominator != 1:
        raise ValueError(f"a length that isn't a whole number of divisions: {q} quarters")
    return int(t)


def value(written):
    """A printed length in quarters as MusicXML's note type and dots: 3/2 is a dotted quarter, 7/4 a double-dotted one."""
    for base, name in TYPES:
        for dots, factor in ((0, F(1)), (1, F(3, 2)), (2, F(7, 4))):
            if base * factor == F(written):
                return name, dots
    raise ValueError(f"no note value is {written} quarters long")


def sub(parent, tag, text=None, **attrs):
    e = ET.SubElement(parent, tag, {k.replace("_", "-"): str(v) for k, v in attrs.items()})
    if text is not None:
        e.text = str(text)
    return e


# ---- voices --------------------------------------------------------------------------------------------------------------

def overlaps(a, b):
    return a["onset"] < b["onset"] + b["dur"] and b["onset"] < a["onset"] + a["dur"]


def voices(events, staff_geometry):
    """One staff's events in one bar as voices. One voice where each thing starts as the one before ends; otherwise the
    stems up are one voice and the stems down another, and a rest or a whole note goes where it fits, the upper voice first
    for one printed above the staff's middle line."""
    seq = sorted(events, key=lambda e: (e["onset"], e["x"]))
    t = F(0)
    for e in seq:
        if e["onset"] != t:
            break
        t = e["onset"] + e["dur"]
    else:
        return [seq] if seq else []
    middle = (staff_geometry["top"] + staff_geometry["bottom"]) / 2
    lanes = [[e for e in seq if e["kind"] == "chord" and e["stem"] == "up"], [e for e in seq if e["kind"] == "chord" and e["stem"] == "down"]]
    for e in seq:
        if e["kind"] == "chord" and e["stem"] in ("up", "down"):
            continue
        high = (e["y0"] + e["y1"]) / 2 < middle
        for k in ((0, 1) if high else (1, 0)):
            if not any(overlaps(e, o) for o in lanes[k]):
                lanes[k].append(e)
                break
        else:
            lanes.append([e])
    out = []
    for lane in lanes:                  # anything that still overlaps its own voice goes to a voice of its own
        while lane:
            lane.sort(key=lambda e: (e["onset"], e["x"]))
            kept, moved, end = [], [], F(0)
            for e in lane:
                if e["onset"] >= end:
                    kept.append(e)
                    end = e["onset"] + e["dur"]
                else:
                    moved.append(e)
            out.append(kept)
            lane = moved
    return [lane for lane in out if lane]


# ---- where things go in a bar --------------------------------------------------------------------------------------------

def columns(bar):
    return sorted({(e["x"], e["onset"]) for s in "UL" for e in bar["staves"][s] if e["kind"] != "barrest"})


def when_before(bar, x):
    """The start of the last column printed at or before x: where a hairpin stops."""
    found = [t for cx, t in columns(bar) if cx <= x + 1]
    return found[-1] if found else F(0)


def placement(y, staves):
    """The staff a mark belongs to, and whether it's printed above or below it."""
    up, low = staves["U"], staves["L"]
    if y < up["top"]:
        return 1, "above"
    if y <= low["top"]:
        return 1, "below"
    return 2, "below"


def clefs_in(bar, data, staff, first_in_line):
    """The clefs printed in a bar on one staff, each with the time it takes effect: the start of the first thing printed after
    it, or the bar's end for one printed after its last (it's the next bar's). A line's own clef comes at its first bar's start."""
    out = []
    evs = bar["staves"][staff]
    for cx, clef in data["clefs"][staff]:
        if (first_in_line and cx <= bar["x0"]) or bar["x0"] < cx < bar["x1"]:
            later = [e["onset"] for e in evs if e["x"] > cx]
            out.append((min(later) if later else (F(0) if cx <= bar["x0"] else bar["length"]), clef))
    return sorted(out, key=lambda c: c[0])


def direction(placement_, staff, *types, sound=None):
    d = ET.Element("direction", {"placement": placement_})
    for build in types:
        build(sub(d, "direction-type"))
    sub(d, "staff", staff)
    if sound:
        sub(d, "sound", **sound)
    return d


def marks(reading):
    """Every direction of the score, by bar: dict(t, staff, el, order) at a time, or dict(before=event) / dict(after=event)."""
    bars = reading["bars"]
    out = collections.defaultdict(list)
    carry, wedges = None, itertools.count()             # a hairpin left open at a line's end, and each hairpin's number
    for s in reading["systems"]:
        data = s["data"]
        staves = data["staves"]
        mine = bars[s["first"]:s["first"] + s["count"]]
        if not mine:
            continue

        def at(x):
            bar = R.bar_at(bars, s, x)
            if bar is None:                 # printed over the clefs, before the line's first bar: it stands for what follows
                later = [b for b in mine if b["x0"] > x]
                bar = later[0] if later else mine[-1]
                return bar, F(0)
            return bar, R.when(bar, x)

        lead = set()
        for t in data["tempo_text"]:
            bar, when = at(t["x"])
            lead.update(round(x, 1) for x in t["lead"])
            types = []
            if t["words"]:
                types.append(lambda dt, w=t["words"]: sub(dt, "words", w, font_style="italic"))

            def metronome(dt, bpm=t["bpm"]):
                m = sub(dt, "metronome")
                sub(m, "beat-unit", "quarter")
                sub(m, "per-minute", bpm)
            types.append(metronome)
            out[bar["index"]].append(dict(t=when, staff=1, order=0, el=direction("above", 1, *types, sound={"tempo": t["bpm"]})))
        # a word printed straight after a loudness mark is part of it: "mp sub."
        follows = {id(w): d for w in data["words_raw"] for d in data["dynamics"] if 0 < w["x"] - d["end"] < 16 and abs(w["y"] - d["y"]) < 4}
        for w in data["words_raw"]:
            if round(w["x"], 1) in lead or id(w) in follows:
                continue
            bar, when = at(w["x"])
            staff, where = placement(w["y"], staves)
            out[bar["index"]].append(dict(t=when, staff=staff, order=1, el=direction(where, staff, lambda dt, text=w["text"]: sub(dt, "words", text, font_style="italic"))))
        for d in data["dynamics"]:
            bar, when = at(d["x"] + 2)
            staff, where = placement(d["y"], staves)

            def dynamic(dt, text=d["text"]):
                box = sub(dt, "dynamics")
                if text in DYNAMICS:
                    sub(box, text)
                else:
                    sub(box, "other-dynamics", text)
            types = [dynamic] + [lambda dt, text=w["text"]: sub(dt, "words", text, font_style="italic") for w in data["words_raw"] if follows.get(id(w)) is d]
            out[bar["index"]].append(dict(t=when, staff=staff, order=2, el=direction(where, staff, *types)))
        pins = sorted(data["hairpins"], key=lambda h: h["x0"])
        for k, h in enumerate(pins):
            # a hairpin starts at the note it begins over, and ends at the last note it reaches, or at the bar line it runs to
            a = R.bar_at(bars, s, h["x0"] + 1) or mine[0]
            start = when_before(a, h["x0"] + 3)
            b = R.bar_at(bars, s, h["x1"] - 1) or mine[-1]
            stop = b["length"] if h["x1"] > b["x1"] - 8 else when_before(b, h["x1"])
            if a["index"] > b["index"] or (a is b and stop <= start):
                later = [t for cx, t in columns(a) if t > start]
                b, stop = a, (later[0] if later else a["length"])
            staff, where = placement(h["y"], staves)
            # one carried over a line break is printed in two pieces: the first runs to the line's end without closing, the
            # second comes in at the next line's start already open (as the reader's loudness takes them): one hairpin
            carried = (k == 0 and carry is not None and carry["louder"] == h["louder"] and h["x0"] < mine[0]["x0"] + 60
                       and ((h["louder"] and not h["opens"]) or (not h["louder"] and not carry["closed"])))
            if carried:
                n = carry["n"]
                out[carry["bar"]].remove(carry["stop"])
            else:
                n = next(wedges) % 3 + 1
                out[a["index"]].append(dict(t=start, staff=staff, order=3, el=direction(where, staff, lambda dt, kind="crescendo" if h["louder"] else "diminuendo", n=n: sub(dt, "wedge", type=kind, number=n))))
            end = dict(t=stop, staff=staff, order=-1, el=direction(where, staff, lambda dt, n=n: sub(dt, "wedge", type="stop", number=n)))
            out[b["index"]].append(end)
            last = k == len(pins) - 1 and h["x1"] > mine[-1]["x1"] - 20 and not h["closes"]
            carry = dict(louder=h["louder"], closed=h["closes"], n=n, bar=b["index"], stop=end) if last else None
        if not pins:
            carry = None
    # octave lines: from the first note of a run under one to its last, on each staff
    for staff, number in (("U", 1), ("L", 2)):
        chords = sorted(((b["index"], e["onset"], e["x"], e) for b in bars for e in b["staves"][staff] if e["kind"] == "chord"), key=lambda c: c[:3])
        shift_of = [c[3]["notes"][0]["shift"] for c in chords]
        for i, (index, onset, x, e) in enumerate(chords):
            shift = shift_of[i]
            if not shift:
                continue
            where = "above" if shift > 0 else "below"
            if i == 0 or shift_of[i - 1] != shift:
                out[index].append(dict(before=e, el=direction(where, number, lambda dt, kind="down" if shift > 0 else "up": sub(dt, "octave-shift", type=kind, size=8, number=number))))
            if i == len(chords) - 1 or shift_of[i + 1] != shift:
                out[index].append(dict(after=e, el=direction(where, number, lambda dt: sub(dt, "octave-shift", type="stop", size=8, number=number))))
    return out


# ---- notes ---------------------------------------------------------------------------------------------------------------

def pitch(parent, dia, alter):
    p = sub(parent, "pitch")
    sub(p, "step", S.STEP_NAMES[dia % 7])
    if alter:
        sub(p, "alter", alter)
    sub(p, "octave", dia // 7)


def note_of(measure, e, staff, voice, tuplet_edge):
    """One event as MusicXML notes: a chord's small notes first, then its notes from the bottom up; a rest; a bar's rest."""
    if e["kind"] == "barrest":
        n = sub(measure, "note")
        sub(n, "rest", measure="yes")
        sub(n, "duration", ticks(e["dur"]))
        sub(n, "voice", voice)
        sub(n, "staff", staff)
        return
    kind, dots = value(e["written"])
    if e["kind"] == "rest":
        n = sub(measure, "note")
        sub(n, "rest")
        sub(n, "duration", ticks(e["dur"]))
        sub(n, "voice", voice)
        sub(n, "type", kind)
        for _ in range(dots):
            sub(n, "dot")
        triplet(n, e, tuplet_edge)
        sub(n, "staff", staff)
        tuplet_notation(n, e, tuplet_edge)
        return
    for g in e.get("grace", []):
        n = sub(measure, "note")
        sub(n, "grace", slash="yes" if g.get("slash") else "no")
        pitch(n, g["dia"], g["alter"])
        sub(n, "voice", voice)
        sub(n, "type", "eighth")
        sub(n, "stem", "up")
        sub(n, "staff", staff)
    for i, t in enumerate(sorted(e["notes"], key=lambda t: (t["dia"], t["alter"]))):
        n = sub(measure, "note")
        if i:
            sub(n, "chord")
        pitch(n, t["dia"], t["alter"])
        sub(n, "duration", ticks(e["dur"]))
        if t["tie_in"]:
            sub(n, "tie", type="stop")
        if t["tie_out"]:
            sub(n, "tie", type="start")
        sub(n, "voice", voice)
        sub(n, "type", kind)
        for _ in range(dots):
            sub(n, "dot")
        if t.get("accidental") is not None:
            sub(n, "accidental", ACCIDENTALS[t["accidental"]])
        triplet(n, e, tuplet_edge)
        if e["stem"] in ("up", "down"):
            sub(n, "stem", e["stem"])
        sub(n, "staff", staff)
        if not i:
            for level in sorted(e.get("beam", {})):
                sub(n, "beam", e["beam"][level], number=level)
        notations = ET.Element("notations")
        if t["tie_in"]:
            sub(notations, "tied", type="stop")
        if t["tie_out"]:
            tied = sub(notations, "tied", type="start")
            if t.get("tie_over") is not None:           # curved over the notes or under them, as printed
                tied.set("orientation", "over" if t["tie_over"] else "under")
                tied.set("placement", "above" if t["tie_over"] else "below")
        if not i:
            tuplet_notation(notations, e, tuplet_edge, inside=True)
            artics = [a for a in ("accent", "tenuto") if e.get(a)]
            if artics:
                box = sub(notations, "articulations")
                for a in artics:
                    sub(box, a)
            if e.get("fermata"):
                sub(notations, "fermata", type="upright")
        if e.get("rolled"):                     # one number to a printed line: chords on both staves under one line roll as one
            sub(notations, "arpeggiate", number=int(e["rolled"]) % 16 + 1)
        if len(notations):
            n.append(notations)


def triplet(n, e, edge):
    if e.get("tuplet"):
        tm = sub(n, "time-modification")
        sub(tm, "actual-notes", 3)
        sub(tm, "normal-notes", 2)


def tuplet_notation(parent, e, edge, inside=False):
    """The start or the end of a triplet group: with its bracket where the print draws one, its 3 on the beam otherwise."""
    kinds = edge.get(id(e), ())
    if not kinds:
        return
    box = parent if inside else sub(parent, "notations")
    for kind in kinds:
        if kind == "start":
            t = sub(box, "tuplet", type="start", bracket="yes" if e["tuplet"] == "bracket" else "no", show_number="actual")
            if e.get("tuplet_place"):
                t.set("placement", e["tuplet_place"])
        else:
            sub(box, "tuplet", type="stop")


# ---- the score -----------------------------------------------------------------------------------------------------------

def build(reading, title=None, composer=None, arranger=None):
    bars = reading["bars"]
    credits = reading.get("credits") or {}
    root = ET.Element("score-partwise", version="3.1")
    work = sub(root, "work")
    sub(work, "work-title", title or reading.get("title") or "Piano")
    ident = sub(root, "identification")
    if composer or credits.get("composer"):
        sub(ident, "creator", composer or credits["composer"], type="composer")
    if arranger or credits.get("arranger"):
        sub(ident, "creator", arranger or credits["arranger"], type="arranger")
    if reading.get("copyright"):
        sub(ident, "rights", reading["copyright"])
    enc = sub(ident, "encoding")
    sub(enc, "software", "Playground pdf-to-midi")
    sub(enc, "encoding-date", datetime.date.today().isoformat())
    misc = sub(ident, "miscellaneous")
    sub(misc, "miscellaneous-field", NOTE, name="note")
    parts = sub(root, "part-list")
    sp = sub(parts, "score-part", id="P1")
    sub(sp, "part-name", "Piano")
    part = sub(root, "part", id="P1")

    system_of = {}
    for s in reading["systems"]:
        for b in bars[s["first"]:s["first"] + s["count"]]:
            system_of[b["index"]] = s
    directions = marks(reading)
    clef_now = {}
    previous = None
    for bar in bars:
        s = system_of[bar["index"]]
        data = s["data"]
        first_in_line = previous is None or (previous["page"], previous["line"]) != (bar["page"], bar["line"])
        m = sub(part, "measure", number=bar["number"])
        if bar["index"] == 0 and bar["length"] < F(bar["time"][0] * 4, bar["time"][1]):
            m.set("implicit", "yes")
        if previous is not None and first_in_line:
            sub(m, "print", **({"new-page": "yes"} if previous["page"] != bar["page"] else {"new-system": "yes"}))
        if bar["repeat_start"]:
            bl = sub(m, "barline", location="left")
            sub(bl, "bar-style", "heavy-light")
            sub(bl, "repeat", direction="forward")
        changes = {staff: clefs_in(bar, data, staff, first_in_line) for staff in "UL"}
        attrs = ET.Element("attributes")
        if previous is None:
            sub(attrs, "divisions", DIVISIONS)
            key = sub(attrs, "key")
            sub(key, "fifths", 0)
        if previous is None or tuple(bar["time"]) != tuple(previous["time"]):
            t = sub(attrs, "time")
            sub(t, "beats", bar["time"][0])
            sub(t, "beat-type", bar["time"][1])
        if previous is None:
            sub(attrs, "staves", 2)
        inserts = []
        for number, staff in ((1, "U"), (2, "L")):
            for t, clef in changes[staff]:
                if clef_now.get(number) == clef:
                    continue
                clef_now[number] = clef
                if t == 0:
                    c = sub(attrs, "clef", number=number)
                else:
                    holder = ET.Element("attributes")
                    c = sub(holder, "clef", number=number)
                    inserts.append(dict(t=t, staff=number, order=-2, el=holder))
                sub(c, "sign", CLEFS[clef][0])
                sub(c, "line", CLEFS[clef][1])
            if number not in clef_now:              # no clef printed yet on this staff: treble above, bass below
                clef_now[number] = "G" if staff == "U" else "F"
                c = sub(attrs, "clef", number=number)
                sub(c, "sign", CLEFS[clef_now[number]][0])
                sub(c, "line", CLEFS[clef_now[number]][1])
        if len(attrs):
            m.append(attrs)
        fill(m, bar, data, inserts + directions.get(bar["index"], []))
        right = None
        if bar["repeat_end"]:
            right = sub(m, "barline", location="right")
            sub(right, "bar-style", "light-heavy")
            sub(right, "repeat", direction="backward")
        elif bar["final"]:
            right = sub(m, "barline", location="right")
            sub(right, "bar-style", "light-heavy")
        elif bar.get("double"):
            right = sub(m, "barline", location="right")
            sub(right, "bar-style", "light-light")
        previous = bar
    return root


def fill(m, bar, data, inserts):
    """A bar's notes: each staff's voices one after another, back to the bar's start between them, with every direction and
    clef change at its time in the voice that has something starting there."""
    length = bar["length"]
    streams = []
    for number, staff in ((1, "U"), (2, "L")):
        lanes = voices(bar["staves"][staff], data["staves"][staff]) or [[]]
        for k, lane in enumerate(lanes):
            items, t = [], F(0)
            for e in sorted(lane, key=lambda e: (e["onset"], e["x"])):
                if e["onset"] > t:
                    items.append((t, "gap", e["onset"] - t))
                items.append((e["onset"], "event", e))
                t = e["onset"] + e["dur"]
            if t < length:
                items.append((t, "gap", length - t))
            streams.append((number, (1 if number == 1 else 5) + k, items))
    before, after, timed = collections.defaultdict(list), collections.defaultdict(list), collections.defaultdict(list)
    for ins in inserts:
        if "before" in ins:
            before[id(ins["before"])].append(ins["el"])
        elif "after" in ins:
            after[id(ins["after"])].append(ins["el"])
        else:
            order = [i for i, st in enumerate(streams) if st[0] == ins["staff"]] + [i for i, st in enumerate(streams) if st[0] != ins["staff"]]
            home = next((i for i in order if any(start == ins["t"] for start, _, _ in streams[i][2])), None)
            t = ins["t"]
            if home is None and t >= length:
                home = order[0]
            elif home is None:          # nothing starts there: at the next thing that does
                home = order[0]
                t = min([start for start, _, _ in streams[home][2] if start >= ins["t"]] + [length])
            timed[home].append((t, ins["order"], ins["el"]))
    for i, (staff, voice, items) in enumerate(streams):
        if i:
            back = sub(m, "backup")
            sub(back, "duration", ticks(length))
        pending = sorted(timed[i], key=lambda p: (p[0], p[1]))
        edges = tuplet_edges([payload for _, kind, payload in items if kind == "event"])
        for start, kind, payload in items:
            while pending and pending[0][0] <= start:
                m.append(pending.pop(0)[2])
            if kind == "gap":
                fwd = sub(m, "forward")
                sub(fwd, "duration", ticks(payload))
                sub(fwd, "voice", voice)
                sub(fwd, "staff", staff)
                continue
            for el in before[id(payload)]:
                m.append(el)
            note_of(m, payload, staff, voice, edges)
            for el in after[id(payload)]:
                m.append(el)
        for _, _, el in pending:
            m.append(el)


def tuplet_edges(events):
    """The first and last event of each triplet group in a voice."""
    groups = collections.OrderedDict()
    for e in events:
        if e.get("tuplet_group"):
            groups.setdefault(e["tuplet_group"], []).append(e)
    edges = collections.defaultdict(list)
    for members in groups.values():
        edges[id(members[0])].append("start")
        edges[id(members[-1])].append("stop")
    return edges


def write(reading, path, title=None, composer=None, arranger=None):
    """Writes the score: zipped (.mxl, what Prima and most programs take) or as plain MusicXML text for any other name."""
    root = build(reading, title, composer, arranger)
    ET.indent(root, space=" ")
    text = '<?xml version="1.0" encoding="UTF-8"?>\n' + ET.tostring(root, encoding="unicode") + "\n"
    if path.lower().endswith(".mxl"):
        inner = "score.musicxml"
        container = ('<?xml version="1.0" encoding="UTF-8"?>\n<container><rootfiles>'
                     f'<rootfile full-path="{inner}" media-type="application/vnd.recordare.musicxml+xml"/></rootfiles></container>\n')
        with zipfile.ZipFile(path, "w") as box:
            box.writestr(zipfile.ZipInfo("mimetype"), "application/vnd.recordare.musicxml")
            box.writestr("META-INF/container.xml", container, compress_type=zipfile.ZIP_DEFLATED)
            box.writestr(inner, text, compress_type=zipfile.ZIP_DEFLATED)
    else:
        with open(path, "w", encoding="utf-8") as f:
            f.write(text)
    notes = sum(1 for _ in root.iter("note"))
    counts = collections.Counter(e.tag for e in root.iter() if e.tag in ("octave-shift", "wedge", "dynamics", "words", "metronome", "clef", "tuplet", "tied", "accidental", "beam"))
    return (f"MusicXML: {len(bars_of(root))} bars, {notes} notes and rests; {counts['clef']} clefs, {counts['octave-shift'] // 2} octave lines, "
            f"{counts['dynamics']} loudness marks, {counts['wedge'] // 2} hairpins, {counts['words']} words, {counts['tuplet'] // 2} triplets, "
            f"{counts['accidental']} accidentals as printed")


def bars_of(root):
    return root.find("part").findall("measure")


# ---- reading a file back -------------------------------------------------------------------------------------------------

def load(path):
    if path.lower().endswith(".mxl"):
        with zipfile.ZipFile(path) as box:
            container = ET.fromstring(box.read("META-INF/container.xml"))
            inner = next(e.get("full-path") for e in container.iter() if e.tag.endswith("rootfile"))
            return ET.fromstring(box.read(inner))
    return ET.parse(path).getroot()


def parse(path):
    """Every note of a MusicXML file as (bar, staff, start, length, key, tied in, tied out), starts and lengths in quarters,
    and what's wrong with its bars' voices (one that doesn't fill its bar, or runs past it)."""
    root = load(path)
    divisions, notes, problems = 1, [], []
    for index, m in enumerate(bars_of(root)):
        t, last_start, filled = 0, 0, collections.Counter()
        for el in m:
            if el.tag == "attributes" and el.find("divisions") is not None:
                divisions = int(el.find("divisions").text)
            elif el.tag == "backup":
                t -= int(el.find("duration").text)
            elif el.tag == "forward":
                d = int(el.find("duration").text)
                filled[(el.findtext("staff"), el.findtext("voice"))] += d
                t += d
            elif el.tag == "note":
                if el.find("grace") is not None:
                    continue
                d = int(el.find("duration").text)
                start = last_start if el.find("chord") is not None else t
                if el.find("chord") is None:
                    filled[(el.findtext("staff"), el.findtext("voice"))] += d
                    last_start, t = t, t + d
                p = el.find("pitch")
                if p is not None:
                    key = 12 * (int(p.findtext("octave")) + 1) + STEP[S.STEP_NAMES.index(p.findtext("step"))] + int(p.findtext("alter") or 0)
                    ties = {x.get("type") for x in el.findall("tie")}
                    notes.append((index, int(el.findtext("staff")), F(start, divisions), F(d, divisions), key, "stop" in ties, "start" in ties))
        if len(set(filled.values())) > 1:
            problems.append(f"bar {m.get('number')}: its voices last {sorted(set(F(v, divisions) for v in filled.values()))} quarters")
    return notes, problems


def check(path, flat):
    """Reads the file back and compares it with the reading, note by note. Returns what differs (nothing, if all is well)."""
    notes, problems = parse(path)
    got = collections.Counter(notes)
    wanted = collections.Counter((n["bar"], 1 if n["staff"] == "U" else 2, F(n["onset"]), F(n["dur"]), n["midi"], n["tie_in"], n["tie_out"])
                                 for n in flat["notes"] if not n.get("grace"))
    number = {b["index"]: b["number"] for b in flat["bars"]}
    for key in sorted(wanted - got):
        problems.append(f"bar {number[key[0]]}: key {key[4]} at {key[2]} for {key[3]} quarters is missing from the file")
    for key in sorted(got - wanted):
        problems.append(f"bar {number.get(key[0], key[0])}: key {key[4]} at {key[2]} for {key[3]} quarters is in the file but not on the page")
    if len(bars_of(load(path))) != len(flat["bars"]):
        problems.append(f"{len(bars_of(load(path)))} bars in the file, {len(flat['bars'])} on the page")
    return problems
