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
  `SupportedOSPlatformVersion` 24. Android's MIDI API needs 23.
- Android 13 (API 33) replaced two MIDI calls: listing devices and the device callback now take a transport. Prima uses the new
  calls behind `OperatingSystem.IsAndroidVersionAtLeast(33)` and the old ones below it, so the build has no deprecation
  warnings.
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
5. **The cursor is only a guide.** No score depends on the web view's timing. The web view's clock (`performance.now()`) isn't
   the system's, so at each count-in the session tells `prima.js` the tempo and how many ms until bar 1, and the script
   counts from there. Interop adds a few ms, which a guide can bear.
6. **Threads.** MIDI and the click arrive off the UI thread. The session collects them under a lock and updates the screen
   through Blazor's `InvokeAsync`.
7. **Pure logic lives in the Core,** so it's tested: the MIDI byte parser, the beat times from the audio timestamp, and the
   jitter of the piano's Active Sensing (below). The Tablet only moves bytes and timestamps.

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
    - a note graded off (more than 10% of the beat early or late, after the lean) gets a small arrow above it;
    - a missed note is outlined.
  - **Details**, on demand: tapping a note shows its error in ms and in % of the beat, and a timeline strip under the sheet
    shows every note's error. The research's "detailed timeline on demand"; hidden until asked for.
  - Read ahead covers the bars you've played and limits the view ahead (see Sight-read).
- **No fingering in sight-reading**, as in the exams. Scales, arpeggios and finger patterns show fingering numbers.

## The keyboard and the click

### MIDI in

- **The connection:** the P-225B's USB to Host port, a USB-B to USB-C cable (or an adapter), then the tablet.
- **Finding the piano:** `MidiManager` lists USB MIDI devices and reports plugging and unplugging. Prima opens the first one
  with an output port, or the one chosen in Settings.
- **Reading it:** a `MidiReceiver` hands the bytes and their timestamp to the Core's parser. It reads note on and off
  (velocity 0 is off), running status, and the pedals (damper CC64; soft CC67 and sostenuto CC66 if a three-pedal unit is
  fitted). Each event keeps its nanosecond timestamp.
  - Bytes may arrive split or several messages at once, so the parser keeps state between calls.
  - System real-time bytes (Active Sensing `FE`, clock `F8`) can sit in the middle of a message and are taken out first.
    Yamaha pianos send Active Sensing about every 300 ms. SysEx is skipped.
- **Ignored input:** notes on the channel Prima sends on, in case the piano echoes them back. That's channel 16 (ours), not 2:
  Yamaha pianos may send a split or dual voice on channel 2, and the left hand mustn't be ignored. To confirm on the device.
- **Status:** the shell shows it with a dot: "P-225B" when connected, "Plug in your keyboard" when not.

### MIDI out

- Prima opens the piano's input port and sends notes with future timestamps, about 200 ms ahead. The Android docs describe
  scheduling events in the future this way, but don't state a delivery guarantee for USB devices. Part 2's device check
  measures it. If future timestamps aren't held, Prima's own thread sends each note at its time.
- `flush()` cancels what's pending. It can drop note-offs, so it's always followed by "all notes off" (CC123) on Prima's
  channel, as the docs recommend.
- Before the first note, a program change on channel 16 sets it to the grand piano voice (ours), since a channel other than 1
  may start on another voice.
- **Used by** Duet, Hear first, and Listen and play back, so their sound comes from the piano itself.
- **If the piano can't receive,** those three are hidden, with a line saying why.

### The click

- **Rendering:** a streaming `AudioTrack` at 48 kHz.
  - A writer thread keeps about 150 ms of audio queued. Each click (a 15 ms blip, higher on the downbeat) is placed on an exact
    frame. Silence is written between clicks, so the frame count never stops.
  - `AudioTrack.getTimestamp` pairs a frame with its time on `CLOCK_MONOTONIC`, the clock `System.nanoTime` and the MIDI
    timestamps use. A beat's time is that time plus (the beat's frame − the timestamp's frame) / 48,000.
  - It's polled often until the frame position is advancing (it returns false while the clock settles and after a route
    change), then every 10 to 60 seconds, as the docs advise. If a route never gives one, the play-head position stands in
    and the screen says timing is approximate.
  - Low-latency mode isn't needed: clicks are placed ahead of time and the timestamp says when they sound, so only start and
    stop wait on the buffer. A normal buffer is safer against gaps.
- **The click never waits.**
  - Count-in: one bar.
  - It can play throughout, play the count-in only, or go silent; the grid keeps running.
  - Volume and sound are in Settings.
- **Where it sounds:** wherever Android routes audio, the tablet's speaker or the piano if it shows up as a USB audio device.
  With headphones in the piano, the tablet's speaker isn't heard. So there's a second choice: "Click on the piano". The click
  becomes short high notes sent over MIDI on channel 16, timed by their scheduled timestamps. The owner tries both.

### Timing calibration (Tools → Timing)

- **The test:** play any key with the click for 16 beats.
- **The offset:** the median of (note − beat) is kept as the timing offset and taken off every note played. It's the delay of
  the click's sound, the MIDI path and your own habit together; without a hardware loop they can't be told apart.
  - So your habit of playing a little early, which the research says is normal, is partly folded into the offset. The lean
    shown afterwards is measured from the day you calibrated: it shows change, not your habit in absolute terms. The screen
    says so.
- **The spread** (interquartile range) is shown as your tapping spread. It's mostly you, not the keyboard, so it isn't used as
  the keyboard's noise floor.
- **The keyboard's noise floor** comes from Active Sensing instead (ours, untested). The piano's firmware sends it at a fixed
  interval, so the spread of the gaps between arrivals is roughly the jitter of the USB and MIDI path. It's measured in the
  background while the piano is connected and shown in Keyboard check. If the piano doesn't send it, the floor is unknown and
  only the 10 ms label applies. It doesn't catch the keyboard's own scanning delay.
- **Redo it** after changing where the click sounds.

### Keyboard check (Tools)

- The last notes played: name, velocity and pedal.
- The USB and MIDI path's jitter, from Active Sensing, once a minute of it has arrived.
- Press the lowest and highest keys to confirm A0 and C8 arrive.
- A touch check: velocity over a few even presses. If the piano's touch is set to Fixed, every velocity is the same, so evenness
  of touch is hidden everywhere.
- No picture of a keyboard.

### The screen

- It stays on while Prima is open, on every screen (`FLAG_KEEP_SCREEN_ON`, set once on the activity).
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
- **Marks that aren't scored:** dynamics, articulation (slurs, staccato, accents, tenuto), hairpins and pauses, so the music
  looks like real music. They follow ABRSM's table (section 6): *f* and *p*, legato and staccato from level 3; *mf*, *mp*,
  hairpins, slurs and accents from level 4; *pp* from 5; tenuto and pauses from 7; *ff* from 8. Not scored (ours). Slowing at
  the end and tempo changes are left out, since the click doesn't follow them.
- **Pieces:** repeats, voltas and grace notes are read too. Practice uses bars in written order; grace notes aren't scored
  (ours).

### The generator

Seeded and reproducible. Prima uses its own small random generator (SplitMix64), so a passage can be rebuilt from the seed kept
with its attempt, in any .NET version.

**Levels.** The twelve levels are research.md's merged ladder (section 6). The researcher filled in a few values no board
gives: the bars at level 1, accompaniment patterns at 8, the wider range at 10, and all of level 12. We add the left hand's
long notes at level 5 and the last two columns.

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
   - Zhukov's course used solo pieces in four styles. The research doesn't say which four, so these templates are ours.
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
- arpeggios: a fifth's range first, then root position and inversions;
- hands separately and together, over 1 to 4 octaves, then scales a third and a sixth apart;
- in ABRSM's order of keys, with its guide speeds shown as a reference (♩ = 54 at Initial to ♩ = 100 at Grade 4, then
  𝅗𝅥 = 60 to 88; the note grouping they mean wasn't extracted, so they're a guide only).

### Scoring a passage

**Inputs:** the expected notes (id, pitch, onset in beats, bar, hand), the click's beat times, the notes played (pitch, on and off
times, velocity), and the timing offset. Expected times come from the beats as actually played by the click, so the grid is what
was heard.

**Matching.**

- **The window.** Per pitch, an order-keeping alignment (dynamic programming) pairs expected and played onsets within ±W.
  - W is 40% of a beat, held between 0.12 and 0.30 s (ours).
  - Kopiez & Lee counted a note right within ±0.25 s; the research suggests scaling it with tempo, as ours does.
  - Notes played during the look are ignored. Scoring starts half a window before bar 1.
- **Wrong notes.** A played note left over within W of a missed note is a wrong note, named: "played G4 for A4". It's sorted
  as:
  - lost on the keyboard: one or two semitones off;
  - an octave off;
  - a reading error: anything else.
- **The rest:** other notes played are extra notes, and expected notes never played are missed.

**Per right note.**

- Its timing error is in ms and as a share of the beat.
- The lean is the signed mean error of the right notes, shown on its own.
- Each note's error after the lean is graded in three bands. The bands are the research's own design inference from the
  perception studies, not a finding:
  - under 5% of the beat: **in time** (a single shifted note starts to be noticed at about 2.5–5%);
  - 5 to 10%: **audible**;
  - over 10%: **off** (a steady pulse starts to break at about 8.6%).

**Per passage.**

- **In-time accuracy, the headline:** right notes not graded off (within 10% of the beat after the lean), out of the notes in
  the passage. Counting only the under-5% band would set the 85% target at the edge of what listeners notice, too strict
  for reading at sight; so audible notes count, and the summary shows how many were audible (ours). This is the figure the
  levels, the 85% aim and the progress trends use.
- **Correct notes a minute.**
- **Kept going:** no stretch longer than a bar where notes were due and nothing was played.
- **Clean:** every note right and kept going. The share of clean passages is a progress figure (Duke: retention follows the
  share of correct tries).
- **Zhukov's four:** tempo drift (a line fitted through played against expected times), extra notes, missed notes, and the RMS
  timing of right notes. A wrong note counts as both a missed and an extra note here, while the summary names it as wrong
  (ours; the research doesn't say how Zhukov counted them). The RMS is taken before the lean comes off, so it holds both (ours).
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
- **Graded** with the same three bands, after the lean. The headline is taps not off, out of the notes in the rhythm.
- **Kept over time:** the figures missed most, by rhythm cell.

### Technique

- **A valid run** has every note right and in order. Otherwise it doesn't count for evenness and shows as a note error.
- **Evenness and tempo,** per hand.
  - The gaps between onsets (IOIs) are taken in order, and a line is fitted through gap against note index. Its mean is the
    run's own gap, so its own tempo; its slope is how steadily it sped up or slowed down.
  - The IOI SD is the spread of the gaps around that line, in ms and as a share of the mean gap. That's the research's "fit
    the grid first, so drift isn't counted as unevenness". A straight line through onsets alone would leave a steady speed-up
    in the figure.
  - The plain IOI SD, without the fit, is kept too. It's the figure Jabusch's 8–9 ms reference uses.
  - Tempo drift: the fitted tempo against the set one, and the slope.
- **Thumb crossings:** each crossing's gap minus the fitted gap, averaged per crossing and hand.
- **Octave seams:** gaps across the tonic, kept apart from the rest.
- **Touch:** velocity spread, and mean velocity per finger (the fingering is fixed).
- **Legato:** the overlap at each step (the last note's release minus the next note's start; positive is overlap), as a mean and
  spread, with the pedal noted.
- **Hands together:** the onset difference between the hands, as a mean and spread.
- **Over 3 to 5 runs:** the consistent part (the same gap long every time) apart from the random part.
- **Small differences**, for the timing figures (IOI SD, crossings, seams, consistent deviations, hand sync):
  - under about 10 ms shows as "below what anyone can hear", the research's wording. Its listeners heard scale unevenness
    from about 10 ms at a 125 ms gap; it's a guide, not a hard line, at other gaps;
  - under the keyboard's measured noise floor (from Active Sensing) shows as "within the keyboard's noise".
  - Velocity and legato get neither label: the research has no threshold for them.
- **Rhythmic variants** (dotted, accents in 2s, 3s and 4s) can be chosen for a scale, labelled as a tradition, untested.
- **Finger independence:**
  - gaps and velocity per finger;
  - trill rate and its steadiness;
  - for held-note patterns: whether held keys stayed down, and whether a neighbouring key sounded.
  - Patterns come from built-in five-finger figures until the Library has pieces, then from the owner's pieces where possible,
    because finger training only partly carries over to other patterns.

### Drill scoring

Each drill item has an answer window and its own measures (the research's catalogue, section "What this means for the app").

- **The answer** is the first note-on for a single note. For a chord, it's all note-ons within 150 ms of the first (ours).
  For a group or figure, it's that many notes in order.
- **Response time** runs from when the item was shown to the first note-on. The sheet reports when it painted, in the frame
  after the SVG goes in, and .NET stamps that with `System.nanoTime` when the interop call arrives. That's good to a frame
  or two, plenty against answers of about a second.

| Drill | Measures kept |
| --- | --- |
| Notes, Ledger lines | Right or wrong, response time, what was played (octave errors apart); per card, so per ledger position too |
| Intervals and shapes | Each step right or wrong; a wrong interval kept apart from a wrong first note; response time to the first note and the gaps |
| Chords | The notes against the target, missing and extra notes, the onset spread, response time |
| Key signatures | The tonic right or wrong; each affected note played without its sharp or flat, kept per key as "forgot the key" |
| Keyboard geography | Wrong notes one or two keys away (lost on the keyboard), response time, and the "Did you look?" answer |
| Listen and play back | The piano plays the phrase after a count-in, the click runs on one bar, then you play it back in time: scored like a passage. Then the notation shows. |

- **Listen and play back's last step** is improvising an answer to the piano's phrase: recorded, not scored. The research's
  creative extension; creative work was one of Mishra's four helpful treatments.

### Note review

**Cards.** One per note and clef, since the same pitch looks different in each clef. Leitner boxes 1 to 6 come back after 0 days
(this session), 1, 2, 4, 8 and 16 days. Those gaps are ours, in the spirit of expanding, half-life-style schedules.

**Moving between boxes.**

- Right and fast: up a box.
- Right but slow: "not yet". It stays in its box and comes back tomorrow.
- Wrong: back to box 1, and asked again later in the same session.

**Fast** means under the target time: 1.25 × your median on right answers over the last two weeks, held between 0.8 and 2 s
(ours). The research has only a teacher's 2-second rule with no data behind it, used here as the upper bound.

**Order.**

- Due cards first, with yesterday's slow and missed ones ahead.
- Mixed so confusable notes come near each other: the same line or space in the other clef, a third apart, octave twins.
- Never the same note twice in a row.

**New notes**, one to three a day, in landmark order (a teaching tradition, not tested). They're added only when yesterday's
review was 85% right or more (ours), so new notes don't pile onto shaky ones.

1. Middle C in both clefs.
2. Treble G4 and bass F3.
3. Treble C5 and bass C3.
4. C6 and C2.
5. The landmarks' neighbours, by step and skip.
6. The ledger lines.

Each new note comes in a short block next to one or two known notes that contrast with it, then joins the mix.

**Fluent** means right and fast in each of the last three answers, over at least two days (ours, for the research's
"accurate, fast and stable over several sessions").

### Levels and how they move

**Reading.** A level from 1 to 12, and a tempo inside the level's range. A short passage is noisy (one note of sixteen is 6%),
so decisions use the mean in-time accuracy of the last three passages at that level and tempo (ours). After each passage:

- **Good:** the mean is over 90%, and all three kept going. The tempo goes up 5%, and the three start again.
  - At the top of the range, the level goes up (below).
- **Hold:** the mean is between 80 and 90%. The research's band for passages is 85–90%, and 80–90% for drills in general; this
  keeps the aim near 85%.
- **Too hard:** the mean is under 80%. The tempo drops 10%.
  - A single passage where you stopped, or under 60%, drops it at once (ours): "tempo before complexity".
  - At the bottom of the range, two more too-hard results make the next passages simpler by one step back: the last
    dimension raised (below) goes back a step. It returns after three good passages.
- **One thing at a time.** Each level adds up to four kinds of difficulty: rhythm, hands and texture, keys and metres, and range
  and leaps. On a new level they come in one at a time, in that order, each after three good passages, before they all mix
  (ours; the research says to raise one dimension at a time).
- **Aim:** about 85%. All these numbers are ours, as the research gives a target, not a rule.
- **Finding a level:** the first time, Sight-read offers "Find my level". It plays two passages each from levels 1, 3, 5 and up,
  at each level's lowest tempo, until their mean falls under 80%, then starts one level below.

**Read ahead** keeps its own view width (ours). It starts at two bars, narrows by half a bar after a good result, widens after
a too-hard one, and never goes under 1.5 s at the tempo. A hold leaves it alone, so slowing at hard spots isn't punished.

**Other drills** (Intervals, Chords, Key signatures, Keyboard geography, Listen and play back, Finger independence) use the same
bands on each set's share right: over 90% in two sets in a row steps up, under 80% steps back (ours).

**Rhythm.** Its own level on the ladder's rhythm column, plus the click's stage (throughout, count-in only, silent).

- Within a set, later tries drop the click one stage: the research's "click fades on later tries".
- The stage a set starts at moves first, then the level, with the same three-result bands as reading (ours).

**Technique**, per scale or arpeggio and per hand or hands:

- **Starting tempo:** about 85% of your comfortable speed, taken from a first run without the click (ours; the research says
  to start below your top speed).
- **Moving up:** +4 beats a minute when the IOI SD (as % of the gap) stays under your target on 3 of 4 valid runs, towards the
  board's guide speed. The step size is ours; the research says good steps are unstudied.
- **The target** comes from your own baseline, since there are no amateur norms: the median IOI SD % of your first week's
  valid runs on that item (ours). It never needs to be under 8%, where listeners start to hear it; professionals are about 7%.
- **After two invalid runs in a row,** the next is offered 10% slower (ours). Duke's best pianists fixed errors at once and
  varied the tempo at the problem spot, and retention followed the share of correct tries.
- **Caps** (ours, precautions): 5 runs per item per set, about 8 minutes of technique a day, and a line saying to stop if
  anything hurts.

### The daily plan

- **Built once a day** and kept. It has the concept's five steps and the pause.
- **Mixes across days:** the scale and arpeggio of the day, rhythm figures and the variant.
- **The five-minute plan:** note review and two passages.
- **Length:** the daily minutes come from the profile. The app never raises them; if the owner raises them more than about 25%
  over last week (ours), a line suggests going gradually.
- **One spot:** the bar with the most errors today, 20% slower (ours), then its line. Or a piece from the Library.
- **The day's summary** closes the plan: what was done, the day's figures in words, and tomorrow's first step. The research's
  step 5 ends this way.

### The streak and the habit

- **A practised day** has 5 minutes or more, or the five-minute plan done (ours).
- **Freezes:** one earned for every 7 practised days, up to 2 banked, used by themselves on a missed day (ours).
- **Weeks:** a week counts as kept at 5 days of 7.
- **Breaks** are described plainly: "Last practised Tuesday. Today's plan is ready."

### Progress

The concept's Progress section, worked out from the attempts and answers, as trends by week.

- **The long-scale reading score:** the level plus how far through its tempo range you are, such as 6.4 (ours).
- **Reading** includes the research's full list: Zhukov's four and the lost-on-the-keyboard errors as well as the level,
  in-time accuracy, correct notes a minute, kept going and the passages and bars a week.
- **Read ahead against plain reading:** in-time accuracy with and without the window at the same level and tempo.
- **Clean passes:** the share of clean passages and valid runs per session.
- **The plateau note** on the You screen.

## The data

One SQLite file, `prima.db`, in the app's data folder. Dapper, with one lane of work like Workout's store. Every attempt keeps
its raw notes, so a result can be worked out again if the scoring changes.

| Table | What it keeps |
| --- | --- |
| Setting | Key and value: the look; the profile (name, daily minutes, practice cue, clefs drilled); the keyboard (device, timing offset, tapping spread, noise floor, where the click sounds, calibration date); notation (music font, sheet size); practice (count-in, click sound and volume, hints) |
| Track | Per track (reading, read ahead, rhythm, each other drill, each scale or arpeggio and hands): level, the step of each dimension, tempo, the click's stage, the view width (read ahead), the baseline and target (technique), the best even tempo, recent results, updated |
| NoteCard | Clef, pitch, box, due day, day introduced, last seen, fluent |
| NoteAnswer | When, clef, pitch, what was played, right, response time, drill |
| Attempt | When, kind, variant, set, level, tempo, the seed (or piece and bars), settings, the notes played with times and velocities, the beat times, the measures, the headline figure, clean, the "Did you look?" answer |
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
  1. Look: 30 seconds, shown as a bar. You can tap the rhythm or try a bar; nothing is scored. Start skips the rest.
  2. Count-in.
  3. Play, with the cursor.
  4. Summary: the marks, in-time accuracy, correct notes a minute, kept going, the lean, and what went wrong in words.
  5. Next, with Slower and "Find my level" on hand.
- **Variants:**
  - **Read ahead:** played bars are covered as the cursor leaves them, and the view ahead starts at two bars and narrows by the
    rule under Levels, never under 1.5 s at the tempo (ours, for the research's "about 1 s plus a margin").
    - The look shows only the opening view, not the whole passage (ours): otherwise it could be learned before it's hidden.
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
  | `drills` | Each drill with a level and a last result, and a technique run just played |

- **A simulated keyboard** stands in for the host's MIDI in, MIDI out and click:

  | Endpoint | What it does |
  | --- | --- |
  | `/keys/connect` and `/keys/disconnect` | Plugs the keyboard in or out |
  | `/keys/play?notes=60,64,67` | Plays notes now |
  | `/keys/perform?right=0.9&lean=-25&spread=15&stop=3` | Plays the current passage or drill item against the session's beats in one go and ends it, so a summary can be screenshotted without waiting |
  | `/keys/tap` | The same for the Rhythm drill |

- The simulated host gives beat times from the server's clock, so a session runs the same code path as on the tablet.

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
- **The MIDI parser:** running status, velocity-0 note-offs, messages split across calls, real-time bytes inside a message,
  SysEx skipped.
- **Clocks:** beat times from an audio timestamp; Active Sensing jitter.
- **Rhythm:** lean, steadiness, drift without the click, missed and extra taps.
- **Drill scoring:** the chord window, interval against first-note errors, "forgot the key", neighbouring-key errors.
- **Technique:**
  - synthetic runs with a known gap spread, and a steady speed-up that isn't counted as unevenness;
  - crossing delays, legato overlap and hand sync;
  - a wrong note invalidating a run;
  - the 10 ms and noise-floor labels.
- **Note review:** box moves, "not yet", the order with confusables, new notes in landmark order, fluent.
- **Levels:** tempo up and down on the mean of three, the immediate drop, the step back, one dimension at a time, level up,
  finding a level, the read-ahead width, the rhythm click's stage, the other drills' bands.
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
- whether the piano plays notes sent to it on channel 16 with the piano voice, whether it echoes them back, and which channel a
  split or dual voice sends on;
- whether notes sent with future timestamps play on time;
- whether the piano sends Active Sensing, and the jitter it shows;
- that the tablet's one USB-C port is taken by the piano, so it runs on battery while practising unless a hub with power
  delivery is used;
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
2. **The keyboard and click on Android, early.** The Core's MIDI parser and clock maths with their tests, then MIDI in and out,
   the click, keeping the screen on, rotation, and Tools: Metronome, Keyboard check and Timing. This is the part only the
   tablet and piano can test, and the scoring leans on what it finds (the jitter, scheduled sending, the channel), so the
   owner tries it while the rest is built.
3. **The sheet.** The model, `MeiWriter` and `MeiReader`, Verovio in the web view (checked on the tablet too), the Sheet
   component, the cursor and the marks.
4. **The logic.** The generator, scoring, drill scoring, technique measures, note review, levels, the daily plan, the streak and
   progress, with their tests.
5. **The store and sessions.** SQLite, the practice sessions, the host interfaces, and the simulated keyboard.
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
- **Our own numbers:** the timing window, the 80/90% bands and the mean of three, the headline counting audible notes, the box
  gaps, the fast threshold, the tempo steps, the technique target, the chord window, the read-ahead steps, the ranges and the
  caps. They're constants in the Core, easy to tune once there's real data.

## Review

An independent review of this plan against [research.md](research.md) and [concept.md](concept.md), and against Android's
reference pages for `MidiReceiver`, `MidiManager`, `AudioTrack`, `AudioTimestamp` and the `android.media.midi` package
summary.

### Verdict

**Approved with changes.** The plan was sound, well labelled and nearly complete. The changes below fix one contradiction in
the scoring, two places where it claimed more than the measurement can give (the noise floor and the lean), a level rule that
didn't hold the 85% aim, missing drill scoring, and a few Android details. Nothing needs the owner's decision before the
build starts. A few things only the tablet and piano can settle, listed at the end.

### Changes to the plan

**Faithful to the evidence**

1. **The headline contradicted the bands.** "In time" was under 5% of the beat, but in-time accuracy counted notes within 10%.
   Now the headline is plainly "right notes not off (within 10%)", with audible notes counted and shown, and that choice
   labelled ours. The bands are now called the research's design inference, with the two perception figures behind them
   (research §3, "What this means": shared rules).
2. **Kopiez's window.** The plan said the research used ±0.25 s "at fixed tempos"; research §1 says only ±0.25 s. Fixed.
3. **Zhukov's four.** It now says how a wrong note counts (missed plus extra, ours, since the research doesn't say) and that
   the RMS is raw (ours). Zhukov's styles: the research says four styles, not which, so the templates are ours (§1).
4. **The ladder's inferred values.** The plan said the board-less values were ours; some were the researcher's ("inf" in §6:
   level 1's bars, accompaniment patterns at 8, wider range at 10, level 12). Now split correctly.
5. **Levels and the 85% aim.** The old hold band (75–90%) let a reader sit at 76% indefinitely, below the research's 85–90%
   for passages (catalogue item 7) and 80–90% for drills (shared rules). Now: decisions on the mean of the last three passages
   (one note in a short passage is 6%), up over 90%, down under 80%, and an immediate drop when you stop ("tempo before
   complexity", rules around the session). All labelled ours.
6. **One dimension at a time** (catalogue item 7; concept's Levels) was missing from the plan. Added: a new level's four kinds
   of difficulty come in one at a time, and the step back undoes the last one. Track keeps each dimension's step.
7. **Read ahead.** Added the research's comparison with and without the window (item 8), a rule for narrowing that doesn't
   punish slowing at hard spots (item 8, "Don't punish slowing"), and a look that shows only the opening view, since a
   30-second look would let the passage be learned before it's hidden (ours). The 1.5 s minimum is now labelled ours.
8. **Feedback after the phrase** stands. Added the "detailed timeline on demand" from the shared rules.
9. **Note review.** "The research sets no number" was wrong: it reports a teacher's 2-second rule with no data (§2), now named
   as our upper bound. Landmarks are labelled tradition; "fluent" and the new-note condition are labelled ours.
10. **IOI SD after fitting the grid.** The old wording fitted a line through onsets, which leaves a steady speed-up inside the
    figure, the very thing the fit is for (§4, Method). Now the gaps are detrended, and the plain IOI SD is kept for
    comparison with Jabusch's 8–9 ms.
11. **The ~10 ms label** is now scoped to timing figures and noted as measured at a 125 ms gap (§4, van Vugt 2013); velocity
    and legato get no threshold, since the research has none. Legato's sign is kept as the plan had it (release minus next
    start, positive is overlap), which matches Repp's key overlap time; research.md's own formula ("next note-on minus
    previous note-off") has the sign the other way round, a slip in research.md, which wasn't edited.
12. **Technique targets** were undefined ("your target"). Now the target is your first week's median, with the 7% and 8%
    reference figures and the board's guide speeds shown (§4, §6, catalogue item 10). Added a slower run after two invalid ones
    (Duke, §4), rhythmic variants labelled tradition, the arpeggio's fifth-range start and scales in 3rds and 6ths (§6).
13. **Clean passes** (progress, Habit, Duke 2009) were stored but never shown. Added to scoring, progress and concept.md.

**Complete**

14. **Drill scoring** was missing for six drills. Added a section with each drill's measures from the catalogue (items 3, 4, 5,
    9, 13), the answer window, how response time is taken, and the improvised answer phrase that ends Listen and play back
    (item 13, creative activities).
15. **The rhythm drill** had no rule for moving; now it uses the bands, and the click fades within a set and across sets (item 6).
16. **The other drills** had no level rule; now they use the same 80/90% bands (shared rules).
17. **The day's summary** closes the plan (daily session, step 5). Dynamics and articulation follow ABRSM's table so the music
    looks real (§6), not scored. Pieces' repeats and grace notes are read, for the Library base.
18. **Progress** now lists the research's full reading set: Zhukov's four and lost-on-the-keyboard errors.

**Consistent with concept.md.** Edited concept.md where it disagreed with the research or the plan: the headline and bands,
the level rule, the Timing tool's spread ("noise the piano and tablet add" was wrong; it's mostly your tapping), latency and
Active Sensing, read ahead's look, the day's summary, the improvised answer, and the progress lists.

**Buildable**

19. **The noise floor.** The calibration's tapping spread is mostly human, so using it as the keyboard's noise floor would
    have hidden real differences as "keyboard noise". It's now shown as your tapping spread. The floor comes from the
    piano's Active Sensing arrivals instead: our idea, untested, and it can't see the keyboard's own scanning.
20. **The lean.** The calibration folds your habit of playing early into the offset, so later leans show change from that
    day. Now said in the plan, on the screen and in concept.md.
21. **MIDI parsing.** Android's package summary says received data "can contain multiple messages or partial messages" and
    real-time messages "interleaved inside other messages". The parser now keeps state across calls, strips real-time bytes
    (Yamaha pianos send Active Sensing) and skips SysEx. It moves into the Core so it's tested.
22. **API 33.** `getDevices()` and `registerDeviceCallback(callback, handler)` are deprecated since API 33 in favour of
    transport-based calls (MidiManager reference). With a minimum of 24, the build needs a version check to stay free of
    warnings.
23. **Sending with timestamps.** The docs describe scheduling in the future but don't state a guarantee for USB devices; part 2
    measures it, with a fallback. `flush()` can drop note-offs, so it's followed by all notes off, as the package summary
    advises. Prima now sends on channel 16 with a program change first: channel 2 may be the piano's own split or dual voice.
24. **The click.** `AudioTimestamp.nanoTime` is on the same monotonic timebase as `System.nanoTime` (AudioTimestamp
    reference), so the plan's idea holds. Low-latency mode isn't needed when clicks are placed ahead on exact frames, so it's
    dropped. The timestamp is polled every 10 to 60 s once stable, as the AudioTrack reference advises, with the play-head
    position as a fallback.
25. **Clocks across the web view.** The cursor's start and drill response times needed a bridge between `performance.now()`
    and `System.nanoTime`. Both are now described. Threads: updates go through `InvokeAsync`.
26. **The data model** gained what the new rules and screens need: each dimension's step, the click's stage, read ahead's
    width, technique baseline and target, the set and the "Did you look?" answer on an attempt, the beat times, the
    calibration date. Preview gained a `drills` seed.
27. **The order of the parts.** Android MIDI and the click moved from part 5 to part 2. It's the only part the cloud can't
    test, and the scoring depends on what it finds, so the owner can try it on the tablet while the rest is built.

### Left for the owner

- Plug the P-225B in once part 2 lands, and try Keyboard check and Timing. The list under "Left for the owner to try" says what
  to look for: channel 16 and its voice, echo, the split voice's channel, scheduled notes, Active Sensing, where the click sounds.
- The piano takes the tablet's only USB-C port, so practice runs on battery unless a hub with power delivery is used.
- The owner's decisions removed parts of the research: the microphone, singing and solfège (one of Mishra's four helpful
  treatments) and the keyboard strip on the reading screen. Nothing in the plan replaces singing; say if a "sing it first"
  step without scoring is wanted.
- The numbers marked ours are starting points to tune once there's real playing data.

## As built

All ten parts are in, in the order above. Where the build went its own way, or stopped short:

- **Today and Sight-read share the day's passages.** They come in a fixed order from the date, so the passage Today shows as up
  next is the one Sight-read gives. Every third, from level 4, is a real excerpt (below).
- **One spot** plays today's worst bar 20% slower and larger, then the four bars around it at their tempo. "Its line" became the
  four-bar phrase, since a line's bars depend on the screen.
- **You** draws twelve weeks of trends: one series each, a hollow point for the week still going, every value in words too.
- **The Library's bundle** is library.md's tier A: 133 MusicXML files from PDMX and 104 PDFs with MIDI from Mutopia, built by
  `Music/tools/Prima.Library` into `artifacts/library` (not committed) and packed into the app. Tier B (Sapp's Humdrum, DCML's
  MuseScore files), the hymns and the Lieder aren't in it: GitHub archives couldn't be fetched from the cloud, and the hymns and
  Lieder were left for the owner to decide on.
- **Grading** follows library.md §3: a level for each feature, the hardest wins. Texture, range and leaps are looser than the
  generator's steps (and leaps count only in the tune), so pieces land near their published anchors: the minuets at 6–8,
  Burgmüller and the sonatinas at 8–10, the Inventions at 8–9. The within-level score is a hand-weighted rubric (density,
  leaps, pitch entropy, range, rhythmic variety). Grading generated passages showed four places where the generator overstepped
  its level (level 1's last note leapt, pickups leapt into the tune, ties made syncopation, the waltz brought two-note chords a
  level early); they're fixed.
- **Real excerpts on the reading track** are every third passage from level 4, at or below your level, four bars (eight from
  level 7), resting three weeks once read. They don't move the level: their grades aren't calibrated against your reading yet.
- **PDFs** find their systems from staff lines, five to a staff, paired into grand staves. The pen fixes and link points for
  repeats aren't built: the bars a line and beats a bar are set with − and +, the same for the whole piece. A PDF isn't followed
  note by note; it scrolls at the tempo, a system at a time.
- **Playing a piece** is the play bar every tempo screen shares, In time or Wait for me, from the start or a tapped bar, scrolling
  a line at a time. It replaced library.md §6's Follow me (a windowed chord matcher that re-anchored on skips and restarts) when
  the owner asked for a mode that keeps time and one that waits for the right notes.
- **Imports** read MusicXML, MEI, ABC and PDF. MIDI, MuseScore and Humdrum files are turned away with how to convert them.
- **Finger-independence patterns** are still the built-in five-finger figures, not taken from pieces.
- **Bar numbers.** Files repeat bar numbers (two endings) or skip them, so Prima numbers bars in order, a pickup 0, and writes
  those numbers back into the MEI, so the numbers printed are the ones it says.
