# Prima: how it works

Prima is a sight-reading and piano practice app for the owner's Lenovo Idea Tab Pro (13 inches), standing on the piano's music
stand. It's played on the owner's Yamaha P-225B (88 keys) over USB MIDI: no microphone, and no on-screen keyboard. Landscape is
for practice; turned to portrait, it shows a full page of sheet music. It's for one person, kept on the tablet, with no accounts
and no cloud. The name is from *prima vista*, Italian for sight-reading, and the icon is the LCD from the
[mockups canvas](https://claude.ai/artifact/UdQVrhTZHqqgsCk3WG7cmM).

This is the design before any code; [plan.md](plan.md) is how it's built. Every rule below comes from
[research.md](research.md), which says how strong each piece of evidence is; where something is a choice of ours rather than a
finding, it says so.

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
  The music looks like printed sheet music, black on paper, on the grand staff (the owner's choice).
- **A forgiving streak and real numbers.** A streak that a missed day can't wipe out, and progress like "92% in time at
  level 6, up from 84%", not points or badges.

## Sections

The rail down the left edge, in both looks: Today, Drills and Sight-read; Library, Tools and You; Settings at the foot. In
Vinyl they're Side A and Side B. In portrait the rail runs along the bottom.

### Today

What to do now. The day's plan as five steps (below), with the next one ready to start; a shorter five-minute plan for busy
days that still counts; this week's minutes and days; the streak; and the practice cue you set once ("When I sit down after
dinner, I'll open the app"). The home mockups show it: the tempo knob or the record, the next passage on a small sheet, and the
steps along the bottom. The day's passages come in a fixed order, so the one shown as up next is the one Sight-read gives. When
every step is done, a line sums up the day.

The first time, a welcome asks for a name (optional), the minutes a day, the cue and the look, then goes to the timing
calibration or straight to Today.

### Sight-read

The reading screen, and the way to read more than the plan asks for. A passage at your level: thirty seconds to look it over
(you can tap its rhythm or try a bar), one bar of count-in, then play it in time with the click. After the last bar, the
summary. Then the next passage, with the level and tempo already adjusted.

Variants, one a day in the plan and any time here:

- **Read ahead**: the bars you've played are covered and the view ahead is limited, but at least a second and a half of music
  ahead is always shown, since about a second is how far everyone's eyes run ahead. The look shows only the opening, so the
  passage can't be learned before it's hidden.
- **Duet**: play one part while the piano plays the other in time (Prima sends it over MIDI). It's how the research tests
  reading, and part of the piano course that did best.
- **Hear first**: the piano plays the passage once, then you read it.

### Drills

Short, focused practice, each a few minutes. Reading drills: Notes, Ledger lines, Intervals and shapes, Chords, Key signatures,
Rhythm, Keyboard geography, and Listen and play back. Technique drills: Scales, Arpeggios, and Finger independence. Each is
described under [Drills](#drills-1) below.

### Library

A collection of real pieces, many of them public domain and bundled with the app, and music you import, PDFs included. Turned to
portrait, a piece shows as a full page that turns or scrolls with the music. The sight-reading track also reads excerpts from the
collection at your level. Practise a piece with the same reading screen: loop a bar, slow it down, and see the same timing
summary. Finger-independence patterns are taken from these pieces where possible, because finger training carries over only
partly to other patterns. A separate research, `library.md`, settles the sources, the formats and how a PDF plays out.

### Tools

- **Metronome**, with a count-in and a click that can fade to silence.
- **Keyboard check**: names each key you press with its velocity and the pedals, and checks the whole keyboard arrives (A0 to
  C8). No picture of a keyboard.
- **Timing**: play any key with the click for 16 beats. The delay is taken off every note from then on. It includes your own
  habit of playing a touch early, so the lean shown afterwards is measured from that day. The spread shown is mostly your own
  tapping, not the keyboard's noise.

### You

Progress, as trends over the last twelve weeks (see [Progress](#progress-and-the-streak)), with a note that a plateau after quick
early gains is normal. The profile it's headed with (name, which clefs you drill, daily goal, the practice cue) is set in
Settings.

### Settings

Profile (name, minutes a day, the cue, which clefs the note review drills), practice (one or two bars of count-in, and whether a
wrong note in a drill is named), look (Studio or Vinyl), keyboard (the device, sending notes to the piano, timing), notation (the
music font and the sheet's size), the click (where it sounds, its sound and volume), and your data. A daily goal much above last
week's average gets a note to go up gradually; Prima never raises it by itself.

Your data: **Back up** shares the data file through the tablet's share sheet (to Drive, or to the PC); **Restore** takes a backup
back, after checking it's one of Prima's; **Start over** clears levels, notes, history and the streak but keeps the settings, after
asking. Nothing leaves the tablet unless you back it up.

## The sight-reading flow

1. **Look.** The passage appears with thirty seconds on a bar at the top. You can tap its rhythm on any key or try a bar; nothing
   is scored.
2. **Count in.** One bar of clicks at the passage's tempo.
3. **Play.** The click keeps time and never waits. The screen shows the music and a cursor on the beat. Nothing is marked yet.
4. **Summary.** After the last bar: in-time accuracy, correct notes a minute, whether you kept going, and the passage again
   with each note marked right or wrong and early or late. A wrong note names what you played instead (G4→A4) and shows it on
   the staff.
5. **Next.** The level and tempo adjust and the next passage comes up.

### How a passage is scored

- A note counts if its pitch is right and it lands within a timing window around its place. The research used ±0.25 s; here the
  window scales with tempo.
- Timing is a share of the beat: under about 5% is in time, 5–10% is audible, over 10% is off. These bands are the research's
  design inference from the perception studies. In-time accuracy, the headline, counts right notes that aren't off, so
  audible ones count too (our choice: the 5% line is where listeners start to notice, too strict a bar for reading at sight).
- Your average early or late lean is measured and taken out before steadiness is graded.
- Measured per passage: in-time accuracy, correct notes a minute, kept going (no gap longer than a bar), whether it was clean
  (every note right), tempo drift, extra notes, missing notes, and the timing error of correct notes. A wrong note one or two keys away from the right one counts as
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

Within a level the app aims for about 85% in time, judged over the last three passages, since one short passage is noisy.
Over 90% raises the tempo, under 80% lowers it, and a passage you stopped in lowers it at once; if it still falls apart at
the slowest tempo, the next passage is simpler. These numbers are ours. Moving up raises one thing at a time: rhythm, hands, key, range and leaps, or texture. The exams set no
limits for range or ledger lines, so those are our choices. No study compares ways of stepping up; this is a reasoned default.

## Drills

Every drill shares the rules above: accuracy first, then speed; results after the try; timing as a share of the beat; about 85%
success.

| Drill | You see and do | It measures | It gets harder by |
| --- | --- | --- | --- |
| Notes | A note, or a short group, on the staff; play the key | Right or wrong, and the time from seeing to playing, per note | Landmarks first (middle C, treble G, bass F, the outer Cs), then their neighbours, then both clefs and a wider range |
| Ledger lines | Notes on one to four ledger lines; play them | As Notes, kept per ledger position | One line, then two, from the C anchors, then by thirds |
| Intervals and shapes | A two- to five-note figure, first note given; play it | Each step right or wrong; a wrong interval apart from a wrong start | Steps, 3rds, 4ths and 5ths, 6ths to octaves; then mixed directions and moved shapes |
| Chords | A stack of notes; play it at once | The notes against the target, how together they land, missing and extra notes | Two-note chords, triads and inversions, then four parts across the hands |
| Key signatures | A signature, then a phrase using its sharps or flats | The tonic, and every note played without its sharp or flat | C and A minor, then one to five sharps and flats |
| Rhythm | One to six bars of rhythm; tap a bar of beat, then the rhythm on any key | Early or late lean, steadiness, drift without the click, missed and extra taps | Quarters, eighths, dotted notes, sixteenths, syncopation, triplets; the click fades to a count-in, then silence |
| Keyboard geography | A note or interval; play it without looking down | Wrong notes one or two keys away, and time to play | Within a hand position, then out of it, octave jumps, larger leaps, both hands |
| Listen and play back | Hear a short phrase on the piano, play it back, then see it written | Pitch and rhythm against the phrase | Five notes of a scale, then six to eight, then more keys; last, improvise an answer phrase (not scored) |
| Scales | A named scale with its fingering, octaves and tempo; play it in sixteenths to the click | Evenness of the gaps between notes, a slip at each thumb crossing and octave, evenness of touch, legato overlap, and the hands' sync | The exams' order of keys and octaves; the tempo rises in small steps from a comfortable speed |
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
| 5. One spot, or a piece | 2–3 min | The bar you missed most today, 20% slower, then the four bars around it at their tempo; or a piece from the Library. Then the day's summary |

The five-minute plan is the note review and two passages. The plan mixes scales, rhythms and variants across days instead of
repeating one block, and doesn't raise the daily minutes sharply from week to week.

## Progress and the streak

Shown as trends over days and weeks. Mixed practice feels worse at the time and works better later, so a single session isn't
judged.

- **Reading**: the level, one long-scale reading score with a trend line, in-time accuracy and correct notes a minute at the
  level, how often you kept going, Zhukov's four (tempo drift, extra and missing notes, timing error), lost-on-the-keyboard
  errors apart from reading errors, read ahead against plain reading at the same level, and the passages and bars read each
  week, the main lever.
- **Notes**: a heat map on the grand staff of how often each note was missed this month, the most-missed and slowest notes, and
  how many notes are fluent (right, fast and steady across sessions).
- **Rhythm**: your early or late lean, steadiness, drift without the click, and the figures you miss most.
- **Technique**, per scale or arpeggio and hand, from the last run: evenness at a tempo, the fastest tempo at which it stays even,
  crossing and octave slips, evenness of touch, legato, and how far apart the hands are.
- **Habit**: minutes a week, days practised out of the last seven, the streak and the freezes banked. The share of clean passages
  shows under Reading (retention follows the share of correct tries). The streak can be repaired: a missed day can be covered
  by a freeze, and the week counts as kept at five days out of seven. A break is described plainly, never as a failure.

Counts for the week still going (passages, minutes) are drawn as a dashed line to a hollow point, so a part week doesn't look like
a drop.

## The keyboard

- **MIDI over USB** from the P-225B is the only input. It gives exact notes, chords, velocity, the pedals and timing, in every
  drill. There's no microphone and no on-screen keyboard.
- **Sound from the piano.** Prima sends notes back to the piano over MIDI, so Duet, Hear first and Listen and play back play
  through the piano itself.
- **Latency** is calibrated once (play along to a click; the median offset is taken out). How much timing jitter the keyboard
  and the tablet add is unknown until measured. Prima estimates the USB and MIDI path's share from the piano's Active Sensing
  messages, which arrive at a fixed interval (our idea, untested); differences smaller than that aren't shown.

## How the data is shaped

Everything lives in one SQLite file on the tablet.

- **Profile**: name, clefs drilled, daily goal, practice cue, look, the keyboard's settings and the timing offset.
- **Levels**: the current reading level and tempo, the rhythm level, and per scale and arpeggio the current tempo and target.
- **Note cards**: one per note and clef, with its review box, when it's next due, its recent answers and response times.
- **Sessions**: each day's practice, its steps and minutes.
- **Attempts**: one per passage, rhythm or technique run, keeping its settings (the level's step, the key it was in and the key it
  avoided), the generator's seed, the notes played with their times and velocities, and the measures worked out from them, so a
  passage can be made again (one spot does) and a result recomputed if the scoring changes.
- **Pieces**: the collection and imported pieces, with their sources and licences, levels, and practice history.
- **Streak**: the days practised and the freezes used.

## How it's built

Like the workout app: .NET 10 MAUI Blazor, with the logic in a plain library tested with xUnit. [plan.md](plan.md) has the
details.

- **Core**: the music model, the passage generator, the note-review schedule, scoring (matching played notes to the score,
  timing, evenness), levels and progress. No UI, all tested.
- **UI**: the Razor screens.
- **Tablet**: the Android app: USB MIDI in and out through Android's MIDI API, the click through `AudioTrack`, and the tablet's
  screen kept on during practice.
- **Preview**: the UI as a Blazor Server page against sample data, for checking screens from the cloud, as `CLAUDE.md`
  describes.
- **Notation** is real sheet music, engraved by Verovio in the web view: black on paper, on the grand staff, the same in both
  looks, with the look around it and the cursor and marks drawn on top.
- **The look** is the [Studio & Vinyl design system](https://claude.ai/artifact/XtgAHfVyw3L94e5DKrj2ii): its own two looks, not
  the Kit's Logbook and Telemetry, switched in Settings. The fonts (Space Grotesk, Space Mono, Bebas Neue, Work Sans) ship with
  the app as woff2 files, so it works offline; Verovio brings its own music fonts.

## Open questions

Only the devices can settle:

- How much timing jitter the keyboard and Android's MIDI path add.
- Where the click sounds (the tablet's speaker, or the piano as a USB audio device) and its delay.
- Whether the piano plays the notes Prima sends it, and whether it echoes them back.

Being researched: the Library's public-domain sources, importing PDFs, and page turns (`library.md`).

Open in the research: how best to step difficulty up, whether read-ahead and rhythm-only drills carry over to full reading in
adults, whether spaced note review helps staff reading itself, and whether streaks help adult music learners keep going.
