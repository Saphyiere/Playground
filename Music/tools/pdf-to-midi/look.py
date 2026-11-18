"""Looks at a score before or after reading it: what the file is made of, and every shape inside one bar.

usage: python -I look.py score.pdf                 how the file is made: pages, fonts, the music font's glyphs (known and not), the words
       python -I look.py score.pdf sheet out.png   a picture of each kind of glyph in its surroundings, its code under it
       python -I look.py score.pdf bar N [N...]    every shape the file draws inside those bars (the page's own numbers), and how they were read

A new score may use glyphs this reader has not met (a key signature, a different rest, a pedal mark). The first form
lists them; the sheet shows what each looks like, so its code can be given a name in score_shapes.py.
"""
import sys, os, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score_shapes as S


def made_of(pdf):
    doc = S.open_score(pdf)
    print(f"{doc.page_count} pages; made by {(doc.metadata or {}).get('producer')}")
    glyphs, words, fonts = collections.Counter(), collections.Counter(), collections.Counter()
    for page in doc:
        for b in page.get_text("dict")["blocks"]:
            for l in b.get("lines", []):
                for s in l["spans"]:
                    fonts[s["font"]] += len(s["text"])
        shapes = S.page_shapes(page)
        for g in shapes["glyphs"]:
            glyphs[(g["code"], g["size"])] += 1
        for t in shapes["texts"]:
            if t["font"].startswith("Tinos,Italic") and t["size"] > 8 and not t["text"].isdigit():
                words[t["text"]] += 1
        systems = S.find_systems(shapes)
        print(f"  page {page.number + 1}: {len(systems)} lines of music, {sum(len(s['bounds']) - 1 for s in systems)} bars between bar lines, {len(shapes['glyphs'])} music glyphs")
    if not any(f.startswith(S.FONT) for f in fonts):
        print(f"no text in the font {S.FONT}: this is not a Musicnotes score as this reader knows them (fonts: {sorted(fonts)})")
    print("the music font's glyphs:")
    for (code, size), n in sorted(glyphs.items(), key=lambda kv: (kv[0][0] in S.KNOWN, kv[0][0], kv[0][1])):
        print(f"  {'      ' if code in S.KNOWN else 'NEW   '}U+{code:04X} at {size:4} pt, {n:5} times: {S.NAMES.get(code, 'a brace half' if code in S.BRACE else 'a loudness letter' if code in S.DYNAMIC else 'a time figure' if code in S.DIGITS else 'not known to the reader')}")
    print("words in the music:", ", ".join(f"{w} ({n})" for w, n in words.most_common()) or "none")


def sheet(pdf, out):
    import fitz
    from PIL import Image, ImageDraw
    doc = S.open_score(pdf)
    seen = collections.OrderedDict()
    for page in doc:
        for g in S.page_shapes(page)["glyphs"]:
            e = seen.setdefault((g["code"], g["size"]), [0, page.number, g])
            e[0] += 1
    keys, cols, w, h = sorted(seen, key=lambda k: (k[0] in S.KNOWN, k[1], k[0])), 7, 220, 200
    img = Image.new("RGB", (cols * w, ((len(keys) + cols - 1) // cols) * h), "white")
    d = ImageDraw.Draw(img)
    for i, key in enumerate(keys):
        count, page, g = seen[key]
        clip = fitz.Rect(g["x"] - 14, g["y"] - 20, g["x"] + 20, g["y"] + 14)
        pix = doc[page].get_pixmap(matrix=fitz.Matrix(6, 6), clip=clip, alpha=False)
        cell = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
        cd = ImageDraw.Draw(cell)
        ox, oy = (g["x"] - clip.x0) * 6, (g["y"] - clip.y0) * 6
        cd.line((ox - 6, oy, ox + 6, oy), fill="red"); cd.line((ox, oy - 6, ox, oy + 6), fill="red")     # the glyph's origin
        cell.thumbnail((w - 10, h - 40))
        img.paste(cell, ((i % cols) * w + 5, (i // cols) * h + 5))
        d.text(((i % cols) * w + 5, (i // cols) * h + h - 30), f"U+{key[0]:04X} {key[1]}pt x{count}{'' if key[0] in S.KNOWN else '  NEW'}", fill="black")
    img.save(out)
    print(f"{len(keys)} kinds of glyph in {out}")


def bars(pdf, numbers):
    import score_reading as R
    reading = R.read(pdf)
    doc = S.open_score(pdf)
    for bar in reading["bars"]:
        if bar["number"] not in numbers:
            continue
        page = doc[bar["page"] - 1]
        shapes = S.page_shapes(page)
        systems = S.find_systems(shapes)
        system = systems[bar["line"] - 1]
        top, bottom = S.zones(systems, page.rect.height)[bar["line"] - 1]
        x0, x1, up, low = bar["x0"], bar["x1"], system["upper"], system["lower"]
        print(f"\nbar {bar['number']}: page {bar['page']} line {bar['line']}, x {x0:.1f} to {x1:.1f}; upper staff y {up['top']:.1f} to {up['bottom']:.1f}, lower {low['top']:.1f} to {low['bottom']:.1f}; {bar['time'][0]}/{bar['time'][1]}")
        for s in "UL":
            print("  read,", "upper:" if s == "U" else "lower:", "  ".join(R.describe(e) + f" @{e['x']:.1f}" for e in bar["staves"][s]))
        for g in sorted(shapes["glyphs"], key=lambda g: (g["x"], g["y"])):
            if x0 - 1 < g["x"] < x1 and top <= g["y"] < bottom:
                st = S.nearest_staff(system, g["y"])
                print(f"  glyph {S.NAMES.get(g['code'], hex(g['code'])):16} {g['size']:4} pt  x {g['x']:6.1f}  y {g['y']:6.1f}  {'upper' if st == 'U' else 'lower'} staff, place {S.staff_pos(system['upper' if st == 'U' else 'lower'], g['y']):6.2f}")
        for l in sorted(shapes["lines"], key=lambda l: (l["x0"], l["y0"])):
            if x0 - 1 < l["x0"] < x1 + 1 and top <= (l["y0"] + l["y1"]) / 2 < bottom and l["len"] < 200 and not (l["o"] == "V" and l["w"] > 0.8):
                kind = "ledger line" if l["o"] == "H" and 0.6 < l["w"] < 0.7 and not l["dashed"] else "stem" if l["o"] == "V" and l["len"] > 4.5 else "hook" if l["o"] == "V" else "level line" if l["o"] == "H" else "slanted line"
                print(f"  {kind:12} {l['w']:.2f} wide  ({l['x0']:6.1f},{l['y0']:6.1f}) to ({l['x1']:6.1f},{l['y1']:6.1f}){' dashed' if l['dashed'] else ''}")
        for b in sorted(shapes["beams"], key=lambda b: b["x0"]):
            if x0 - 1 < b["x0"] < x1 and top <= b["y0"] < bottom:
                print(f"  beam         x {b['x0']:6.1f} to {b['x1']:6.1f}  y {b['y0']:6.1f} to {b['y1']:6.1f}")
        for c in shapes["curves"]:
            if x0 - 12 < c["x0"] < x1 and top <= c["y0"] < bottom:
                print(f"  curve        ({c['x0']:6.1f},{c['y0']:6.1f}) to ({c['x1']:6.1f},{c['y1']:6.1f}), bowing {c['bow']}")
        for t in shapes["texts"]:
            if x0 - 1 < t["x"] < x1 and top <= t["y"] < bottom:
                print(f"  words        {t['text']!r} in {t['font']} {t['size']} pt at x {t['x']:6.1f} y {t['y']:6.1f}")


if __name__ == "__main__":
    a = sys.argv[1:]
    if len(a) == 1:
        made_of(a[0])
    elif len(a) == 3 and a[1] == "sheet":
        sheet(a[0], a[2])
    elif len(a) >= 3 and a[1] == "bar" and all(n.isdigit() for n in a[2:]):
        bars(a[0], [int(n) for n in a[2:]])
    else:
        print(__doc__)
        sys.exit(2)
