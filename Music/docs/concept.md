# Music: how it works

A sight-reading and piano practice app for the Lenovo tablet, held in landscape on the piano's music stand and played on a MIDI
keyboard, or into the tablet's microphone for single notes. It's for one person, kept on the tablet, with no accounts and no
cloud. Its name isn't settled; the candidates are on the [mockups canvas](https://claude.ai/artifact/UdQVrhTZHqqgsCk3WG7cmM).

This is the design before any code. Every rule below comes from [research.md](research.md), which says how strong each piece of
evidence is; where something is a choice of ours rather than a finding, it says so.

## The idea in one paragraph

The one thing that reliably builds sight-reading is reading a lot of fresh, easy-enough music in time, every day. So the heart
of the app is a generator that writes new passages at your level and a reading screen that plays them with you, never waiting.
Around it sit short drills for what reading leans on (notes, rhythm, key signatures, intervals, chords, finding keys without
looking) and for clean technique (scales, arpeggios, even fingers, measured from MIDI), a daily plan of about twenty minutes,
and progress shown as honest numbers over weeks.

## What the research decided

- **Volume of reading is the main lever.** Specific methods add a little on top. The Sight-read screen comes first, and the
  daily plan spends the most time there.
- **Musical material, not random notes.** Good readers see scales, chord shapes and familiar rhythms as chunks. The generator
  writes tonal music from those pieces.
- **Keep going.** The click never waits during a passage; a wrong note is left behind, as it would be in an exam or when
  accompanying someone. There is no "wait for me" mode.
- **Feedback after the phrase, not during it.** Live visual feedback on timing didn't help in two controlled studies, and may
  load the eyes. While you play, the screen shows the music and the cursor; the marks come after the last bar.
- **Rhythm gets its own drill.** It's where sight-reading breaks most. Timing is graded as a share of the beat, after taking
  out your own habit of playing a little early or late.
- **About 85% success.** Each drill and the reading level adjust so that you get most things right. When a passage falls
  apart, the tempo drops before the music gets simpler.
- **Spaced, mixed review for notes.** Notes you miss or read slowly come back sooner; confusable notes are mixed together.
- **Short, accurate technique.** A few runs at a comfortable tempo, measured, then stop. Evenness is measured from MIDI the way
  the research does it.
- **No colour-coded notes.** Colour-coded and simplified notation didn't help in the rhythm-reading studies that tried them.
- **A forgiving streak and real numbers.** A streak that a missed day can't wipe out, and progress like "92% in time at
  level 6, up from 84%", not points or badges.

## Sections

The rail down the left edge, in both looks: Today, Drills and Sight-read; Library, Tools and You; Settings at the foot. In
Vinyl they're Side A and Side B.

### Today

What to do now. The day's plan as five steps (below), with the next one ready to start; a shorter five-minute plan for busy
days that still counts; this week's minutes and days; the streak; and the practice cue you set once ("When I sit down after
dinner, I'll open the app"). The home mockups show it: the tempo knob or the record, the next passage on the screen, and the
steps along the bottom.

### Sight-read

The reading screen, and the way to read more than the plan asks for. A passage at your level: thirty seconds to look it over
(you can tap its rhythm or try a bar), one bar of count-in, then play it in time with the click. After the last bar, the
summary. Then the next passage, with the level and tempo already adjusted.

Variants, one a day in the plan and any time here:

- **Read ahead**: bars fade once the cursor reaches them, but at least a second of music ahead is always shown, since about a
  second is how far everyone's eyes run ahead.
- **Duet**: play one part while the app plays the other in time. It's how the research tests reading, and part of the piano
  course that did best.
- **Hear first**: hear the passage once, then read it.

### Drills

Short, focused practice, each a few minutes. Reading drills: Notes, Ledger lines, Intervals and shapes, Chords, Key signatures,
Rhythm, Keyboard geography, and Listen and play back. Technique drills: Scales, Arpeggios, and Finger independence. Each is
described under [Drills](#drills-1) below.

### Library

Your own music, from MusicXML or MIDI files. Practise a piece with the same reading screen: loop a bar, slow it down, and see
the same timing summary. Finger-independence patterns are taken from these pieces where possible, because finger training
carries over only partly to other patterns.

### Tools

- **Metronome**, with a count-in and a click that can fade to silence.
- **Keyboard check**: shows each key you press and its velocity, and measures how steady the keyboard's timing is.
- **Microphone calibration**: measures the delay of the tablet's microphone and the lowest note it can hear on your
  instrument.

### You

The local profile and progress. Name, instrument, which clefs you read, daily goal, the practice cue, and the look. Progress is
shown as trends over days and weeks (see [Progress](#progress-and-the-streak)), with a note that a plateau after quick early
gains is normal.

### Settings

Look (Studio or Vinyl), input (MIDI device or microphone, and their calibration), notation (letter names, which clefs), practice
(session length, count-in, the click's sound, hints), sound, and data (export and back up). Nothing leaves the tablet unless
you export it.

## The sight-reading flow

1. **Look.** The passage appears with thirty seconds on a bar at the top. You can tap its rhythm on any key or try a bar; nothing
   is scored.
2. **Count in.** One bar of clicks at the passage's tempo.
3. **Play.** The click keeps time and never waits. The screen shows the music and a cursor on the beat. Nothing is marked yet.
   The on-screen keyboard is hidden while reading, so your eyes stay on the staff; it's there in drills and on demand.
4. **Summary.** After the last bar: in-time accuracy, correct notes a minute, whether you kept going, and the passage again
   with each note marked right or wrong and early or late. A wrong note names what you played instead (G4→A4).
5. **Next.** The level and tempo adjust and the next passage comes up.

### How a passage is scored

- A note counts if its pitch is right and it lands within a timing window around its place. The research used ±0.25 s; here the
  window scales with tempo.
- Timing is a share of the beat: under about 5% is in time, 5–10% is audible, over 10% is off. These bands are our reading of
  the perception studies.
- Your average early or late lean is measured and taken out before steadiness is graded.
- Measured per passage: in-time accuracy, correct notes a minute, kept going (no gap longer than a bar), tempo drift, extra
  notes, missing notes, and the timing error of correct notes. A wrong note one or two keys away from the right one counts as
  "lost on the keyboard", apart from reading errors.

### Levels

Twelve levels, merged from what the ABRSM, Trinity and RCM exams expect in sight-reading at each grade. Each adds one kind of
difficulty, and what's added stays.

| Level | About | Keys | Time | Rhythm added | Hands |
| --- | --- | --- | --- | --- | --- |
| 1 | RCM Prep A | C position | 4/4 | Whole, half, quarter | One hand, five-finger position |
| 2 | Trinity Initial | C | + 2/4 | Half rest | Melody shared between hands |
| 3 | ABRSM Initial | C, G, F; A and D minor | | Paired eighths | Hands separate |
| 4 | ABRSM 1 | One sharp or flat | + 3/4 | Dotted half, quarter rest, ties | Any five-finger position |
| 5 | ABRSM 2 | Two | | Dotted quarter and eighth | Hands together |
| 6 | ABRSM 3 | Three | + 3/8 | Dotted eighth and sixteenth | Out of position; two-note chords |
| 7 | ABRSM 4 | Three | + 6/8 | Pickups, chromatic notes | Position shifts |
| 8 | ABRSM 5 | Four | | Syncopation | Four-part chords |
| 9 | ABRSM 6 | Four | + 9/8, 5/8, 5/4 | Triplets | Clef changes, pedal |
| 10 | ABRSM 7 | Five | + 7/8, 7/4 | | 8va, wider range |
| 11 | ABRSM 8 | All to five | + 12/8, 2/2 | Duplets | Three-note chords a hand, ornaments |
| 12 | Beyond | All | Mixed | Free | Full textures |

Within a level the app keeps you at about 85–90% in time. Below that, the tempo drops first; if it still falls apart, the next
passage is simpler. Moving up raises one thing at a time: rhythm, hands, key, range and leaps, or texture. The exams set no
limits for range or ledger lines, so those are our choices. No study compares ways of stepping up; this is a reasoned default.

## Drills

Every drill shares the rules above: accuracy first, then speed; results after the try; timing as a share of the beat; about 85%
success.

| Drill | You see and do | It measures | It gets harder by |
| --- | --- | --- | --- |
| Notes | A note, or a short group, on the staff; play the key | Right or wrong, and the time from seeing to playing, per note | Landmarks first (middle C, treble G, bass F, the outer Cs), then their neighbours, then both clefs and a wider range |
| Ledger lines | Notes on one to four ledger lines; play them | As Notes, kept per ledger position | One line, then two, from the C anchors, then by thirds |
| Intervals and shapes | A two- to five-note figure, first note given; play it | Each step right or wrong; a wrong interval apart from a wrong start | Steps, 3rds, 4ths and 5ths, 6ths to octaves; then mixed directions and moved shapes |
| Chords | A stack of notes; play it at once (MIDI only) | The notes against the target, how together they land, missing and extra notes | Two-note chords, triads and inversions, then four parts across the hands |
| Key signatures | A signature, then a phrase using its sharps or flats | The tonic, and every note played without its sharp or flat | C and A minor, then one to five sharps and flats |
| Rhythm | One to six bars of rhythm; tap a bar of beat, then the rhythm on any key (or clap) | Early or late lean, steadiness, drift without the click, missed and extra taps | Quarters, eighths, dotted notes, sixteenths, syncopation, triplets; the click fades to a count-in, then silence |
| Keyboard geography | A note or interval; play it without looking down | Wrong notes one or two keys away, and time to play | Within a hand position, then out of it, octave jumps, larger leaps, both hands |
| Listen and play back | Hear a short phrase, play it back, then see it written | Pitch and rhythm against the phrase | Five notes of a scale, then six to eight, then more keys |
| Scales | A named scale with its fingering, octaves and tempo; play it in sixteenths to the click (MIDI only) | Evenness of the gaps between notes, a slip at each thumb crossing and octave, evenness of touch, legato overlap, and the hands' sync | The exams' order of keys and octaves; the tempo rises in small steps from a comfortable speed |
| Arpeggios | As Scales | As Scales, with a close look at the crossings | A fifth's range, then one octave hands separate, two, hands together, inversions |
| Finger independence | Five-finger patterns, trills (3–4, 4–5), held notes; patterns from your pieces where possible | Each finger's timing and touch, trill rate and steadiness, whether held keys stay down | Slow and even, then faster, then the weak pairs, then both hands |

Notes come back on a spacing schedule: a note answered right but slowly counts as not yet learned, and comes back sooner than a
fast one. Confusable notes are mixed. Technique results are compared with your own earlier runs, since there are no amateur
norms; differences under about 10 ms are shown as below what anyone can hear. Repetitions are capped per set and per day, with
a reminder to stop if anything hurts: a sensible precaution, not a proven one.

## The daily plan

About twenty minutes, in this order. The order is a design choice; the evidence supports its parts.

| Step | Time | What |
| --- | --- | --- |
| 1. Technique | 3–4 min | One scale and one arpeggio, three to five runs at a comfortable tempo, with the evenness result after each |
| 2. Notes | 3–4 min | The notes due for review, yesterday's slow and missed ones first, both clefs, and one to three new notes |
| 3. Rhythm | 2–3 min | Three or four rhythms at your level; the click fades on later tries |
| Pause | 1 min | Stand up, shake out your hands, look away |
| 4. Sight-reading | 7–8 min | Four to six new passages at your level; one of them a variant (read ahead, duet or hear first), a different one each day |
| 5. One spot, or a piece | 2–3 min | The bar you missed most today, slowly, then its whole line; or a piece from the Library |

The five-minute plan is the note review and two passages. The plan mixes scales, rhythms and variants across days instead of
repeating one block, and doesn't raise the daily minutes sharply from week to week.

## Progress and the streak

Shown as trends over days and weeks. Mixed practice feels worse at the time and works better later, so a single session isn't
judged.

- **Reading**: the level, one long-scale reading score with a trend line, in-time accuracy and correct notes a minute at the
  level, how often you kept going, and the passages and bars read each week, the main lever.
- **Notes**: a heat map on the grand staff of each note's accuracy and speed, the most-missed and slowest notes, and how many
  notes are fluent (right, fast and steady across sessions).
- **Rhythm**: your early or late lean, steadiness, drift without the click, and the figures you miss most.
- **Technique**, per scale or arpeggio and hand: evenness at a tempo, the fastest tempo at which it stays even, crossing and
  octave slips, evenness of touch, legato, and the hands' sync.
- **Habit**: minutes this week and days practised out of the last seven. The streak can be repaired: a missed day can be covered
  by a freeze, and the week counts as kept at five days out of seven. A break is described plainly, never as a failure.

## Input

- **MIDI is the main input.** It gives exact notes, chords, velocity and timing, in every drill. USB or Bluetooth.
- **The microphone** works for single notes from about C3 up, in a quiet room, with headphones so the app's own sound doesn't
  leak in, after a one-time calibration. It can do Notes, Ledger lines above C3, Intervals played one note at a time, Key
  signatures, Rhythm (clapping, onsets only) and single-line Sight-read passages above C3. It can't do Chords, Scales, Arpeggios, Finger
  independence or anything below its lowest reliable note.
- **Latency** is calibrated once for each input (play along to a click; the median offset is taken out). How much timing
  jitter the owner's keyboard and the tablet add is unknown until measured; differences smaller than that aren't shown.

## How the data is shaped

Everything lives in one SQLite file on the tablet.

- **Profile**: name, instrument, clefs read, daily goal, practice cue, look, input settings, and calibration offsets (MIDI,
  microphone, and your own tapping lean).
- **Levels**: the current reading level and tempo, the rhythm level, and per scale and arpeggio the current tempo and target.
- **Note cards**: one per note and clef, with its review box, when it's next due, its recent answers and response times.
- **Sessions**: each day's practice, its steps and minutes.
- **Attempts**: one per passage, rhythm or technique run, keeping its settings, the generator's seed, the notes played with
  their times and velocities, and the measures worked out from them, so a result can be recomputed if the scoring changes.
- **Pieces**: Library files and their practice history.
- **Streak**: the days practised and the freezes used.

## How it's built

Like the workout app: .NET 10 MAUI Blazor, with the logic in a plain library tested with xUnit.

- **Core**: the music model, the passage generator, the note-review schedule, scoring (matching played notes to the score,
  timing, evenness), levels and progress. No UI, all tested.
- **UI**: the Razor screens.
- **Tablet**: the Android app: MIDI through Android's MIDI API, the microphone through `AudioRecord` with a YIN or MPM pitch
  detector in C#, and the tablet's screen kept on during practice.
- **Preview**: the UI as a Blazor Server page against sample data, for checking screens from the cloud, as `CLAUDE.md`
  describes.
- **Notation** is drawn as SVG: Noto Music for clefs, accidentals, notes and rests, with lines, stems and beams drawn.
- **The look** is the [Studio & Vinyl design system](https://claude.ai/artifact/XtgAHfVyw3L94e5DKrj2ii): its own two looks, not
  the Kit's Logbook and Telemetry, switched in Settings. The fonts (Space Grotesk, Space Mono, Bebas Neue, Work Sans, Noto
  Music) ship with the app as woff2 files, so it works offline.

## Open questions

For the owner:

- The app's name and icon.
- Tablet only, as you said, though the repo's README lists it for tablet and phone.
- Which keyboard, and how many keys: it sets the generator's range and the Scales drill's octaves.
- Whether the microphone is for the piano too, or for voice or another instrument.

Only the devices can settle:

- How much timing jitter the keyboard and Android's MIDI path add, and whether USB and Bluetooth differ.
- The tablet microphone's delay, its lowest reliable note, and how it copes with the pedal, fast repeated notes and room noise.

Open in the research: how best to step difficulty up, whether read-ahead and rhythm-only drills carry over to full reading in
adults, whether spaced note review helps staff reading itself, and whether streaks help adult music learners keep going.
