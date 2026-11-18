"""Writes a reading as a Standard MIDI File, and reads a MIDI file back to hold it against the reading.

The file: right hand and left hand on their own tracks, repeats played out, ties joined, every bar the length the page
gives it, the page's tempo numbers with its rit., a tempo and fermatas, and the page's loud-and-soft marks as key speeds.
"""
import struct, collections
from fractions import Fraction as F

PPQ = 480
GRACE = 60              # a small note sounds this many ticks before the note it leans on
FERMATA = 0.5           # under a fermata the tempo halves, so the note lasts twice as long
RAMP_LIMIT = 8          # a rit. slows over at most this many quarters, then holds


def play_order(bars):
    """Bar indexes in the order they are played: a repeat sign sends it back once to the last repeat start."""
    order, start, i, been = [], 0, 0, set()
    while i < len(bars):
        if bars[i]["repeat_start"]:
            start = i
        order.append(i)
        if bars[i]["repeat_end"] and i not in been:
            been.add(i)
            i = start
        else:
            i += 1
    return order


def signature(bar):
    """The time signature to write for a bar: the page's, or for a short first or last bar one that fits its length."""
    num, den = bar["time"]
    length = F(bar["length"])
    if length == F(num * 4, den):
        return num, den
    for d in (den, 8, 16, 32):
        if (length * d / 4).denominator == 1:
            return int(length * d / 4), d
    return num, den


def place(flat):
    """Every bar's first tick in playing order, and every note with its ticks."""
    bars = flat["bars"]
    by_bar = collections.defaultdict(list)
    for n in flat["notes"]:
        by_bar[n["bar"]].append(n)
    played, sounding, tick = [], [], 0
    for i in play_order(bars):
        played.append((i, tick))
        for n in by_bar[i]:
            on = tick + F(n["onset"]) * PPQ
            if on.denominator != 1:
                raise ValueError(f"bar {bars[i]['number']}: a start that isn't a whole tick ({n['onset']})")
            if n.get("grace"):
                sounding.append(dict(staff=n["staff"], midi=n["midi"], on=int(on) - GRACE, off=int(on), vel=n["vel"], tie_in=False, tie_out=False, bar=i, grace=True))
                continue
            ticks = F(n["dur"]) * PPQ
            if ticks.denominator != 1 or ticks <= 0:
                raise ValueError(f"bar {bars[i]['number']}: a length that isn't a whole tick ({n['dur']})")
            sounding.append(dict(staff=n["staff"], midi=n["midi"], on=int(on), off=int(on + ticks), vel=n["vel"], tie_in=n["tie_in"], tie_out=n["tie_out"], bar=i))
        tick += int(F(bars[i]["length"]) * PPQ)
    return played, sounding, tick


def join(sounding, bars):
    """Ties joined into one sound; a key struck again while still held ends the first sound there. Returns notes, counts, warnings."""
    notes, joined, again, warnings = [], 0, [], []
    for staff in "UL":
        for midi in sorted({s["midi"] for s in sounding if s["staff"] == staff}):
            out = []
            for s in sorted((s for s in sounding if s["staff"] == staff and s["midi"] == midi), key=lambda s: (s["on"], -s["off"])):
                prev = out[-1] if out else None
                if prev and prev["tie_out"] and s["tie_in"] and s["on"] == prev["off"]:
                    prev["off"] = s["off"]; prev["tie_out"] = s["tie_out"]; joined += 1
                    continue
                if s["tie_in"]:
                    warnings.append(f"bar {bars[s['bar']]['number']}: a tie arrives at a note that nothing ties into; it sounds on its own")
                if prev and prev["on"] == s["on"]:          # two voices strike one key together: one sound, the longer length
                    prev["off"] = max(prev["off"], s["off"]); prev["tie_out"] = prev["tie_out"] or s["tie_out"]
                    again.append((bars[s["bar"]]["number"], midi))
                    continue
                if prev and prev["off"] > s["on"]:          # struck again while held: the first sound ends there, the new one lasts as long as either
                    s["off"] = max(s["off"], prev["off"]); prev["off"] = s["on"]
                    again.append((bars[s["bar"]]["number"], midi))
                out.append(s)
            for s in out:
                if s["tie_out"]:
                    warnings.append(f"bar {bars[s['bar']]['number']}: a tie leaves a note and reaches nothing; the note keeps its own length")
            notes += out
    return notes, joined, again, warnings


def tempo_map(flat, played, end_tick):
    """(tick, beats a minute) at every change: the page's numbers, rit. as a slope toward three quarters of the tempo (poco
    rit. seven eighths) until the next tempo word, a tempo back to the last number, and half tempo under a fermata."""
    bars = flat["bars"]
    starts = collections.defaultdict(list)
    for i, tick in played:
        starts[i].append(tick)
    events = []
    for t in flat["tempo"]:
        at = F(t["at"])
        bar = next((b for b in bars if F(b["start"]) <= at < F(b["start"]) + F(b["length"])), bars[-1])
        for tick in starts[bar["index"]]:
            here = tick + int((at - F(bar["start"])) * PPQ)
            e = dict(t, tick=here)
            if "until" in t:
                e["until_tick"] = here + int((F(t["until"]) - at) * PPQ)
            events.append(e)
    events.sort(key=lambda e: (e["tick"], e["kind"] != "tempo"))
    numbers = [e for e in events if e["kind"] == "tempo"]
    base = numbers[0]["bpm"] if numbers else 100
    points, current = [(0, float(base))], float(base)
    turning = [e for e in events if e["kind"] in ("tempo", "a tempo", "fermata")]
    for e in events:
        if e["kind"] == "tempo":
            base = current = float(e["bpm"])
            points.append((e["tick"], current))
        elif e["kind"] == "a tempo":
            current = float(base)
            points.append((e["tick"], current))
        elif e["kind"] == "slower":
            stop = min([x["tick"] for x in turning if x["tick"] > e["tick"]] + [end_tick, e["tick"] + RAMP_LIMIT * PPQ])
            steps = max(1, (stop - e["tick"]) // (PPQ // 2))
            target = base * e["to"]
            for j in range(steps):
                points.append((e["tick"] + j * (PPQ // 2), current + (target - current) * (j + 1) / steps))
            current = target
    points.sort(key=lambda p: p[0])
    held = [(e["tick"], e["until_tick"]) for e in events if e["kind"] == "fermata"]
    ticks = sorted({p[0] for p in points} | {t for h in held for t in h})
    out = []
    for t in ticks:
        under = [bpm for tick, bpm in points if tick <= t][-1]
        bpm = under * (FERMATA if any(a <= t < b for a, b in held) else 1)
        if t < end_tick and (not out or abs(out[-1][1] - bpm) > 0.01):
            out.append((t, bpm))
    return out


def vlq(n):
    out = [n & 0x7F]
    n >>= 7
    while n:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(out))


def meta(kind, payload):
    return bytes([0xFF, kind]) + vlq(len(payload)) + payload


def track(events):
    """events: (tick, order, bytes); order puts key-ups before key-downs on the same tick."""
    body, last = b"", 0
    for t, _, ev in sorted(events, key=lambda e: (e[0], e[1])):
        body += vlq(t - last) + ev
        last = t
    return b"MTrk" + struct.pack(">I", len(body)) + body


def plain(text):
    return text.encode("latin-1", "replace")


def write(flat, path, title=None, copyright=None):
    """Writes the file and returns a few lines saying what went into it."""
    bars = flat["bars"]
    played, sounding, end_tick = place(flat)
    notes, joined, again, warnings = join(sounding, bars)
    tempos = tempo_map(flat, played, end_tick)
    conductor = [(0, 0, meta(0x03, plain(title or flat.get("title") or "Piano")))]
    if copyright or flat.get("copyright"):
        conductor.append((0, 0, meta(0x02, plain(copyright or flat["copyright"]))))
    conductor.append((0, 0, meta(0x01, b"Made from the purchaser's own copy of the score, for personal practice.")))
    last = None
    for i, tick in played:
        sig = signature(bars[i])
        if sig != last:
            conductor.append((tick, 0, meta(0x58, bytes([sig[0], {1: 0, 2: 1, 4: 2, 8: 3, 16: 4, 32: 5}[sig[1]], 24, 8]))))
            last = sig
    conductor.append((0, 0, meta(0x59, bytes([0, 0]))))
    for tick, bpm in tempos:
        conductor.append((tick, 1, meta(0x51, struct.pack(">I", round(60_000_000 / bpm))[1:])))
    conductor.append((end_tick, 9, meta(0x2F, b"")))
    tracks = [track(conductor)]
    for staff, name, channel in (("U", b"Right hand", 0), ("L", b"Left hand", 1)):
        events = [(0, 0, meta(0x03, name)), (0, 1, bytes([0xC0 | channel, 0]))]
        for n in notes:
            if n["staff"] == staff:
                events.append((n["on"], 2, bytes([0x90 | channel, n["midi"], n["vel"]])))
                events.append((n["off"], 1, bytes([0x80 | channel, n["midi"], 0])))
        events.append((end_tick, 9, meta(0x2F, b"")))
        tracks.append(track(events))
    with open(path, "wb") as f:
        f.write(b"MThd" + struct.pack(">IHHH", 6, 1, len(tracks), PPQ) + b"".join(tracks))
    seconds, at = 0.0, 0
    for (t0, bpm), nxt in zip(tempos, tempos[1:] + [(end_tick, 0)]):
        seconds += (nxt[0] - t0) / PPQ * 60 / bpm
    lines = [f"bars played: {len(played)}" + (f" (the page's {len(bars)} and its repeats)" if len(played) != len(bars) else ""),
             f"sounds: {len(notes)} (right hand {sum(n['staff'] == 'U' for n in notes)}, left hand {sum(n['staff'] == 'L' for n in notes)}); ties joined: {joined}; "
             f"small notes: {sum(1 for n in notes if n.get('grace'))}; keys struck again while another voice held them: {len(again)}",
             f"length: {int(seconds // 60)}:{seconds % 60:04.1f}; tempo changes: {len(tempos)} ({', '.join(str(round(b)) for _, b in tempos[:12])}{'...' if len(tempos) > 12 else ''}); "
             f"lowest key {min(n['midi'] for n in notes)}, highest {max(n['midi'] for n in notes)}"]
    return "\n".join(lines + ["  ! " + w for w in warnings])


def parse(path):
    """A MIDI file's tracks as (name, [(on, off, key, speed)], last tick), and its tempo and time-signature events."""
    raw = open(path, "rb").read()
    if raw[:4] != b"MThd":
        raise ValueError("not a MIDI file")
    fmt, ntrks, ppq = struct.unpack(">HHH", raw[8:14])
    pos, tracks, metas = 14, [], []
    for _ in range(ntrks):
        size = struct.unpack(">I", raw[pos + 4:pos + 8])[0]
        body, pos = raw[pos + 8:pos + 8 + size], pos + 8 + size
        i, tick, status, held, notes, name = 0, 0, 0, {}, [], ""
        while i < len(body):
            delta = 0
            while True:
                b = body[i]; i += 1
                delta = (delta << 7) | (b & 0x7F)
                if not b & 0x80:
                    break
            tick += delta
            if body[i] & 0x80:
                status = body[i]; i += 1
            if status == 0xFF:
                kind = body[i]; i += 1
                n = 0
                while True:
                    b = body[i]; i += 1
                    n = (n << 7) | (b & 0x7F)
                    if not b & 0x80:
                        break
                payload = body[i:i + n]; i += n
                if kind == 0x03:
                    name = payload.decode("latin-1")
                metas.append((tick, kind, payload))
            elif status & 0xF0 in (0x90, 0x80):
                key, vel = body[i], body[i + 1]; i += 2
                if status & 0xF0 == 0x90 and vel:
                    if key in held:
                        raise ValueError(f"key {key} struck at tick {tick} while already down in track {name}")
                    held[key] = (tick, vel)
                else:
                    on, v = held.pop(key)
                    notes.append((on, tick, key, v))
            elif status & 0xF0 == 0xC0:
                i += 1
            else:
                raise ValueError(f"an event this parser doesn't know: {status:#x}")
        if held:
            raise ValueError(f"keys left down at the end of track {name}: {sorted(held)}")
        tracks.append((name, notes, tick))
    return ppq, tracks, metas


def check(path, flat):
    """Reads the file back and compares it with the reading, note by note. Returns what differs (nothing, if all is well)."""
    ppq, tracks, metas = parse(path)
    bars = flat["bars"]
    problems = []
    starts, tick = [], 0
    for i in play_order(bars):
        starts.append((i, tick))
        tick += int(F(bars[i]["length"]) * ppq)
    if any(t[2] != tick for t in tracks):
        problems.append(f"the tracks don't all end where the last bar ends (tick {tick})")
    by_bar = collections.defaultdict(list)
    for n in flat["notes"]:
        by_bar[n["bar"]].append(n)
    for staff, (name, notes, _) in zip("UL", tracks[1:]):
        played = {(on, key): off - on for on, off, key, vel in notes}
        if len(played) != len(notes):
            problems.append(f"{name}: one key struck twice on one tick")
        expected = {}
        for i, start in starts:
            for n in by_bar[i]:
                if n["staff"] != staff or n.get("tie_in"):
                    continue
                at = start + int(F(n["onset"]) * ppq) - (GRACE if n.get("grace") else 0)
                length = GRACE if n.get("grace") else int(F(n["dur"]) * ppq)
                if (at, n["midi"]) in expected:
                    expected[(at, n["midi"])] = (max(expected[(at, n["midi"])][0], length), True, bars[i]["number"], n["name"])
                else:
                    expected[(at, n["midi"])] = (length, n.get("tie_out", False), bars[i]["number"], n["name"])
        for key, (length, tied, number, note) in sorted(expected.items()):
            if key not in played:
                problems.append(f"{name}: bar {number}, {note} is missing from the file")
            elif not tied and played[key] != length:
                again = [k for k in expected if k[1] == key[1] and key[0] < k[0] < key[0] + length]      # the same key struck again before this one ends
                before = [k for k in expected if k[1] == key[1] and k[0] < key[0] < k[0] + expected[k][0]]
                if not again and not before:
                    problems.append(f"{name}: bar {number}, {note} lasts {played[key]} ticks in the file, {length} on the page")
        for key in sorted(set(played) - set(expected)):
            problems.append(f"{name}: a sound at tick {key[0]} (key {key[1]}) that the page doesn't call for")
    return problems
