# Prima

Sight-reading and piano practice for the owner's Lenovo Idea Tab Pro, played on a Yamaha P-225B over USB MIDI. New music at
your level every day, read in time with a click that never waits, short drills for what reading leans on and for even
technique, a catalogue of technique exercises with how to do each, and progress as honest numbers over weeks. Local data only:
no accounts, no cloud.

- How it works: [docs/concept.md](docs/concept.md)
- How it's built, part by part: [docs/plan.md](docs/plan.md)
- The evidence behind it: [docs/research.md](docs/research.md), and for the Library, [docs/library.md](docs/library.md)
- The research behind the Exercises tab, and what it proposes to build next: [docs/exercises.md](docs/exercises.md)

## Layout

```
Prima.slnx                The solution. Open it in Visual Studio.
build/Prima.Build/        Solution items: this folder's and the repo's top-level files and docs.
src/
  Prima.Core/             The music, the generator, scoring and practice logic, and reading a PDF into notation with Claude
                          (Transcription/). No UI; tested.
  Prima.UI/               The screens, the look (wwwroot/prima.css and its fonts) and the store (SQLite).
  Prima.Tablet/           The Android app: hosts the screens in a web view, and talks to the piano.
tools/Prima.Library/      Builds the Library's bundle of public-domain music into artifacts/library (below).
tools/Prima.Preview/      The screens in a browser against sample data, for checking them from the cloud.
tests/Prima.Core.Tests/   Tests for the Core. One reads a printed system with Claude, when PLAYGROUND_CLAUDE_KEY holds a key.
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
more). They're in the repo, in `library/` (about 17 MB), and the tablet build packs them into the app, so there's nothing to
run first. The MusicXML comes prepared: the tool has already engraved each piece as MEI (the `.mei.gz` beside it), split the
two collections engraved as one score (Chopin's Preludes, Tchaikovsky's album) into their pieces, and cut and graded the
excerpts, so the first time the Library opens on the tablet it only stores them, in a few seconds. Doing that on the tablet took
minutes and froze the screen.

To change the selection, edit the tool and run it; it rewrites `library/`, printing its progress as it goes, and then the
changes are committed like any others. Preparing needs [Node.js](https://nodejs.org) on the PATH, which runs the app's own copy
of Verovio:

```bash
dotnet run --project Music/tools/Prima.Library
```

A full run takes about 15 minutes: PDMX's index (225 MB), one pass through its 1.9 GB archive of MusicXML keeping only the chosen
files, then Mutopia's PDFs and MIDI, then half a minute preparing. `-- prepare` only prepares what's there again (after a change
to the grader, say). Downloads are kept in `artifacts/library-cache`, so a second run only fetches what's missing;
`-- pdmx` or `-- mutopia` runs one source.

### More music, for your tablet only

The tool also builds a **local collection**: about 1,200 more pieces whose terms allow personal, non-commercial use (CC BY-NC-SA
4.0) but not a place in this public repo, so they're never committed. They're Craig Sapp's Humdrum editions (Mozart's, Haydn's
and Scarlatti's sonatas, Joplin, Bach's 370 chorales) and the DCML's research corpora as MuseScore files (Beethoven's sonatas,
Bach's English and French Suites, Chopin's mazurkas, Grieg's Lyric Pieces, Kinderszenen, The Seasons, Debussy, Liszt, the Bach
sons and more), converted with webmscore. [docs/library.md](docs/library.md) has the list and what's left out. It needs Node.js
with npm, and git (without git it downloads each repository as a zip):

```bash
dotnet run --project Music/tools/Prima.Library -- local
```

It fetches from each repository only the folder it reads (`kern` or `MS3`) and the files beside it, about 300 MB in all, into
`artifacts/library-cache` as plain files (a sparse git clone, its `.git` then removed). It installs Verovio's build that reads
Humdrum and webmscore from npm there, converts and prepares everything into `artifacts/library-local`, and packs it as
`artifacts/prima-collection.zip` (about 26 MB). A first run takes about 7 minutes. If it stops because Windows won't let it
delete a folder in the cache, something has a file in it open (Visual Studio's Git window, or a virus scan of the new files):
close it, or delete the folder yourself, and run it again; it picks up where it stopped. Copy that file to the tablet (over USB, or through a cloud drive) and open it
in the Library with **Import files**; it's stored in the background, a minute or so. `-- local humdrum` or `-- local dcml`
runs one source again, `-- local prepare` only prepares again.

Imports on the tablet (**Import files**, one or several at once): MusicXML (`.musicxml`, `.xml`, `.mxl`), MEI, ABC, MIDI
(written out as notation: best from a file exported from notation software), PDF, and zips of any of them, like a download that
bundles a song's files. A PDF picked or zipped with a MIDI file of the same name takes that MIDI too, to be heard on the piano
and scrolled at its tempo. The local collection's zip is recognised by its manifest. For MuseScore files or Humdrum, export
MusicXML on the PC first.

## Putting it on the tablet

1. Tablet: **Settings > About tablet**, tap **Build number** 7 times, then turn on **USB debugging** (or **Wireless debugging**)
   in **Developer options**.
2. Plug the tablet into the PC and accept the "Allow USB debugging" prompt. `adb devices` should list it. With wireless
   debugging, pair once with `adb pair` and the code the tablet shows, then `adb connect`.
3. Visual Studio: set **Prima.Tablet** as the startup project, pick the tablet in the device dropdown, and press F5.
   CLI: `dotnet build src/Prima.Tablet -t:Run`

**Back up before installing from Visual Studio** (Settings › Your data › Back up). A Release install from Visual Studio has
wiped Prima's data before, and .NET's Android tooling has a known bug that can do it in Debug too
([dotnet/android#9627](https://github.com/dotnet/android/issues/9627)). For the copy you use day to day, take a release from
GitHub (below), or build Release and install it with `adb install -r`. That replaces the app and keeps its data, or stops with
an error and changes nothing:

```bash
dotnet build src/Prima.Tablet -c Release
```

```bash
adb install -r ../artifacts/bin/Prima.Tablet/release/com.saphyiere.prima-Signed.apk
```

Don't uninstall the app to fix a build problem: that deletes your practice history.

## Updates from GitHub

Prima is released with the other apps, from the repo's **Actions › Release › Run workflow**: a release, **Prima 1.0.N**, with the
app as `prima.apk`, whenever Prima changed since its last one. [tools/release/README.md](../tools/release/README.md) has the
one-time setup (the PC's key, as a GitHub secret) and how a release is made. Signed with the key Visual Studio signs with, a
release installs over the copy on the tablet as an update and keeps its data, wherever that copy came from. The repo is public,
so its releases are too; the app holds nothing secret (Claude's key is only ever on the tablet).

**On the tablet:** open the release in the tablet's browser, download `prima.apk` and open it. Android asks once to let the
browser install apps, then offers **Update**. An app signed with any other key can't install over Prima: Android refuses and
changes nothing.

## Claude's key

Reading a PDF's notes asks Claude, with your Anthropic API key. It goes onto the tablet once, as a file, and Prima keeps it in
secure storage ([the framework's README](../Kit/Playground.AI.Assist/README.md) has the Console side):

1. In the [Claude Console](https://platform.claude.com), make an API key in the workspace for the apps, the one with a monthly
   spend limit. The Console shows it once: keep it in your password manager.
2. Save it alone in a text file named `claude-key.txt`, outside the repo and outside OneDrive.
3. Open Prima once on the tablet, so its folder exists. Then, with the tablet plugged in:

   ```bash
   adb push claude-key.txt /sdcard/Android/data/com.saphyiere.prima/files/
   ```

4. Delete the file on the PC with Shift+Delete.
5. Open Prima, or press **Look for the key file** in **Settings › Claude**. Prima moves the key into secure storage and deletes the
   file on the tablet. **Test** says whether it works.

Push it again after reinstalling or restoring the app: secure storage isn't backed up. If the tablet is lost or the key leaks,
disable it in the Console, make a new one and push that.

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

Then, in the **Library** (the first visit stores the collection, a few seconds):

- Open a piece and turn pages with the soft pedal (on) and the sostenuto pedal (back), if the LP-1 pedal unit is fitted, or with
  C8 and A0. If a pedal does nothing, the piano may have controller messages switched off: hold METRONOME and RHYTHM and press
  C♯4 (manual, p. 54).
- A minuet you know, **Wait for me** and then **In time**: does the cursor wait for the right keys, keep up with the click, and
  scroll the next line into view in time?
- In portrait, a PDF's half-page turns, and **Scroll at the tempo**: is a line at a time the right pace?
- Whether 7 mm staves read comfortably from the music stand (Settings → Notation changes the size).
- With Claude's key on, a PDF's **Options › Read the notes…**: are the systems boxed right on the page, does the reading finish
  in about the time it says, and does each system's reading match its print? Keep it, play it, and press **Print** for the PDF.

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

The scenarios are `empty` (the first run), `fresh`, `today`, `summary` and `months`. The Library uses the bundle in `library/`
and stores it the first time it's opened. `/keys/piece?id=…&bpm=…` plays a piece's notes
in real time, for following along, and `/files/next?path=…` hands the next import a file on the machine.

Claude in the preview is a stand-in that never calls the API (`tools/Prima.Preview/PreviewClaude.cs`). `/claude/key` puts a key
on (one ending `a1B2`) and `/claude/key?clear=true` takes it off; `/claude/file` leaves a `claude-key.txt` for Settings to find;
`/claude/spend` adds three pieces read this month. A PDF's systems are read from saved readings of Czerny's No. 13, whatever the
PDF. `/claude/script?delay=…` sets how long each answer takes, in ms, and `?fail=OutOfCredit&after=2` (or `KeyRejected`,
`SpendLimit`, `Busy`, `Offline`, `Cannot`) makes it fail after that many systems.

## Licences

The fonts (Space Grotesk, Space Mono, Bebas Neue and Work Sans) are under the SIL Open Font License; their licences are next to
them in `src/Prima.UI/wwwroot/fonts`. Verovio (LGPL 3) and pdf.js (Apache 2.0) are in `src/Prima.UI/wwwroot`, each with its
licence. The Library's music keeps its own terms, shown piece by piece in the app's credits (**Library → Where the music
comes from**): public domain or CC0 from PDMX (the dataset itself is CC BY 4.0), and public domain, CC BY or CC BY-SA from the
Mutopia Project. The local collection's pieces are CC BY-NC-SA 4.0 and credited the same way. The Library tool fetches webmscore
(GPL) and Verovio from npm to convert them; neither goes into the app beyond the Verovio it already has.
