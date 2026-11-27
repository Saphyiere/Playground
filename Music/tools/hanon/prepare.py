"""Re-engrave RANOTE's licensed Hanon score data as numbered, offline MEI studies.

Usage: python Music/tools/hanon/prepare.py SOURCE_DIRECTORY OUTPUT_DIRECTORY
Inputs, credits and licences are documented beside the bundled scores.
"""

import collections
import gzip
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


MEI = "http://www.music-encoding.org/ns/mei"
ID = "{http://www.w3.org/XML/1998/namespace}id"
NATURAL = (0, 2, 4, 5, 7, 9, 11)
ACCIDENTAL = {-2: "ff", -1: "f", 0: "n", 1: "s", 2: "ss"}
DYNAMICS = {"E520": "p", "E522": "f", "E52A": "ppp", "E52B": "pp",
            "E52C": "mp", "E52D": "mf", "E52F": "ff", "E530": "fff"}
ET.register_namespace("", MEI)


def element(parent, tag, **attributes):
    return ET.SubElement(parent, f"{{{MEI}}}{tag}", {k: str(v) for k, v in attributes.items()})


def groups(node, kind):
    return (g for g in node.iter() if g.get("class") == kind)


def uses(node):
    return [g for g in node.iter() if g.get("href", "").startswith("#E")]


def position(glyph):
    return tuple(map(int, re.search(r"translate\((-?\d+),\s*(-?\d+)\)", glyph.get("transform")).groups()))


def signature(fifths):
    return str(abs(fifths)) + ("s" if fifths > 0 else "f" if fifths < 0 else "")


def brigham_fingers(path):
    # The separately licensed, fingered edition of Nos. 21–38. Match on the music itself.
    fingers = {}
    study = 20
    division = 1
    for bar in ET.parse(path).findall(".//part/measure"):
        number = int(bar.get("number"))
        if number == 1:
            study += 1
        division = int(bar.findtext("attributes/divisions", division))
        tick = previous = 0
        for item in bar:
            length = int(item.findtext("duration", 0)) * 480 // division
            if item.tag == "backup":
                tick -= length
            elif item.tag == "forward":
                tick += length
            elif item.tag == "note":
                start = tick if item.find("chord") is None else previous
                p = item.find("pitch")
                f = item.findtext("notations/technical/fingering", "")
                if p is not None and f in ("1", "2", "3", "4", "5"):
                    midi = 12 * (1 + int(p.findtext("octave"))) + NATURAL["CDEFGAB".index(p.findtext("step"))] + int(p.findtext("alter", 0))
                    hand = 1 if int(item.findtext("voice", 1)) < 5 else 2
                    fingers[study, number, hand, start, midi] = int(f)
                if item.find("chord") is None:
                    previous, tick = tick, tick + length
    return fingers


def study(number, source, out, extra_fingers):
    directory = source / f"{number:02}"
    data = json.loads((directory / "notes.json").read_bytes())
    printed = ET.fromstring("<pages>" + (directory / "score-1100.svg").read_text(encoding="utf-8") + "</pages>")
    measures = data["measures"]
    ids = list(dict.fromkeys(m["id"] for m in measures))
    index = {name: i for i, name in enumerate(ids)}
    order = [index[m["id"]] for m in measures]
    jumps = {(b, a) for a, b in zip(order, order[1:]) if b <= a}
    pages = {g.get("id"): g for g in groups(printed, "measure")}
    first = {}
    for i, bar in enumerate(order):
        first.setdefault(bar, i)
    lengths = [round(((measures[i + 1]["q"] if i + 1 < len(measures) else data["end"]) - measures[i]["q"]) * 480) for i in first.values()]
    sounded = {n["id"]: n for n in data["notes"] if n["m"] == first[order[n["m"]]]}
    root = ET.Element(f"{{{MEI}}}mei", meiversion="5.0")
    header = element(element(root, "meiHead"), "fileDesc")
    element(element(header, "titleStmt"), "title").text = f"Hanon No. {number}"
    element(header, "pubStmt")
    score = element(element(element(element(root, "music"), "body"), "mdiv"), "score")
    initial = element(score, "scoreDef", ppq=480)
    staff_group = element(initial, "staffGrp", symbol="brace")
    for staff, shape, line in [(1, "G", 2), (2, "F", 4)]:
        element(staff_group, "staffDef", n=staff, lines=5, **{"clef.shape": shape, "clef.line": line})
    section = element(score, "section")
    clefs = ["G", "F"]
    output_clefs = {}
    previous_key = previous_meter = None
    preview = []
    expected_order = []
    repeat_from = 0
    carry = collections.defaultdict(list)
    for b, name in enumerate(ids):
        drawing = pages[name]
        staffs = list(groups(drawing, "staff"))
        key = previous_key or 0
        sig = next(groups(staffs[0], "keySig"), None)
        if sig is not None:
            chars = [g.get("href")[1:5] for g in uses(sig)]
            key = chars.count("E262") - chars.count("E260")
        meter = previous_meter or (4, 4)
        printed_meter = next(groups(staffs[0], "meterSig"), None)
        if printed_meter is not None:
            digits = [g.get("href")[1:5] for g in uses(printed_meter)]
            if digits == ["E08A"]:
                meter = (4, 4)
            elif digits == ["E08B"]:
                meter = (2, 2)
            elif len(digits) == 2:
                meter = tuple(int(d[-1]) for d in digits)
        if key != previous_key or meter != previous_meter:
            definition = initial if b == 0 else element(section, "scoreDef")
            definition.attrib.update({"key.sig": signature(key), "meter.count": str(meter[0]), "meter.unit": str(meter[1])})
        previous_key, previous_meter = key, meter
        bar = element(section, "measure", n=b + 1)
        if lengths[b] != meter[0] * 1920 // meter[1]:
            bar.set("metcon", "false")
        if any(start == b for start, _ in jumps):
            bar.set("left", "rptstart")
            repeat_from = b
        expected_order.append(b)
        if any(end == b for _, end in jumps):
            bar.set("right", "rptend")
            expected_order.extend(range(repeat_from, b + 1))
        coordinates = []
        marked = {}
        accents = set()
        staff_lines = [[int(re.search(r"M\d+ (-?\d+)", g.get("d")).group(1)) for g in staff if g.tag.endswith("path") and re.match(r"M\d+ -?\d+ L", g.get("d", ""))] for staff in staffs]
        clef_changes = []
        for k, staff in enumerate(staffs):
            changes = [(float("-inf"), clefs[k])]
            for sign in groups(staff, "clef"):
                for glyph in uses(sign):
                    changes.append((position(glyph)[0], "F" if glyph.get("href")[1:5] in ("E062","E07C") else "G"))
            clef_changes.append(sorted(changes))
        for s, staff in enumerate(staffs, 1):
            written_staff = element(bar, "staff", n=s)
            for layer_no, layer in enumerate(groups(staff, "layer"), 1):
                entries = collections.defaultdict(list)
                continuation = carry.pop((s, layer_no), [])
                if continuation:
                    entries[0].extend(continuation)
                for g in layer.iter():
                    if g.get("class") != "note" or g.get("id") not in sounded:
                        continue
                    note = sounded[g.get("id")]
                    if any(glyph.get("href")[1:5] in ("E4A0", "E4A1") for a in groups(g, "artic") for glyph in uses(a)):
                        accents.add(g.get("id"))
                    head = uses(next(groups(g, "notehead")))[0]
                    x, y = position(head)
                    # Ledger lines and octave signs keep their printed spelling. The MIDI
                    # observation determines the sounding octave independently of the clef.
                    # A hand sometimes plays on the other printed staff. Read its note
                    # there while keeping the hand that owns the original voice.
                    positions = []
                    for k, lines in enumerate(staff_lines):
                        clef = next(c for at,c in reversed(clef_changes[k]) if at<=x)
                        offset = 8*(lines[-1]-y)/(lines[-1]-lines[0])
                        staff_step = (30 if clef=="G" else 18) + round(offset)
                        accidental = (note["p"]-NATURAL[staff_step%7]+6)%12-6
                        octave_shift = (note["p"]-NATURAL[staff_step%7]-accidental)//12-1-staff_step//7
                        cost = (abs(accidental)>2 or abs(octave_shift)>2, abs(offset-round(offset))>0.05, k!=s-1, abs(accidental), abs(octave_shift))
                        positions.append((cost, staff_step, clef))
                    _, diatonic, clef = min(positions)
                    letter = diatonic % 7
                    alter = (note["p"] - NATURAL[letter] + 6) % 12 - 6
                    if abs(alter) > 2:
                        raise ValueError(f"No. {number}, bar {b+1}, pitch {note['p']} disagrees with its printed staff")
                    octave = (note["p"] - NATURAL[letter] - alter) // 12 - 1
                    onset = round((note["q"] - measures[note["m"]]["q"]) * 480 / 20) * 20
                    end = round((note["q"] - measures[note["m"]]["q"] + note["d"]) * 480 / 20) * 20
                    if number == 43 and b == len(ids) - 1:
                        onset = 0  # The final written chord is rolled in the source audio.
                    entry = (note["p"], g.get("id"), x, y, letter, alter, diatonic // 7, octave, end, clef)
                    entries[onset].append(entry)
                    coordinates.append((x + 112, y, s, onset, g.get("id"), note["p"]))
                    f = extra_fingers.get((number, b+1, s, onset, note["p"]))
                    if f:
                        marked[g.get("id")] = f
                    if b < 2:
                        beat = 1920 / meter[1]
                        preview.append([note["p"], (sum(lengths[:b]) + onset) / beat, (end-onset) / beat, s])
                if not entries and layer_no > 1:
                    continue
                # The playback map gives a tied tone its entire sounding length. A
                # later chord may continue it while the other fingers attack together.
                tied_in, tied_out = {n[1] for n in continuation}, set()
                boundaries = sorted({*entries, *(min(n[8], lengths[b]) for chord in entries.values() for n in chord)})
                slices = collections.defaultdict(list)
                for start, chord in entries.items():
                    for note in chord:
                        end = min(note[8], lengths[b])
                        if note[8] > lengths[b]:
                            carry[s, layer_no].append((note[0], f"{note[1]}b{b+2}", *note[2:8], note[8]-lengths[b], note[9]))
                        cuts = [at for at in boundaries if start <= at < end] + [end]
                        for at, release in zip(cuts, cuts[1:]):
                            written_id = note[1] if at == start else f"{note[1]}t{at}"
                            slices[at].append((note[0], written_id, *note[2:8], release, note[9]))
                            if at > start:
                                tied_in.add(written_id)
                            if release < note[8]:
                                tied_out.add(written_id)
                written_layer = element(written_staff, "layer", n=layer_no)
                last_clef = output_clefs.get((s, layer_no), "G" if s == 1 else "F")
                position_in_bar = 0
                tuplet_parent = None
                for onset, chord in sorted(slices.items()):
                    chord.sort()
                    if onset > position_in_bar:
                        element(written_layer, "rest", **{ID:f"rest{b}s{s}l{layer_no}t{position_in_bar}", "dur.ppq":onset-position_in_bar})
                    assert onset >= position_in_bar, (number, b, s, layer_no, onset, position_in_bar)
                    duration = min(chord[0][8], lengths[b]) - onset
                    assert all(min(n[8], lengths[b])-onset == duration for n in chord), (number, b, chord)
                    if chord[0][9] != last_clef:
                        last_clef = chord[0][9]
                        element(written_layer, "clef", shape=last_clef, line=2 if last_clef == "G" else 4)
                    # Express a held pitch crossing an internal subdivision as tied values.
                    at, remaining = onset, duration
                    while remaining:
                        value = next((v for v in (1920, 1440, 960, 720, 480, 360, 240, 180, 160, 120, 90, 80, 60, 40, 30, 20) if v <= remaining), None)
                        assert value, (number, b, remaining)
                        parent = written_layer
                        if value in (160, 80, 40, 20):
                            if tuplet_parent is None or at % (value*3) == 0:
                                tuplet_parent = element(written_layer, "tuplet", num=3, numbase=2)
                            parent = tuplet_parent
                        else:
                            tuplet_parent = None
                        dur = {1440:2, 720:4, 360:8, 180:16, 90:32, 160:8, 80:16, 40:32, 20:64}.get(value, 1920//value)
                        attrs = {"dur":dur, "dur.ppq":value}
                        if value in (1440,720,360,180,90):
                            attrs["dots"] = 1
                        host = element(parent, "chord", **{ID:f"ch{b}s{s}l{layer_no}t{at}", **attrs}) if len(chord)>1 else parent
                        for _, note_id, _, _, letter, alter, written, sounding, _, _ in chord:
                            pitch = element(host, "note", **{ID:note_id if at == onset else f"{note_id}t{at}", "pname":"cdefgab"[letter], "oct":written, "oct.ges":sounding, "accid.ges":ACCIDENTAL[alter], **(attrs if len(chord)==1 else {})})
                            if at == onset and note_id in accents:
                                pitch.set("artic", "acc")
                            before = at != onset or note_id in tied_in
                            after = remaining > value or note_id in tied_out
                            if before or after:
                                pitch.set("tie", "m" if before and after else "i" if after else "t")
                        at += value
                        remaining -= value
                    position_in_bar = onset + duration
                if position_in_bar < lengths[b]:
                    rest = "space" if not entries and any(g.get("id") in sounded for g in groups(staff, "note")) else "rest"
                    element(written_layer, rest, **{ID:f"rest{b}s{s}l{layer_no}end", "dur.ppq":lengths[b]-position_in_bar})
                output_clefs[s, layer_no] = last_clef
            clefs[s-1] = clef_changes[s-1][-1][1]

        # Fingering numbers share the notehead centre. Group stacked numbers at an
        # onset before assigning them from the top tone to the bottom of a chord.
        columns = collections.defaultdict(list)
        for index_in_bar, fing in enumerate(groups(drawing, "fing")):
            t = next((t for t in fing.iter() if t.tag.endswith("}text")), None)
            digit = "".join(fing.itertext()).strip()
            if t is None or digit not in ("1", "2", "3", "4", "5"):
                continue
            x,y = int(t.get("x")),int(t.get("y"))
            at = min(coordinates, key=lambda n:(abs(n[0]-x), abs(n[1]-y)))
            columns[at[3]].append((y,int(digit),index_in_bar))
        offsets = collections.defaultdict(list)
        printed_hands = {}
        for onset, values in columns.items():
            notes = sorted((n for n in coordinates if n[3]==onset), key=lambda n:(n[2],n[1]))
            if len(values)==len(notes):
                for n,(y,finger,i) in zip(notes,sorted(values)):
                    marked[n[4]] = finger
                    offsets[n[2]].append(n[1]-y)
                    printed_hands[i] = n[2]
        for onset, values in columns.items():
            notes = [n for n in coordinates if n[3]==onset]
            if len(values)==len(notes):
                continue
            by_hand = collections.defaultdict(list)
            for y, finger, i in values:
                nearest = min(notes, key=lambda n: min(abs(n[1]-d-y) for d in offsets[n[2]] or [400]))
                before = max((p for p in printed_hands if p<i), default=None)
                after = min((p for p in printed_hands if p>i), default=None)
                hand = printed_hands[before] if before is not None and after is not None and printed_hands[before]==printed_hands[after] else nearest[2]
                by_hand[hand].append((y,finger))
            for hand, digits in by_hand.items():
                chord = sorted((n for n in notes if n[2]==hand), key=lambda n:n[1])
                if len(chord)==len(digits) and len({d for _,d in digits})==len(digits):
                    for n,(_,finger) in zip(chord,sorted(digits)):
                        marked[n[4]] = finger
        for note_id, finger in marked.items():
            element(bar, "fing", startid="#"+note_id).text = str(finger)
        right = [n for n in coordinates if n[2]==1] or coordinates
        for slur in groups(drawing, "slur"):
            path = next((c.get("d", "") for c in slur if c.get("d")), "")
            points = [tuple(map(int,p)) for p in re.findall(r"(-?\d+),(-?\d+)", path)]
            if len(points) >= 4:
                (x1,y1),(x2,y2) = points[0],points[3]
                start = min(coordinates, key=lambda n:(abs(n[0]-x1),abs(n[1]-y1)))
                finish = min((n for n in coordinates if n[2]==start[2]), key=lambda n:(abs(n[0]-x2),abs(n[1]-y2)))
                if start[3]<finish[3]:
                    element(bar, "slur", startid="#"+start[4], endid="#"+finish[4])
        for pause in groups(drawing, "fermata"):
            if uses(pause):
                x,y = position(uses(pause)[0])
                anchor = min(coordinates, key=lambda n:(abs(n[0]-x),abs(n[1]-y)))
                element(bar, "fermata", startid="#"+anchor[4])
        if next(groups(drawing, "arpeg"), None) is not None:
            for chord in bar.findall(".//{*}chord"):
                element(bar, "arpeg", startid="#"+chord.get(ID))
        for g in groups(drawing, "dynam"):
            glyphs = uses(g)
            if glyphs and glyphs[0].get("href")[1:5] in DYNAMICS:
                x, _ = position(glyphs[0])
                anchor = min(right, key=lambda n:abs(n[0]-112-x))
                element(bar, "dynam", startid="#"+anchor[4]).text = DYNAMICS[glyphs[0].get("href")[1:5]]
        for hairpin in groups(drawing, "hairpin"):
            line = next((c for c in hairpin if c.get("points")), None)
            if line is not None:
                pts = [tuple(map(int,p.split(","))) for p in line.get("points").split()]
                if len(pts)==3:
                    left, tip, _ = pts
                    start = min(right, key=lambda n:abs(n[0]-min(left[0],tip[0])))
                    end = min(right, key=lambda n:abs(n[0]-max(left[0],tip[0])))
                    if start[3]<end[3]:
                        element(bar, "hairpin", form="dim" if tip[0]>left[0] else "cres", startid="#"+start[4], endid="#"+end[4])
    assert expected_order == order, (number, "printed repeat order")
    assert not carry, (number, "tie past the end of the study")
    payload = ET.tostring(root, encoding="utf-8")
    (out / f"{number:02}.mei.gz").write_bytes(gzip.compress(payload, compresslevel=9, mtime=0))
    return preview


if __name__ == "__main__":
    source, out = map(Path, sys.argv[1:])
    out.mkdir(parents=True, exist_ok=True)
    fingers = brigham_fingers(source / "book2a.musicxml")
    previews = [study(n, source, out, fingers) for n in range(1,61)]
    (out / "openings.json").write_text(json.dumps(previews,separators=(",",":")), encoding="utf-8")
