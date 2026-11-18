"""Turns a Musicnotes PDF into a MIDI file by reading the PDF's own shapes. See README.md beside this file.

usage: python -I pdf_to_midi.py score.pdf [out.mid] [--reading out.json] [--pictures folder] [--quiet]

Prints every bar as it read it (unless --quiet), every doubt it has and what it read as printed but thinks worth a look,
writes the MIDI (beside the PDF, with its name, unless told where), reads that MIDI back with a separate parser and
compares it with the reading, and with --pictures draws each line of the score with the reading written over it, to check
against the page. Exits 0 only when there are no doubts and the file read back matches.
"""
import sys, os, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score_reading, score_midi


def main(argv):
    opts, args, i = {}, [], 0
    while i < len(argv):
        if argv[i] in ("--reading", "--pictures") and i + 1 < len(argv):
            opts[argv[i]] = argv[i + 1]
            i += 2
        elif argv[i].startswith("--"):
            opts[argv[i]] = True
            i += 1
        else:
            args.append(argv[i])
            i += 1
    if not args or set(opts) - {"--reading", "--pictures", "--quiet"}:
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
    print(f"\n{os.path.basename(pdf)}: {flat['title'] or 'untitled'}")
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
    print(score_midi.write(flat, out))
    problems = score_midi.check(out, flat)
    print("read back from the file: " + ("every sound the page calls for is there, and nothing else" if not problems else f"{len(problems)} differences"))
    for p in problems:
        print("  !", p)
    if "--pictures" in opts:
        import overlay
        names = overlay.draw(pdf, flat, opts["--pictures"])
        print(f"{len(names)} pictures of the reading over the page in {opts['--pictures']}")
    print(f"wrote {out}")
    return 0 if not flat["doubts"] and not problems else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
