# Prima

Sight-reading and piano practice for the owner's Lenovo Idea Tab Pro, played on a Yamaha P-225B over USB MIDI. New music at
your level every day, read in time with a click that never waits, short drills for what reading leans on and for even
technique, and progress as honest numbers over weeks. Local data only: no accounts, no cloud.

- How it works: [docs/concept.md](docs/concept.md)
- How it's built, part by part: [docs/plan.md](docs/plan.md)
- The evidence behind it: [docs/research.md](docs/research.md), and for the Library, [docs/library.md](docs/library.md)

## Layout

```
Prima.slnx                The solution. Open it in Visual Studio.
build/Prima.Build/        Solution items: this folder's and the repo's top-level files and docs.
src/
  Prima.Core/             The music, the generator, scoring and practice logic. No UI; tested.
  Prima.UI/               The screens, the look (wwwroot/prima.css and its fonts) and the store (SQLite).
  Prima.Tablet/           The Android app: hosts the screens in a web view, and talks to the piano.
tools/Prima.Library/      Builds the Library's bundle of public-domain music into artifacts/library (below).
tools/Prima.Preview/      The screens in a browser against sample data, for checking them from the cloud.
tests/Prima.Core.Tests/   Tests for the Core.
docs/                     How it works, the plan and the research.
library/                  The Library's music (MusicXML, PDF and MIDI) and its manifest, packed into the app.
```

## One-time setup (Windows)

Visual Studio 2026 with the **.NET Multi-platform App UI development** workload, which brings the .NET 10 SDK, the Android SDK
and a JDK. The repo's [README](../README.md) has the details, and the Workout app's README has the CLI-only route.

## The Library's music

The Library comes with about 240 public-domain and openly licensed pieces: 133 engravings from MuseScore users (through PDMX,
as MusicXML) and 104 PDFs with their MIDI from the Mutopia Project, chosen by the named works in
[docs/library.md](docs/library.md) (Czerny, Lemoine, Duvernoy, Anna Magdalena's notebook, Mozart's first pieces, Schumann's
and Tchaikovsky's albums, Clementi, Burgmüller, Bach's Little Preludes and Inventions, Satie, Chopin's Preludes, Joplin and
more). They're in the repo, in `library/` (about 14 MB), and the tablet build packs them into the app, so there's nothing to
run first. The first time the Library opens on the tablet, it engraves each MusicXML piece once, cuts it into excerpts and grades
them (a minute or two, in the background).

To change the selection, edit the tool and run it; it rewrites `library/`, printing its progress as it goes, and then the
changes are committed like any others:

```bash
dotnet run --project Music/tools/Prima.Library
```

A full run takes about 15 minutes: PDMX's index (225 MB), one pass through its 1.9 GB archive of MusicXML keeping only the chosen
files, then Mutopia's PDFs and MIDI. Downloads are kept in `artifacts/library-cache`, so a second run only fetches what's missing;
`-- pdmx` or `-- mutopia` runs one source.

Imports on the tablet: MusicXML (`.musicxml`, `.xml`, `.mxl`), MEI, ABC and PDF. For MIDI, MuseScore files or Humdrum, export
MusicXML on the PC first.

## Putting it on the tablet

1. Tablet: **Settings > About tablet**, tap **Build number** 7 times, then turn on **USB debugging** (or **Wireless debugging**)
   in **Developer options**.
2. Plug the tablet into the PC and accept the "Allow USB debugging" prompt. `adb devices` should list it. With wireless
   debugging, pair once with `adb pair` and the code the tablet shows, then `adb connect`.
3. Visual Studio: set **Prima.Tablet** as the startup project, pick the tablet in the device dropdown, and press F5.
   CLI: `dotnet build src/Prima.Tablet -t:Run`

For the copy you use day to day, install **Release** (Ctrl+F5 with Release picked, or `dotnet build src/Prima.Tablet -t:Run -c
Release`). It compiles the whole app ahead of time, so it opens quickly, and it installs over a Debug build without losing your
data. Don't uninstall the app to fix a build problem: that deletes your practice history.

## Connecting the piano

The P-225B's **USB TO HOST** port (a square USB-B socket on the back) goes to the tablet's USB-C port, with a USB-B to USB-C
cable or a USB-B cable and an adapter. The tablet then has no free port, so it practises on battery unless a USB-C hub with power
delivery sits in between. Prima finds the piano by itself, and its name lights up at the foot of the rail.

Then try, in **Tools**:

- **Keyboard check**: play a few keys and the pedals, and the lowest and highest keys (A0 and C8). Leave it a minute for the
  MIDI path's jitter, from the piano's Active Sensing. **Play middle C** checks the piano plays notes Prima sends it, on channel
  16, with the grand piano voice.
- **Metronome**: does the click sound on time, and where (the tablet's speaker, or the piano if Android sends audio there)? In
  **Settings → Click**, "On the piano" plays it as short high notes on the piano instead, for headphones in the piano.
- **Timing**: play a key with the click for 16 beats and keep the offset. Redo it after changing where the click sounds.

Worth noting: whether the piano echoes the notes Prima sends back to it, which channel a split or dual voice sends on, the
jitter Keyboard check shows, and the timing offset and spread.

Then, in the **Library** (the first visit prepares the collection, a minute or two):

- Open a piece and turn pages with the soft pedal (on) and the sostenuto pedal (back), if the LP-1 pedal unit is fitted, or with
  C8 and A0. If a pedal does nothing, the piano may have controller messages switched off: hold METRONOME and RHYTHM and press
  C♯4 (manual, p. 54).
- **Follow me** on a minuet you know: does the lit bar keep up, and does the page turn early enough?
- In portrait, a PDF's half-page turns, and **Scroll at the tempo**: is a line at a time the right pace?
- Whether 7 mm staves read comfortably from the music stand (Settings → Notation changes the size).

## The looks

Prima has its own two looks, **Studio** (a music workstation: warm grey keys, an amber screen, one orange key) and **Vinyl** (a
record sleeve: deep teal, cream type, mustard), switched in Settings. They come from the
[Studio & Vinyl design system](https://claude.ai/artifact/XtgAHfVyw3L94e5DKrj2ii): its tokens and components are in
`src/Prima.UI/wwwroot/prima.css`, with Prima's own screens after them. Sheet music is black on paper in both. Prima uses the
Kit only for the way back and its settings store, not its Logbook and Telemetry looks.

## Checking screens from the cloud

The preview host runs the real screens against sample data on http://127.0.0.1:5299, and `tools/screens/screens.mjs` at the
repo's root screenshots them (CLAUDE.md has the details):

```bash
TZ=America/Los_Angeles dotnet run --project Music/tools/Prima.Preview
```

```bash
curl -s http://127.0.0.1:5299/seed/empty
```

The scenarios are `empty` (the first run), `fresh`, `today`, `summary` and `months`. The Library uses the bundle in
`artifacts/library` (built as above) and prepares it the first time it's opened. `/keys/piece?id=…&bpm=…` plays a piece's notes
in real time, for following along, and `/files/next?path=…` hands the next import a file on the machine.

## Licences

The fonts (Space Grotesk, Space Mono, Bebas Neue and Work Sans) are under the SIL Open Font License; their licences are next to
them in `src/Prima.UI/wwwroot/fonts`. Verovio (LGPL 3) and pdf.js (Apache 2.0) are in `src/Prima.UI/wwwroot`, each with its
licence. The Library's music keeps its own terms, shown piece by piece in the app's credits (**Library → Where the music
comes from**): public domain or CC0 from PDMX (the dataset itself is CC BY 4.0), and public domain, CC BY or CC BY-SA from the
Mutopia Project.
