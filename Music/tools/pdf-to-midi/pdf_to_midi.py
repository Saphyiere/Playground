"""Turns a Musicnotes PDF into a MIDI file, and a MusicXML file if asked, by reading the PDF's own shapes. See README.md.

usage: python -I pdf_to_midi.py score.pdf [out.mid] [--musicxml [out.mxl]] [--reading out.json] [--pictures folder]
                                [--title "..."] [--composer "..."] [--arranger "..."] [--quiet]

Prints every bar as it read it (unless --quiet), every doubt it has and what it read as printed but thinks worth a look,
writes the MIDI (beside the PDF, with its name, unless told where), reads that MIDI back with a separate parser and
compares it with the reading, and with --pictures draws each line of the score with the reading written over it, to check
against the page. With --musicxml it also writes the score as MusicXML, the way it's printed (beside the PDF as .mxl
unless given a name), and reads that back too. --title, --composer and --arranger name what the print doesn't (a PDF split
out of a longer score has no head) or say it otherwise. Exits 0 only when there are no doubts and every file read back
matches.
"""
import sys, os, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score_reading, score_midi, score_musicxml

VALUED = ("--reading", "--pictures", "--title", "--composer", "--arranger")


def main(argv):
    opts, args, i = {}, [], 0
    while i < len(argv):
        if argv[i] in VALUED and i + 1 < len(argv):
            opts[argv[i]] = argv[i + 1]
            i += 2
        elif argv[i] == "--musicxml":
            named = i + 1 < len(argv) and argv[i + 1].lower().endswith((".mxl", ".musicxml", ".xml"))
            opts["--musicxml"] = argv[i + 1] if named else True
            i += 2 if named else 1
        elif argv[i].startswith("--"):
            opts[argv[i]] = True
            i += 1
        else:
            args.append(argv[i])
            i += 1
    if not args or set(opts) - set(VALUED) - {"--musicxml", "--quiet"}:
        print(__doc__)
        return 2
    pdf = args[0]
    out = args[1] if len(args) > 1 else os.path.splitext(pdf)[0] + ".mid"
    reading = score_reading.read(pdf)
    if "--quiet" not in opts:
        score_reading.print_reading(reading)
    flat = score_reading.flatten(reading)
    if "--reading" in opts:
        with open(opts["--reading"], "w") as f:
            json.dump(flat, f, indent=1)
    title = opts.get("--title") or flat["title"]
    print(f"\n{os.path.basename(pdf)}: {title or 'untitled'}")
    if not flat["bars"]:
        print("no bars of music found: nothing written")
        return 1
    print(f"bars {flat['bars'][0]['number']} to {flat['bars'][-1]['number']}, {len(flat['notes'])} noteheads, {len(flat['doubts'])} doubts")
    for d in flat["doubts"]:
        print("  ?", d)
    if flat["remarks"]:
        print("read as printed, but worth a look:")
        for r in flat["remarks"]:
            print("  -", r)
    print(score_midi.write(flat, out, title=title))
    problems = score_midi.check(out, flat)
    print("read back from the file: " + ("every sound the page calls for is there, and nothing else" if not problems else f"{len(problems)} differences"))
    for p in problems:
        print("  !", p)
    if "--musicxml" in opts:
        xml = opts["--musicxml"] if isinstance(opts["--musicxml"], str) else os.path.splitext(pdf)[0] + ".mxl"
        credits = reading.get("credits") or {}
        composer, arranger = opts.get("--composer") or credits.get("composer"), opts.get("--arranger") or credits.get("arranger")
        print(score_musicxml.write(reading, xml, title=title, composer=composer, arranger=arranger))
        print(f"title {title!r}; composer {composer!r}; arranger {arranger!r}")
        more = score_musicxml.check(xml, flat)
        print("read back from the MusicXML: " + ("every note the page prints is there, at its time and length, and nothing else" if not more else f"{len(more)} differences"))
        for p in more:
            print("  !", p)
        problems += more
        print(f"wrote {xml}")
    if "--pictures" in opts:
        import overlay
        names = overlay.draw(pdf, flat, opts["--pictures"])
        print(f"{len(names)} pictures of the reading over the page in {opts['--pictures']}")
    print(f"wrote {out}")
    return 0 if not flat["doubts"] and not problems else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
