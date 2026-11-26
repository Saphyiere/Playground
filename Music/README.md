# Prima

Sight-reading and piano practice for the owner's Lenovo Idea Tab Pro, played on a Yamaha P-225B over USB MIDI. New music at
your level every day, read in time with a click that never waits, short drills for what reading leans on and for even
technique, a catalogue of technique exercises with how to do each, and progress as honest numbers over weeks. Practice history
and accepted routines stay on the tablet; there is no application server or account. Optional PDF conversion and routine
planning send the requested source information and practice summaries to the configured inference provider.

- How it works: [docs/concept.md](docs/concept.md)
- How it's built, part by part: [docs/plan.md](docs/plan.md)
- The evidence behind it: [docs/research.md](docs/research.md), and for the Library, [docs/library.md](docs/library.md)
- The research behind the Exercises tab, and what it proposes to build next: [docs/exercises.md](docs/exercises.md)
- Optional local OMR verification and companion-app packaging idea: [docs/local-omr-plugin-idea.md](docs/local-omr-plugin-idea.md)

## Personal routines

Make your own with **Routines → New routine** (or Exercises'): name it, then put exercises and drills in it with the **+** on
any card in Exercises (or **Add to a routine** on an exercise's page). On its page **Edit** changes its name, each activity's
minutes and place, or takes one out. In a routine of yours each exercise plays as you last left it, and what you change
playing it is kept, as anywhere.

Or open **Routines → Ask Claude for one** (or Today's **Ask for routine**) for general practice or help with a library piece.
Choose the time available, optionally select bars and describe the difficulty, then generate a proposal. Review its order,
durations, hands, tempos and music before using it. The accepted routine becomes Today’s default until changed. Claude's are
kept as accepted; **Make it yours** copies one as a routine of your own.

**Routines**, under Practice in the rail, keeps yours and Claude's: three starred on top as cards with how each is going (a lamp
for each day of the last four weeks it was played, its clean runs, what's next and Start), the rest listed under them, and every
request that isn't a routine yet, until it's dismissed. A routine's page has **On Today**, to make it the one Today follows.
Each routine keeps its own progress for the day, so switching between a piece's routine and a technique routine loses nothing,
and one not on Today can be practiced any day without changing Today.

Suggestions use separate reading, rhythm and technique evidence. Sparse history stays provisional, and Wait for me results
are kept separate from timed playing. The model chooses existing activities or recipes; Prima constructs and validates source
excerpts, jumps, chord shapes and changes, accompaniment, rhythm reductions and coordination sequences. Imported MIDI hand
assignments need confirmation. Exercises retain their source edition when the library score changes.

Each activity's bar counts down its minutes: the time on it, played or not (a run stopped to read the music counts too). The
clock stops only while the activity is left or Prima is put away, and carries on when Prima is back in front. Once the time is
up the bar says so and Next stands out, without stopping you. **Next**, or **Skip**, goes straight to the next activity not done
yet, and back to the routine after the last. Where an activity has a play bar, a run played to its end starts again by itself
until you press Stop: each run is kept, and the bar counts the runs and shows how the last went, up or down on the one before.
The bar's **Loop** takes an exercise round and round without a break instead, each time round a pass (the same Loop every
exercise and technique drill has).

Each activity saves its own daily progress, and tomorrow starts fresh progress on the same routine. Completing practice does
not require perfect accuracy. In Claude's routines, feedback (**Too easy**, **About right**, **Too hard**) and comparable
results can suggest changes, which are reviewed before acceptance; the app does not request a new model routine automatically.

Routine generation continues in Android’s foreground service when the screen is left or locked, with a completion notification
when permitted. Only Cancel requests cancellation. If Android terminates execution, the saved request shows an interruption;
retrying a request whose response was not received is explicit and may incur another provider charge. A received draft is
validated locally after recovery. Failures never replace the previously accepted routine.

For a local, unpaid source/generator check:

```powershell
dotnet run --project Music/tools/Prima.Accuracy -- routine source.mid report.json --reference reference.json
```

The report and sibling music folder contain the source comparison and generated notation for each applicable template. A paid
evaluation is opt-in with `--model <explicit-model-id>` and `ANTHROPIC_API_KEY`; it performs one planner request, with automatic
request retries, validation retries and model fallback disabled.

## Layout

```
Prima.slnx                The solution. Open it in Visual Studio.
build/Prima.Build/        Solution items: this folder's and the repo's top-level files and docs.
src/
  Prima.Core/             The music, the generator, scoring and practice logic, and provider-neutral PDF notation contracts
                          (Transcription/). No UI; tested.
  Prima.UI/               The screens, the look (wwwroot/prima.css and its fonts) and the store (SQLite).
  Prima.Tablet/           The Android app: hosts the screens in a web view, and talks to the piano.
tools/Prima.Library/      Builds the Library's bundle of public-domain music into artifacts/library (below).
tools/Prima.Preview/      The screens in a browser against sample data, for checking them from the cloud.
tools/pdf-to-midi/        Makes the MusicXML and MIDI for a bought Musicnotes PDF by reading the PDF's own shapes (Python, on the PC).
tests/Prima.Core.Tests/   Tests for the Core. One reads a printed system with Claude, when PLAYGROUND_CLAUDE_KEY holds a key.
tests/Prima.UI.Tests/     Component, persistence and conversion-lifecycle tests, with a stand-in inference provider.
docs/                     How it works, the plan and the research.
library/                  The Library's music (MusicXML, PDF, MIDI), its manifest and RCM's levels, packed into the app.
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

`library/rcm-2022.json` says where The Royal Conservatory's Piano Syllabus, 2022 Edition, lists the bundle's pieces: each one's
level, its list (or that it's an étude) and the page, looked up by hand in the syllabus's free PDF. The tool doesn't write it,
so a run that changes a piece's id leaves its entry behind; `SyllabusTests` in the Core tests names it. Prima isn't affiliated
with The Royal Conservatory.

### More music, for your tablet only

The tool also builds a **local collection**: several thousand more pieces that aren't committed, either because their terms
allow personal, non-commercial use (CC BY-NC-SA 4.0) but not a place in this public repo, or because there's too much of it for
the repo. [docs/library.md](docs/library.md) has what's in it, what's left out and why, and the other sources looked at. Its
sources:

- **humdrum**: Craig Sapp's Humdrum editions (Mozart's, Haydn's and Scarlatti's sonatas, Joplin, Bach's 370 chorales), CC BY-NC-SA 4.0.
- **dcml**: the DCML's research corpora as MuseScore files (Beethoven's sonatas, Bach's English and French Suites, Chopin's
  mazurkas, Grieg's Lyric Pieces, Kinderszenen, The Seasons, Debussy with his Études and Arabesques, Liszt, Bartók's Bagatelles,
  Koželuch's sonatas, Frescobaldi, Poulenc, the Bach sons and more), converted with webmscore, CC BY-NC-SA 4.0. The dynamics
  its editors typed as lyrics are made into dynamics and hairpins (`LyricDynamics` in the Core).
- **asap**: the scores of the ASAP dataset (Bach's Well-Tempered Clavier, Chopin's études, ballades and scherzos, Liszt,
  Schubert's impromptus, Schumann's Kreisleriana, Rachmaninoff's preludes), CC BY-NC-SA 4.0.
- **pdmx**: more of PDMX, public-domain and CC0 piano music whose own composer field names a composer who died before 1931
  (an uploader's artist field, which names the music a score is linked to, doesn't count, nor does the surname with someone
  else's first name, "Michael Schubert"), beyond the named works the repo keeps, and no transcriptions ("Schubert/Liszt").
- **mutopia**: the rest of the Mutopia Project's keyboard music, as PDF with its MIDI (public domain, CC BY, and CC BY-SA,
  which is share-alike: a tier of its own), each graded from its MIDI, listed under its opus ("Op. 68") with its number in
  its title.
- **hymns**: the Open Hymnal's hymns that are public domain in tune, setting, words and translation (320 of its 352), each set
  for four voices on the grand staff, as a hymnal prints them, with its verses under the notes. They come as ABC, which the
  app's Verovio reads only one voice of, so abc2svg writes each as MEI first.

It needs Node.js with npm, and git (without git it downloads each repository as a zip):

```bash
dotnet run --project Music/tools/Prima.Library -- local
```

It fetches from each repository only what it reads (the `kern`, `MS3` or `ftp` folder, or ASAP's scores) and the files beside
it, into `artifacts/library-cache` as plain files (a sparse git clone, its `.git` then removed), with PDMX's index and its 1.9 GB
archive of MusicXML, kept there so a change to the selection doesn't download it again, and the Open Hymnal's ABC (one 1.2 MB
zip of its current edition): about 2.8 GB in all. It installs Verovio's build that reads Humdrum, webmscore and abc2svg from
npm there, converts and prepares everything into `artifacts/library-local`, and packs it into `artifacts/prima-collection`: a
zip for each part, none much over 40 MB, and `contents.txt`, which says what each holds, by source, with its licence. A first
run takes about an hour, most of it the PDMX archive's download and Mutopia's files, which its site gives one at a time; a
second, about ten minutes. If it stops because Windows won't let it delete a folder in the cache, something has a file in it
open (Visual Studio's Git window, or a virus scan of the new files): close it, or delete the folder yourself, and run it again;
it picks up where it stopped. `-- local mutopia` (or `humdrum`, `dcml`, `asap`, `pdmx`, `hymns`) runs one source again and
prepares the rest as they were, `-- local prepare` only prepares again, and `--artifacts D:\Prima` puts the cache, the
collection and the zips in another folder. A source that yields nothing (its site down, say) keeps the last run's pieces, says
so, and the run ends with an error (exit code 1): look at what it printed, and run it again.

**Onto the tablet.** Copy the folder to the tablet's Download folder, with the tablet plugged in (or through a cloud drive):

```powershell
adb push artifacts/prima-collection /sdcard/Download/
```

Then in Prima, **Library › Import files**, open Downloads › prima-collection and pick `prima-collection.zip`. It's stored in
the background, with its progress at the top of the Library (a minute or two). Then the same for each of the other zips, one at
a time: the tablet reads a zip it imports whole, so picking them all at once holds them all in memory. A zip whose pieces are
all there already says so and adds nothing, so importing one again is harmless. A collection built again needs nothing done by
hand: each of its zips names the whole collection, so the first one imported takes out of the Library the collection's pieces
the new build no longer has (their runs stay in your history), and each brings its pieces' titles and credits up to date.
Bundled pieces and your own imports are never touched. The hymns are `prima-collection-hymns.zip`
(2.6 MB, about 10 seconds to store); once it's in, they're under Chorales and hymns in the Library's kinds.

The zips stay in Download when Prima is uninstalled, so after a reinstall, import them again. Prima's own copies (Settings ›
App & data) keep the collection once it's imported, with everything else, so restoring a copy brings it back too. The
collection isn't built into the app: a release from GitHub couldn't have it, and an update would take out music that a build
from the PC had put in the bundle.

Imports on the tablet (**Import files**, one or several at once): MusicXML (`.musicxml`, `.xml`, `.mxl`), MEI, ABC, MIDI
(written out as notation: best from a file exported from notation software), PDF, and zips of any of them, like a download that
bundles a song's files. A PDF picked or zipped with a MIDI file of the same name takes that MIDI too, to be heard on the piano
and scrolled at its tempo. The local collection's zips are recognised by their manifests. For MuseScore files or Humdrum, export
MusicXML on the PC first.

## PDF conversion and review

The PDF toolbar opens **Convert to notation**. Confirm the proposed bar boundaries across both staves before starting;
dividers can be moved, added or removed. The final conversion action remains inside the form, separate from the toolbar's
Back action. Conversion and review controls do not respond to the piano-key shortcuts.

The tablet owns a durable conversion job. Leaving the reader or switching apps detaches the view; only **Cancel** cancels
the job. Android hosts active work in a foreground service, and the native renderer does not depend on a visible WebView.
Completed readings and renderings are checkpointed independently. Process termination or an execution deadline becomes
**Interrupted**, with an explicit resume action. An interrupted request may already have been charged; the app does not
pretend its response can be retrieved using an ordinary provider message ID.

Requests own small ranges of complete source bars. An output-limit response splits only its unfinished range; a single bar
that still exceeds the limit needs attention. Request and output-token allowances are finite and scale with the source;
an exhausted allowance requires an explicit additional request. The first adapter remains Anthropic, while job execution,
review, rendering and correction contracts use `IInferenceProvider` and explicit model settings.

Every mapped bar can be selected, marked correct, flagged, edited or rechecked. Rechecks save a proposed change for explicit
acceptance. Accepted bars must be reopened before editing, and stale proposals cannot replace newer work. A new full
conversion has a separate review session. Review history and undo retain written notation independently of its rendered
appearance. The original system-wide automatic comparison is not part of the new job pipeline: it made the measured
Sweden baseline worse, so a completed model comparison is not treated as evidence of correctness.

Printed octave lines retain their written pitches separately from sounding playback. Deterministic notation checks flag
unresolved ties/slurs/hairpins/pedal and marks that cannot retain their original anchor. These findings are shown separately
from model doubts and user flags; a valid continuation across saved chunks is not flagged merely because it crosses a chunk.

See [the implementation plan](docs/pdf-conversion-redesign-plan.md), [the Sweden accuracy baseline](docs/sweden-accuracy-baseline.md),
and [the Android rendering proof](tools/Prima.RenderingProof/README.md). Full source music and device evidence stay under
ignored `artifacts/` rather than in the repository. The offline evaluator is `tools/Prima.Accuracy`.

## A bought score's MusicXML and MIDI

A PDF bought from Musicnotes comes with no MusicXML or MIDI. A Musicnotes PDF isn't a picture, though: each notehead is a
glyph of their music font at an exact place. `tools/pdf-to-midi` reads those shapes on the PC, checks that every bar adds
up and both staves agree, and writes the MIDI beside the PDF, and with `--musicxml` the score as MusicXML too:

```powershell
python -I Music/tools/pdf-to-midi/pdf_to_midi.py "C:\Scores\Piece.pdf" --musicxml
```

The MusicXML is the one to give Prima for a piece's notes: it carries the score as printed (clefs, 8va lines, triplets, two
voices, the marks), so Prima draws it like the page. A MIDI holds only when keys go down and up, so Prima has to guess the
writing back from it; Prima pairs a PDF with a MIDI of the same name beside it. It costs nothing and takes seconds, and it
only works for Musicnotes' own engraving: a scan or another publisher's PDF still goes through **Convert to notation**.
[Its README](tools/pdf-to-midi/README.md) has what it reads, what it stops at, the choices it makes, and how to check a
reading against the page. The scores and everything made from them stay out of this repository.

## Putting it on the tablet

1. Tablet: **Settings > About tablet**, tap **Build number** 7 times, then turn on **USB debugging** (or **Wireless debugging**)
   in **Developer options**.
2. Plug the tablet into the PC and accept the "Allow USB debugging" prompt. `adb devices` should list it. With wireless
   debugging, pair once with `adb pair` and the code the tablet shows, then `adb connect`.
3. Visual Studio: set **Prima.Tablet** as the startup project, pick the tablet in the device dropdown, and press F5.
   CLI: `dotnet build src/Prima.Tablet -t:Run`

**Back up before installing from Visual Studio** (Settings › App & data › **Back up now**; Prima keeps a copy every two days
and before each update too, below). **Install Debug only, never a Release build from the PC:** installing a Release build
uninstalls Prima first, which erases its data whatever its key
([dotnet/android#8472](https://github.com/dotnet/android/issues/8472)); that's how Prima's data was lost on 15 November. Every
build is signed with the tablet's key, and before installing, the build's install guard checks the Prima on the tablet: when it
has another key or a newer version, the build stops with nothing installed rather than let the tooling uninstall it
([tools/release/README.md](../tools/release/README.md#installing-from-the-pc) has how). For the copy you use day to day, take a
release from GitHub (below).

Don't uninstall the app to fix a build problem: that deletes your practice history.

**Trying it without the piano:** a build from the PC has a stand-in piano, the preview's, in front of the real one: Settings ›
Keyboard › **Stand-in piano** turns it on, and Prima then plays as if a piano were plugged in. Its keys are played from the
web view's DevTools (`adb forward tcp:9339 localabstract:webview_devtools_remote_<Prima's pid>`):
`DotNet.invokeMethodAsync('Prima.UI', 'StandIn', 'on', {})`, then `'perform'` (the In time run in progress, `{ right: 1 }` for
every note right), `'answer'` (what a drill or Wait for me waits for; `apart: 250` plays a chord a key at a time) or `'play'`
(`{ notes: [60, 64, 67] }`), as the preview's `/keys` routes do. The piano's own keys still come through. A release has none
of it. What it plays is kept as practice, so back up first and restore afterwards to leave the history as it was.

## Updates from GitHub

Prima is released with the other apps, from the repo's **Actions › Release › Run workflow**: a release, **Prima 1.0.N**, with the
app as `prima.apk`, whenever Prima changed since its last one. [tools/release/README.md](../tools/release/README.md) has the
one-time setup (the PC's key, as a GitHub secret) and how a release is made. Signed with the key Visual Studio signs with, a
release installs over the copy on the tablet as an update and keeps its data, wherever that copy came from. The repo is public,
so its releases are too; the app holds nothing secret (Claude's key is only ever on the tablet).

**On the tablet:** open the release in the tablet's browser, download `prima.apk` and open it. Android asks once to let the
browser install apps, then offers **Update**. An app signed with any other key can't install over Prima: Android refuses and
changes nothing.

## Your data: copies, and getting them back

Prima keeps a copy of everything but Claude's key (the data file, the music imported, what reading a PDF made, the problem log)
in **Documents/Prima backups** on the tablet, a folder for each time Prima was installed: as it starts when the last copy is more
than two days old, before each update, and on **Back up now** in Settings › App & data. Uninstalling Prima doesn't touch it. Only
what changed is written, so a copy takes seconds.

**To get it back** (after a reinstall, or to go back): Settings › App & data › **Restore from a backup…** lists this install's
copies. **Choose a folder…** opens the tablet's folder picker on Documents/Prima backups: choose that folder, **Use this folder**
and **Allow**, and the copies of every install in it are listed. **Choose a file…** takes an export, or a Prima data file on its
own (an older backup, such as one in Download/Prima backups, which restores the data file alone). Choose a copy, and **Restore this
copy** replaces everything with it. Then push Claude's key again: it's never in a copy.

**On the PC**, with the tablet plugged in (`adb devices` lists it):

```powershell
pwsh ../tools/sync/pull-copy.ps1
```

It pulls the newest copy into Documents\Prima copies on the PC (all of it the first time, then only what's new), and writes its
files out under their own names, checked: `Prima 2026-11-15 1530\prima.db`, `library\…`, `errors.log`. Windows PowerShell runs it
too. `-From` reads a folder copied off in Windows Explorer instead (Internal shared storage › Documents › Prima backups). To put a
copy back, push its folder from the store the script keeps, then choose Documents/Prima backups in Restore from a backup…:

```powershell
adb push "$HOME\Documents\Prima copies\store\Installed 2026-10-01 090000" "/sdcard/Documents/Prima backups/From the PC"
```

**Export everything** puts it all in one zip for the share sheet (to Drive, or the PC); it restores like any copy.

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

## When something goes wrong

Prima keeps every warning and error in **Settings › App & data › Problems** (with **Copy**, to paste them into a message), and in
`errors.log` in the same folder as the key file, so it can be copied off even when Prima won't open:

```bash
adb pull /sdcard/Android/data/com.saphyiere.prima/files/errors.log
```

Or open the tablet in Windows Explorer over USB: Internal shared storage › Android › data › com.saphyiere.prima › files.
`errors.old.log` beside it holds the half megabyte before. A screen that fails shows **This screen stopped** with **Try again**,
and the rest of Prima keeps working; Android's own log has the errors too (`adb logcat -s Prima`), but it moves on in minutes.

## Connecting the piano

The P-225B's **USB TO HOST** port (a square USB-B socket on the back) goes to the tablet's USB-C port, with a USB-B to USB-C
cable or a USB-B cable and an adapter. The tablet then has no free port, so it practices on battery unless a USB-C hub with power
delivery sits in between. Prima finds the piano by itself, and the lamp on You's round key at the head of the rail lights up.

Then try, in **Settings → Tools**:

- **Keyboard check**: play a few keys and the pedals, and the lowest and highest keys (A0 and C8). Leave it a minute for the
  MIDI path's jitter, from the piano's Active Sensing. **Play middle C** checks the piano plays notes Prima sends it, on channel
  16, with the grand piano voice.
- **Metronome**: does the click sound on time, and where (the tablet's speaker, or the piano if Android sends audio there)? In
  **Settings → Keyboard & sound → Click**, "On the piano" plays it as short high notes on the piano instead, for headphones in the piano.
- **Timing**: play a key with the click for 16 beats and keep the offset. Redo it after changing where the click sounds.

Worth noting: whether the piano echoes the notes Prima sends back to it, which channel a split or dual voice sends on, the
jitter Keyboard check shows, and the timing offset and spread.

Then, in the **Library** (the first visit stores the collection, a few seconds):

- ▶ beside a piece plays its opening: on the piano with it plugged in, and with it unplugged on the tablet's speaker (Android's
  own MIDI player, which only the tablet can show). Does it start promptly, and does ■ stop it?
- Open a piece and turn pages with the soft pedal (on) and the sostenuto pedal (back), if the LP-1 pedal unit is fitted, or with
  C8 and A0. If a pedal does nothing, the piano may have controller messages switched off: hold METRONOME and RHYTHM and press
  C♯4 (manual, p. 54).
- A minuet you know, **Wait for me** and then **In time**: does the cursor wait for the right keys, keep up with the click, and
  scroll the next line into view in time?
- In portrait, a PDF's half-page turns, and **Scroll at the tempo**: is a line at a time the right pace?
- In Practice, the play key before the tempo starts the metronome: does it keep time as you play along, and do its settings
  (the metronome key beside Options) change where it's heard and how it sounds straight away?
- Whether 7 mm staves read comfortably from the music stand (Settings → Look & music → Notation changes the size).
- With Claude's key on, a PDF's **Convert to notation** toolbar button: are the systems boxed right on the page, does the reading finish
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
and stores it the first time it's opened. `/seed/practice/{who}/{minutes}` sets up a practice session on the Minuet in G to look
back on (`tools/Prima.Preview/PracticeSeeds.cs`): `general`, `learning`, `phrasing` or `mastering`, each with the days before
behind it, or `first`, the first sitting on the piece; 20, 40 or 60 minutes. Each run is played by a pretend pianist and scored
by the app's own scoring, and the session is left going: open the piece (`library/pdmx-QmUq8P8pvoDes682mBCKFiSSdvmfdAXak31oEPnxVNBAqv`)
and Finish practice is on its cover. Open the Library once first, so the piece is stored. `/keys/piece?id=…&bpm=…` plays a piece's notes
in real time, for following along, and `/files/next?path=…` hands the next import a file on the machine. `/keys/dual?on=true`
layers two voices on the simulated piano's keys, as the P-225's Dual does: each key and pedal then says its message on channels
1 and 2. The stand-in is the same one a build from the PC has on the tablet (`src/Prima.UI/Host/StandInKeyboard.cs` and
`StandInPlayer.cs`). `/keys/answer` plays what a drill or a Wait for me run is waiting for (`right=false` for a wrong key), a chord's keys
together, or with `apart=600` one at a time, that many ms apart. `/keys/perform` plays the In time run in progress: all at
once, ending it, or with `live=true` each note as it's due, which is how a looped run is played (the player stops when the
run does).

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
Mutopia Project; CC BY-SA is share-alike, a tier of its own, since whatever is made from such a piece keeps its licence. The
local collection is credited the same way: its pieces from Sapp, the DCML and ASAP are CC BY-NC-SA 4.0, for
this tablet only, its pieces from PDMX and Mutopia are on the same terms as the bundle's, and its hymns from the Open Hymnal are
public domain. The Library tool fetches webmscore (GPL), abc2svg (LGPL and GPL) and Verovio from npm to convert them; none goes
into the app beyond the Verovio it already has. `tools/pdf-to-midi` uses PyMuPDF (AGPL), installed with pip on the PC: it is
in neither the repository nor the app.
