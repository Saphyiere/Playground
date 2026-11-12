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
looking) and for clean technique (scales, arpeggios, even fingers, measured from MIDI), a catalogue of technique exercises
with how to do each, a daily plan of about twenty minutes, and progress shown as honest numbers over weeks.

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

The rail down the left edge, in both looks: Today, Drills, Sight-read and Exercises; Library, Tools and You; Settings at the
foot. In Vinyl they're Side A and Side B. In portrait the rail runs along the bottom.

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
  marked as it's played, and a summary after (see [The summary](#the-summary) and
  [How a passage is scored](#how-a-passage-is-scored)).
- **Wait for me**: no click and no count-in. The cursor waits at each note or chord until its keys are played. Notes written
  together, a chord in one hand or both hands at once, go down **together**: in any order, the first key to the last within
  0.15 of a second. Played a key at a time, which finds the chord but doesn't play it, its notes turn amber, "together" shows
  over them, and the cursor waits until they're struck together; they stay amber, so it shows where that was. A key of the
  chord on its own before the chord is played together is no fault. A wrong key turns the note grey, named in grey, with a
  red head where the key is, named in red, and the cursor waits on; the note stays so once it's put right, so the slip shows
  where it was. A trill's, a mordent's or a turn's keys, and grace notes leading into a note, aren't slips. A chord the music
  marks to be rolled (an arpeggio sign, on one hand's chord or across both) is played as a roll: its keys one after
  another, none more than 0.3 s after the one before. With a longer wait between two of them it's marked like a chord played
  a key at a time, says "in one roll", and waits to be rolled in one go. (In time doesn't know a roll yet: its notes are
  all judged against the one moment they're written at.) Afterwards: the notes right first time
  (not a note with a slip, nor a chord played a key at a time first), the wrong keys, the chords not together, and notes a
  minute at your own pace.
- The mode is kept for each screen. The tempo shows as its figure; tapped, it opens its − and +, which move by a metronome's
  marks (… 60, 63, 66, 69, 72, 76 …) so a step is about the same share of the tempo, slow or fast, and a field to type any
  tempo from 20 to 240. Stop ends a run where it is, and its summary covers the music up to the last key you played: what was
  due between that key and Stop, while the hands were on their way to the screen, isn't counted as missed. A run stopped
  before any key leaves no summary and isn't kept. A run is played on the piano: with none plugged in, Start and Listen look
  off, and a tap says so under the bar.
- **While playing** (Settings › Practice, and a piece's Options) says what shows on the music In time: the **Cursor**, and the
  **Marks as you play**, each on or off, for playing with only the click and the page. With the cursor off the music still
  scrolls with the run. With the marks off every note is still judged, and the marks show when the run ends, to go back
  over. Wait for me always shows where it's waiting, and "together" over a chord that needs it.

### Drills

Short, focused practice, each a few minutes. Reading drills: Notes, Endless notes, Ledger lines, Intervals and shapes, Chords,
Key signatures, Rhythm, Keyboard geography, and Listen and play back. Technique drills: Scales, Arpeggios, and Finger
independence. Each is described under [Drills](#drills-1) below.

### Exercises

Technique to work through, at the piano and away from it: fourteen sections, from how you sit to the pedal, each exercise
with what you do, how, and what to notice. They come as two lists, the ones **to play** and the ones **to read**, which can
be searched and show when each was last done; an exercise opens on a page of its own, with its music if it's played.
Described under [Exercises](#exercises-1) below.

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
- **A local collection**: more music for this tablet alone, never in the repo, whose terms allow personal, non-commercial use
  but not a place in a public repo. The Library tool builds it on the PC (`-- local`): Craig Sapp's Humdrum editions (Mozart's,
  Haydn's and Scarlatti's sonatas, Joplin's rags, Bach's 370 chorales on a grand staff) and the DCML's research corpora as
  MuseScore files (Beethoven's sonatas, Bach's suites, Chopin's mazurkas, all of Grieg's Lyric Pieces, Kinderszenen, The
  Seasons, Debussy, and more), both CC BY-NC-SA 4.0: about 1,200 pieces, prepared like the bundle and packed in one file.
  **Import files** with that file stores them in the background with the same meter, its pieces credited like the
  collection's; those already here stay as they are. The list then holds about 1,500 pieces, so it's drawn again a second at a
  time at most while pieces are stored, and the search follows once typing pauses.
- **Levels**: each MusicXML piece is cut into 2-, 4- and 8-bar excerpts at phrase starts (never into a tie or a slur, and never
  the same bars twice) and each is graded by the ladder's own features: key, metre, rhythm figures, hands, chords in a hand,
  accidentals, range, leaps in the tune, ledger lines, clef changes, pedal, 8va and ornaments. The hardest sets the level, as
  Henle grades. A piece shows its hardest excerpt's level and where most of it reads. A PDF can't be graded from its notes until
  they're read (below), so it shows about where its collection sits.
- **A piece** opens on its bar and its music. The bar ends with two keys: **Options**, which shows the piece's options in
  place of the music (the view, the hands, play along, where to start; a PDF's tempo and bars) until it's pressed again,
  and **Full screen**, which leaves only the bar and the music, the tablet's own bars hidden too, in either orientation; the
  same key, or leaving the piece, brings everything back. A piece fits the screen, only its music scrolling: between runs with
  About this piece under it, and after a run with the run's summary there instead; while a run plays the music has that space
  too. A notation piece shows whole in
  one scroll unless it's been set to Pages (kept for each piece; a run plays in the scroll all the same, a line at a time, and
  the piece opens in Pages again the next time). As pages: two systems at a time in landscape, the whole page
  in portrait under a slim bar. They turn by a swipe, a page turner's or a keyboard's arrow keys, the soft and sostenuto pedals
  (forward and back), or the keyboard's top and bottom keys (C8 and A0); never the sustain pedal, which the music needs. A tap
  on the music never turns: it's for the bars.
- **Playing a notation piece** is on the play bar every screen with a tempo shares ([Playing to a tempo](#playing-to-a-tempo)):
  In time or Wait for me, the tempo, and Play, from the start or from a bar you tap, anywhere in it: it's lit and outlined
  (the first bar too; a scroll is never a tap), with a tab over it saying where the run starts, and **Loop**. On a piece's bar its keys, switches and figures are one height: one row in landscape, and in portrait the figures and
  keys over the run's controls, Play at the end. A
  piece on two staves can be played a hand at a time: **Hands** (Both, Right, Left, kept for each piece) has the run listen for
  the upper staff's notes or the lower's alone, dims the other's, and says the hand on Play. A run follows the music's repeats:
  each repeated part twice, the second time by its second ending (D.C. and D.S. aren't followed yet), and the cursor goes back
  with it; a note played twice keeps the mark of the latest time. Loop plays bars again and again until you stop: on the tab
  it loops that bar, then a tap past either end stretches the loop to that bar and a tap inside trims its nearer end, or
  either end's round handle drags it, onto other lines too, the bar under the finger named; × on the tab ends it. **The
  strip** under the piece's bar holds every bar as a cell, each line's first marked and the bars on screen boxed: tap a cell
  for that bar, as on the music (which comes into view, or turns to its page), or drag along it to loop the bars between,
  then drag its slim handles. The music scrolls under a finger, with nothing beside it. Full screen hides the strip. While a
  run plays, tapping a bar, on the music or the strip, starts it again from there, and **‹ Bar** goes back a bar (in a loop, to its start). **Play along**, with one hand chosen, has the piano play the other In time. **Listen**, beside Play, has
  the piano play the piece, to hear how it goes: from the start bar (or round the loop) at the tempo, after a bar of count-in
  so you can join in, the click then quiet and the cursor moving with it. It plays what a run would sound like with your part
  played right: the hand chosen, the other too with play along, or both. Nothing is marked or kept; taps and ‹ Bar move it as
  they move a run, still listening. It needs the piano taking notes from the tablet, as play along does: without
  one its key looks off, and a tap on it says what it needs. While it plays, the music
  scrolls in its frame, the line being played kept in the upper third so the next one shows; afterwards the
  bar shows the headline (**right in time**: the share of the notes played right and not off the beat; or right first time),
  every note stays marked, and the summary takes About this piece's place under the music, so how it went is on the screen
  with it, as in full screen: its few lines fit, and its details scroll in the summary's own share of the screen. It
  doesn't move your level.
- **A PDF** turns by the same means, and by a tap on the page's outer thirds. Portrait shows a whole page, and by default a half-page turn first shows the next page's
  top over this one's bottom (as forScore does). Landscape moves a screenful of systems at a time. **Scroll at the tempo** finds
  the page's systems from its staff lines, and after a bar of count-in moves a system at a time, each lasting bars × beats a bar
  × 60 ÷ tempo seconds, with the system being played lit. The tempo and metre start from the piece's MIDI, which the piano can
  also play (**Hear it on the piano**).
- **Reading a PDF's notes**, with Claude (Options › Notes › **Read the notes…**, with your key in Settings › Claude). First
  what it will cost: the page with the systems Prima found boxed (each is sent to Anthropic as a picture), the pages (all, or from
  one to another), the systems, the cost range ($0.15–0.60 a system) and the time (about 2½ minutes a round: the first system
  alone, since its time signature is printed only there, then four at a time). **Start reading** cuts each system from the page,
  drawn 2,576 px wide and cut at the widest white between systems, and each goes through three steps, shown as lamps on its
  row: Claude reads what's printed; Prima checks that every bar's beats add up and engraves the reading; Claude sets the
  engraving beside the print and corrects the bars that differ, leaving those it still can't be sure of in doubt. The bar shows
  the systems read, what's been spent, the time and about how long is left; **Cancel** stops at once, and what's been spent stays
  spent. Then each system's print over its reading, engraved at the same width: a bar in doubt is tinted on both, with Claude's
  note on it; **Listen from bar** has the piano play the reading from there; **Read again** reads the system once more ($0.15–0.60,
  about 2½ minutes, its cost and time added to the reading's), taking what you tell Claude you see there, in your own words, as
  something to check against the print.
  The reading waits on the piece, to come back to, until it's kept or thrown away. A key that's turned down, used-up credit, the
  workspace's spend limit, no connection or a busy Claude stops what's left, keeping what's read, and says what to do (Settings for
  the key, the Claude Console for credit or the limit, or a few minutes' wait); the systems not read can be read again, and **Keep**
  waits until every system has been. A first system that can't be read (Claude has 64,000 tokens for a system, thinking
  included) holds up the rest, since they're read with the time signature only it prints: they wait unsent, with no **Read
  again** of their own, and reading the first again carries on with them. Reading again stops what's left just as reading does. Until then a bar is named by its place in its system, since a missing system would shift
  the numbers. Kept, the reading is the piece's notation, graded and cut into
  excerpts like an import: it plays like any notation piece, with a **Print** key on its bar that shows the PDF again, as before,
  and lights while it does. About this piece says when its notes were read, what it cost and any bars left in doubt, with **Read
  the notes again…**. Claude reads the notes, chords, rests, ties, grace notes, tuplets, clef changes, 8va, repeats and endings;
  staccato, accents and tenuto; slurs, dynamics, hairpins and the pedal, which the expression judges then use; and the fingering,
  shown on the notation. Notes under an 8va line are kept at the pitch they sound, without the line. Clean engraved PDFs read
  well; scans less so.
- **The sight-reading track** reads real music too: every third passage of the day, from level 4, is an excerpt at or below your
  level of the length the level reads (four bars, eight from level 7), engraved from the piece itself. A read excerpt rests
  three weeks. Excerpts don't move the level: their grades aren't calibrated against your reading yet.
- **Imports** (**Import files**, one or several at once, or a zip of them, like a download that bundles a song's files):
  MusicXML, MEI and ABC are engraved and graded like the collection; a PDF is kept as pages, and takes a MIDI file of the same
  name picked or zipped with it, to be heard on the piano and scrolled at its tempo. One piece imported opens; several stay in
  the Library with a line saying what came in and what couldn't. A MIDI file is written out as notation first, since it holds
  when notes sound, not how they're written: the hands from its tracks (or middle C), the bars from its time signatures, the
  key from its signature or its notes, each beat snapped to sixteenths, thirty-seconds or triplets, a hand's held notes as a
  second voice, ties across beats and barlines. Notation software lets each note go a little before its written end (a file
  from MuseScore holds each for 95% of its length), so where a file's notes stop a little short of the next, each is written
  at its full length: a half note is a half note, not a dotted quarter tied to a scrap with a rest after it. A staccato note
  stays as short as it sounds, with its rest, and a written rest stays. One exported from notation software comes out close to
  its score, less what a MIDI file can't hold: its repeats are written out, a trill is the notes it plays, and slurs and
  dynamics are gone. One recorded from playing comes out ragged, and a pickup comes out as a full bar. MuseScore and Humdrum
  files need converting to MusicXML on the PC first (the Library tool does it for its corpora).

Finger-independence patterns are meant to come from these pieces where possible, since finger training carries over only partly
to other patterns; for now they're built-in five-finger figures.

### Tools

- **Glossary**: everything on a sheet of music. First and largest, **where the notes live**: the grand staff from the bass
  staff's bottom line to just over the treble staff, each note named under it, middle C in both, and the same notes on a strip
  of keys below; a note or a key tapped is lit in both, said in words (its line or space, a ledger line, the clef it's named
  for) and played on the piano when Prima sends it notes. Then every symbol and term by group (the staff; notes and rests;
  rhythm and time; sharps, flats and keys; loud and soft; touch; speed and mood; repeats and the roadmap; ornaments; hands,
  fingers and pedals), the most common first in each. Each says what it is, how it's played and how it sounds, and opens to a
  bar or two engraved as the sheet engraves it. A search finds things by name or by what they look like ("curved line",
  "dots", "hairpin"). On a piece, **What's this** (the ? key on its bar) makes a tap on the music name the symbol under it from
  the glossary, a note with its name and where it sits, with the way to its full entry.
- **Metronome**, with a count-in and a click that can fade to silence.
- **Keyboard check**: names each key you press with its velocity and the pedals, and checks the whole keyboard arrives (A0 to
  C8). **Your soft and loud**: four notes as softly as you can and still hear them, then four as loudly as you'd play *ff*; the
  middle of each is your range, which loudness is judged across (a usual 25 to 105 until then; too close together to tell *p*
  from *f*, it asks again). No picture of a keyboard.
- **Timing**: play any key with the click for 16 beats. The delay is taken off every note from then on. It includes your own
  habit of playing a touch early, so the lean shown afterwards is measured from that day. The spread shown is mostly your own
  tapping, not the keyboard's noise.

### You

Progress, as trends over the last twelve weeks (see [Progress](#progress-and-the-streak)), with a note that a plateau after quick
early gains is normal. The profile it's headed with (name, which clefs you drill, daily goal, the practice cue) is set in
Settings.

### Settings

Profile (name, minutes a day, the cue, which clefs the note review drills), practice (one or two bars of count-in, and whether the
right note is named after a wrong one in a note drill), look (Studio or Vinyl), keyboard (the device, sending notes to the piano, the piano buttons,
timing), notation (the
music font and the sheet's size), the click (where it sounds, its sound and volume), Claude, and your data. A daily goal much above last
week's average gets a note to go up gradually; Prima never raises it by itself.

Claude: the Anthropic API key on this tablet, shown masked (`sk-ant-…a1B2`), with **Test** (asks the API whether the key works,
which costs nothing) and **Remove**. Without a key, the steps to add one: a file named `claude-key.txt` holding the key, copied
into Prima's folder over USB with `adb push`. Prima takes it in when it opens, or at once with **Look for the key file**, and
deletes the file. The key lives in the tablet's secure storage, never in Prima's data file, its settings or the web view, and
Android's backups leave it out, so a restored tablet needs it pushed again. **This month** says what Claude has cost since the
1st, piece by piece.

**Updates:** new versions come from the repo's GitHub releases (Prima 1.0.N), through the Kit's shared updater. Prima checks at
most once a day; Settings shows the version on the tablet (1.0 for a build from the PC), the newest release with its changes, and
**Install**. Installing downloads it, checks it against GitHub's fingerprint and that it's signed with the same key as the copy on
the tablet (so Android keeps the data), copies the data file to Download/Prima backups, and hands it to Android, which asks to
confirm. The first time, Android asks to let Prima install apps.

Your data: **Back up** shares the data file through the tablet's share sheet (to Drive, or to the PC); **Restore** takes a backup
back, after checking it's one of Prima's; **Start over** clears levels, notes, history and the streak but keeps the settings, after
asking. Nothing leaves the tablet unless you back it up.

## The sight-reading flow

1. **Look.** The passage appears with thirty seconds on a bar at the top (10, 20 or 30 in Settings, or no limit). You can tap
   its rhythm on any key or try a bar; nothing is scored. Start, or the piano's C♯4, begins the count-in whenever you're ready.
2. **Count in.** One bar of clicks at the passage's tempo (two, in Settings), seen as well as heard: a cell for each beat
   lights on its click, and the cursor waits at the first note, pulsing with it. The timed drills (rhythm, scales, arpeggios,
   finger independence, listen and play back) show the same cells.
3. **Play.** The click keeps time and never waits. The cursor moves with the beat: it passes every note and rest printed,
   either hand's, as its time comes, and moves evenly between them, so it keeps going through rests and through bars the hand
   you're playing has nothing in. It reaches a line's closing barline as the line's last bar ends and is at the next line's
   start at once; at a repeat or a loop's end it goes straight back. Each note is marked as it's played: green when it's right; grey when it's another key, with a red head
   on the staff where the key you played is, each named above (red is only ever a key you played); amber when it's more than 10%
   of a beat early or late (a ring on its line shows when you played it, an arrow which way); and outlined grey once its time has
   passed with nothing played. A slip stays: another key played before the right one marks the note wrong, and the right key a
   moment later leaves it so (it isn't held against the next note either). A chord's own key struck twice makes no mark, and a
   trill's, a mordent's or a turn's keys and grace notes aren't slips. These are judged note by note, before your lean is known.
4. **Summary.** After the last bar, [the summary](#the-summary), and the passage again with each note marked the same way, now
   from the whole take lined up and your lean taken out, so a note can change from the mark it had while playing. A wrong note
   names what you played instead (G4→A4) and shows it on the staff. Notes let go early and expression aren't marked on the
   staff, which is busy enough: the summary has them, bar by bar.
5. **Next.** The level and tempo adjust and the next passage comes up.

### How a passage is scored

- A note counts if its pitch is right and it lands within a timing window around its place. The research used ±0.25 s; here the
  window scales with tempo.
- Its pitch is the key it sounds: under an 8va, 15ma or 8vb line, an octave or two from where it's printed (a piece's file gives
  both). A wrong key shows beside the printed note as far from it as the keys are apart, both named as they sound, and a tapped
  note is named by its key and placed where it's printed. Levels read the print, so a passage under an 8va line has the ledger
  lines it's printed with.
- Timing is a share of the beat: under about 5% is in time, 5–10% is audible, over 10% is off. These bands are the research's
  design inference from the perception studies. In-time accuracy, the headline, counts right notes that aren't off, so
  audible ones count too (our choice: the 5% line is where listeners start to notice, too strict a bar for reading at sight).
- Your average early or late lean is measured and taken out before steadiness is graded.
- Measured per passage: in-time accuracy, correct notes a minute, kept going (no gap longer than a bar), whether it was clean
  (every note right), tempo drift, extra notes, missing notes, and the timing error of correct notes. A wrong note one or two keys away from the right one counts as
  "lost on the keyboard", apart from reading errors.
- Strict about slips: another key in a note's window, played before its right key, makes the note wrong, even when the right
  key follows at once; that key isn't counted extra, and its timing isn't graded. A key of a note written at that moment (a
  chord's, struck twice) is only extra, and a key within a tone of an ornamented note (a trill, mordent or turn, or grace notes
  before it) is part of the ornament.
- Held notes: a note is let go early when it sounds (its key down, or the damper pedal holding it until the pedal comes up or
  the key is struck again) short of its written length by more than a fifth of it and more than a quarter of a beat. So a half
  note held a beat and a half is let go early, a quarter needs three quarters of a beat, and notes a quarter of a beat or
  shorter aren't judged: strict, with room for the lift a pianist makes between notes (our choice). Not judged: staccato notes
  (and staccatissimo, spiccato and portato), the last note under a slur when it's a quarter or shorter (lifted to end the slur),
  ornamented notes, and a note still down when a run is stopped before its end. The summary's **Held** is the share of judged
  notes held; holds don't change in-time accuracy, clean, or the level. Wait for me doesn't judge them.
- Expression, judged only where the music marks it, and apart from the notes and timing: it doesn't change in-time accuracy,
  clean, or the level (our choice). The marks are the score's own: a generated passage's, or a piece's own file's (its slurs,
  dynamics, crescendos, pedal and ornaments, read for this, apart from what the Library's grading and excerpt cuts read).
  - **Staccato** held long: its key down more than about half the note (at least 0.12 s, at most 0.45 s). **Tenuto** let go
    short: under nine tenths of it. Under a **slur**, a note let go more than about a tenth of a beat (60 ms at least) before
    the next note breaks the line (staccato under a slur, portato, is detached by design). An **accent**, or a sforzando, struck
    no harder than the notes within a beat of it, by 8 or more (velocity).
  - **Loudness**: each dynamic's stretch at the middle of how hard its keys were struck, as a share of your soft-to-loud range,
    against the share it's played in (*pp* to 25%, *p* 10 to 40%, *mp* 30 to 55%, *mf* 45 to 70%, *f* 60 to 90%, *ff* from 75%,
    ours), and against the one before: marked louder or softer, it's played so by 5 or more. A crescendo or diminuendo: the line
    through its notes rises or falls by 6 or more. A piano set to Fixed touch sends one velocity, so loudness isn't judged.
  - **Pedal**, where it's marked: pressed by half a beat after its mark, down for most of the stretch, and up (or changed) by a
    quarter of a beat after its release; down for over half a beat where none is marked is noted by bar.

### The summary

The same after every run to a tempo (Sight-read, a Library piece). First **a few lines**, to take in at a glance, each with
its bars: the **notes** (how many were right, what the others were, and the bars with the most of them) and the **timing**
(the share of the right notes that were in time, since a wrong note has no timing to be out, and where it rushed, held back
or was off the beat); then a line only for what there is to work on: notes
**held** too short, **loud and soft**, **touch** and **pedal** not as marked, and the bar to **look at**, the one with the
most to fix, where a wrong or missed note counts as three of anything else (a note off the beat or let go early, a marking
not played as asked, the pedal). On a piece, **Loop bars** loops the four bars of that bar's phrase and **Play from bar**
starts the next run at it. **Show details** opens the rest under them, brought into view (and stays open next time if you
leave it so): three panels, side by side:

- **Notes right**: the share right, a bar of right and not, and the ones that weren't as slips put right (hatched), wrong
  (solid) and missed (hollow), with extra keys.
- **In time**: the share of the right notes that were in time, and the lean, and every right note's timing after the lean in a strip from 20% of a beat early
  to 20% late, the in-time band shaded.
- **Held**: the share of judged notes held their length, and how long each was held, with the 80% line.

Each says in a line what stands out (slips or misreadings; where you rushed; which bars you let go early in). Under them, where
the music marks expression, a line says what wasn't as asked: a dynamic played nearer another, no change at the dynamics, a
crescendo that didn't grow, staccato held long, gaps under slurs, accents no stronger, the pedal. Then **Bar by bar**: the bars
as one strip, brighter where there was more to fix, which opens (and stays open next time if you leave it so) into a map of
the bars, both times through a repeat together: wrong and missed notes, how early or late each bar ran, notes let go early,
then loudness against each bar's dynamic, touch (staccato, tenuto, accents, slurs) and the pedal, each only where it's marked.
Tap a bar's number to read it in words; on a piece, **Play from bar** starts the next run there. Each look draws it in its
own two colours, what went right and what fell short,
told apart by colour blindness and by lightness too, and with a fill and a word for each kind.

The details **come in a part at a time**, so the eye is led through them and not met with everything at once. Each part (a
panel, the expression line, the bars) is an empty frame until most of it is in view and the part before has had its turn:
about four seconds from the first to the last when they're all in view. What fell short comes last in each, where the eye is
by then: the notes' bar fills with the right ones as the figure counts up, then the ones that weren't, and what those were
rises under it; the timing rises from the beat outwards, the notes that were off last; the held notes from their full length
down, the ones let go early last. The bars come in from **the one with the most to fix**: the worst three a moment apart,
then the others in a run, then the bars with nothing to fix. The map's marks grow in the same order, whenever it opens, and
the page follows it down as it does. With the tablet set to remove animations, everything is there at once.

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

A passage prints what each level's exam table expects: *f* and *p* and staccato from level 3; *mf*, *mp*, slurs, hairpins and
accents from 4; *pp* from 5; tenuto and pauses from 7; *ff* from 8; pedal from 9; ornaments from 11. Each is judged as
expression where it's printed (above), apart from the level.

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
| Rhythm | One to six bars of rhythm; tap a bar of beat, then the rhythm on any key. No line moves across the music while you tap: keeping the pulse is the point. Afterwards each note shows where its tap landed against the click, green on the beat, amber more than 10% of a beat off with an arrow, a ring wherever the tap was 5% or more off, outlined grey if missed; and each bar is summed up: one that ran early or late as a whole and by how much, the notes in it well off, where you came back on the beat | Early or late lean, steadiness, drift without the click, missed and extra taps | Quarters, eighths, dotted notes, sixteenths, syncopation, triplets; the click fades to a count-in, then silence |
| Keyboard geography | A note or interval; play it without looking down | Wrong notes one or two keys away, and time to play | Within a hand position (C to G from middle C), then just out of it, then leaps of a sixth to a ninth either way, then anywhere an octave either side |
| Listen and play back | Hear a short phrase on the piano, play it back, then see it written | Pitch and rhythm against the phrase | Five notes of a scale, then six to eight, then more keys; last, improvise an answer phrase (not scored) |
| Scales | A named scale with its fingering, octaves and tempo; play it in sixteenths to the click | Evenness of the gaps between notes, a slip at each thumb crossing and octave, evenness of touch, legato overlap, and the hands' sync | The exams' order of keys and octaves; the tempo rises in small steps from a comfortable speed |
| Arpeggios | As Scales | As Scales, with a close look at the crossings | A fifth's range, then one octave hands separate, two, hands together, inversions |
| Finger independence | Five-finger patterns, trills (3–4, 4–5), held notes; patterns from your pieces where possible | Each finger's timing and touch, trill rate and steadiness, whether held keys stay down | Slow and even, then faster, then the weak pairs, then both hands |

The drills read from a staff (Notes, Ledger lines, Intervals and shapes, Chords, Key signatures, Keyboard geography) mark each
answer on it as soon as it's played, as Sight-read's summary does: green right; wrong, the note grey and the key you played
beside it as a red head, each named above (a single note's name as Settings says); outlined grey missed; and a chord's extra
keys as red heads beside it, named. The words under the staff name the same notes: "Off: note 2, E5 for F5", "The shape was
right, from the wrong first note: …", and for a chord "Not quite: F4 for E4" when as many keys were wrong as notes missing, or
"G4 missing; A4, B4 extra". A set's summary lists the single notes and chords you missed. The drills with steps show the step
beside the item count, with − and + to choose it: a new set starts at the step chosen, and from there over 90% in two sets in a
row steps up and under 80% steps back.

Notes come back on a spacing schedule: a note answered right but slowly counts as not yet learned, and comes back sooner than a
fast one. Confusable notes are mixed. After each technique run the music is marked as Sight-read's summary is: green right;
wrong, the note grey and the key you played beside it in red, each named above; outlined grey missed; and a red head beside a
note for an extra key. The keys are lined up with the notes by the fewest slips, so one slip counts once instead of throwing out
every note after it, and the result says how many were wrong, missed or extra. A run with a slip doesn't count for evenness.
Technique results are compared with your own earlier runs, since there are no amateur norms; differences under about 10 ms are
shown as below what anyone can hear. Repetitions are capped per set and per day, with
a reminder to stop if anything hurts: a sensible precaution, not a proven one.

What a technique drill plays is yours to choose, whatever your reading level: the key, stepped easier or harder in the exams'
order (C, A minor, G, F, D …), a minor scale harmonic or melodic, one to four octaves (or an arpeggio's broken triad), and
the hands; finger independence picks its pattern. **Today's** goes back to the day's plan. Each scale or arpeggio keeps its own
tempo and target, by key, octaves past one, and hands. The first time, a run in Wait for me at your own speed sets the
comfortable speed the In time runs start a little under (the pace from one moment of the music to the next, so the two keys
of a moment played hands together count once), and the bar is set to Wait for me for that run; the tempo can also be set
with − and +. Wait for me runs are practice for the notes: they count towards the set's five runs and the day, not towards
evenness or the tempo. A set is five runs. After three, the way on shows under the bar: **Done**, back to the drills (or to
Today, from there); with all five played Run goes off, the way on is the orange key, and **Another set** is beside it. From
Today the day's Technique step is the scale and then the arpeggio: the scale's screen has **Next: the arpeggio** from the
start, and the step is done with the arpeggio's third run, not the scale's.

## Exercises

Where a drill measures a few things closely, the exercises are a catalogue to work through: 66 entries in 14 sections, from the
owner's own list of technique work for an adult beginner and from the research in `exercises.md`. The words are Prima's; the
patterns are the common stock of piano teaching, and Prima writes each one's notes itself, in the key and for the hands you
choose.

| Section | What's in it |
| --- | --- |
| Sitting and hand shape | Bench height and distance, sitting balanced, the hand's own shape, shoulders down, a tension check, filming yourself from the side |
| Warm-ups away from the keys | Arm swings and shoulder rolls, wrist circles and doorknob turns, shaking out, fan and close, finger taps on a table, gentle forearm stretches, an arm drop onto the knee |
| Arm weight and rotation | Dropping onto one note, rocking between thumb and little finger, smaller turns between fingers a third apart, wrist circles on a held note |
| Five-finger patterns | The pattern in every key, major then minor, the hands in mirror, long–short, the pattern then its chord, and Hanon's first pattern played quietly |
| Scales | The Scales drill, a scale in contrary motion, the chromatic scale |
| Broken chords and left-hand patterns | The Arpeggios drill, a chord passed between the hands, broken chords through the inversions, an Alberti bass |
| Chords and cadences | One shape up the scale, up through the inversions, four kinds of chord on one note, I–IV–I–V–I with the hands close, four chords round and round |
| Jumps | An octave with less time to travel on each line, further each time (a third, a fifth, an octave, a tenth), a chord an octave away, the cadence with each chord from its own bottom note, bass note then chord |
| Thumb under | Under three fingers, and under four |
| Trills | The Finger independence drill, and slow trills on fingers 2 and 3, 1 and 2, 1 and 3 |
| Hands doing different things | One hand holds while the other moves, two notes against one, smooth against detached, two against three |
| Touch, loud and soft | Two-note slurs, five fingers detached, one note soft to loud, growing and fading |
| Pedal | The pedal by itself, and changed on each chord |
| Ways to practise anything | Slow enough to be right, hands separately then together, small sections, fixing a mistake at once, the metronome ladder, mixing things up, sleeping on it, recording yourself, the hard bit first, when to stop |

There are four kinds. One **played at the piano** (39 of them) shows its music with the fingering under the play bar every
screen with a tempo shares: In time, Wait for me or Listen, with the same summary afterwards. Its notes, timing and held notes
are judged as a passage's are, and so, where its music marks them, are the touch (slurs and staccato), loud and soft, and the
pedal. One **away from the keys** (sitting, the warm-ups, the pedal by itself) has nothing for Prima to follow: you do it and
press Done, once a day. A **way to practise** is a few lines to read. And the three technique **drills** are listed in the
sections they belong to, their rows opening the drill.

An exercise's page has what you do in a line, how in a few steps, and what to notice. One that's played offers its keys with −
and +, easier to harder in the exams' order (every major and minor key, the major keys, or C alone, as suits it), and the hands
it's written for: right, left, together. It opens as you last played it, at the tempo you left it. What's been done of it is
under its words: how many times and on how many days, when last, and the fastest clean run In time. **In my routine** keeps it
in the list's My routine. The row under that leads to the next in its section, or, when the exercise was opened from My
routine, to the routine's next. In landscape the music and the words sit side by side, so both show at the piano; in portrait
one is under the other.

They come as **two lists**, so the ones you play aren't among the ones you read. **To play**, which the tab opens on, has the
42 played at the piano, the three drills among them; **To read** has the other 24, to read and do: how you sit, the warm-ups,
the pedal by itself, and the ways to practise. Each shows its sections with a line on what they're for, and each exercise as
a row: its name, what you do, when it was last done (Today, a date, or Not tried), and tags for Routine, Drill, Start here
(played, and for the first weeks) and Later (for when the basics are easy). Beside the two are **My routine**, your own list
of both kinds in the catalogue's order, without the sections' headings, where one to read is tagged so; and two picks of the
ones to play, **Start here** and **Not tried**. The search looks in an exercise's name, its section and all its words, so
"jump", "thumb" or "pedal" finds whatever mentions it in the list showing, and a key under it says how many the other of the
two lists has and goes there. The list showing is kept in the address, so coming back from an exercise comes back to it.

What it doesn't do yet: the exercises are fixed patterns, not adapted to how the last run went; they aren't a step of the
daily plan; and the measures `exercises.md` proposes for chord changes and jumps (the first landing, how together a chord's
notes arrive, the time to travel, how far a miss missed) aren't taken. What MIDI can't carry isn't judged at all: the arm's
weight, the forearm's turn, a loose wrist and the fingering are yours to feel, and the exercises that depend on them say so.

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
- **A key is one key, whatever voices sound on it.** With two voices layered (the piano's Dual, say piano and strings), the
  piano sends each key's note twice, the second voice's on the MIDI channel after the first's, and Prima used to take that as
  the key pressed twice: the second press landed on the next notes, as a wrong key or as the next note played already. Prima
  now passes a key on once, when it first goes down and when its last voice lets go, and a pedal's message once. This is from
  Yamaha's MIDI references for the P-145 and P-125 (the P-225's couldn't be opened) and from how it failed; only the piano
  can confirm it, and whether a second voice set an octave away is sent as a different key.
- **Sound from the piano.** Prima sends notes back to the piano over MIDI, so Duet, Hear first and Listen and play back play
  through the piano itself, and so do a piece's Listen and play along. Each note goes out a fifth of a second ahead, its time
  worked out then from the click's beats: the tablet's click knows when its beats really sound only once its audio timestamp
  settles, a moment after it starts, so notes timed at the start came early by the speaker's delay.
- **Latency** is calibrated once (play along to a click; the median offset is taken out). The piano's own notes are played that
  much after the beat, so they land where yours do: the piano's hand with yours in play along, Listen with the way you hear
  the beat. How much timing jitter the keyboard
  and the tablet add is unknown until measured. Prima estimates the USB and MIDI path's share from the piano's Active Sensing
  messages, which arrive at a fixed interval (our idea, untested); differences smaller than that aren't shown.

## How the data is shaped

Everything lives in one SQLite file on the tablet.

- **Profile**: name, clefs drilled, daily goal, practice cue, look, the keyboard's settings and the timing offset.
- **Levels**: the current reading level and tempo, the rhythm level, and per scale and arpeggio the current tempo and target.
- **Note cards**: one per note and clef, with its review box, when it's next due, its recent answers and response times.
- **Sessions**: each day's practice, its steps and minutes.
- **Attempts**: one per passage, rhythm, technique or exercise run, keeping its settings (the level's step, the key it was in and the key it
  avoided), the generator's seed, the notes played with their times and velocities, and the measures worked out from them, so a
  passage can be made again (one spot does) and a result recomputed if the scoring changes.
- **Pieces**: the collection's, a local collection's and imported pieces (kept apart, so a newer bundle never takes out the
  others), with their sources and licences, levels, and the MEI Verovio made from each notation piece (bars numbered in order, a
  pickup 0, so the numbers printed are the ones Prima says). A PDF keeps its file, its systems found on each page with their
  bars, its metre and tempo, and where you were, and once its notes are read and kept, their MEI as its notation. Imported
  files, and a local collection's PDFs and MIDI, sit beside the data file.
- **Readings**: a PDF's reading by Claude, waiting to be reviewed or kept: each system's step and note, its reading as Claude
  gave it, the doubts by bar (in the piece and in its system), the MEI, the pages it covered, what it cost and how long it took
  (Read again's included), and when it was kept.
- **Excerpts**: each piece's 2-, 4- and 8-bar stretches with their levels, how often each was read, and when last.
- **Exercises**: each one's runs are attempts under its id, with the key and hands (a Done away from the keys is one too); the
  key, hands and tempo each was last played in; and the ones kept in My routine.
- **Streak**: the days practised and the freezes used.
- **Calls to Claude**: each one's time, the piece it was for, the task and model, how it went, what was asked and answered (no
  images, and never the key), and the tokens and cost, so Settings can show the month's cost.

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
  looks, with the look around it and the cursor and marks drawn on top. Bars are as wide as their notes need, as in print, and
  each line is spread to the paper's width. A whole piece's lines break where its bars fit, so a busy bar is never squeezed to
  share a line, and a short last line keeps its bars' own widths; a short passage's bars are shared evenly between its lines.
  In the music Prima writes itself (passages, drills, exercises, a piece from a MIDI file, notes read from a PDF), notes
  shorter than a quarter are joined under a beam within the beat, a dotted eighth with its sixteenth as well as plain eighths
  and sixteenths; a bundled piece keeps its own engraving's beams. The brace joining the staves stands as far in from the
  paper's left edge as the last bar line does from its right. A sheet that prints fingering (the technique drills, the
  exercises) keeps a deeper top margin, and a mark's words go above the fingering, so the names of a wrong key and the note it
  was played for sit over their own note without covering the finger numbers.
- **The look** is the [Studio & Vinyl design system](https://claude.ai/artifact/XtgAHfVyw3L94e5DKrj2ii): its own two looks, not
  the Kit's Logbook and Telemetry, switched in Settings. The fonts (Space Grotesk, Space Mono, Bebas Neue, Work Sans) ship with
  the app as woff2 files, so it works offline; Verovio brings its own music fonts.

## Open questions

Only the devices can settle:

- How much timing jitter the keyboard and Android's MIDI path add.
- Where the click sounds (the tablet's speaker, or the piano as a USB audio device) and its delay.
- Whether the piano plays the notes Prima sends it, and whether it echoes them back.

Being researched: the Library's public-domain sources, importing PDFs, and page turns (`library.md`).

Partly built from its research: the Exercises tab has a first set of technique exercises as fixed patterns; the landing
measures and the adapting steps that `exercises.md` proposes for chord changes and jumps aren't built.

Open in the research: how best to step difficulty up, whether read-ahead and rhythm-only drills carry over to full reading in
adults, whether spaced note review helps staff reading itself, and whether streaks help adult music learners keep going.
