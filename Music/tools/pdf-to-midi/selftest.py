"""Checks the parts that need no score: start times within a bar, the MIDI writer, and reading a MIDI back.

usage: python -I selftest.py      (says "all passed", or stops at the first thing that isn't so)

The reader itself can only be tried on a real Musicnotes PDF, and those are bought music that doesn't belong in a
repository; its own checks (every bar adds up, both staves agree, read back from the file) are its test on each score.
"""
import os, sys, tempfile
from fractions import Fraction as F
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score_reading as R, score_midi as M


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


if __name__ == "__main__":
    starts()
    midi()
    print("all passed")
