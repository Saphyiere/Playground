"""Checks the parts that need no score: start times within a bar, the MIDI and MusicXML writers, and reading both back.

usage: python -I selftest.py      (says "all passed", or stops at the first thing that isn't so)

The reader itself can only be tried on a real Musicnotes PDF, and those are bought music that doesn't belong in a
repository; its own checks (every bar adds up, both staves agree, read back from the file) are its test on each score.
"""
import os, sys, tempfile, zipfile
import xml.etree.ElementTree as ET
from fractions import Fraction as F
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score_reading as R, score_midi as M, score_musicxml as X, score_shapes as S


def event(x, length, kind="chord", stem="up"):
    return dict(kind=kind, x=x, dur=F(length), written=F(length), stem=stem, tuplet=None)


def starts():
    one = [event(10, 1), event(30, F(1, 2)), event(40, F(1, 2)), event(50, 1)]            # a quarter, two eighths, a quarter in 3/4
    assert R.sweep(one, F(3)) == (None, 3) and [e["onset"] for e in one] == [0, 1, F(3, 2), 2]
    two = [event(10, 2, stem="down"), event(12.8, 1), event(30, F(1, 2)), event(40, F(1, 2))]   # a half note under a quarter nudged half a head, and two eighths
    assert R.sweep(two, F(2)) == (None, 2) and [e["onset"] for e in two] == [0, 0, 1, F(3, 2)]
    triplet = [event(10, F(1, 3)), event(20, F(1, 3)), event(30, F(1, 3)), event(40, 1)]
    assert R.sweep(triplet, F(2)) == (None, 2) and triplet[3]["onset"] == 1
    assert "past" in R.sweep([event(10, 1), event(30, 1), event(50, 1)], F(2))[0]
    assert "short" in R.sweep([event(10, 1)], F(2))[0]
    rest = [dict(kind="barrest", x=50, dur=None, written=None, stem=None, tuplet=None)]
    assert R.sweep(rest, F(3)) == (None, 3) and rest[0]["dur"] == 3
    assert R.units(F(1, 4)) == "3" and R.units(F(1, 3)) == "4" and R.runs([81, 82, 83, 90]) == "81-83, 90"


def note(bar, staff, onset, length, key, **more):
    return dict(dict(bar=bar, number=bar, staff=staff, onset=str(F(onset)), dur=str(F(length)), midi=key, name=f"key {key}", tie_in=False, tie_out=False, vel=64), **more)


def bar(index, length, time, start, **more):
    return dict(dict(index=index, number=index, length=str(F(length)), time=list(time), start=str(F(start)), repeat_start=False, repeat_end=False), **more)


def a_reading():
    """A pickup, two bars repeated with a tie over their bar line, a bar of 3/4 with a key struck again while held and a
    triplet, and a last bar under a fermata with a small note before it; a rit. through the 3/4 bar, then a tempo."""
    return dict(title="Self-test", copyright=None,
                bars=[bar(0, 1, (2, 4), 0), bar(1, 2, (2, 4), 1, repeat_start=True), bar(2, 2, (2, 4), 3, repeat_end=True), bar(3, 3, (3, 4), 5), bar(4, 3, (3, 4), 8)],
                notes=[note(0, "U", 0, 1, 64),
                       note(1, "U", 0, 1, 60), note(1, "U", 1, 1, 62, tie_out=True), note(2, "U", 0, 1, 62, tie_in=True), note(2, "U", 1, 1, 64),
                       note(1, "L", 0, 2, 48), note(2, "L", 0, 2, 43),
                       note(3, "U", 0, 3, 72), note(3, "U", 1, 1, 72),
                       note(3, "L", 0, F(1, 3), 36), note(3, "L", F(1, 3), F(1, 3), 40), note(3, "L", F(2, 3), F(1, 3), 43),
                       note(4, "U", 0, 3, 60, fermata=True), note(4, "L", 0, 0, 59, grace=True), note(4, "L", 0, 3, 48)],
                tempo=[dict(at="0", kind="tempo", bpm=120), dict(at="5", kind="slower", to=0.75), dict(at="8", kind="a tempo"), dict(at="8", kind="fermata", until="11")])


def midi():
    flat = a_reading()
    assert M.play_order(flat["bars"]) == [0, 1, 2, 1, 2, 3, 4]
    assert M.signature(flat["bars"][0]) == (1, 4) and M.signature(flat["bars"][3]) == (3, 4)
    path = os.path.join(tempfile.mkdtemp(), "selftest.mid")
    M.write(flat, path)
    assert M.check(path, flat) == [], M.check(path, flat)
    ppq, tracks, metas = M.parse(path)
    q = ppq
    right, left = sorted(tracks[1][1]), sorted(tracks[2][1])
    assert ppq == 480 and [t[0] for t in tracks] == ["Self-test", "Right hand", "Left hand"] and all(t[2] == 15 * q for t in tracks)
    assert len(right) == 10 and len(left) == 9
    assert (2 * q, 4 * q, 62, 64) in right and (6 * q, 8 * q, 62, 64) in right              # the tie joined, both times through the repeat
    assert (9 * q, 10 * q, 72, 64) in right and (10 * q, 12 * q, 72, 64) in right            # struck again: the first sound ends, the second lasts as long as either
    assert [n[:3] for n in left if n[0] < 10 * q and n[0] >= 9 * q] == [(9 * q, 9 * q + 160, 36), (9 * q + 160, 9 * q + 320, 40), (9 * q + 320, 10 * q, 43)]
    assert (12 * q - M.GRACE, 12 * q, 59, 64) in left                                        # the small note, just before its beat
    signatures = [(t, p[0], 2 ** p[1]) for t, k, p in metas if k == 0x58]
    assert signatures == [(0, 1, 4), (q, 2, 4), (9 * q, 3, 4)], signatures
    tempos = [(t, round(60_000_000 / int.from_bytes(p, "big"))) for t, k, p in metas if k == 0x51]
    assert tempos[0] == (0, 120) and tempos[1] == (9 * q, 115) and tempos[-2] == (9 * q + 5 * (q // 2), 90) and tempos[-1] == (12 * q, 60), tempos
    del flat["notes"][4]
    assert any("missing" in p for p in M.check(path, a_reading() | {"notes": a_reading()["notes"] + [note(4, "U", 1, 1, 77)]}))
    assert any("doesn't call for" in p for p in M.check(path, flat))
    os.remove(path)


def chord(x, onset, length, notes, stem="up", written=None, **more):
    """An event as the reader leaves it: its notes as (scale step, sharp or flat) pairs, C4 being step 28."""
    tones = [dict(dia=d, alter=a, shift=0, tie_in=False, tie_out=False) for d, a in notes]
    return dict(dict(kind="chord", x=x, onset=F(onset), dur=F(length), written=F(written if written is not None else length), stem=stem, tuplet=None,
                     notes=tones, beam={}, y0=0, y1=0), **more)


def rest(x, onset, length, y=108, kind="rest"):
    return dict(kind=kind, x=x, onset=F(onset), dur=F(length), written=F(length) if kind == "rest" else None, stem=None, tuplet=None, y0=y - 6, y1=y + 6)


def a_score():
    """A pickup; a bar with two voices above (one with a printed sharp and a tie into the next bar) and below a triplet on a
    beam, then a treble clef for a half note; a last bar with a repeat sign, two notes under an 8va line, a fermata and a
    bar's rest below; mf, a hairpin, a tempo of 96 and rit."""
    triplet = [chord(80 + 15 * k, F(k, 3), F(1, 3), [(d, 0)], written=F(1, 2), tuplet="beam", tuplet_group=("beam", 1), tuplet_place="below") for k, d in enumerate((14, 16, 18))]
    for k, e in enumerate(triplet):
        e["beam"] = {1: ("begin", "continue", "end")[k]}
    held = chord(120, 1, 2, [(39, 0)])
    held["notes"][0]["tie_out"] = True
    sharp = chord(80, 0, 1, [(38, 1)], rolled=5)
    sharp["notes"][0]["accidental"] = 1
    triplet[0]["rolled"] = 5                    # one wavy line through both staves: one roll
    tied = chord(210, 0, 1, [(39, 0)], rolled=6)        # a line of its own: rolled alone
    tied["notes"][0]["tie_in"] = True
    high = [chord(240, 1, 1, [(47, 0)]), chord(270, 2, 1, [(49, 0)], fermata=True, accent=True)]
    for e in high:
        e["notes"][0]["shift"] = 7
    bars = [dict(index=0, number=0, page=1, line=1, x0=10, x1=60, time=(3, 4), length=F(1), repeat_start=False, repeat_end=False, final=False,
                 staves={"U": [chord(40, 0, 1, [(35, 0)], stem="down")], "L": [rest(40, 0, 1, y=168)]}),
            dict(index=1, number=1, page=1, line=1, x0=60, x1=200, time=(3, 4), length=F(3), repeat_start=False, repeat_end=False, final=False,
                 staves={"U": [sharp, held, chord(82, 0, 3, [(33, 0)], stem="down", written=3)], "L": triplet + [chord(150, 1, 2, [(32, 0)])]}),
            dict(index=2, number=2, page=1, line=1, x0=200, x1=330, time=(3, 4), length=F(3), repeat_start=False, repeat_end=True, final=False,
                 staves={"U": [tied] + high, "L": [rest(260, 0, 3, y=168, kind="barrest")]})]
    staves = {"U": dict(top=100, bottom=116.5), "L": dict(top=160, bottom=176.5)}
    data = dict(staves=staves, clefs={"U": [(12, "G")], "L": [(12, "F"), (140, "G")]}, tempo_text=[dict(x=20, y=80, bpm=96, words="", lead=[])],
                words_raw=[dict(x=300, y=90, text="rit.")], dynamics=[dict(x=38, end=38, y=140, text="mf")],
                hairpins=[dict(x0=78, x1=160, louder=True, opens=True, closes=False, y=140)])
    reading = dict(bars=bars, systems=[dict(first=0, count=3, data=data)], title="Self-test", copyright=None, credits=dict(composer="A. Composer", arranger=None))
    flat = dict(bars=[dict(index=b["index"], number=b["number"]) for b in bars],
                notes=[dict(bar=b["index"], staff=s, onset=str(e["onset"]), dur=str(e["dur"]), midi=S.midi_of(n["dia"], n["alter"]), tie_in=n["tie_in"], tie_out=n["tie_out"])
                       for b in bars for s in "UL" for e in b["staves"][s] if e["kind"] == "chord" for n in e["notes"]])
    return reading, flat


def musicxml():
    assert X.value(F(3, 2)) == ("quarter", 1) and X.value(F(7, 4)) == ("quarter", 2) and X.value(F(1, 8)) == ("32nd", 0) and X.value(F(3)) == ("half", 1)
    reading, flat = a_score()
    path = os.path.join(tempfile.mkdtemp(), "selftest.mxl")
    X.write(reading, path)
    assert X.check(path, flat) == [], X.check(path, flat)
    root = ET.fromstring(zipfile.ZipFile(path).read("score.musicxml"))
    bars = root.find("part").findall("measure")
    assert [m.get("number") for m in bars] == ["0", "1", "2"] and bars[0].get("implicit") == "yes"
    assert root.findtext("work/work-title") == "Self-test" and root.findtext("identification/creator") == "A. Composer"
    first = bars[0].find("attributes")
    assert first.findtext("divisions") == "480" and first.findtext("time/beats") == "3" and [c.findtext("sign") for c in first.findall("clef")] == ["G", "F"]
    voices = {(n.findtext("staff"), n.findtext("voice")) for n in bars[1].iter("note")}
    assert voices == {("1", "1"), ("1", "2"), ("2", "5")}, voices                       # two voices above, one below
    change = [a for a in bars[1].findall("attributes") if a.find("clef") is not None]
    assert len(change) == 1 and change[0].find("clef").get("number") == "2" and change[0].findtext("clef/sign") == "G"
    sharp = [n for n in bars[1].iter("note") if n.findtext("accidental")]
    assert [(n.findtext("pitch/step"), n.findtext("pitch/alter"), n.findtext("accidental")) for n in sharp] == [("F", "1", "sharp")]
    beams = [n.findtext("beam") for n in bars[1].iter("note") if n.find("time-modification") is not None]
    tuplets = [t.get("type") for t in bars[1].iter("tuplet")]
    assert beams == ["begin", "continue", "end"] and tuplets == ["start", "stop"] and next(bars[1].iter("tuplet")).get("placement") == "below"
    assert [t.get("type") for t in bars[1].iter("tied")] == ["start"] and [t.get("type") for t in bars[2].iter("tied")] == ["stop"]
    shifts = [o.get("type") for o in bars[2].iter("octave-shift")]
    assert shifts == ["down", "stop"], shifts                                                # 8va: printed an octave down from the pitch given
    under = [(n.findtext("pitch/step"), n.findtext("pitch/octave")) for n in bars[2].iter("note") if n.find("pitch") is not None][1:]
    assert under == [("A", "6"), ("C", "7")], under                                         # at the pitch they sound
    rests = [n for n in bars[2].iter("note") if n.find("rest") is not None]
    assert len(rests) == 1 and rests[0].find("rest").get("measure") == "yes" and rests[0].findtext("staff") == "2"   # the bar's rest below
    assert bars[2].find("barline/repeat").get("direction") == "backward"
    last = [n for n in bars[2].iter("note") if n.findtext("pitch/octave") == "7"][0]
    assert last.find("notations/fermata") is not None and last.find("notations/articulations/accent") is not None
    rolled = [(n.findtext("staff"), n.find("notations/arpeggiate").get("number")) for n in bars[1].iter("note") if n.find("notations/arpeggiate") is not None]
    assert len(rolled) == 2 and rolled[0][1] == rolled[1][1] and {s for s, _ in rolled} == {"1", "2"}, rolled     # both hands, one roll
    alone = [n.find("notations/arpeggiate").get("number") for n in bars[2].iter("note") if n.find("notations/arpeggiate") is not None]
    assert len(alone) == 1 and alone[0] != rolled[0][1]
    assert root.find(".//metronome/per-minute").text == "96" and root.find(".//dynamics/mf") is not None
    assert [w.get("type") for w in root.iter("wedge")] == ["crescendo", "stop"] and root.find(".//words").text == "rit."
    # a voice that comes up short in the file is caught
    text = zipfile.ZipFile(path).read("score.musicxml").decode().replace("<duration>960</duration>", "<duration>480</duration>", 1)
    plain = path.replace(".mxl", ".musicxml")
    open(plain, "w", encoding="utf-8").write(text)
    assert X.check(plain, flat), "a changed length wasn't noticed"
    os.remove(path)
    os.remove(plain)


if __name__ == "__main__":
    starts()
    midi()
    musicxml()
    print("all passed")
