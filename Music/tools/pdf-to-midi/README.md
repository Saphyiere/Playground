# A bought score's MusicXML and MIDI, read from the PDF's own shapes

Musicnotes sells a piano arrangement as a PDF, with no MusicXML or MIDI file. This tool makes both:

- the **MusicXML** is the score as printed: each note's value, its stem and beams, the voices, triplets, clefs, 8va lines,
  the marks. It is the one to give Prima for a piece's notes (**Import files**, or the piece's details to give a PDF its
  notes): Prima draws it like the page, plays it, and judges playing against it.
- the **MIDI** holds only when each key goes down and up, and how hard. Prima pairs a PDF with a MIDI of the same name
  beside it, and anything that plays MIDI can play it, but whatever reads a MIDI has to guess the writing back, and some of
  it can't be guessed (a clef, an octave line, which voice a note is in).

It does not look at the page. A Musicnotes PDF is not a picture: every notehead, rest, clef, sharp and flag is a glyph of
their music font at an exact place, and every staff line, stem, beam, bar line and tie is a drawn path. The tool reads those
shapes, so a note's pitch is geometry (how high its head sits on its staff), not something recognised by eye. Then it
makes the reading prove itself before it writes anything:

- every bar adds up to its time signature, in both staves;
- the two staves agree on when each printed column sounds;
- every notehead hangs on a stem (or is a whole note), and has the ledger lines its place needs;
- the bars it counts match the numbers the page prints at the start of each line;
- the MIDI it wrote, read back with a separate parser, holds every sound the reading calls for and nothing else;
- the MusicXML it wrote, read back, holds every note the page prints, at its bar, staff, time, length and key, with its
  ties, and nothing else, and each of its voices fills its bar.

Whatever fails is a **doubt**, printed with its bar. Nothing is guessed silently.

It is for Musicnotes' own engraving only (their font is called Doremi). A scan, a photo, or a PDF from another publisher
has no such shapes: Prima's own **Convert to notation** is for those.

## Running it

It needs Python 3 with PyMuPDF, and Pillow for the pictures: `python -m pip install --user pymupdf pillow`.

```powershell
python -I Music/tools/pdf-to-midi/pdf_to_midi.py "C:\Scores\Piece.pdf" --musicxml
```

That writes `Piece.mid` and `Piece.mxl` beside the PDF (`.mid` by the name Prima pairs it by). Give a second path to write
the MIDI elsewhere, and a name after `--musicxml` for the MusicXML. It prints each bar as it read it, then the doubts, then
what it read as printed but thinks worth a look or a listen, then what went into each file. It exits 0 only when there are
no doubts and every file read back matches.

| Option | What it does |
|---|---|
| `--musicxml [file.mxl]` | Writes the score as MusicXML too: zipped (`.mxl`), or plain text for a `.musicxml` or `.xml` name. |
| `--title`, `--composer`, `--arranger` | Names the piece, who wrote it and who arranged it where the print doesn't (a PDF split out of a longer score has no head), or says it otherwise. |
| `--pictures <folder>` | Draws each line of the score with the reading written over it, to check against the page. |
| `--reading <file.json>` | Saves the reading itself: every bar, and every note with its bar, hand, start, length and key. |
| `--quiet` | Leaves out the bar-by-bar print. |

Pieces of one work belong together in Prima's Library when their titles share what comes before a colon and they name one
composer: `--title "Interstellar Piano Suite: Murph"` for each movement of a suite split into one PDF per piece.

In the bar-by-bar print and the pictures, starts and lengths are in twelfths of a quarter note, the smallest step that fits
both sixteenths and triplets: a quarter is 12, an eighth 6, a sixteenth 3, a triplet eighth 4. `0:E4+G4/12` is a chord at
the bar's start lasting a quarter. `~` marks a tie, `>` an accent, `^` a fermata.

A picture has the printed line in the middle, the upper staff's reading above it and the lower staff's below, each chord's
label at that chord's place: its start and length, then its notes from the top down. Red is stems up, blue stems down. The
pictures are how a person (or a second reader) checks a reading: they show at a glance a clef change missed or an octave
line that stops a note early, which no sum can catch.

`python -I Music/tools/pdf-to-midi/selftest.py` checks the parts that need no score, the MIDI and MusicXML writers among
them. The reader itself can only be tried on a real score, and those don't belong in this repository (below).

## What it reads

| On the page | In the MIDI | In the MusicXML |
|---|---|---|
| Noteheads, stems, beams and flags, dots, whole notes, rests | Each note's key, start and length. | Each note's value, dots, stem and beams as printed, and the rests. |
| Clefs, and clef changes in the middle of a line | The pitch of everything after them on that staff. | The clefs, changing where the print changes them. |
| Sharps, flats and naturals | To the end of the bar on that line or space; a tied note keeps the one it was struck with. | The same pitches, and each sign on the note it's printed before. |
| 8va and 8vb lines, carried over line breaks, and a lone 8va or 8vb | The notes under them an octave up or down. | The line, from its first note to its last; the notes at the pitch they sound. |
| Time signatures and their changes, a short first or last bar | Every bar its own length; a pickup is a bar of its own. | The time signatures as printed; a pickup is bar 0, as the page numbers it. |
| Triplets, by bracket or on a beam | Two thirds of the written length. | Triplets, with a bracket where the print draws one, on its side of the notes. |
| Two voices on one staff | Things printed in one column start together. | Two voices: stems up and stems down. |
| Ties, within a line and over a line break | One sound. | Ties. |
| Repeat signs | Played out. | Repeat signs, double and final bar lines. |
| Tempo numbers, rit., poco rit., a tempo, fermatas | The tempo, below. | The tempo mark, the words, the fermatas. |
| pp to ff, hairpins, cresc. and dim., accents | How hard each key goes down, below. | The marks, where they're printed. |
| Small (grace) notes, rolled chords, tenuto lines | Below. | Small notes (slashed where the print slashes them), rolled chords (one roll through both hands where the print's line runs through both), tenuto lines. |
| Where each line and page begins | | The same breaks. |
| Title, composer, arranger, copyright line | The title and copyright line. | All four. |

The two hands are the two staves, on two tracks named for them, and one part of two staves.

## What it does not know, and says so

A **key signature**, **first and second endings**, **D.S., D.C., Coda and Fine**, tuplets other than triplets, a tempo
given for a note other than a quarter, pedal marks, staccato dots, and any glyph of the font it has not met. Each is a
doubt, not a guess: the score needs the reader taught first. So does a PDF that holds several pieces: split it into one file
per piece first, without resizing anything, because the reader goes by exact sizes. A curve between two different notes is
a slur: it changes no sound, it's listed under "worth a look", and neither file has it.

## The choices it makes

Some things on a page are not notes, and a MIDI file has to settle them one way:

- **Before the first loudness mark** the level is mp. The levels are pp 33, p 49, mp 64, mf 80, f 96, ff 112; an accent
  adds 14.
- **A hairpin** leads to the mark printed at its end. With no mark there it moves the level one step. If the mark at its
  end is no louder than where it began, the page means the music arrives there, so the hairpin starts a step away.
- **A small note** sounds just before the beat it leans on, an eighth of a quarter long.
- **A rolled chord** is written as a plain chord.
- **rit.** slows evenly to three quarters of the tempo (poco rit. to seven eighths) by the next tempo word, a fermata or
  the end, over two bars of 4/4 at most. **a tempo** goes back to the last tempo number.
- **A fermata** halves the tempo for the length of its note.
- **A key struck again while another voice still holds it** ends the first sound there, and the new one lasts as long as
  either.
- **Ties are read as printed.** A chord tied only by its top and bottom notes sounds its middle notes again.

The MusicXML settles fewer, since it says what the page says: a mark goes with the first note at or after its place, and a
word printed straight after a loudness mark goes with it ("mp sub."); a hairpin starts at the note it begins over and ends
at the last note it reaches, or at the bar line where it runs up to one; ties curve over or under as printed; and the key
is C (the reader knows no key signature, so a score with one stops at a doubt first).

## When the print itself is wrong

Scores have misprints, and the checks find the ones that break a bar's sum. Two kinds are mended, and each mend is listed
under "worth a look" with its bar: three beamed eighths with no 3 over them in a bar that otherwise overflows are a
triplet (the MusicXML prints the 3 the page forgot); and where one staff is whole and the other stops short, the one note
whose longer length makes both staves agree on every column (a flag that shouldn't be there) is read at that length.
Where no mend is the only one that fits, the doubt stands.

An octave mark printed between the two staves is read as printed, toward the staff its hook points at, and listed as
worth checking by ear: such marks are sometimes printed for the wrong direction.

## A score it stumbles on

```powershell
python -I Music/tools/pdf-to-midi/look.py "C:\Scores\Piece.pdf"                  # what the file is made of; glyphs it doesn't know say NEW
python -I Music/tools/pdf-to-midi/look.py "C:\Scores\Piece.pdf" sheet glyphs.png # a picture of each kind of glyph, its code under it
python -I Music/tools/pdf-to-midi/look.py "C:\Scores\Piece.pdf" bar 81           # every shape inside a bar, and how it was read
```

A new glyph gets its code named in `score_shapes.py` and its meaning in `score_reading.py`. A doubt names its bar: `look.py
... bar N` shows what the file draws there, which is the evidence for deciding what the page means.

| File | What is in it |
|---|---|
| `pdf_to_midi.py` | The command. |
| `score_shapes.py` | What the PDF draws: the font's glyphs by code, the paths, staves and bar lines. |
| `score_reading.py` | Shapes into bars of notes, with the checks, the mends and the doubts. |
| `score_midi.py` | The MIDI writer, and the parser that reads a file back to compare. |
| `score_musicxml.py` | The MusicXML writer, and the reader that reads a file back to compare. |
| `overlay.py` | The pictures. |
| `look.py` | Looking at a score: its glyphs, its words, one bar's shapes. |
| `selftest.py` | Checks start times in a bar and both writers, with no score. |

## The music stays out of the repository

A bought score is licensed to its buyer for their own use, and so is anything made from it. The PDF, the MIDI, the
MusicXML, the saved reading and the pictures all hold the music: keep them beside the PDF or under `artifacts/` (which git
ignores), never in a commit, and never send the PDF to an online converter. The tool needs none: it runs on the PC and
sends nothing anywhere. Both files carry the print's own title and copyright line; the name the PDF was sold to stays out.

PyMuPDF is under the AGPL. It is used here only by this tool on the PC, installed with pip; none of it is in the repository
or in any app.
