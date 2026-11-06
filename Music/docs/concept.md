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
- **Feedback during the phrase as well as after it: the owner's call.** Live visual feedback on timing didn't help in two
  controlled studies, and may load the eyes, so the research put the marks after the last bar. The owner wants to see how it's
  going while playing, so each note is marked on the sheet as it's played; the click still never waits, and the summary after
  the last bar lines up the whole take and replaces those marks. Worth watching whether reading suffers.
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
(you can tap its rhythm or try a bar; Settings makes the look 10, 20 or 30 seconds, or as long as you like, and Start begins
the count-in sooner), one bar of count-in, then play it in time with the click, each note marked as you play it. After the last bar, the summary. Then the next passage, with the level and tempo already adjusted.

Variants, one a day in the plan and any time here:

- **Read ahead**: the bars you've played are covered and the view ahead is limited, but at least a second and a half of music
  ahead is always shown, since about a second is how far everyone's eyes run ahead. The look shows only the opening, so the
  passage can't be learned before it's hidden.
- **Duet**: play one part while the piano plays the other in time (Prima sends it over MIDI). It's how the research tests
  reading, and part of the piano course that did best.
- **Hear first**: the piano plays the passage once, then you read it.

After a passage, **Again** reads the same one once more. A second reading is practice, not reading at sight, so it's kept in the
history as a repeat and doesn't move the level or count as one of the day's passages.

The play bar's two modes are here too ([Playing to a tempo](#playing-to-a-tempo)). In time is reading at sight, as below. **Wait
for me** waits at each note for the right keys and counts the notes right first time: practice for the notes, so it doesn't
move the level or count as one of the day's passages. A passage stopped early is kept, but doesn't move the level either. The
level can be set by hand on the first screen, with − and + and a line on what it reads (keys, metres, hands, tempo): it starts
at the bottom of that level's tempo range, as Find my level sets it, and moves on from there as usual. The tempo's − and + set
it for this passage and the next ones at the level.

### The piano buttons

So the hands needn't leave the keys between runs, the C♯ above middle C presses the screen's main button and the D♯ beside it
presses Again: C♯ for a new passage, C♯ again to start it without waiting out the look, and after it the next one; D♯ to read
it again. Any screen's primary button (the orange one, or Play) answers to C♯: the drills' Start, Next try, Run and Another
set, Today's next step. They're in the middle of the keyboard, a short reach from where the hands are, so only a deliberate
press counts: the key on its own, with no other key in the moment before it. A C♯ played in a passage you're trying in the
look, or among the notes of a scale, presses nothing. While a run counts in or plays, every key is music; Endless notes' Stop
is a tap; and the buttons that mustn't be pressed by accident never answer to the piano: clearing your progress, removing a
piece, the Metronome you play along with, the timing calibration's Start. A key pressed in a technique run's count-in, or just
after its last note, isn't part of the run. While the piano is plugged in, the buttons they press are marked C♯4 and D♯4. They
can be turned off in Settings.

### Playing to a tempo

Every screen that plays music to a tempo has the same play bar: the screen's own figures, then the mode, the tempo and Start
(the piano's C♯4). Sight-read, a Library piece, Scales, Arpeggios and Finger independence all play this way.

- **In time**: a bar of count-in (two, in Settings), the click that never waits, the cursor moving with the beat, each note
  marked as it's played, and the timing summed up after: in time, correct notes a minute, kept going, and the lean.
- **Wait for me**: no click and no count-in. The cursor waits at each note or chord until its keys are played, in any order. A
  wrong key marks the note red, with a grey head where the key is and its name, and the cursor waits on; the note stays red
  once it's put right, so the slip shows where it was. A trill's or a mordent's extra notes aren't slips. Afterwards: the notes
  right first time, the wrong keys, and notes a minute at your own pace.
- The mode is kept for each screen. The tempo's − and + move by a metronome's marks (… 60, 63, 66, 69, 72, 76 …), so a step
  is about the same share of the tempo, slow or fast. Stop ends a run where it is, and its summary covers the music up to there.

### Drills

Short, focused practice, each a few minutes. Reading drills: Notes, Endless notes, Ledger lines, Intervals and shapes, Chords,
Key signatures, Rhythm, Keyboard geography, and Listen and play back. Technique drills: Scales, Arpeggios, and Finger
independence. Each is described under [Drills](#drills-1) below.

### Library

A collection of real pieces bundled with the app, and music you import. `library.md` settles the sources, the formats and how
a PDF plays out; this is what came of it.

- **The collection**: about 240 public-domain and openly licensed pieces, chosen by named works published before 1931: 133
  MuseScore users' engravings through PDMX, as MusicXML, and 104 PDFs with their MIDI from the Mutopia Project. Each is credited
  as its licence asks (**Where the music comes from**). The list is grouped by level, and filtered to the pieces near yours, those
  to play along with, PDFs, or your imports. The collection comes prepared, so its first opening only stores it: the Library tool
  engraves, grades and cuts each MusicXML piece when the bundle is built, and a collection engraved as one score (Chopin's
  Preludes, Tchaikovsky's album) is split at its final barlines into its pieces ("Children's Album, No. 5"), each starting with
  the key, metre and clefs in force there, so no piece is the whole collection to open or engrave. An import is read and graded
  on the tablet, away from the screen's thread.
- **Levels**: each MusicXML piece is cut into 2-, 4- and 8-bar excerpts at phrase starts (never into a tie or a slur, and never
  the same bars twice) and each is graded by the ladder's own features: key, metre, rhythm figures, hands, chords in a hand,
  accidentals, range, leaps in the tune, ledger lines, clef changes, pedal, 8va and ornaments. The hardest sets the level, as
  Henle grades. A piece shows its hardest excerpt's level and where most of it reads. A PDF can't be graded from its notes, so it
  shows about where its collection sits.
- **A piece** shows as pages: two systems at a time in landscape, the whole page in portrait under a slim bar. It turns by the
  screen's outer thirds, a swipe, a page turner's or a keyboard's arrow keys, the soft and sostenuto pedals (forward and back),
  or the keyboard's top and bottom keys (C8 and A0); never the sustain pedal, which the music needs. Or it scrolls, a system at
  a time.
- **Playing a notation piece** is on the play bar every screen with a tempo shares ([Playing to a tempo](#playing-to-a-tempo)):
  In time or Wait for me, the tempo, and Play, from the start or from the bar of a note you tap (it's lit until you play). A
  piece on two staves can be played a hand at a time: **Hands** (Both, Right, Left, kept for each piece) has the run listen for
  the upper staff's notes or the lower's alone, dims the other's, and says the hand on Play. While
  it plays, the music scrolls in its frame, the line being played kept in the upper third so the next one shows; afterwards the
  bar shows the headline (in time, or right first time), every note stays marked, and the summary is under the music. It
  doesn't move your level.
- **A PDF** turns by the same means. Portrait shows a whole page, and by default a half-page turn first shows the next page's
  top over this one's bottom (as forScore does). Landscape moves a screenful of systems at a time. **Scroll at the tempo** finds
  the page's systems from its staff lines, and after a bar of count-in moves a system at a time, each lasting bars × beats a bar
  × 60 ÷ tempo seconds, with the system being played lit. The tempo and metre start from the piece's MIDI, which the piano can
  also play (**Hear it on the piano**).
- **The sight-reading track** reads real music too: every third passage of the day, from level 4, is an excerpt at or below your
  level of the length the level reads (four bars, eight from level 7), engraved from the piece itself. A read excerpt rests
  three weeks. Excerpts don't move the level: their grades aren't calibrated against your reading yet.
- **Imports**: MusicXML, MEI and ABC are engraved and graded like the collection; a PDF is kept as pages. MIDI, MuseScore and
  Humdrum files need converting to MusicXML on the PC first.

Finger-independence patterns are meant to come from these pieces where possible, since finger training carries over only partly
to other patterns; for now they're built-in five-finger figures.

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
wrong note in a drill is named), look (Studio or Vinyl), keyboard (the device, sending notes to the piano, the piano buttons,
timing), notation (the
music font and the sheet's size), the click (where it sounds, its sound and volume), and your data. A daily goal much above last
week's average gets a note to go up gradually; Prima never raises it by itself.

Your data: **Back up** shares the data file through the tablet's share sheet (to Drive, or to the PC); **Restore** takes a backup
back, after checking it's one of Prima's; **Start over** clears levels, notes, history and the streak but keeps the settings, after
asking. Nothing leaves the tablet unless you back it up.

## The sight-reading flow

1. **Look.** The passage appears with thirty seconds on a bar at the top (10, 20 or 30 in Settings, or no limit). You can tap
   its rhythm on any key or try a bar; nothing is scored. Start, or the piano's C♯4, begins the count-in whenever you're ready.
2. **Count in.** One bar of clicks at the passage's tempo (two, in Settings), seen as well as heard: a cell for each beat
   lights on its click, and the cursor waits at the first note, pulsing with it. The timed drills (rhythm, scales, arpeggios,
   finger independence, listen and play back) show the same cells.
3. **Play.** The click keeps time and never waits. The cursor moves with the beat, through the last note of each line to its
   closing barline, and each note is marked as it's played: green when it's right, red when it's another key (a grey head on
   the staff shows the one you played, and its name above), amber when it's more than 10% of a beat early or late (a ring on its
   line shows when you played it, an arrow which way), and outlined red once its time has passed with nothing played. A slip put
   right at once takes the right key's mark. These are judged note by note, before your lean is known.
4. **Summary.** After the last bar: in-time accuracy, correct notes a minute, whether you kept going, and the passage again
   with each note marked the same way, now from the whole take lined up and your lean taken out, so a note can change from
   the mark it had while playing. A wrong note names what you played instead (G4→A4) and shows it on the staff.
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
| Endless notes | One note after another until you stop: a right answer moves straight on, a wrong one shows what you played on the staff; treble, bass or both, the staff or out to two ledger lines, and sharps and flats if you like | The time of each answer; the median of your last ten right answers, the best of those in the run, and your best ten from earlier runs to beat | By you: faster. A missed note comes back three to five notes later. It's practice on its own, outside the Notes drill's spaced review |
| Ledger lines | A run of four or more notes stepping from the staff out past it, above or below, in the clefs Settings has you read: from a named note on the staff out, or from the far note, unnamed, back in; or one note out there, read by counting its lines. Play them | Each note right or wrong, and the time to play | Out to one ledger line, then two, then three, where half the runs go by thirds, line to line |
| Intervals and shapes | A two- to five-note figure, first note given; play it | Each step right or wrong; a wrong interval apart from a wrong start | Steps, 3rds, 4ths and 5ths, 6ths to octaves; then mixed directions and moved shapes |
| Chords | A stack of notes; play it at once | The notes against the target, how together they land, missing and extra notes | Two-note chords, triads and inversions, then four parts across the hands |
| Key signatures | A signature, then a phrase using its sharps or flats | The tonic, and every note played without its sharp or flat | C and A minor, then one to five sharps and flats |
| Rhythm | One to six bars of rhythm; tap a bar of beat, then the rhythm on any key. No line moves across the music while you tap: keeping the pulse is the point. Afterwards each note shows where its tap landed against the click, green on the beat, amber more than 10% of a beat off with an arrow, a ring wherever the tap was 5% or more off, outlined red if missed; and each bar is summed up: one that ran early or late as a whole and by how much, the notes in it well off, where you came back on the beat | Early or late lean, steadiness, drift without the click, missed and extra taps | Quarters, eighths, dotted notes, sixteenths, syncopation, triplets; the click fades to a count-in, then silence |
| Keyboard geography | A note or interval; play it without looking down | Wrong notes one or two keys away, and time to play | Within a hand position (C to G from middle C), then just out of it, then leaps of a sixth to a ninth either way, then anywhere an octave either side |
| Listen and play back | Hear a short phrase on the piano, play it back, then see it written | Pitch and rhythm against the phrase | Five notes of a scale, then six to eight, then more keys; last, improvise an answer phrase (not scored) |
| Scales | A named scale with its fingering, octaves and tempo; play it in sixteenths to the click | Evenness of the gaps between notes, a slip at each thumb crossing and octave, evenness of touch, legato overlap, and the hands' sync | The exams' order of keys and octaves; the tempo rises in small steps from a comfortable speed |
| Arpeggios | As Scales | As Scales, with a close look at the crossings | A fifth's range, then one octave hands separate, two, hands together, inversions |
| Finger independence | Five-finger patterns, trills (3–4, 4–5), held notes; patterns from your pieces where possible | Each finger's timing and touch, trill rate and steadiness, whether held keys stay down | Slow and even, then faster, then the weak pairs, then both hands |

The drills read from a staff (Notes, Ledger lines, Intervals and shapes, Chords, Key signatures, Keyboard geography) mark each
answer on it as soon as it's played, as Sight-read's summary does: green right, red wrong with a grey head where the key you
played is and its name above, outlined red missed, and a chord's extra keys as grey heads beside it, named. The words under
the staff name the same notes: "Off: note 2, E5 for F5", "The shape was right, from the wrong first note: …", "Not quite: B4,
D5 missing; C5 extra". A set's summary lists the single notes and chords you missed. The drills with steps show the step
beside the item count, with − and + to choose it: a new set starts at the step chosen, and from there over 90% in two sets in a
row steps up and under 80% steps back.

Notes come back on a spacing schedule: a note answered right but slowly counts as not yet learned, and comes back sooner than a
fast one. Confusable notes are mixed. After each technique run the music is marked as Sight-read's summary is: green right,
red wrong with a grey head where the key you played is and its name above, outlined red missed, and a grey head beside a note
for an extra key. The keys are lined up with the notes by the fewest slips, so one slip counts once instead of throwing out
every note after it, and the result says how many were wrong, missed or extra. A run with a slip doesn't count for evenness.
Technique results are compared with your own earlier runs, since there are no amateur norms; differences under about 10 ms are
shown as below what anyone can hear. Repetitions are capped per set and per day, with
a reminder to stop if anything hurts: a sensible precaution, not a proven one.

What a technique drill plays is yours to choose, whatever your reading level: the key, stepped easier or harder in the exams'
order (C, A minor, G, F, D …), a minor scale harmonic or melodic, one to four octaves (or an arpeggio's broken triad), and
the hands; finger independence picks its pattern. **Today's** goes back to the day's plan. Each scale or arpeggio keeps its own
tempo and target, by key, octaves past one, and hands. The first time, a run in Wait for me at your own speed sets the
comfortable speed the In time runs start a little under; the tempo can also be set with − and +. Wait for me runs are practice
for the notes: they count towards the set's five runs and the day, not towards evenness or the tempo.

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
- **Pieces**: the collection and imported pieces, with their sources and licences, levels, and the MEI Verovio made from each
  notation piece (bars numbered in order, a pickup 0, so the numbers printed are the ones Prima says). A PDF keeps its file, its
  systems found on each page with their bars, its metre and tempo, and where you were. Imported files sit beside the data file.
- **Excerpts**: each piece's 2-, 4- and 8-bar stretches with their levels, how often each was read, and when last.
- **Streak**: the days practised and the freezes used.

## How it's built

Like the workout app: .NET 10 MAUI Blazor, with the logic in a plain library tested with xUnit. [plan.md](plan.md) has the
details.

- **Core**: the music model, the passage generator, the note-review schedule, scoring (matching played notes to the score,
  timing, evenness), levels and progress. No UI, all tested.
- **UI**: the Razor screens.
- **Tablet**: the Android app: USB MIDI in and out through Android's MIDI API, the click through `AudioTrack`, and the tablet's
  screen kept on while Prima is open (every screen is read from the music stand; another app sleeps as usual).
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
