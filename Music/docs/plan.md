# Prima: the build plan

How Prima gets built: its projects, how the keyboard, the click and the sheet music work, what the logic does, what each screen
shows, and the order of the work. It turns [concept.md](concept.md) (how the app works) and [research.md](research.md) (the
evidence) into code. A separate session reviews this plan against both before any code is written. The Library also gets its own
research, in `library.md` when it lands; the Library section below says what waits for it.

Where a number or rule is our own choice rather than a finding, it says so.

## Decided by the owner

- **Name and icon:** Prima, with the LCD icon from the Studio icon sheet.
- **MIDI only.** The owner plays a Yamaha P-225B (88 keys, A0 to C8) plugged into the tablet over USB. No microphone, no
  on-screen keyboard, and no picture of a keyboard anywhere. A wrong note is named in words and shown on the staff.
- **The tablet:** a Lenovo Idea Tab Pro Gen 2 (13-inch 3.5K screen at 144 Hz, Snapdragon 8s, 8 GB). The app works in landscape;
  turned to portrait, it shows a full page of sheet music. No phone app for now.
- **Real sheet music.** Notation looks like printed music: black engraving on paper, on the grand staff (treble and bass) for
  piano, the same in both looks. The look goes around the sheet, with light marks on top (the bar you're on, the tempo, a
  cursor). The mockups' single treble staff in the LCD style was a shortcut, not the design.
- **The looks:** Studio and Vinyl, the app's own (not the Kit's Logbook and Telemetry), switched in Settings.

## The projects

```
Music/
  Prima.slnx                The solution: build, kit, src, tools, tests, like Workout's.
  README.md                 Building it, putting it on the tablet, connecting the piano.
  docs/                     concept.md, research.md, this plan, and library.md from the Library research.
  build/Prima.Build/        Solution items: the app's and the repo's top-level files and docs.
  src/Prima.Core/           net10.0, no UI: the music model, MEI in and out, the generator, scoring, the note schedule,
                            levels, the daily plan, the streak and progress. All of it tested.
  src/Prima.UI/             Razor class library: the screens, the look (prima.css, fonts), the sheet (Verovio and prima.js),
                            the store (SQLite), the practice sessions, and the interfaces a host fills in.
  src/Prima.Tablet/         The Android app, MAUI Blazor Hybrid on net10.0-android: USB MIDI in and out, the click,
                            keeping the screen on, rotation, picking files, the icon and splash. com.saphyiere.prima.
  tools/Prima.Preview/      The same screens as a Blazor Server page on http://127.0.0.1:5299, against sample data, with a
                            simulated keyboard, for checking screens from the cloud.
  tests/Prima.Core.Tests/   xUnit v3 on Microsoft.Testing.Platform.
```

- The projects copy Workout's settings: the same package versions (Dapper, Microsoft.Data.Sqlite,
  Microsoft.AspNetCore.Components.Web, MAUI), `UseInterpreter` and profiled AOT off, `EnableDefaultCssItems` off, and
  `SupportedOSPlatformVersion` 24. Android's MIDI API needs 23. The click's low-latency mode needs 26 and is skipped below it.
- Prima.UI references the Kit for `AppNavigation` (the way back and the back gesture) and `ISettingStore`. It doesn't load
  `kit.css`. The look setting is Prima's own: `LookSetting`, shaped like the Kit's `ThemeSetting`, with Studio and Vinyl.
- The manifest declares `android.software.midi` as required. USB MIDI through Android's `MidiManager` asks for no permission.

## How it fits together

1. **The keyboard.** MIDI notes arrive on Android's MIDI thread, each with a timestamp in nanoseconds on the system's
   monotonic clock (`System.nanoTime`).
2. **The click.** It's rendered by `AudioTrack`. Its timestamp API puts each beat on that same clock, so beats and notes compare
   directly.
3. **A practice session** (in the UI project) collects the notes and the beat times. When a passage or try ends, the Core scores
   it, the store keeps the attempt with its raw notes, and the screen shows the result.
4. **Notation.** The Core writes the music as MEI, an XML format for scores: generated passages and drill items, or the stored
   MEI of a piece. The sheet component hands it to Verovio in the web view, which engraves it as SVG with an id on every note.
   `prima.js` draws the cursor while you play, and the marks by id afterwards.
5. **The cursor is only a guide.** No score depends on the web view's timing.

## Notation: real sheet music

- **Engraved by Verovio** (6.3.0; LGPL 3.0; [verovio.org](https://www.verovio.org)), the open-source engraving library RISM
  develops, used by many digital editions.
  - Prima ships its single-file build, `verovio-toolkit-wasm.js`: 7.3 MB with WebAssembly inside. It goes in
    `Prima.UI/wwwroot/verovio` with its licence and is loaded once in the background at start.
  - In this cloud's Chromium it was ready in 0.2 s. A four-bar grand staff took under 50 ms to engrave in Node.
  - Verovio reads MEI, MusicXML, Humdrum and ABC. It writes SVG with ids, a time map (each note's onset in beats and ms) and
    MIDI.
- **The sheet** is paper with ink: the tokens `--mu-sheet` (a warm white) and `--mu-sheet-ink` (near black), the same in both
  looks. Studio sets it in the device's dark bezel, with the LCD readouts on the bezel. Vinyl lays it on the teal ground like
  an insert from a sleeve.
- **Music fonts:** Leipzig (Verovio's default) to start, with Bravura and Leland in Settings → Notation. All three come inside
  Verovio, so nothing else ships.
- **The grand staff** is used for passages, notes, ledger lines, intervals, chords, keys, keyboard geography, play back, scales,
  arpeggios and finger patterns. The Rhythm drill uses a one-line rhythm staff.
- **Size:** printed piano music's staff is about 7 to 8 mm tall. The sheet starts there on the 13-inch screen, and Settings has
  smaller and larger. `library.md` may refine this for music-stand distance.
- **Layouts:**
  - Landscape: a passage on one or two systems filling the sheet; drill items larger, a few notes at a time.
  - Portrait: Verovio's page layout at the screen's size, a full page for pieces, and for a passage when the tablet is turned.
- **Overlays.** `prima.js` draws them on the SVG and never changes the engraving.
  - While playing: a thin cursor in the look's accent color moving with the beat. The bar and beat show on the readouts.
    Nothing marks right or wrong until the end; feedback comes after the phrase.
  - After the last bar, each note is marked by its id:
    - right notes stay black;
    - a wrong note turns the miss color, with what you played as a small grey notehead on the staff and its name (G4→A4);
    - a note early or late beyond the audible band gets a small arrow above it;
    - a missed note is outlined.
  - Read ahead covers the bars you've played and limits the view ahead (see Sight-read).
- **No fingering in sight-reading**, as in the exams. Scales, arpeggios and finger patterns show fingering numbers.

## The keyboard and the click

### MIDI in

- **The connection:** the P-225B's USB to Host port, a USB-B to USB-C cable (or an adapter), then the tablet.
- **Finding the piano:** `MidiManager` lists USB MIDI devices and reports plugging and unplugging. Prima opens the first one
  with an output port, or the one chosen in Settings.
- **Reading it:** a `MidiReceiver` parses note on and off (velocity 0 is off), running status, and the pedals (damper CC64;
  soft CC67 and sostenuto CC66 if a three-pedal unit is fitted). Each event keeps its nanosecond timestamp.
- **Ignored input:** notes on the channel Prima sends on (2), in case the piano echoes them back.
- **Status:** the shell shows it with a dot: "P-225B" when connected, "Plug in your keyboard" when not.

### MIDI out

- Prima opens the piano's input port and sends notes with future timestamps. Android plays them at those times, and `flush()`
  cancels the rest.
- **Used by** Duet, Hear first, and Listen and play back, so their sound comes from the piano itself.
- **If the piano can't receive,** those three are hidden, with a line saying why.

### The click

- **Rendering:** `AudioTrack` at 48 kHz in low-latency mode where available.
  - A writer thread keeps about 150 ms of audio queued. Each click (a 15 ms blip, higher on the downbeat) is placed on an exact
    frame.
  - `AudioTrack.getTimestamp` maps frames to the system clock, so each beat's time is known on the MIDI clock.
- **The click never waits.**
  - Count-in: one bar.
  - It can play throughout, play the count-in only, or go silent; the grid keeps running.
  - Volume and sound are in Settings.
- **Where it sounds:** wherever Android routes audio, the tablet's speaker or the piano if it shows up as a USB audio device.
  With headphones in the piano, the tablet's speaker isn't heard. So there's a second choice: "Click on the piano". The click
  becomes short high notes sent over MIDI on channel 2, timed by their scheduled timestamps. The owner tries both.

### Timing calibration (Tools → Timing)

- **The test:** play any key with the click for 16 beats.
- **The offset:** the median of (note − beat) is kept as the timing offset and taken off every note played. It's the delay of
  the click's sound, the MIDI path and your own habit together; without a hardware loop they can't be told apart, and only the
  sum matters.
- **The spread** (interquartile range) is shown and kept as the noise floor, below which differences aren't shown.
- **Redo it** after changing where the click sounds.

### Keyboard check (Tools)

- The last notes played: name, velocity and pedal.
- Press the lowest and highest keys to confirm A0 and C8 arrive.
- A touch check: velocity over a few even presses. If the piano's touch is set to Fixed, every velocity is the same, so evenness
  of touch is hidden everywhere.
- No picture of a keyboard.

### The screen

- It stays on while a practice screen is open (`FLAG_KEEP_SCREEN_ON`).
- Rotation follows the tablet. The activity handles orientation changes itself, so the page just resizes.

## The Core

### The music model

- **Pitch:** a MIDI number (21 to 108 on 88 keys) and a spelling (step, alteration, octave), spelled from the key.
- **Durations:** ticks, 480 to a quarter note. That fits triplets, sixteenths and thirty-seconds.
- **A score:** measures, each with a voice of events per staff (note, chord, rest), plus ties, beams, tuplets, clefs, key and
  metre (and their changes), tempo and fingering.
- **`MeiWriter`** turns a score into MEI, with an id on every note, chord and measure.
- **`MeiReader`** reads it back, including the MEI Verovio makes from an imported file, with the same ids. Its scope:
  - notes, chords, rests, layers and staves;
  - tuplets and ties;
  - key, metre and clef changes.
- **Timing:** every event knows its bar, its onset in beats, and, at a tempo, its time in ms.

### The generator

Seeded and reproducible. Prima uses its own small random generator (SplitMix64), so a passage can be rebuilt from the seed kept
with its attempt, in any .NET version.

**Levels.** The twelve levels are research.md's merged ladder (section 6). The values no board gives are filled in by us: the
bars at levels 1 and 12, the left hand's long notes at level 5, and the last two columns.

| Level | Bars | Keys | Time | Rhythm added | Hands and texture | Range, leaps, ledger lines (ours) | Tempo (ours) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 2 | C position | 4/4 | Whole, half, quarter | One hand, fixed five-finger position, steps in one direction | C4–G4 or C3–G3; steps | 60–80 |
| 2 | 4 | C | + 2/4 | Half rest | Melody split between the hands | Both C positions; steps and 3rds | 60–84 |
| 3 | 4 (2/4: 6) | C, G, F; A, D minor | | Paired eighths | Hands separate; tonic to dominant | Five-finger positions on those keys; up to a 5th | 60–88 |
| 4 | 4–6 | Up to 1 sharp or flat | + 3/4 | Dotted half, quarter rest, ties, 4 eighths | Any five-finger position, may move beyond | 1 ledger line | 60–92 |
| 5 | 4–8 | Up to 2 | | Whole, dotted quarter and eighth | **Hands together**: left hand in long notes | An octave a hand; up to a 6th | 60–96 |
| 6 | Up to 8 | Up to 3 | + 3/8 | Dotted eighth and sixteenth, sixteenths | Outside the five-finger position; two-note chords | Up to a 10th a hand; octave leaps; 2 ledger lines | 60–100 |
| 7 | 8 | Up to 3 | + 6/8 | Pickups, chromatic notes | Position shifts | Two octaves a hand | 60–104 |
| 8 | 8–12 | Up to 4; to the dominant | | Syncopation | Four-part chords; accompaniment patterns | 3 ledger lines | 60–108 |
| 9 | 12–16 | Up to 4 | + 9/8, 5/8, 5/4 | **Triplets** | Clef changes; pedal marks | | 60–112 |
| 10 | 16–20 | Up to 5 | + 7/8, 7/4 | | 8va; wider range | 4 ledger lines | 60–116 |
| 11 | About a page | All to 5 | + 12/8, 2/2, changes | Duplets | Three-note chords a hand, spread chords, ornaments | The whole keyboard | 60–120 |
| 12 | A page or more | All | Mixed | Free | Full textures | The whole keyboard | 60–132 |

- **Tempo** is the beat's (the dotted quarter in 6/8). A level's top tempo comes down when its shortest notes are short: no faster
  than about five notes a second below level 9 (ours).

**A passage** is built in this order:

1. **The frame.**
   - The key rotates rather than repeating the last one.
   - Then the metre and the length.
2. **A harmonic plan.**
   - Progressions from the I–IV–V–I family and their relatives.
   - A half cadence midway from 8 bars up, and an authentic cadence at the end.
3. **Rhythm.**
   - Cells from the level's vocabulary, with repetition: bar 1's rhythm returns or is sequenced.
   - The last bar holds.
4. **Melody.**
   - Chord tones on strong beats, with steps and passing notes between.
   - Leaps stay within the level's limit and resolve by step, inside the hand's position.
5. **The left hand by level.**
   - It goes from none, to a shared melody, to its own phrases, to long notes on roots, to two-note chords, to broken chords,
     to four-part chords.
   - Then chromatic passing notes and pickups (7), syncopation (8), triplets and clef changes (9), 8va (10) and ornaments (11).
6. **Styles.**
   - Four templates rotate from level 5: chorale, dance (waltz bass in 3/4), march (2/4) and song (melody over broken chords).
   - Zhukov's course read in several styles. The templates are ours.
7. **Spelling and ending.**
   - Notes are spelled from the key, and the passage ends on the tonic.
   - It stays inside A0–C8 and inside the level's range.

**Rhythm passages** use the ladder's rhythm column on a one-line staff, in RCM's shape: one bar of beat, then 2 bars (early levels),
4 (middle) or 4 to 6 (top).

**Drill material.** Each drill's items come from its own ladder in concept.md:

- Notes and ledger lines: from the note schedule.
- Intervals and shapes.
- Chords.
- Key signatures.
- Keyboard geography.
- Listen and play back: five notes of a scale, then six to eight, then more keys.

**The scale book** has the standard fingerings for:

- every major and minor (harmonic and melodic) scale;
- arpeggios in root position and inversions;
- hands separately and together, over 1 to 4 octaves;
- in ABRSM's order of keys.

### Scoring a passage

**Inputs:** the expected notes (id, pitch, onset in beats, bar, hand), the click's beat times, the notes played (pitch, on and off
times, velocity), and the timing offset. Expected times come from the beats as actually played by the click, so the grid is what
was heard.

**Matching.**

- **The window.** Per pitch, an order-keeping alignment (dynamic programming) pairs expected and played onsets within ±W.
  - W is 40% of a beat, held between 0.12 and 0.30 s (ours).
  - The research used ±0.25 s at fixed tempos. Ours scales with tempo.
- **Wrong notes.** A played note left over within W of a missed note is a wrong note, named: "played G4 for A4". It's sorted
  as:
  - lost on the keyboard: one or two semitones off;
  - an octave off;
  - a reading error: anything else.
- **The rest:** other notes played are extra notes, and expected notes never played are missed.

**Per right note.**

- Its timing error is in ms and as a share of the beat.
- The lean is the signed mean error, shown on its own.
- Each note's error after the lean is graded:
  - under 5% of the beat: in time;
  - 5 to 10%: audible;
  - over 10%: off.

**Per passage.**

- **In-time accuracy:** right notes within 10% of the beat (after the lean), out of the notes in the passage. The headline.
- **Correct notes a minute.**
- **Kept going:** no stretch longer than a bar where notes were due and nothing was played.
- **Zhukov's four:** tempo drift (a line fitted through played against expected times), extra notes, missed notes, and the RMS
  timing of right notes.
- **Lost-on-the-keyboard errors,** kept apart.
- **Errors per bar,** for "one spot".
- **How together each chord landed** (the spread of its onsets).

### Rhythm

- **Matching:** taps on any key are matched to the rhythm the same way, without pitch.
- **Measured**, in % of the beat:
  - the lean (signed mean);
  - steadiness (the spread after the lean);
  - drift while the click is silent (the fitted beat against the set one);
  - missed and extra taps.
- **The opening bar of beat** is graded on its own.
- **Kept over time:** the figures missed most, by rhythm cell.

### Technique

- **A valid run** has every note right and in order. Otherwise it doesn't count for evenness and shows as a note error.
- **Evenness and tempo.**
  - A line is fitted through onset against note index: the run's own tempo.
  - The spread of the gaps between notes after that fit (IOI SD) is shown in ms and as a share of the fitted gap.
  - Tempo drift: the fitted tempo against the set one, and the first half's gaps against the second's.
- **Thumb crossings:** each crossing's gap minus the fitted gap, averaged per crossing and hand.
- **Octave seams:** gaps across the tonic, kept apart from the rest.
- **Touch:** velocity spread, and mean velocity per finger (the fingering is fixed).
- **Legato:** the overlap at each step (the last note's release minus the next note's start; positive is overlap), as a mean and
  spread, with the pedal noted.
- **Hands together:** the onset difference between the hands, as a mean and spread.
- **Over 3 to 5 runs:** the consistent part (the same gap long every time) apart from the random part.
- **Small differences:**
  - under 10 ms shows as "below what anyone can hear";
  - under the keyboard's measured noise floor shows as "within the keyboard's noise".
- **Finger independence:**
  - gaps and velocity per finger;
  - trill rate and its steadiness;
  - for held-note patterns: whether held keys stayed down, and whether a neighbouring key sounded.

### Note review

**Cards.** One per note and clef, since the same pitch looks different in each clef. Leitner boxes 1 to 6 come back after 0 days
(this session), 1, 2, 4, 8 and 16 days. Those gaps are ours, in the spirit of expanding, half-life-style schedules.

**Moving between boxes.**

- Right and fast: up a box.
- Right but slow: "not yet". It stays in its box and comes back tomorrow.
- Wrong: back to box 1, and asked again later in the same session.

**Fast** means under the target time: 1.25 × your median on right answers over the last two weeks, held between 0.8 and 2 s
(ours). The research sets no number.

**Order.**

- Due cards first, with yesterday's slow and missed ones ahead.
- Mixed so confusable notes come near each other: the same line or space in the other clef, a third apart, octave twins.
- Never the same note twice in a row.

**New notes**, one to three a day, in landmark order:

1. Middle C in both clefs.
2. Treble G4 and bass F3.
3. Treble C5 and bass C3.
4. C6 and C2.
5. The landmarks' neighbours, by step and skip.
6. The ledger lines.

Each new note comes in a short block next to one or two known notes that contrast with it, then joins the mix.

**Fluent** means right and fast in each of the last three answers, over at least two days.

### Levels and how they move

**Reading.** A level from 1 to 12, and a tempo inside the level's range. After each passage:

- **Good:** 90% or more in time and kept going.
  - Three good passages in a row raise the tempo 5%.
  - At the top of the range, the next level starts at its lowest tempo.
- **Hold:** between 75 and 90%.
- **Too hard:** under 75%, or you stopped. The tempo drops 10%.
  - At the bottom of the range, two more under 75% make the next passage simpler by one step back: first the previous level's
    rhythm or texture, then its keys.
  - It returns when passages go well.
- **Aim:** about 85% across passages. All these numbers are ours, as the research gives a target, not a rule.
- **Finding a level:** the first time, Sight-read offers "Find my level". It plays passages from levels 1, 3, 5 and up until one
  falls under 75%, then starts one level below.

**Rhythm.** Its own level on the ladder's rhythm column, plus the click's stage (throughout, count-in only, silent). The click
fades on later tries.

**Technique**, per scale or arpeggio and per hand or hands:

- **Starting tempo:** about 85% of your comfortable speed, taken from a first run without the click (ours; the research says
  to start below your top speed).
- **Moving up:** +4 beats a minute when the gap spread stays under your target on 3 of 4 valid runs. Targets come from your own
  baseline, since there are no amateur norms.
- **Caps** (ours, precautions): 5 runs per item per set, about 8 minutes of technique a day, and a line saying to stop if
  anything hurts.

### The daily plan

- **Built once a day** and kept. It has the concept's five steps and the pause.
- **Mixes across days:** the scale and arpeggio of the day, rhythm figures and the variant.
- **The five-minute plan:** note review and two passages.
- **Length:** the daily minutes come from the profile. The app never raises them; if the owner raises them more than about 25%
  over last week (ours), a line suggests going gradually.
- **One spot:** the bar with the most errors today, 20% slower, then its line. Or a piece from the Library.

### The streak and the habit

- **A practised day** has 5 minutes or more, or the five-minute plan done.
- **Freezes:** one earned for every 7 practised days, up to 2 banked, used by themselves on a missed day (ours).
- **Weeks:** a week counts as kept at 5 days of 7.
- **Breaks** are described plainly: "Last practised Tuesday. Today's plan is ready."

### Progress

The concept's Progress section, worked out from the attempts and answers, as trends by week.

- **The long-scale reading score:** the level plus how far through its tempo range you are, such as 6.4.
- **The plateau note** on the You screen.

## The data

One SQLite file, `prima.db`, in the app's data folder. Dapper, with one lane of work like Workout's store. Every attempt keeps
its raw notes, so a result can be worked out again if the scoring changes.

| Table | What it keeps |
| --- | --- |
| Setting | Key and value: the look; the profile (name, daily minutes, practice cue, clefs drilled); the keyboard (device, timing offset, noise floor, where the click sounds); notation (music font, sheet size); practice (count-in, click sound and volume, hints) |
| Track | Per track (reading, rhythm, each scale or arpeggio and hands): level, tempo, recent results, updated |
| NoteCard | Clef, pitch, box, due day, day introduced, fluent |
| NoteAnswer | When, clef, pitch, what was played, right, response time, drill |
| Attempt | When, kind, variant, level, tempo, the seed (or piece and bars), settings, the notes played with times and velocities, the measures, the headline figure, clean |
| PracticeDay | Day, minutes, the day's plan and the steps done, short plan, freeze used |
| Piece | Title, composer, source and licence, format, the original file, its MEI, level, bars, added, last played, where you were (shaped by library.md) |

## The screens

**The rail:** Today, Drills and Sight-read; Library, Tools and You; Settings at the foot. In Vinyl the two groups are Side A and
Side B. The keyboard's status sits at the rail's foot.

**First run:** your name, daily minutes, the practice cue and the look. Then plug in the keyboard and do the timing calibration.

**Today.**

- The hero: Studio's tempo knob with the next passage on a small sheet in the bezel, or Vinyl's record.
- The day's steps with the next one ready, Start, and the five-minute plan.
- The week's minutes and days, the streak with its freezes, and the practice cue.

**Sight-read.**

- **Before starting:** the level, the tempo, the variant, and where the music comes from: new passages, or excerpts from the
  collection once the Library has them.
- **The reading screen:**
  1. Look: 30 seconds, shown as a bar.
  2. Count-in.
  3. Play, with the cursor.
  4. Summary: the marks, in-time accuracy, correct notes a minute, kept going, the lean, and what went wrong in words.
  5. Next, with Slower and "Find my level" on hand.
- **Variants:**
  - **Read ahead:** played bars are covered, and the view ahead starts at two bars and narrows over sessions, never under
    1.5 s at the tempo (about 1 s plus a margin).
  - **Duet:** you play one hand and the piano plays the other in time. At one-hand levels it plays a simple accompaniment.
  - **Hear first:** the piano plays it once, with the click, and then you read it.

**Drills.** A list in two groups, Reading and Technique, each drill with its level and last result. A drill's screen shows:

- the sheet, and what to do;
- the try, with its result after;
- the set's summary at the end.

Two drills have their own touches:

- Keyboard geography asks "Did you look?" after a set; your answer is the only check, as the research says.
- Key signatures asks for the tonic in a named mode, then a phrase with the affected notes.

**Library.** The base:

- the collection with each piece's level and composer;
- importing a file;
- a piece's view: two systems in landscape, a full page in portrait;
- practising a piece: loop bars, slow it down, the click, and the same timing summary.

The rest follows `library.md`.

**Tools.** Metronome (tempo, metre, count-in, fade), Keyboard check, and Timing.

**You.**

- Name, practice cue and daily minutes.
- Progress, as the concept describes: reading, the notes' heat map on a grand staff, rhythm, technique, and the habit.

**Settings.**

- **Look.**
- **Keyboard:** the device, MIDI out on or off, the timing offset, and redo timing.
- **Notation:** the music font, the sheet size, and letter names after a wrong answer in drills.
- **Practice:** daily minutes, count-in, and the click's sound, volume and where it sounds.
- **Data:** back up the database to a file, restore one, or start over.

**Portrait.** Sight-read and Library pieces show the page full screen with a slim top bar. The other screens keep their layout,
with the rail along the bottom.

## The look

- **From the Studio & Vinyl design system:**
  - the tokens as `tokens.css`, from `tokens.json`;
  - its components as Razor components: Button, Toggle, NavRail, Step, Screen, Readout, Meter and Status;
  - the Staff and Keyboard components retired, and the Sheet added. The design system's artifact is updated at the end to match
    what was built.
- **Fonts** ship as woff2 with their OFL licences: Space Grotesk, Space Mono, Bebas Neue and Work Sans. Verovio brings the music
  fonts, so Noto Music isn't needed.
- **Applying the look:**
  - `data-theme` is set on `<html>` from local storage before the first paint.
  - `LookSetting` keeps the choice in Setting.
  - The tablet's status and navigation bars follow the look.
- **The icon:** the LCD icon as an adaptive icon, a background and a foreground SVG with the music glyphs turned into paths.
- **The splash:** the LCD mark on Studio's ground.

## The preview host

- Blazor Server on port 5299, with sample data in `preview.db`. Verovio runs in Chromium as it does in the web view.
- **`/seed/{scenario}`** sets up the data:

  | Scenario | What it is |
  | --- | --- |
  | `empty` | The first run |
  | `today` | Two weeks in, halfway through the day's plan |
  | `months` | Three months of history, for progress |
  | `summary` | A passage just played |

- **A simulated keyboard** stands in for the host's MIDI in, MIDI out and click:

  | Endpoint | What it does |
  | --- | --- |
  | `/keys/connect` and `/keys/disconnect` | Plugs the keyboard in or out |
  | `/keys/play?notes=60,64,67` | Plays notes now |
  | `/keys/perform?right=0.9&lean=-25&spread=15&stop=3` | Plays the current passage or drill item against the session's beats in one go and ends it, so a summary can be screenshotted without waiting |
  | `/keys/tap` | The same for the Rhythm drill |

## Tests

xUnit v3 in `Prima.Core.Tests`, for everything in the Core:

- **The model, MEI and timing:**
  - spelling in every key;
  - bars that add up;
  - MEI written and read back with the same ids and notes;
  - onsets in beats and ms;
  - an MEI file from Verovio read in.
- **The generator**, over many seeds per level:
  - keys, metres, note values, ranges, leaps and ledger lines inside the level;
  - bars full;
  - ends on the tonic;
  - the same seed gives the same passage;
  - inside A0–C8.
- **Scoring:**
  - perfect;
  - early or late lean, and steadiness;
  - wrong notes (neighbouring, octave, reading), extra and missed notes;
  - stopping for more than a bar;
  - drift;
  - chords;
  - the window at different tempos.
- **Rhythm:** lean, steadiness, drift without the click, missed and extra taps.
- **Technique:**
  - synthetic runs with a known gap spread;
  - crossing delays, legato overlap and hand sync;
  - a wrong note invalidating a run;
  - the 10 ms and noise-floor labels.
- **Note review:** box moves, "not yet", the order with confusables, new notes in landmark order, fluent.
- **Levels:** tempo up and down, the step back, level up, finding a level.
- **The daily plan:** the steps, rotation across days, the five-minute plan.
- **The streak:** freezes earned and used, 5 of 7.

## Checking it

1. **Build** `Music/src/Prima.Tablet/Prima.Tablet.csproj` (Core and UI under it) with no warnings, and the preview host.
2. **Test:** `dotnet test --project Music/tests/Prima.Core.Tests`.
3. **Screens** in both looks:
   - landscape at 1280 × 800 and portrait at 800 × 1280, the 13-inch tablet's likely size in CSS pixels, to be confirmed on
     the device;
   - before-and-after shots where a change shouldn't touch the rest.

**Left for the owner to try on the tablet and piano.** The cloud can't:

- the P-225B over USB: notes, velocity, pedals, and plugging and unplugging;
- whether the piano plays notes sent to it, and whether it echoes them back;
- where the click sounds (speaker, or the piano as USB audio), and "Click on the piano";
- the timing calibration's offset and spread;
- rotation to portrait;
- the screen staying on;
- how quickly Verovio starts on the tablet.

## The parts, in order

Each part is built, tested and checked on screen, then committed and pushed to main with a message that says what changed for
the owner.

1. **Setup.**
   - The solution, the projects, the build items, the Music README.
   - The repo's README and CLAUDE.md for Prima (shared root files, said in the commit).
   - The look: tokens, fonts and components, and the shell with the rail, both looks.
   - Settings → Look, the preview host with seeds, the icon and splash.
   - The tablet app building.
2. **The sheet.** The model, `MeiWriter` and `MeiReader`, Verovio in the web view, the Sheet component, the cursor and the marks.
3. **The logic.** The generator, scoring, technique measures, note review, levels, the daily plan, the streak and progress, with
   their tests.
4. **The store and sessions.** SQLite, the practice sessions, the host interfaces, and the simulated keyboard.
5. **The keyboard and click on Android.** MIDI in and out, the click, keeping the screen on, rotation. Tools: Metronome,
   Keyboard check and Timing.
6. **Sight-read**, with its variants and "Find my level".
7. **The drills**, all eleven.
8. **Today, You and Settings**, complete, and the first run.
9. **The Library**, as below.
10. **The docs,** a last check of every screen, and the design system brought up to date.

## The Library

Built last, from `library.md`, whose research covers:

- public-domain piano music, its licences and formats;
- a graded collection for reading;
- grading real pieces onto the levels;
- importing files, PDFs included;
- page turns and scrolling at the tempo;
- following along with the keyboard;
- the full-page view.

What's planned regardless:

- **Pieces keep their MEI.** An imported MusicXML, MXL, MEI, Humdrum or ABC file goes through Verovio once. The piece keeps the
  original and the MEI Verovio makes, with stable ids (`xmlIdChecksum`). The Core reads the MEI for grading, excerpts, scoring
  and following, and Verovio engraves it.
- **MIDI files** have no notation, so the Core turns them into a score by quantizing them.
- **PDFs** are shown page by page, the way `library.md` recommends.
- **The sight-reading track reads real music too.** Excerpts of a few bars from pieces at your level join the passages, and the
  app remembers which you've read so they stay fresh.
- **The page** turns or scrolls with the music, and the owner can set the pace.

If `library.md` isn't in yet when part 9 comes, the base above is built first and the rest added after.

## Risks and open questions

- **The click's timing.** The audio timestamp is only as good as the tablet's audio driver. The timing calibration covers a
  steady offset; the spread shows the rest.
- **The piano's timing** (how even its MIDI timestamps are) is unknown until measured. The noise floor keeps small differences
  from being shown as faults.
- **Verovio's start-up** on the tablet: about 7 MB of script. It loads in the background at start, and the first screens don't
  need it.
- **The LGPL.** Verovio is used unchanged as a separate file, which the LGPL allows. Fine for a personal app; it would only
  matter if the app were shared.
- **Our own numbers:** the timing window, the 75/90% bands, the box gaps, the fast threshold, the tempo steps, the ranges and
  the caps. They're constants in the Core, easy to tune once there's real data.
