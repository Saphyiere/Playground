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

Prima has two looks, chosen in Settings: **Porcelain** and its dark version **Night**: white cards floating on soft shadows
over a light grey (in Night, near-black layers told apart by hairlines, the paper a shade softer), names and figures in a serif
and what they measure in its italic, black for what's chosen and for a list's keys (in Night, the chosen tinted), and one
accent for the key to press, where you are and what's lit: red, or persimmon, amber, green, blue or violet, the same in both. A
wrong note is red in every accent. The music is black on paper in both. Studio and Vinyl, the first two looks, are gone (the
owner, 28 November 2026: "We're only doing porcelain and it's dark mode"); a tablet still set to either opens in Porcelain.

The rail down the left edge: at its head You, a round key in the look's colour with the first letter of your name
(a figure without one), with the keyboard's lamp on its edge, lit when it's plugged in and hollow when it isn't; under Practice,
Today, Sight-read, Exercises (the drills among them) and Routines; under Collection, the Library and the Repertoire; and Settings alone at
the foot. The rail is a card floating at the screen's edge, each section with its icon, the current one black (in Night a
lighter layer). In portrait the rail runs along the bottom, You's
round key at its start. The tools are a tab of Settings (Tools, below), whose key is lit on them.

Every screen has the same **head**, one row as the wireframes drew it: on its left the way back, a key like the head's others
named for where it goes (the screen it was opened from; none on the rail's own screens), then the screen's name with a printed
line after it (the day, what it's for); on its right the screen's own keys (a search, Import, New routine), then **About**,
**full screen**'s key and the piano's lamp where it has them, always in that order. The owner, 28 November 2026: "the head should be normal in every single location in
the app". A screen that plays music goes on from it the same way too ([Playing to a tempo](#playing-to-a-tempo)).

Every screen is drawn inside a guard: a screen that fails, in drawing itself or in what a tap does, gives way to a card saying
**This screen stopped**, with what went wrong, **Try again**, and the way to Settings › Problems, where it's kept. The rail
still goes anywhere, and another screen starts afresh. Before, a failure left the whole page not answering taps under Blazor's
yellow bar, until Reload.

How Prima opens, how screens arrive and how a wait looks (all in prima.css's Motion; the mock-ups they were chosen from are the
page "Prima in Motion", https://claude.ai/artifact/HDkvVecovjYqnYaj3tLjJi):

- **Opening.** Android's splash is in the look last used (from the start after it's chosen, on Android 13 and later): a card
  with a staff and three notes on the look's ground. Prima's page then shows the same mark in the same place while .NET starts
  and the settings are read, alive (its notes lighting in the accent one after another), and it fades as the first screen is
  drawn: the rail slides in from its edge and the screen rises in.
  On Today, once: the tempo knob sweeps round and settles at the tempo. The settings
  are read once while Prima is open, so no later screen waits on them.
- **A screen arrives** rising a few pixels as it fades in (0.18 s and 6 px); its rows and cards follow one after another, the first eight only. A list drawn again for another choice (a composer,
  a work, a tab, another list of exercises) arrives again whole; a screen that redraws in place (a filter, a search) doesn't.
  The rail stays still.
- **A wait takes the shape of what's coming**: where a screen is still reading (the Library and a composer's works, Routines, a
  routine, Repertoire's cards), its own rows and cards stand faint in their places with a light passing down them, as a long
  piece's paper waits, and the real ones take their places. They show only after 0.15 s, so a quick screen never shows them;
  the Library no longer says it has no collection while it's reading it.
- **A small wait** keeps its words and loses the "…": a working mark on the key or the line (three segments taking
  turns): the key's Test, Check, Install as it downloads (with a meter), Back
  up now, Export everything, Ask Claude's Generate, a request being asked on Routines, the PDF's boundaries key as it opens.
  A meter counting something lights each segment as it's reached, the last one lit breathing while the next comes.
- **Claude at work.** A routine being made shows notes written onto a staff one after another, and how long it's been asked; a
  PDF being read marks the batch Claude is reading now, lights each batch saved and fills a meter by the count.
- **The play bar and the music.** Pop-ups come out of their key, lamps warm up, the keys a run turns off dim, what comes into the
  bar as a run starts or ends fades in where it goes (the count-in comes at once), More, Change and About fade in over the
  music, and full screen slides the bar and the music to their places as the rail, the head and the foot fade. On a piece's or an exercise's stage the
  music fades onto its paper as it's drawn; never while a run plays, and never on the drills that time how soon you answer.
- **Remove animations** (Android's setting) keeps the fades, quicker, and stills the rest: nothing travels, turns or sweeps.

### Today

What to do now. The day's plan as five steps (below), with the next one ready to start; a shorter five-minute plan for busy
days that still counts; this week's minutes (from Monday, as You's weeks are; a piece's are its practice sessions' playing
time and its plays through) and its days practiced; the streak; and the
practice cue you set once ("When I sit down after
dinner, I'll open the app"). The home mockups show it: the tempo knob or the record, the next passage on a small sheet, and the
steps along the bottom. The day's passages come in a fixed order, so the one shown as up next is the one Sight-read gives. When
every step is done, a line sums up the day. The pieces played today are in it, each with its runs and how the last went, and
show as a line of their own before the plan is done. After the plan, **Your pieces** lists the three most recently played repertoire pieces. Each links to the score and shows the last recorded practice.

The first time, a welcome asks for a name (optional), the minutes a day, the cue and the look, then goes to the timing
calibration or straight to Today. From the welcome the timing leads on: once its offset is kept, **Done** goes to Today. With
the timing set already, as after Start over, the welcome skips that step: its one key goes straight to Today. Nothing else
comes between these steps and Today: no tour or guide.

### Sight-read

The reading screen, and the way to read more than the plan asks for. A passage at your level: thirty seconds to look it over
(you can tap its rhythm or try a bar; Settings makes the look 10, 20 or 30 seconds, or as long as you like, and Start begins
the count-in sooner), one bar of count-in, then play it in time with the click, each note marked as you play it. A look that
runs out with no piano plugged in starts nothing: the bar says to plug in the keyboard, and the passage waits with its Start
for when it is. After the last bar, the summary. Then the next passage, with the level and tempo already adjusted.

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
move the level or count as one of the day's passages. A passage stopped early is kept, but doesn't move the level either.

The first screen is the same from the first time on: a new Prima reads at level 1 until the level is set, found or moved by
the passages read. It has the level, with − and + and a line on what it reads (keys, metres, hands, tempo); ▶ for a new
passage; and beside it **Find my level**, two short passages from levels 1, 3, 5 and up until they fall under 80% in time,
then the level below. A level set by hand starts at the bottom of its tempo range, as Find my level sets it, and moves on from there as usual. The
tempo's − and + set it for this passage and the next ones at the level.

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

Every screen that plays music to a tempo is the one **practice screen**, top to bottom: the **head** every screen has (the
way back, a key named for where it goes, the screen's name, and on its right **About**, the screen's words in the music's
place,
**full screen**'s key and the piano's lamp); the **music**, taking the rest of the screen; a run's **review** under it; the
**play bar**, near the hands; and the **foot**. The owner, 28 November 2026: "we have practices and exercises, and the issue is
that they do the exact same thing", and of the wireframes' C, the bar under the music, "the one practice screen for all of
it". The bar's keys are in the same places everywhere: first a screen's own (a piece's page turns), then **More**, then what
every run needs, the **hands** (**Left**, **Right** or **Both**, one tap for the one wanted, where there's a choice), the
mode, the **tempo**, **Loop**, **Listen** and **Play** (the piano's C♯4) at the row's end. **More** opens one panel upward
from the bar, over the music, with what a run doesn't need every time, in groups: **The click** (**The click alone**, a click
at the tempo with nothing judged, below; where it's heard, its sound, how loud), **The music** (a piece's view, lyrics and print,
**What's this**, and the cursor and the marks while you play), then the screen's own (a piece's feedback after a run and its
play along). Its key's lamp is lit while the click goes by itself; **Done** closes it, and so does a run starting. The
**foot** has on its left what this one is (an exercise's key and variation on its card, with **Change**; a piece's bars; a
drill's target and its runs; Sight-read's level) and on its right the way on (the next exercise in its section, a session's
clocks and Finish practice, a routine's time, Skip and Next, Another set, the next passage). Sight-read, a Library piece and
its PDF, an exercise, Hanon, a routine's exercise, Scales, Arpeggios and Finger independence all play this way: it's the one
engine and the one screen for music played to a tempo, so what it can do (the modes, the click, full screen, Listen, Loop)
is the same wherever it is, and a screen only says which of its keys it has. The reading drills, Rhythm, Listen and play back
and Endless notes run their own sets on the same screen: their music, how the last answer or try went under it, their bar at
the foot (their figures, More where they have the click's settings, then what to play now, Loop and Start at the end), and
the foot under it (a reading drill's step on its card with Change, a routine's keys, Endless notes' Start); their music is
printed at the size every sheet has. **Full screen**
leaves the music, the review and the bar alone: the rail, the head and the foot go, and the tablet's own bars; its key, on the
bar while the head is gone, or leaving the screen, brings them back. A piece's Practice goes full screen by itself.

- **In time**: a bar of count-in (two, in Settings), the click that never waits, the cursor moving with the beat, each note
  marked as it's played, and a summary after (see [The summary](#the-summary) and
  [How a passage is scored](#how-a-passage-is-scored)). In a Library piece the click follows the music: each bar's beats are
  its metre's, the first clicked higher (a change from 6/8 to 2/4 goes from dotted quarters to quarters, the eighth kept), and
  an upbeat (a short bar opening the music, or making a whole bar with the one before it, as after a repeat sign) is counted
  back from the next downbeat, none of it higher. Where the music marks a tempo (a metronome mark, or a word like "a tempo"
  or "Più lento" with the tempo the file plays it at), the click goes at it in proportion to the tempo you set, which is the
  one the piece mostly goes at: at 72 where it mostly goes at ♩ = 120, a bar marked ♩ = 60 goes at 36, and a written-out
  ritardando slows a beat at a time. A bar played again has its own tempo again, and the count-in has the first bar's metre
  and tempo. The cursor keeps to the same beats, and a note's timing is judged as a share of the beat it was played in. The
  click stops where the music does: its last click is the beat the last note ends in, and the run waits one more beat, silent,
  so a late last note still counts, before the summary.
- **Wait for me**: no click and no count-in. The cursor waits at each note or chord until its keys are played. Notes written
  together, a chord in one hand or both hands at once, go down **together**: in any order, the first key to the last within
  0.15 of a second. Played a key at a time, which finds the chord but doesn't play it, its notes turn amber, "together" shows
  over them, and the cursor waits until they're struck together; they stay amber, so it shows where that was. A key of the
  chord on its own before the chord is played together is no fault. A wrong key turns the note grey, named in grey, with a
  red head where the key is, named in red, and the cursor waits on; the note stays so once it's put right, so the slip shows
  where it was. A trill's, a mordent's or a turn's keys, and grace notes leading into a note, aren't slips. A chord the music
  marks to be rolled (an arpeggio sign, on one hand's chord or across both) is played as a roll: its keys one after
  another, none more than 0.3 s after the one before. With a longer wait between two of them it's marked like a chord played
  a key at a time, says "in one roll", and waits to be rolled in one go. In time takes it the same way: its keys count as
  struck when the roll began, so the roll is timed by its first key, and its later keys aren't late or missed. Afterwards: the
  notes right first time
  (not a note with a slip, nor a chord played a key at a time first), the wrong keys, the chords not together, and notes a
  minute at your own pace.
- The mode is kept for each screen. The tempo shows as its figure; tapped, it opens its − and + over it, which move by a
  metronome's marks (… 60, 63, 66, 69, 72, 76 …, carried on to 20 and 240) so a step is about the same share of the tempo, slow
  or fast, a field to type any tempo from 20 to 240, and once it's been changed **Original**, the music's own tempo to go back
  to. Stop ends a run where it is, and its summary covers the music up to the last key you played: what was
  due between that key and Stop, while the hands were on their way to the screen, isn't counted as missed. A run stopped
  before any key leaves no summary and isn't kept. A run is played on the piano: with none plugged in, Play and Listen look
  off, and a tap says so over the bar.
- **While playing** (Settings › Practice, and More on the play bar) says what shows on the music In time: the **Cursor**, and the
  **Marks as you play**, each on or off, for playing with only the click and the page. With the cursor off the music still
  scrolls with the run. With the marks off every note is still judged, and the marks show when the run ends, to go back
  over. Wait for me always shows where it's waiting, and "together" over a chord that needs it.
- **Loop**, on every play bar after the tempo: lit, a run doesn't end.
  The music goes round and round until Stop, In time without a break and with no count-in after the first, and it's printed
  with repeat signs round the bars that go round. An exercise goes round without its close (the long note or chord it ends
  on), so the pattern runs straight on into itself; a scale, an arpeggio or a routine's exercise goes round whole. Each time
  round is a **pass**, counted in the bar and scored by itself as a piece's loop is, and a stopped run is its last pass
  played to its end, with every pass in its summary. A loop is as many times round as fill half an hour at the tempo, which
  is as good as until stopped. Listen still plays the music once, as written. Loop is kept for each screen, like the mode. A
  piece's Loop goes round the bars you choose (see [Library](#library)).

### Exercises

The drills and the exercises as one catalogue, laid out as the Library is: its lists down the left, and what's chosen beside
them as cards, each with a picture of how its notes move. The **drills** are a type of their own, first in the list: short
and focused, a few minutes each, new every time, moving you on a step as you get them right. Reading drills: Notes, Endless
notes, Ledger lines, Intervals and shapes, Chords, Key signatures, Rhythm, Keyboard geography, and Listen and play back.
Technique drills: Scales, Arpeggios, and Finger independence. Each is described under [Drills](#drills) below. The rest is
technique to work through, at the piano and away from it, from how you sit to the pedal: each exercise with what you do,
how, and what to notice, on a page of its own, with its music if it's played. Ten of them are **families**: a main exercise
that a few choices vary. Described under [Exercises](#exercises-1) below.

### Library

A collection of real pieces bundled with the app, and music you import. `library.md` settles the sources, the formats and how
a PDF plays out; this is what came of it.

- **The collection**: about 240 public-domain and openly licensed pieces, chosen by named works published before 1931: 133
  MuseScore users' engravings through PDMX, as MusicXML, and 104 PDFs with their MIDI from the Mutopia Project. Each is credited
  as its licence asks (**Where the music comes from**). The collection comes prepared, so its first opening only stores it: the Library tool
  engraves, grades and cuts each MusicXML piece when the bundle is built, and a collection engraved as one score (Chopin's
  Preludes, Tchaikovsky's album) is split at its final barlines into its pieces ("Children's Album, No. 5"), or where each of
  its pieces starts with its own key or metre and a metronome mark (Kinderszenen, whose pieces end at repeats as often as at
  final barlines, each by its name: "Träumerei, No. 7"), each starting with the key, metre and clefs in force there, so no piece
  is the whole collection to open or engrave. Tempo changes a score keeps only for playback (bare numbers, which MuseScore hides)
  are left out, from the bundle and from pieces stored before, since they'd print on the music. An import is read and graded on
  the tablet, away from the screen's thread.
- **A local collection**: more music for this tablet alone, never in the repo: music whose terms allow personal,
  non-commercial use but not a place in a public repo, and more public-domain music than the repo should carry. The Library
  tool builds it on the PC (`-- local`): Craig Sapp's Humdrum editions (Mozart's, Haydn's and Scarlatti's sonatas, Joplin's
  rags, Bach's 370 chorales on a grand staff), the DCML's research corpora as MuseScore files (Beethoven's sonatas, Bach's
  suites, Chopin's mazurkas, all of Grieg's Lyric Pieces, Kinderszenen, The Seasons, Debussy, Bartók's Bagatelles, and more)
  and ASAP's concert scores (the Well-Tempered Clavier, Chopin's études and ballades, Liszt, Schubert), all CC BY-NC-SA 4.0;
  more of PDMX's public-domain piano music, each score's own composer one who died before 1931, and no transcriptions; the rest
  of the Mutopia Project's keyboard music as PDFs with their MIDI, each PDF at the level its MIDI reads at, those under CC BY-SA
  share-alike (a tier of their own, as in the bundle); and the Open Hymnal's public-domain hymns, four voices on the grand staff
  with their verses under the notes, shelved with the chorales. About 2,860 pieces, all keyboard music, prepared like the bundle
  and packed in a few zips, none much over 40 MB, since the tablet reads a zip it imports whole. The DCML types many of its
  dynamics into the lyrics line, a sign under a note (82 pieces: most of Beethoven's sonatas, a few of Chopin's mazurkas): its
  letters, "<" or ">" where a crescendo or diminuendo starts, "(" or ")" on each note under one, "[" or "]" where it ends. They
  become the marks they are, below their staff: a dynamic, a hairpin where its notes or end are marked, and where they aren't,
  the word "cresc." or "dim." (there's no end to draw a hairpin to); a sign the score marks already at that place is left out,
  and so is one that can't be placed (a pedal's "*"). A piece whose syllables include any of a song's words keeps them all as
  they are. Prima does the same as it reads a piece stored before the tool did, so those show their dynamics without being
  imported again. **Import files** with a zip
  stores its pieces in the background with the same meter, credited like the collection's; those already here take its titles
  and credits. Each zip names the whole collection it's part of, so importing any one of a collection built again takes out the
  collection's pieces it no longer has, with their excerpts and files: their runs stay, as a piece removed by hand's do. Bundled
  pieces and your own imports are never touched. The Library then holds about 3,160 pieces, so a list is drawn again a second at
  a time at most while pieces are stored, and the search follows once typing pauses.
- **Finding your way**: the Library is a catalogue. Its lists stay down the left, each row with how many pieces it holds:
  **Uploaded pieces** at their head once anything is uploaded, **Near your level**, then every composer by surname, surname
  first ("Beethoven, Ludwig van"; a surname of two words whole, so Ralph Vaughan Williams is under V and Mendelssohn Bartholdy
  under M, a name written surname first by what's before its comma, and "Jr." or "II" after a name no part of it), with **Find
  a composer** over them (accents aside: "Dvorak" finds Dvořák). A switch over the composers turns the list to the **Kinds**
  (sonatas, études, preludes and fugues, dances, character pieces, chorales and hymns, variations, by words in the title and
  work; a piece that's none of them is a character piece, and so is a fantasy, a capriccio or a toccata unless its title says
  more, as a fantasy and fugue or a polonaise-fantasy does; a cake-walk is a dance, and Debussy's Doctor Gradus ad Parnassum
  isn't an étude), the **Levels** (where most of a piece reads), or **RCM**'s levels (RCM's, below; upright the switch takes
  a second line). What's chosen in the list is beside it, the row turned
  over, and each column scrolls by itself. With nothing chosen the Library opens on the uploaded pieces; with none uploaded, on
  the pieces near your level, and else on its first composer.
  - A **composer** has its kinds as tabs, the fullest first, each with how many pieces it holds; past five kinds the smallest
    share one tab, Other. Under them its **works**, a row each: its number among its kind where its name has one (14 for
    Sonata No. 14), its name without its catalogue number, then that number, how many movements, the other versions of it, and
    the levels its movements are at. The work chosen (the first, until one is) is beside them: its movements in order, each
    with ▶, its bars, its level and its star, then **Other versions of it**, each by its own title with whose it is (the
    Mutopia Project's edition as a PDF, a MuseScore user's score). Upright, a work opens where it stands in the list, and
    closes again. A kind whose pieces each stand alone is a list of them, each under its own name with its catalogue number
    and the set its source has it in.
  - **Works** are read from the titles and from the work each source names, since the Library keeps a piece a movement and
    its sources title them differently: a sonata's movements were three rows among a hundred, and the same sonata stood in
    three places. A title that names a work and a part of it is that work's part ("Sonata No. 12, Op. 26: III. …", "Sonata
    No. 8 “Pathétique” (1st Movement: Grave)", and, where two or more titles share what stands before it, "English Suite No. 1,
    BWV 806: Allemande", "24 Préludes Opus 28, No. 1", "Die Kunst der Fuge, Contrapunctus I, BWV 1080"). Otherwise a piece
    belongs to the set its source names (Mazurkas, The Well-Tempered Clavier, Book I), unless that's only a catalogue number;
    and stands by itself where it names none, or is the only piece of its set here. The editions that title every movement
    the same way (the DCML's, the Humdrum scores, ASAP) give a work its movements; another edition's PDFs, or an arrangement,
    of the same work are its other versions: told by the same catalogue number (the work's own, or one of a set's pieces'),
    by the same number of the same kind ("Sonata No. 14", "Ballade No. 1" among the Ballades) where their catalogue numbers
    don't say otherwise, by the same name, or by the name the work goes by ("Moonlight") when only one work of its kind goes
    by it. Movements are in their order; a suite's dances as they're played (the prelude, allemande, courante, sarabande, the
    lighter dances, the gigue); a set's pieces by opus and number, a prelude before its fugue. One edition leaves the
    Moonlight's number out of its titles: it's taken from the piece's id. Beethoven's 187 pieces are 64 works this way, and
    Bach's 806 are 105.
  - A **kind** is an index of every composer's works of it, under their names, each going to its composer opened at that
    work. A **level**, and the pieces **near your level** (the ones to play along with whose most reads from your
    sight-reading level to two above it, since a piece to learn sits above what you read at sight; with none there, the
    easiest there are), are lists of pieces under their composers by surname, which can be cut to the pieces to play along
    with or the PDFs; a long list starts with a key for each composer, or, past twelve, for each letter their surnames start
    with, and the pieces without a composer are a group of their own there, not counted as a composer. PDFs aren't near your
    level: a PDF's level is only about where its collection sits, or what its MIDI reads at. Everything, the PDFs and a
    period's pieces (Baroque, Classical, Romantic, 1890–1930, Ragtime, Teaching pieces and Other, by composer, a few by their
    own names where a namesake's would mislead) are such lists too, by their addresses (`library?by=all`, `by=pdf`,
    `by=period&of=Baroque`); the catalogue has no rows for them.
  - The **search** is in the Library's head and looks through everything, whatever is shown: each word typed by itself and
    in any order (accents aside; a number is a whole number or a movement's, so "moonlight first movement", "moonlight 1st"
    or "moonlight 1" find the Moonlight's I. and the arrangements of its first movement, "movement" itself left out), in a
    piece's title, its composer, the work its source names, and its work as the catalogue names it, so "beethoven sonata 14"
    finds the Moonlight's movements, whose own titles leave the number out. What's found is listed under its composers, a
    work's pieces together, each with its work under its title; the composers whose names have the words head the list, as
    ways to all of theirs. Past 150 pieces it shows the first 150 and says another word narrows them.
  - A title is shown whole, on as many lines as it takes (an upload's file name can run long), and in a piece's own bar
    upright. The Library remembers where it was left: what's chosen, a composer's tab and work, a list's filter and the search
    are in its address (`library?by=composer&of=…&kind=Sonatas&work=…`, `library?by=level&of=3&only=pdf`, `library?q=sonata`),
    taking its place as they change, so the way back doesn't step through each tab or work, and coming back to it from a
    piece, by the way back or the rail's Library key, finds them as they were, each column scrolled to where it was (a
    composer's works stay where they are while another work is chosen). A column not seen before starts where what's chosen
    in it shows (a composer far down the list, a work arrived at from a kind's index), or at its top; a row taller than its
    column, as a work open upright with all its movements is, starts at the column's top, its name showing. From a
    piece or another screen the rail's Library key goes back to what was shown last, and from the Library itself to what it
    opens on.
- **Uploaded pieces** are a list in the same frame: each with its title whole and its artist under it (and who arranged it),
  when it was last played and how that went, what it has (**Pages**, a PDF; **Notes**, to play along with), its level and its
  star; the latest first, or **A–Z**, or **By artist**; and **Import files** on its heading, as in the Library's head. A
  piece without an artist says **Add the artist** where the artist goes. The key at a row's end opens its **details**: its
  title (with the file's name it came in as), its artist or composer and who arranged it, to change, since a file's name is
  only where they start; what it has, with a way to add or replace its pages (a PDF) or its notes (a MIDI, MusicXML, MEI or
  ABC file, graded and cut like an import; Convert PDF on the piece's own screen reads them from its pages instead); and
  **Remove from the Library…**, with Undo as below. A piece given an artist is among the composers too, with its works read
  like any composer's.
- **Stars**: every piece's row ends with a star. The first tap keeps the piece in the repertoire (Repertoire, below); a tap
  on a star that's lit opens, where it was tapped, where the piece is kept: the repertoire itself, each collection, and **New
  collection…**, which makes one with the piece in it. Taking it out of the repertoire takes it out of every collection.
- **Hearing a piece first**: ▶ beside each piece plays its opening, about half a minute from its first note (a pickup's
  included; rests it starts with, and a drum part's bars before the piano comes in, aren't played through), at the tempo it
  starts at (else the one it goes at most, else 80 a minute): on the piano, on Prima's own channel as Listen plays, when one is
  plugged in and takes notes; otherwise on the tablet's speaker, through Android's own MIDI player. A PDF plays the MIDI it
  came with, without its drum channel, from its first note; one without MIDI or read notes has nothing to play. One opening at
  a time: ■ stops it, another's ▶ starts that one instead (the last tapped, even while the one before is still being read), and
  it stops by itself at the opening's end or when the Library is left. A line over the foot of the Library says what's
  playing and where, while it plays. What was just done is said there too (a piece starred or taken out, details saved, what
  an import brought in): the line takes no taps and moves nothing, since in the columns' way it pushed every row down under
  the finger, and it goes by itself once there's been time to read it, a line about something still going on staying. It
  names a movement as the catalogue does ("Sonata No. 14 “Moonlight”: I. Adagio sostenuto"), not by its stored title.
- **Levels**: each MusicXML piece is cut into 2-, 4- and 8-bar excerpts at phrase starts (never into a tie or a slur, and never
  the same bars twice) and each is graded by the ladder's own features: key, metre, rhythm figures, hands, chords in a hand,
  accidentals, range, leaps in the tune, ledger lines, clef changes, pedal, 8va and ornaments. The hardest sets the level, as
  Henle grades. A piece shows its hardest excerpt's level and where most of it reads. A PDF can't be graded from its notes until
  they're read (below), so it shows about where its collection sits.
- **RCM's levels**: where The Royal Conservatory's Piano Syllabus, 2022 Edition, lists a piece of the collection, as 121 of its
  295 are (94 different pieces, some in more than one edition): its level, 1 to 10, and its list (A to D, by period), or that
  it's one of the level's études, with the page. It's RCM's own statement, a fact looked up for each piece in the syllabus's
  free PDF (`library/rcm-2022.json`, beside the manifest, brought up to date with each bundle as its credits are), not a copy of
  its lists, and Prima isn't affiliated with RCM. A board's level rates a piece learned and played, as teachers place it, where
  Prima's own is where its music reads at sight, so the two are shown side by side and neither is worked out from the other
  (RCM Level 3's pieces read at Prima's levels 7 to 11). About this piece says it ("RCM's 2022 piano syllabus lists it at Level
  3, List A (page 33)."); the line under a piece in the Library's lists ends "RCM 3"; and the list's switch has **RCM**: RCM
  Level 1 to 10, each with how many it holds, and each a list of its pieces under their composers (`library?by=rcm&of=3`).
  Where the music comes from says where the levels come from. The local collection's pieces and imports have none yet.
- **A piece** opens on its front: its music, About this piece under it, and on its bar its level, **Practice**, **Play through** and **Practice history** ([practice.md](practice.md) has the design). At its head, the head every screen has, is **the way back**, a key named for
  where it goes: the list the piece was opened from ("Ludwig van Beethoven", "Uploaded pieces"), or the Library when it was
  opened from anywhere else. Only a tap presses Practice and Play, never the piano's buttons,
  so starting is always deliberate; the music scrolls, and a tap on it does nothing there. (A PDF whose notes haven't been
  read opens on its pages, as below.)
- **Opening a piece never holds the page.** A long piece takes seconds to engrave on the tablet (Waldstein's last movement,
  543 bars; Liszt's Sonata, 763), and the page used to freeze for all of them, on an empty screen with no way back. Now its
  head, its bar (its level and its bars, Practice and Play there but off) and About are there at once, with the paper
  waiting: faint staves and a light sweeping across them. A card over the foot of the paper says what's happening
  ("Engraving 543 bars"), with **Cancel**, which goes back; the bar says "Getting its music ready". Its first lines show as
  soon as they're drawn, to read and scroll, and Practice and Play come on: the rest follows a few lines at a time, the bar
  and the card saying how far it has got ("Line 6 of 38") on a meter, with **Stop**, which leaves what's drawn ("Stopped at
  line 6 of 38", and **Draw the rest** beside it). The way back, the tablet's own back, Cancel and Stop are taken at once at
  any moment. A piece that opens at once shows none of it: the paper and the card fade in only after a moment. A run started
  on the first lines is given the rest of its way when they're all there. A piece left in Practice opens in Practice again,
  full screen, and is laid out once, at that width (as it's laid out once in the view it was left in, not as pages first
  and then the scroll).
  How: a whole piece is engraved by a Verovio of its own in a worker (`engraver.js`), never on the page's thread, since
  laying a piece out is one step that can't be broken up (3 s for Waldstein on a stand-in for the tablet). A scroll is drawn
  in parts, each a page two or three lines high, put one under the other with the space Verovio would have left between their
  lines: its lines break the same whatever the page's height, so the parts make the one tall drawing the sheet used to ask
  for, to within a pixel over the whole of Waldstein (and all of a piece longer than that one drawing could hold, which
  used to stop part-way: Liszt's Sonata). A song is still shown whole, once its words fit: its fits are tried out of sight.
  Whatever ends a drawing (the piece left, the tablet turned) lets go of the engraver at once, ending the worker in the middle
  of a layout and starting another for the next piece. Should the worker not start on a device, the same steps are taken on
  the page's thread, which then can't answer while a long piece is laid out, and Settings › Problems says so. A passage (a
  drill's, a sight-reading's) is still engraved on the page's thread as it's asked for: it's a few bars, and answers are
  timed from when it's painted.
- **In Practice** a piece is the practice screen every screen with music is ([Playing to a tempo](#playing-to-a-tempo)): its
  head (the way back, the composer and the title, **About**, full screen's key, the piano's lamp), the music, a run's review
  under it, the play bar, and the foot, with **the strip** of its bars on its left and the session's clocks and **Finish
  practice** on its right. **About** shows About this piece in place of the music, until it's pressed again (without Remove
  from the Library, which is the front's). **More**, first on the bar, has the rest: the click (below); the view, a song's
  lyrics, the print of a PDF whose notes are read, **What's this** (below), the cursor and the marks; how much Practice tells
  you after a run and whether a loop waits after each pass; and play along. **Full screen** leaves only the music, the review
  and the bar, the tablet's own bars hidden too, in either orientation; the key, on the bar then, or leaving the piece, brings
  everything back. A piece fits the screen, only its music scrolling, and after a run (practicing the Standard way) the run's
  summary is under it, over the bar; while a run plays the music has that space too. About this piece
  shows the last recorded run, its passage, hand and tempo. The piece details include its level and the
  collection's credit and licence, or for an uploaded piece when it was imported and the file it was, as that file was named
  ("Imported 17 November from Numb.pdf"; a PDF is kept under a name of Prima's own, and one imported before its name was kept
  says only when). The profile totals include runs that reached at least eight notes across all passes. Practice history also includes shorter runs. A piece opens where it was left: the line that was at the top of the screen there again, and the
  bar it was to start from, or its loop. A notation piece shows whole in
  one scroll unless it's been set to Pages (kept for each piece; a run plays in the scroll all the same, a line at a time, and
  the piece opens in Pages again the next time). As pages: two systems at a time in landscape, the whole page
  in portrait under a slim bar. They turn by a swipe, a page turner's or a keyboard's arrow keys, the soft and sostenuto pedals
  (forward and back), or the keyboard's top and bottom keys (C8 and A0); never the sustain pedal, which the music needs. A tap
  on the music never turns: it's for the bars.
- **Playing a notation piece** is on the play bar every screen with a tempo shares ([Playing to a tempo](#playing-to-a-tempo)):
  In time or Wait for me, the tempo, and Play, from the start or from a bar you tap, anywhere in it, or on a mark over it (an
  8va's line, a slur, a hairpin or a pedal line that runs on over several bars chooses the bar under the finger, not the one it
  starts in): it's lit and outlined from its bar line to the next (the first bar too, with its number; a scroll is never a
  tap), taking in an octave line over or under it so its dashes show, with a tab over it saying where the run starts,
  **Loop**, and **Clear**. Every part of the sheet measures a bar that one way (`bar-box.js`). Clear
  removes the selected start or loop and its highlight, returning the next run to the beginning without changing the visible
  page or scroll position. On the bar its keys, switches and figures are one height: one row in landscape where they fit (a long Play's words take two
  lines first; where the row is shorter still, Play takes the next row, at its end),
  and in portrait More and the hands, then the run's controls with Listen and Play at the row's end. More's **The click
  alone** starts **the metronome**: a click at the tempo, each bar's first beat accented by the metre where the music starts,
  with nothing judged, to play to however you like; while it goes More's key has its lamp lit, and a new tempo carries it on at
  that. So the tempo shows in either mode. A run, or Listen, has the click to itself, so starting one stops the metronome;
  leaving the piece or putting Prima away stops it too. Its settings are beside it in More: where it's heard (Tablet or
  Piano), its sound (Wood or Beep) and how loud, which are Settings' click's. A piece opens at its own tempo where it marks one (the one it goes at
  most, or a MIDI file's) and at 72 where it marks none, until a tempo is set for it, which is then kept for that piece. Once
  it's been set to another, **Original**, with the tempo's − and +, goes back to the piece's own. A
  piece on two staves can be played a hand at a time: the bar's hands, **Left**, **Right** or **Both**, one tap each and kept
  for each piece, have the run listen for the lower staff's notes or the upper's alone, and dim the other's. A run follows the music's repeats:
  each repeated part twice, the second time by its second ending (D.C. and D.S. aren't followed yet), and the cursor goes back
  with it; a note played twice keeps the mark of the latest time. **Loop**, on the bar as on every bar and on the chosen bar's tab,
  plays bars again and again until you stop: it loops the bar chosen (the first, when none is), then a tap past either end
  stretches the loop to that bar and a tap inside trims its nearer end, or either end's round handle drags it, onto other
  lines too, the bar under the finger named; × on the tab, or Loop on the bar, ends it. A repeat
  printed inside the loop, both its signs in it, is played every time round as the piece plays it, its endings in turn; one
  the loop cuts through isn't, and goes as it does the last time through. Each time
  round is a **pass**, scored by itself: the bar counts them, and as each begins shows how the one before went (In time the
  share right in time, Wait for me the notes right first time), with ↑ or ↓ on the one before, unless Practice is set to say
  nothing of it; nothing stops for it, unless a loop is set to wait after each pass (below). In time
  a pass is scored once the next has begun and a late key for its last note has had its chance, on the keys played for it:
  from half a window before its first note to half a window before the next pass's first, so the next one's first keys
  aren't extra in it. Wait for me counts a pass once a step of the next is played. Stopped, or left to run its course, the
  run is its last pass played to its end, with that pass's marks (a pass whose last note was due after the last key played
  isn't one; stopped in the first, the run is what was played of it), and it's kept with the loop's bars and every pass. **The
  strip**, in the foot under the bar, holds every bar as a cell, each line's first marked and the bars on screen boxed: tap a cell
  for that bar, as on the music (which comes into view, or turns to its page), or drag along it to loop the bars between,
  then drag its slim handles. The music scrolls under a finger, with nothing beside it. Full screen hides the strip. While a
  run plays, tapping a bar, on the music or the strip, starts it again from there, and **‹ Bar** goes back a bar (in a loop, to
  its start): it's the same go, moved, so what was played before it isn't judged or kept as a run of its own, and its time
  carries on. The bar stays as it plays all the while, never showing Start for a moment between: Wait for me waits at the bar
  tapped at once, and In time counts in to it. More can't be opened while a run plays, and looks off then. **Play along**, with one hand chosen, has the piano play the other In time. **Listen**, beside Play, has
  the piano play the piece, to hear how it goes: from the start bar (or round the loop) at the tempo, after a bar of count-in
  so you can join in, the click then quiet and the cursor moving with it. It plays what a run would sound like with your part
  played right: the hand chosen, the other too with play along, or both. Nothing is marked or kept; taps and ‹ Bar move it as
  they move a run, still listening. It needs the piano taking notes from the tablet, as play along does: without
  one its key looks off, and a tap on it says what it needs. While it plays, the music
  scrolls in its frame, the line being played kept in the upper third so the next one shows; afterwards
  every note stays marked, and (practicing the Standard way) the run's review is under the music, over the bar, with its
  headline (**right in time**: the share of the notes played right and not off the beat; or right first time), so how it went
  is on the screen with it, as in full screen: its few lines fit, and its details scroll in the review's own share of the
  screen. It doesn't move your level.
- **Practice** starts a practice session on the piece in full screen, hiding the navigation and Android system bars. Returning
  to Practice from a review or reopening an unfinished session also enters full screen. The full-screen button can still
  bring the surrounding controls back; finishing the session restores them for the look back. Outside full screen, two clocks sit
  at the foot's right: **Playing**, the time a run or Listen is going or you're playing the piano yourself with the music
  stopped (going over a bar on your own: a key in the last ten seconds counts), and **Idle**, the rest of the session: looking
  at the music, working out a fingering, a drink of water. **Finish practice**, beside them, ends the session (a run going
  stops first, and is kept with it; Listen going stops, and the session ends then) and shows its look back. Full screen has
  no foot, so no clocks, upright too: it's for the music. It has its own **Finish practice** instead, first on the bar, before
  More and away from Play and the run's keys, there only in full screen: it does what the foot's does, as if you'd left full
  screen to press it. Only a tap presses it; no piano key does. A PDF read into
  notes, with **The print** (in More) showing its print, keeps the session's clocks and both Finish practice keys, and finishing brings
  the notation back for the look back. After 15 minutes with nothing played the session pauses: the
  page is covered by **Still practicing?**, with **Keep practicing** and **Finish practice**, and the clocks stop until you
  carry on. Leaving the piece, or the tablet sleeping or Prima put away, pauses it the same way, and coming back shows the
  cover; a key played on the piano carries on too. Finished from the cover, the session ends as of the last thing played, and
  one left paused until another day is finished that way, the piece opening on its front. The session is kept every half
  minute and as it pauses, and its playing time is the day's practice minutes as it's counted.
- **Piece feedback** applies to all library pieces. Minimal keeps the score visible, Standard shows a short summary and loop results, and Detailed opens the full review and pauses between repeats. **Customize feedback** controls the summary, loop result and pause independently. **Repeat automatically** is a separate playback choice. A full review or a paused loop waits for the player.
- **Finish practice** shows [the session's quick review](#the-quick-review): playing time, runs and loops, what the session came to and the places that still need help, the session run by run (**Where it went wrong**) or in order (**Over time**), and **Next** with **Practice it**. **Passages and results** expands the passage breakdown. **Details** opens the in-depth review. **Keep practicing** resumes the same session.
- **Practice history** is available from Today, the profile, repertoire cards and each playable piece. It lists finished sessions by date and time, including short runs and standalone play-throughs. Opening a session shows its saved runs. Select a run or a bar to inspect its results and available note marks. Archive views read the saved records without restarting practice.
- **Play through** starts at the beginning with both hands at the selected tempo. Afterward, the player can replay the piece, inspect the run or finish.

- **Screens over the music don't draw it again.** The look back, the in-depth review and Play cover the music or take it full
  screen; the music isn't drawn while it's hidden, and the last three sizes it was drawn at are kept, so coming back, leaving
  full screen or turning the tablet back puts the drawing back rather than laying out the piece again (seconds, on a long
  piece on the tablet). The first time it goes full screen it's drawn at that size once, its lines coming as they do when it
  opens. Drawn again at another size, the music stays where it was: the line that was first in view is first in view again,
  by its bar (it used to be left at the same distance down, which is other bars at another width, or at the top).
- **A PDF** turns by the same means, and by a tap on the page's outer thirds. Portrait shows a whole page, and by default a half-page turn first shows the next page's
  top over this one's bottom (as forScore does). Landscape moves a screenful of systems at a time. A PDF's first page shows
  as soon as it's drawn (the paper waiting until then, as a sheet's does). The first time it's opened, each page's systems
  are then found behind it, a page at a time, the bar saying which are still to come ("Pages 3 to 6 still coming") with the
  pages down the frame's edge, those to come in outline; they're kept once they're all found. **Scroll at the tempo** finds
  the page's systems from its staff lines (it waits for them, that first time), and after a bar of count-in moves a system at a time, each lasting bars × beats a bar
  × 60 ÷ tempo seconds, with the system being played lit. The tempo and metre start from the piece's MIDI, which the piano can
  also play (**Hear it on the piano**). A staff line is one unbroken stroke across more than half the page, so a row of beams
  lying along a staff, the ties under a system or a dashed octave line isn't taken for one, however much ink it adds up to.
  Where a scan's lines are too broken to follow, the rows that are more than half ink count instead.
- **A PDF's bar** is the notation's play bar, its keys in the same places: its page turns and its page, More (the click; the
  view, half-page turns, beats a bar and bars a line; the print or the notation, once its notes are read; **Hear it on the
  piano**), then the tempo, which the scroll goes at. Nothing played on it is followed, so it has no hands, mode or Play: its
  own keys end it where Play would be, **Scroll** (in Scroll at the tempo) and **Convert to notation**, the one to press
  orange. **About**, in its head, shows About this piece in the print's place, with Remove from the Library, since a PDF has
  no front.
- **Converting a PDF's notes** starts from the orange **Convert to notation** button on the PDF's bar. The PDF opens
  normally even when it has a saved conversion; the button reopens its progress or review. The toolbar then shows **Back to
  sheet music**, while **Convert to notation** moves into the form below, requiring a separate tap before a paid request.
  Conversion and correction controls never respond to the piano's shortcut keys. Image-only PDFs are read from their pixels,
  including light-gray staff lines; no connected keyboard or selectable PDF text is needed. Empty staff detection is retried,
  and a page with no detected music explains why conversion is unavailable without making an API call.
  First choose the pages and check the proposed bar boundaries. Each box must cover one complete bar across both staves;
  dividers can be moved, added or removed before confirming the whole set. Confirmation saves exact page coordinates and
  stable bar identities against these PDF bytes and this boundary revision. The selected provider and explicit model are
  recorded with the job; the conversion workflow does not depend on a particular provider or model. The form shows the batch
  count and request/output-token allowances, rather than promising a fixed price or duration.
  Conversion is a durable job on the tablet. It reads complete-bar batches, normally at most four bars and 12,000 output
  tokens per request, checks the notation and engraves it using rendering owned by the job. Received readings, completed
  batches and their artifacts are saved as work progresses. An output-limit failure splits only the affected batch; observed
  output size can also reduce later batches. A single bar that still exceeds the limit pauses for attention. Completed
  batches are not sent again, and an exhausted allowance requires explicit permission for one additional request.
  Each request carries the bar before it as context, and says whether its first bar starts a printed system. A key signature
  is printed at the start of every system, right after the clefs and the same in both staves, so there the key is read from
  the print alone: a system that prints none has none, whatever came before. Inside a system the key carries on from the bar
  before until a new key signature follows a barline. A sharp or flat right before a notehead is that note's accidental, even
  on the first chord after the clefs. A reading whose key changes inside a system after an ordinary barline, where a chord's
  sharps are far likelier than a new key, is sent back with that problem like any failed check; read the same way again, it
  is kept, and the review flags it.
  **Back to sheet music** closes the reader and restores the PDF with Options closed; it does not cancel conversion. The
  Android foreground service keeps work running when another screen is opened or the display sleeps, and a notification
  returns to the same piece when attention or review is needed. **Cancel** is the explicit stop action. If Android or a
  connection interruption prevents completion, saved work remains and the job can be resumed; a request whose outcome is
  uncertain requires acknowledgement before it is sent again because the provider may already have billed it. Key, credit,
  spending-limit, connection and preparation failures appear as local, actionable errors. A failed review-draft save offers
  **Save reading** without another model request. Cost readouts distinguish the known subtotal from unpriced or uncertain
  charges; a recheck's cost is shown separately from the original reading.
  Review shows each printed system with its interactive engraving beneath it. Every generated bar is selectable, and only the
  selected bar is outlined at its actual rendered position, including when the notation wraps. The matching source box is
  shown only for a confirmed association; a legacy reading needs explicit confirmation of its correspondence as well as
  matching per-system counts. Model doubts, personal flags and deterministic **Notation checks** remain separate. The last
  identify unresolved ties, slurs, hairpins or pedal marks and dropped or reanchored marks, with their bar, staff, voice and
  event, and every change of key signature, as **Both staves · key signature**: at a system's start, after a double barline,
  or after an ordinary barline inside a system, where a chord's sharps or flats were probably taken for one. A wrong key goes
  on to every later bar of its system, so a flagged change is checked against the print first. They are recomputed from the
  stored notation after edits and reopening. An empty model-doubt list is not a verified match to the print. **Listen from bar** plays the current reading on the piano.
  A bar can be marked correct, flagged with an observation, or edited directly with **Edit bar**: its notes and rests, and its
  key signature. A key set there also goes to the bars after it in its system that were read in the same key, up to one in
  another key or one already accepted, and the editor says how many; the next system keeps its own, so a misread key is put
  right a system at a time, and **Undo last change** takes the whole change back. Accepted bars must be reopened before they
  can be changed. **Recheck this bar** requires a separate confirmation and sends only that bar with its context and the
  owner's observation. Its result is a proposal: it changes no notation until explicitly accepted, and a proposal for an
  older review revision cannot overwrite newer edits. Review history and undo are saved; a new full conversion starts a fresh
  review session while preserving the earlier session. **Keep** uses the current reviewed notation, graded and cut into
  excerpts like an import, with **Print** returning to the original PDF and **Convert PDF again** available from About.
  Notes, chords, rests, ties, grace notes, tuplets, clef changes, repeats, endings, articulation, fingering, dynamics and
  expression marks are retained where supported. Written 8va, 15ma and 8vb notation keeps its written pitches and octave
  markings while playback keeps the sounding pitches. A song's lyrics printed in the PDF aren't read: the notation is its notes
  and marks, and the words stay in the print. The result still needs checking against the source before use.
- **The sight-reading track** reads real music too: every third passage of the day, from level 4, is an excerpt at or below your
  level of the length the level reads (four bars, eight from level 7), engraved from the piece itself. A read excerpt rests
  three weeks. Excerpts don't move the level: their grades aren't calibrated against your reading yet.
- **Imports** (**Import files**, one or several at once, or a zip of them, like a download that bundles a song's files):
  MusicXML, MEI and ABC are engraved and graded like the collection, a song's lyrics kept with its notes (Notation, below,
  says how they're printed); a PDF is kept as pages, and a PDF with a MIDI file of the same name picked or zipped with it is
  one piece: the PDF its pages, the MIDI its notes, written out, graded and cut as a MIDI file by itself is, to play along with
  and to be heard, the print a key away (a MIDI that can't be written out as notes leaves the piece its pages, and is still
  heard if it plays). They were two pieces of one name before; pieces imported then stay as they are, and a PDF's details
  give it its notes from its MIDI afterwards. A piece's title starts from its file's name without what a shop adds after it
  ("Hallelujah - C Major - MN0258239" is Hallelujah), or from the title its file gives; its details change it. One piece
  imported opens; several stay in the Library with a line saying
  what came in and what couldn't: a file that can't be read is named there, whatever it trips on, and the others still come
  in. A part with no pitches, like a drum kit's, is printed with the piece but has no keys to play: its notes only keep their
  time, and its own rests and marks (its loud and soft) aren't the pianist's to read, so they don't change the piece's level
  or how its playing is judged. A MIDI file is written out as notation first, since it holds
  when notes sound, not how they're written: the hands from its tracks (or middle C), the bars from its time signatures, the
  key from its signature or its notes, each hand's beats snapped to sixteenths, thirty-seconds or triplets on their own, so
  one hand's triplets stay triplets against the other's eighths or sixteenths (and in a file from notation software, whose
  notes start exactly where they're written, triplets in one voice stay triplets against eighths in another of the same
  hand), a hand's held notes as a second voice, ties across beats and barlines, its sustain pedal as pedal marks under the
  bass staff (a change where it's lifted and pressed again at once, as under every bar), and its first tempo kept, unprinted,
  as the piece's own. A note from the half beat stays one note over the next beat when it ends within its half of the bar (in
  2/4 and 3/4, the bar), as print writes an eighth, a quarter and an eighth; rests show every beat. A key signature whose last
  flat or sharp the notes never play, while they play its natural often, has one too many when the key the notes fit best
  agrees: a musescore.com file says four flats over music in three, with D after D and not one D flat, while Burgmüller's
  Ballade keeps its three flats with every A raised. A small note played just before another a step away, over the notes
  before it, is a grace note, written small before it and not played, as a file made from a Musicnotes PDF or by LilyPond
  plays one. A first bar in a short time signature of its own, as notation software writes a pickup (a 1/4 bar before the
  2/4, two beats of 2/4 before the 4/4), is a pickup, so the bars are numbered as printed. Notation software lets each note
  go a little before its written end (a file from MuseScore holds each for 95% of its length), so where a file's notes stop a
  little short of the next, each is written at its full length: a half note is a half note, not a dotted quarter tied to a
  scrap with a rest after it. A written rest stays. Such a file plays a staccato note for a share of its value of its own,
  about half (MuseScore's is half, less a tick; LilyPond's articulated MIDI's half, its other notes seven eighths): where three
  in four of a file's notes let go well short fit one such share, each of them is written at its value, up to a dotted quarter,
  with a staccato dot and no rest after it. A sixteenth held 59 ticks of 120 is a staccato sixteenth, not a thirty-second and a
  thirty-second rest, while a thirty-second let go at 95% keeps its rest. A file that holds its notes in full (LilyPond's
  plain MIDI) can't say which short notes are staccato, so its short notes and rests stay as they are, and so do a
  performance's, whose notes are let go anywhere. Listen, a hand played along and Hear first play a staccato note for half its
  length. A hand that stays far from its staff for a while is printed as an engraver would: the left hand in the treble clef
  and back, the right hand in the bass clef, a right hand high above its staff (or a left hand above the treble's) under an
  8va line, and a left hand far below its staff under an 8vb line; a note or two out stay on their ledger lines. How far and
  for how long was fitted to how some 1,900 engraved piano scores in the library and the local collection print their hands
  (research editions, MuseScore users', Humdrum's and ASAP's): of the notes two or more ledger lines from their staff, the
  rule prints the right hand's as their engravers did 82% of the time and the left hand's 84%. A clef changes only at a
  barline or where the hand's first voice strikes a chord, never while a note of the hand sounds on through it. The keys to
  play are the same however a note is printed, and levels read the ledger lines as drawn. One
  exported from notation software comes out close to its score, less what a MIDI file can't hold: its repeats are written
  out, a trill is the notes it plays, and slurs and dynamics are gone, as are any lyrics it carries (a song's words come with
  its MusicXML). One recorded from playing comes out ragged, and a pickup written from the bar's start comes out as a full
  bar. MuseScore and Humdrum files need converting to MusicXML on the PC first (the Library tool does it for its corpora).
- **Taking pieces out**: a piece's **Remove from the Library…**, under About this piece, takes it out once asked again, with
  its excerpts and readings, and an import's or a collection's PDF and MIDI. Its runs stay, so its days and the streak still
  count them, and every screen that lists pieces goes on without it. Then it's back on what it was opened from, as that was
  left, with a line at the foot of the screen saying what went and
  **Undo**, which puts it back as it was, with its excerpts, reading, conversion and files, until the Library is left. One of
  Prima's own stays out: the Library doesn't put it back when it stores the collection again. One of the collection's stays out
  too: importing its zip again leaves it out (the import says so), and the Library's list says how many are out in a line at
  its end, with **Let it back in** (**Let them back in**), after which their collection's next import brings them back.
  Settings' **Remove library** (Your data, below) takes every piece out at once,
  Prima's own too, and Prima then puts none of its own in by itself, not even those a newer release brings. The Library says
  so: when it's empty, in place of everything else, with **Put Prima's pieces back**; otherwise in a line at the end of its
  list, with **Put them back** (**Put it back** for one removed by hand). Either stores every one of Prima's own again, those
  removed by hand too, as on the first open, with the meter showing how far it's got.

Finger-independence patterns are meant to come from these pieces where possible, since finger training carries over only partly
to other patterns; for now they're built-in five-finger figures.

### Repertoire

The pieces kept to come back to, by themselves: nothing of them is in the Library but their stars. A star on a piece in the
Library keeps it here, and it can be put in **collections** under names of your own ("Learning pieces 5"), from its star or
here with **New collection**. The collections are tabs after **Everything**, each with how many pieces it holds; the one shown
can be renamed, or removed, which leaves its pieces kept.

Each repertoire card shows the piece, its composer and its most recent recorded practice. The bar meter shows which bars were played in time. **Practice history** opens that piece’s past sessions, and **Practice** opens its score. Cards can be sorted by last practice, number of bars in time as a share of the piece, or title. The star manages its collections.

Under the cards, **Lately played, not starred**: the pieces played in the last month that aren't kept, eight at most, the
latest first, each with ▶, when it was played and how that went, and a star that keeps it. A piece the Library no longer has
keeps its star, and shows again when it's put back.

### Routines

Your routines and Claude's, kept to come back to as the Repertoire keeps pieces, so several can go at once (one for a piece, one
for technique, one to warm up). **New routine** in its head makes one of yours: named, then filled from the exercises (Exercises
has the key too); **Ask Claude for one** asks Claude for one. Tabs show Everything, Yours or From Claude, each with how many.

- **Starred**, on top as cards, three at most (a fourth star waits for one to come off): whose it is, how many exercises and its
  minutes, and On Today on the one Today follows; its name; its exercises' shapes in a row, as on their cards (six, and how many
  more); a lamp for each of the last four weeks' days it was played (time played on one of its activities: one only opened, or
  skipped, isn't practice); when it last was, how many of its activities had a clean run that day, and on how many of the 28
  days; and what's next, the first activity not done today, with **Start** (**Go on** once some are done, **Again** once all
  are), which opens it at once.
- **Your other routines**: the rest, the last played first, each a row with ▶ to start its next activity, whose it is, its
  exercises and minutes, when it was last played and how many went clean, and a star.
- **Asked of Claude**: every request that isn't a routine yet, however many are accepted after it, newest first, each with
  what it's called, where it is (Asking Claude…, Ready to look over, Didn't finish, Interrupted, Cancelled), its minutes, its
  piece and when. Each opens the request: its proposal to look over and use, or why it didn't finish and **Retry request**. The
  list follows a request as it changes. **Dismiss…** beside one that isn't being asked just now asks, then takes it off for
  good; one waiting to be asked or asking Claude stays until it's back.

A routine opens on its own page (the one Today follows at routine, any of them at routines/…): its activities as numbered
tracks, each with its shape, its name, how it's played, how it's going there (faster, "66 → 76" from its first run In time in
the routine to its latest; or its clean runs; or New) and its minutes, and each opening its activity. Beside them, **How it's
going**: the four weeks' lamps, the days and the last, this week's minutes, the clean runs of the four weeks, how many of its
activities got faster and since when, what's next, and **On Today**, which makes it the routine Today follows, or gives Today
back its standard plan. In its head, the star and **Start**; for one of yours, **Edit**: its name, each activity a minute more
or less, moved up or down, or taken out, and **Remove this routine…**, which asks first (its days go; the runs played in it
stay, as runs of their exercises); with **Add exercises** under the tracks, to the catalogue, where a card's **+** puts one in.
One of Claude's is kept as it was accepted, with its summary over the tracks; **Make it yours** copies it as one of yours to
change, and its suggested adjustments, from your results, are looked over and make a routine of their own, as before.

In a routine of yours an exercise or a drill is just itself, played as you last left it, and what's changed playing it (the
key, the hands, a variation, the tempo, Loop, a drill's step) is kept as it would be anywhere: the routine is the practice, and
each exercise remembers its own. Its track says how it's played now ("C major · right hand · ♩ 76", "Step 2 of 4"). In one of
Claude's, an activity is played as its recipe says (a key, hands, a mode, a tempo, a step), and what's yours is left as it was.
Either way the routine's keys at its foot's right (the activity's place in the routine and its minutes counting down, how it
felt in one of Claude's, **Skip** and **Next**) go on to the next activity not done, then back to the routine's page, and its
head's way back goes to the routine; a routine not on Today keeps its own day's progress, so it can be practiced any day without Today changing.
The exercises once kept as My routine, a list with nothing else to it, are now a routine of yours by that name.

Prima comes with twelve routines of yours, put in the first time it starts ([routines.md](routines.md) has the research behind
them): Beginner daily, Intermediate daily and Advanced daily; Quick warm-up, Concert warm-up and Short concert warm-up; Reading
focus, Even scales and arpeggios, Two hands, two jobs, Chords and jumps, and Touch and pedal; and Light day. Each has its line
on what it's for over its tracks, saying what follows it (Sight-read, your pieces) where that matters. They're yours like any
other: changed, they stay changed; taken away, they stay away; and one a later Prima adds is put in once. Starting over puts
them in again.

**RCM technique**, beside New routine, makes RCM's technique for a level a routine of yours: Preparatory A, Preparatory B or
Level 1 to 4, as The Royal Conservatory's Piano Syllabus, 2022 Edition asks it (pages 9 to 38), named "RCM Level 1 technique"
with its line on what it is over its tracks; asked for again, the one made before opens. Its activities are the catalogue's
own: the Scales and Arpeggios drills, and the exercises for pentascales (The pattern, then its chord; Five fingers, detached),
contrary motion, the chromatic scale, the triad sequence (One shape up the scale) and tonic triads broken and solid (through
the inversions). Each plays as RCM asks it, as one of Claude's activities plays its recipe: RCM's keys come round a key a day,
and where RCM asks for hands separately, each hand on a day of its own (Level 1's nine scales, two hands each, come round in
eighteen days), so a day holds two or three minutes of each, about ten in all. Its track says how it's played that day ("Today:
A minor, natural, right hand · RCM ♩ 69 in eighths"). RCM's minimum tempo is the target. A drill writes RCM's notes (its scales
in eighths, two octaves from Level 1) and shows RCM's tempo beside its own target, finding your own speed first in Wait for me
and building towards RCM's as it does anywhere, its tempo kept by the item in eighths apart from the drill's own; each valid
run says how it stood against RCM's. An exercise keeps its own notes, so it starts at RCM's tempo in its beat at the same notes
a minute (RCM's triplets at ♩ 50 are its quarters at ♩ 150), and says so over the music, with where Prima's differs from RCM's
(one octave where RCM asks two; the left hand playing the chords' roots). Not in Prima yet: the formula pattern (Levels 2 to 4)
and Preparatory A's triad sequence broken. Prima isn't affiliated with RCM. What's changed playing them (the key, the hands,
the tempo of an exercise) isn't kept as yours, as with Claude's; the routine itself is yours, to rename, time, reorder, add to
or take away.

The Routines key in the rail is lit on every routine screen: this one, a routine's page, asking for one, a request and an
activity, an exercise or a drill opened from a routine among them (Exercises is lit when they're opened from there). Routines
Claude wrote before it was asked for American spelling are shown with practice spelled as the rest of Prima spells it; only
Claude's words are respelled, never the goal you typed, a routine you named or a piece's title.

### Tools

A tab of Settings (Settings › Tools), a card for each, which Settings' search finds by name; each opens a screen of its own, at
the address it always had, and the old Tools screen's address opens the tab.

- **Glossary**: everything on a sheet of music. First and largest, **where the notes live**: the grand staff from the bass
  staff's bottom line to just over the treble staff, each note named under it, middle C in both, and the same notes on a strip
  of keys below; a note or a key tapped is lit in both, said in words (its line or space, a ledger line, the clef it's named
  for) and played on the piano when Prima sends it notes. Then every symbol and term by group (the staff; notes and rests;
  rhythm and time; sharps, flats and keys; loud and soft; touch; speed and mood; repeats and the roadmap; ornaments; hands,
  fingers and pedals), the most common first in each. Each says what it is, how it's played and how it sounds, and opens to a
  bar or two engraved as the sheet engraves it. A search finds things by name or by what they look like ("curved line",
  "dots", "hairpin"). On a piece, **What's this** (in More, on its bar; **Done** on the line over the music ends it) makes a tap on the music name the symbol under it from
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

Opened by the round key at the head of the rail. Progress, as trends over the last twelve weeks (see [Progress](#progress-and-the-streak)), with a note that a plateau after quick
early gains is normal. First, **Pieces**: the Library's pieces you've played, the latest first (twelve at most), each with its
runs, the days it was played on, when it was last played, how the last run was played and went, and the best of the times it
was played through In time. The profile it's headed with (name, which clefs you drill, daily goal, the practice cue) is set in
Settings.

### Settings

Six tabs, a grey track with the open one on its thumb:

- **Practice**: the profile (name, minutes a day, the cue, which clefs the note review drills) and practice (one or two bars of
  count-in, how much Practice tells you on a piece (Simple, Standard or Hands-on, or their four settings one by one: what shows
  after a run, each pass's figure, whether a loop waits after each pass, and whether a run goes again at its end), and whether
  the right note is named after a wrong one in a note drill).
- **Keyboard & sound**: the keyboard (the device, sending notes to the piano, the piano buttons, timing) and the click (where it
  sounds, its sound and volume). On the piano, heard in its headphones, the click is one of four, each tried there by ear with
  **Try it** (two bars at 96): **Prima's click**, the tablet's own Wood or Beep sent to the piano as sound over its USB cable (the
  P-225 is a USB audio interface as well as MIDI; where the tablet doesn't list it as an audio output, a piano key plays
  instead), **Metronome**, the piano's own drum sounds on channel 10 (General MIDI's metronome click, its bell on each
  bar's first beat), **Wood block** (its high and low wood blocks on channel 10), or **Piano key**, a high note in its piano voice
  on Prima's channel, as the click on the piano first was. The P-225 plays all 16 channels it's sent and takes no MIDI clock, so
  its own metronome can't be kept in time with Prima's; whether its drums answer on channel 10 only the piano can say. Notes the
  piano sends back on channel 10 or 16 aren't taken for keys played.
- **Look & music**: the look (Porcelain or Night, each choice drawn in its own look), their accent (red, persimmon, amber,
  green, blue or violet; the same in both), and the notation (the music font and the
  sheet's size, with two bars to show them,
  and whether a song's lyrics are printed under its notes, on unless turned off; a song's More has the same switch).
- **Claude**.
- **App & data**: updates, your data, and the problems Prima kept.
- **Tools**: the glossary, the metronome, the keyboard check and the timing ([Tools](#tools), above), a card each.

A daily goal much above last week's average gets a note to go up gradually; Prima never raises it by itself.

The tab open is kept in the address (`settings?tab=keyboard`), so coming back to Settings, by the way back from Timing say, it's
open again; a tab tapped takes the address's place, so the way back leaves Settings rather than stepping back through its tabs.
Settings from the rail opens on Practice. A link can open a tab, and bring a section of it into view: the stopped screen's card
opens App & data at Problems (`settings?tab=app&section=problems`), and a routine's **Provider settings** opens Claude.

**Search**, beside Settings' heading, finds a setting in any tab by its name, its choices or what it says: every word typed, in
any order, whatever the case (# finds ♯). What it finds shows in the tabs' place, each section headed by its tab's name ("Keyboard
& sound › Click"; just "Claude" where they're the same), and works there as in its tab; a section's name finds all of it.
Cleared, the tabs come back on the one that was open.

Claude: the Anthropic API key on this tablet, shown masked (`sk-ant-…a1B2`), with **Test** (asks the API whether the key works,
which costs nothing) and **Remove**. Without a key, the steps to add one: a file named `claude-key.txt` holding the key, copied
into Prima's folder over USB with `adb push`. Prima takes it in when it opens, or at once with **Look for the key file**, and
deletes the file. The key lives in the tablet's secure storage, never in Prima's data file, its settings or the web view, and
Android's backups leave it out, so a restored tablet needs it pushed again. **This month** says what Claude has cost since the
1st, piece by piece.

**Updates:** new versions come from the repo's GitHub releases (Prima 1.0.N), through the Kit's shared updater. Prima checks at
most once a day; Settings shows the version on the tablet (1.0 for a build from the PC), the newest release with its changes, and
**Install**. Installing downloads it, checks it against GitHub's fingerprint and that it's signed with the same key as the copy on
the tablet (so Android keeps the data), takes a full copy of Prima's data (below; the sentence under Install counts it up), and
hands it to Android, which asks to confirm. The first time, Android asks to let Prima install apps. The first time Prima starts
after the update, it checks its data holds all the copy did before a fresh copy takes its place.

Your data: Prima keeps a copy of everything but Claude's key in Documents/Prima backups on the tablet, where uninstalling Prima
doesn't reach it (How the data is shaped, below). **Last copy** says when the newest was made, its size and what it holds (the runs
and the days they were played on, and the files imported), and **Copies kept** how many there are and what they take together;
a copy under way shows its progress on the meter. **Back up now** takes
one. **Restore from a backup…** lists this install's copies, and with **Choose a folder…** (the tablet's folder picker, opening on
Documents/Prima backups) the copies of every install in the folder chosen, or with **Choose a file…** an export or a Prima data file
on its own; choosing one says what it replaces, and **Restore this copy** keeps a copy of what's here first, held, so the restore
can be undone (not on a fresh install, with nothing of yours yet), then puts all of the copy in place but its problems (the
tablet's own stay), or nothing if any of it is damaged, and opens Prima afresh on App & data, where it says what was restored. If
what's here can't be kept, nothing is restored
and it says why, and **Restore without keeping what's here** restores all the same. **Export everything** puts it all in one zip
for the share sheet (to Drive, or to the PC), which restores like a copy. The copies held stand first, newest first, each saying
what it was kept from before (an update that didn't check out, with what was missing; a restore; Start over, Remove library or
Reset settings) and when it was taken, with **Restore it…** and **Let it go** of its own.

Under them, three keys start afresh, each asked twice: the key, then what goes and what stays, with the key that does it and one
that leaves it. Each keeps a copy of everything first, held in Your data under its name; if it can't be, nothing is done and it
says why, and a key of its own does it all the same without a copy (**Clear it without a copy**, **Remove them without a
copy**, **Reset them without a copy**).

- **Start over** (**Clear my progress**) takes everything Prima keeps about your playing, so it's as on its first open: levels
  and tracks (the drills' and the exercises'), note cards and answers, every run, practice day and session (and so the
  streak), routines (yours and Claude's, and their stars) and requests, which sight-reading passages have come up, and on each piece what's next, where it was
  left, the tempo set for it, the hand practiced and when it was last opened. The settings stay as they are, and so does the
  Library, with the music imported and the collection (the music isn't progress, and only a copy or an export carries it), and
  how each piece shows and each screen plays. Then Prima opens on its welcome.
- **Remove library** (**Remove every piece**) takes every piece out of the Library, Prima's own too, with their files,
  readings and excerpts, and what reading a PDF made; Prima puts none of its own back by itself after that (the Library,
  above, has a key that does). The progress stays, the runs on the pieces that go still counted, and so do the settings. It
  stays on Settings, saying how many pieces went.
- **Reset settings** (**Reset the settings**) puts everything on the Settings screen back as a new install has it: the
  profile, the practice settings, the keyboard's, the timing, the click, the look and the notation, and the welcome to be done
  again. Claude's key and the copies stay, and so do the progress and the Library, and what's kept elsewhere: the touch Keyboard
  check measured, how a run's summary was left open, and each piece's view and hand. Then Prima opens on its welcome, which
  asks for the timing again.

Nothing leaves the tablet unless you send it.

**Problems:** what went wrong, the newest twelve, each line its time, how bad (Warning, Error, Critical), what said it (a
screen, a job) and what it said, opening on the rest of it (the stack, the page's own words), with **Copy** (every problem
kept, both files', not only the twelve shown, to paste into a message) and **Clear…**. Everything Prima logs as a warning or an
error goes in: a screen that stopped, a job that failed, the page that stopped answering, anything thrown that nothing caught
(kept as Android closes Prima), and work nobody waited for that failed, but not what a page that has gone throws as it goes (a
call into its script once it was let go). They're kept in `errors.log` in Prima's folder too
(Android/data/com.saphyiere.prima/files, where `claude-key.txt` goes), which a PC reaches over USB even when Prima won't open;
past half a megabyte it becomes `errors.old.log` and a new one starts. The page can't tell Prima why it stopped answering at
the time, so it keeps what Blazor said just before its yellow bar showed in its own storage, even a line it leaves out at any
other time (a call back into a page that had gone, say), and never an older problem in its place (with nothing said just then,
"It didn't say why."); the bar says it ("Prima stopped answering taps", why, and that it's kept); Prima writes it in the log
the next time it starts, as of when it happened.

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
   trill's, a mordent's or a turn's keys and grace notes aren't slips. These are judged note by note, each with your lean so
   far taken out (the average of the right notes before it), so a steady habit of playing a little early or late isn't marked
   amber as you play and green afterwards; only the first note or two, before there's a lean, can still change.
4. **Summary.** After the last bar, [the summary](#the-summary), and the passage again with each note marked the same way, now
   from the whole take lined up and the whole run's lean taken out, so a note can still change here and there from the mark it
   had while playing. A wrong note
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
- How each hand played, for [a run's review](#a-runs-review) (in Prima's music a staff is a hand): how **even** the gaps between
  its notes were against the gaps written (the spread of each gap's difference, in % of it, so a run that speeds up or slows
  down steadily isn't uneven); how **joined** its notes were where one is written to last until the next (the median silence
  between a key coming up and the next going down, in % of the gap, less than none an overlap; staccato notes, a slur's lifted
  end and a key struck again aren't asked to join); and where the touch varies and nothing in that hand shapes its loudness (a
  slur, an accent), how much the velocity varied, and each finger's mean over four notes of it or more. With both hands, where
  they play at once, how long after the left's the right's keys land, on average and from one moment to the next. And how
  far **ahead** of the click the run got from its first note to its last (behind it, less than none), on the line the tempo
  drift comes from. None of these changes in-time accuracy, clean, or the level.
- Expression, judged only where the music marks it, and apart from the notes and timing: it doesn't change in-time accuracy,
  clean, or the level (our choice). The marks are the score's own: a generated passage's, or a piece's own file's (its slurs,
  dynamics, crescendos, pedal and ornaments, read for this, apart from what the Library's grading and excerpt cuts read).
  - **Staccato** held long: its key down more than about half the note (at least 0.12 s, at most 0.45 s). **Tenuto** let go
    short: under nine tenths of it. Under a **slur**, a note let go more than about a tenth of a beat (60 ms at least) before
    the next note breaks the line (staccato under a slur, portato, is detached by design). An **accent**, or a sforzando, struck
    no harder than the notes within a beat of it, by 8 or more (velocity).
  - **Loudness**, worked out and kept with each run, but since 28 November neither shown nor said, here or in the summary's
    details: a marking has no fixed level, a passage's marking can't be told from its loudness in recordings, and MIDI doesn't
    say how loud a velocity is (the owner: it may come back later). As it's worked out: each dynamic's stretch at the middle of how hard its keys were struck, as a share of your soft-to-loud range,
    against the share it's played in (*pp* to 25%, *p* 10 to 40%, *mp* 30 to 55%, *mf* 45 to 70%, *f* 60 to 90%, *ff* from 75%,
    ours), and against the one before: marked louder or softer, it's played so by 5 or more. A dynamic is heard where it holds
    as written: from its mark to the first crescendo or diminuendo after it, not under one going on from before; under a
    hairpin the notes are meant to grow or fade, and past one the level is wherever it went, so a *p* that swells from its first
    note isn't judged as a *p* at all. A crescendo or diminuendo: the line through its notes rises or falls by 6 or more. A
    piano set to Fixed touch sends one velocity, so loudness isn't judged.
  - **Pedal**, where it's marked: pressed by half a beat after its mark, down for most of the stretch, and up (or changed) by a
    quarter of a beat after its release; down for over half a beat where none is marked is noted by bar.

### The summary

After a run of a Library piece, [the quick review](#the-quick-review) comes first, and what's below is under its
**Details**. After every other run to a tempo (Sight-read; on an exercise or a technique drill, [a run's
review](#a-runs-review)'s two lines take the few lines' place), first **a few lines**, to take in at a glance, each with
its bars: the **notes** (how many were right, what the others were, and the bars with the most of them) and the **timing**
(the share of the right notes that were in time, since a wrong note has no timing to be out, and where it rushed, held back
or was off the beat); then a line only for what there is to work on: notes
**held** too short, **touch** and **pedal** not as marked, and the bar to **look at**, the one with the
most to fix, where a wrong or missed note counts as three of anything else (a note off the beat or let go early, a marking
not played as asked, the pedal). Round a loop, a line comes first with each pass's figure (In time the share right in time,
Wait for me the notes right first time; past a dozen passes, the first, the best and the last), and the rest is the last
pass's. On a piece, **Loop bars** loops the four bars of that bar's phrase and **Play from bar** starts the next run at it. **Show details** opens the rest under them, brought into view (and stays open next time if you
leave it so): three panels, side by side:

- **Notes right**: the share right, a bar of right and not, and the ones that weren't as slips put right (hatched), wrong
  (solid) and missed (hollow), with extra keys.
- **In time**: the share of the right notes that were in time, and the lean, and every right note's timing after the lean in a strip from 20% of a beat early
  to 20% late, the in-time band shaded.
- **Held**: the share of judged notes held their length, and how long each was held, with the 80% line.

Each says in a line what stands out (slips or misreadings; where you rushed; which bars you let go early in). Under them, where
the music marks expression, a line says what wasn't as asked: staccato held long, gaps under slurs, accents no stronger than
the notes around them, the pedal (loud and soft against the dynamics isn't said: see Loudness, above). Then **Bar by bar**: the bars
as one strip, brighter where there was more to fix, which opens (and stays open next time if you leave it so) into a map of
the bars, both times through a repeat together: wrong and missed notes, how early or late each bar ran, notes let go early,
then touch (staccato, tenuto, accents, slurs) and the pedal, each only where it's marked.
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

### The quick review

After a run of a Library piece, round a loop on it, and after a session: what it came to in a sentence, the key to play next,
and a chart under them in **two kinds of mistake**, **wrong notes** (wrong, missed, or a wrong key put right at once) and
**off the beat** (a right note more than a tenth of a beat out, once your lean is taken out), each in its own colour, with every
count on its mark, nothing where a bar was clean, and a bar not played hatched. Every number the words say is on the chart, and
every mark on it is named on it (the owner, of the first drawings: "I should understand exactly what the chart is doing by the
legend alone and the chart").

- **After a run**: the figure, correct and on time, with the run before's under it; one sentence naming the place, what went
  wrong there and what to do ("Bars 21–22 need help: 13 wrong notes. 2 more mistakes elsewhere."); its first key (**Loop
  bars 21–22**, or **Again at ♩ 69**, which sets the tempo); and two rows, **Wrong notes** and **Off the beat**, a column a
  bar. A clean run, timing alone (rushed or held back), mistakes all through, a stretch or half the piece, and a stop each
  have their own sentence. **Details** opens the summary below.
- **Round a loop**: **Loop by loop**, a column each time round with its mistakes (both kinds stacked, the two together on top),
  and under each column what each bar got wrong that time round, so a bar coming right shows along its row. It's getting
  better, no better or worse, and its keys follow: the bar still wrong, or a notch slower, or a notch faster once it's clean.
- **After a session**: what it came to and the places that still need help, each with what it needs ("Bar 7 still needs help
  with wrong notes and timing, bar 21 with wrong notes"), and a switch: **Where it went wrong**, a row for each run through the
  piece with each bar's count of one kind (**Notes** or **Timing**), or **Over time**, each run and each time round a loop in
  order, as high as it went correct and on time. **Next** loops the first place a notch under the tempo you ended at, with
  **Practice it**.

The rules (`QuickReview` in Prima.Core, tested sentence by sentence) and the research behind them are in
[practice.md](practice.md).

### A run's review

After a run, the player sees one observation. Timed practice reports the recorded note or timing result. Wait for notes reports first-try accuracy. A loop uses its last completed pass. A specific bar can be inspected before the player chooses where to play again.

**Show details** opens the graphs for notes, timing and note lengths, the expression marks the passage contains, and the bar map. Technique drills also show their measured evenness and hand coordination. An explicitly selected course or routine still offers its next activity.

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
| Key signatures | A signature, then a phrase using its sharps or flats | The tonic, and every note played without its sharp or flat: the plain letter (F for F♯ in G major; G for F♯ is a slip, not the key forgotten) | C and A minor, then one to five sharps and flats |
| Rhythm | One to six bars of rhythm; tap a bar of beat, then the rhythm on any key. No line moves across the music while you tap: keeping the pulse is the point. Afterwards each note shows where its tap landed against the click, green on the beat, amber more than 10% of a beat off with an arrow, a ring wherever the tap was 5% or more off, outlined grey if missed; and each bar is summed up: one that ran early or late as a whole and by how much, the notes in it well off, where you came back on the beat | Early or late lean, steadiness, drift without the click, missed and extra taps | Quarters, eighths, dotted notes, sixteenths, syncopation, triplets; the click fades to a count-in, then silence |
| Keyboard geography | A note or interval; play it without looking down | Wrong notes one or two keys away, and time to play | Within a hand position (C to G from middle C), then just out of it, then leaps of a sixth to a ninth either way, then anywhere an octave either side |
| Listen and play back | Hear a short phrase on the piano, play it back, then see it written | Pitch and rhythm against the phrase | Five notes of a scale, then six to eight, then more keys; last, improvise an answer phrase (not scored) |
| Scales | A named scale with its fingering, octaves and tempo; play it in sixteenths to the click | Evenness of the gaps between notes, a slip at each thumb crossing and octave, evenness of touch, legato overlap, and the hands' sync | The exams' order of keys and octaves; the tempo rises in small steps from a comfortable speed |
| Arpeggios | As Scales | As Scales, with a close look at the crossings | A fifth's range, then one octave hands separate, two, hands together, inversions |
| Finger independence | Five-finger patterns, trills (3–4, 4–5), held notes; patterns from your pieces where possible | Each finger's timing and touch, trill rate and steadiness, whether held keys stay down | Slow and even, then faster, then the weak pairs, then both hands |

The drills read from a staff (Notes, Ledger lines, Intervals and shapes, Chords, Key signatures, Keyboard geography) mark each
answer on it as soon as it's played, as Sight-read's summary does: green right; wrong, the note grey and the key you played
beside it as a red head, each named above (a single note's name as Settings says); outlined grey missed; and a chord's extra
keys as red heads beside it, named. The next item takes the marks off; one asking for the same note as the item before
(Keyboard geography's first step has only five keys) shows it unmarked, listened for as any other. The words under the staff
name the same notes: "Off: note 2, E5 for F5", "The shape was right, from the wrong first note: …", and for a chord "Not quite:
F4 for E4" when as many keys were wrong as notes missing, or "G4 missing; A4, B4 extra". A chord's keys go down together,
within 150 ms; when the first are right and the rest still to come, it's listened to until all of it is down, a key not in it
comes, or nothing more for 0.6 s, and the words say "Not quite: played a key at a time. A chord's keys go down together.", the
notes played late amber. A set's summary lists the single notes
and chords you missed. The drills with steps show the step on a card over the bar, as an exercise shows its key, and its
**Change** has every step on a numbered pad, any of them one tap (the bar keeps the item, what's right and Loop): a new set starts at the step chosen, and from there over 90% in two sets in a row steps up and under 80% steps back. Key signatures shows a
signature alone and asks for its tonic; the signature with no sharps or flats is a bare staff, so its prompt says so ("Play
the tonic of this key: major (no sharps or flats)"), where a bare staff with nothing said looked like music that hadn't
drawn.

**Loop** is on every drill's bar, kept for each drill. On the drills read from a staff it runs the sets together: a set
is kept as it ends (its attempt, the ladder's step) and the next begins at once, with how the last set went in the bar, and no
stop at a summary; turned off, the set being played ends at its summary as before. Rhythm starts its next try by itself a
few seconds after each one's result, set after set; Listen and play back goes from hearing a phrase straight to playing it
back, and on to the next phrase a few seconds after showing it. Endless notes has no end to begin with.

Notes come back on a spacing schedule: a note answered right but slowly counts as not yet learned, and comes back sooner than a
fast one. Confusable notes are mixed. After each technique run the music is marked as Sight-read's summary is: green right;
wrong, the note grey and the key you played beside it in red, each named above; outlined grey missed; and a red head beside a
note for an extra key. The keys are lined up with the notes by the fewest slips, so one slip counts once instead of throwing out
every note after it, and the result says how many were wrong, missed or extra. A run with a slip doesn't count for evenness.
After a run, [a run's review](#a-runs-review) says in two lines what you did and what's next; under Show details are the
evenness (each hand's, named, only with both), how far apart the hands landed, and a line on each measure.
Technique results are compared with your own earlier runs, since there are no amateur norms; differences under about 10 ms are
shown as below what anyone can hear. Repetitions are capped per set and per day, with
a reminder to stop if anything hurts: a sensible precaution, not a proven one.

What a technique drill plays is yours to choose, whatever your reading level. It's the practice screen every screen with music
is ([Playing to a tempo](#playing-to-a-tempo)): the hands are on its bar, Left, Right or Both, with Play and Listen, and a card
in its foot says what else is chosen: the key lit on a small keyboard and named (a minor scale's form under it), the octaves
(or the pattern), and Through the keys when it's on, with its target and its runs beside it. **Change**, on the card, opens the choices over the music, which stays drawn underneath: the
key on an octave of the piano, each key named as the key it picks is written (D♭ major, C♯ minor), so any key is one tap,
with Major or Minor beside it; a minor scale natural, harmonic or melodic; one to four octaves on numbered pads (an arpeggio's
broken triad too); finger independence's pattern. **Done** closes it, and so
does a run starting; **Today's**, beside Done, goes back to the day's plan. Each scale or arpeggio keeps its own
tempo and target, by key, octaves past one, and hands. The first time, a run in Wait for me at your own speed sets the
comfortable speed the In time runs start a little under (the pace from one moment of the music to the next, so the two keys
of a moment played hands together count once), and the bar is set to Wait for me for that run; the tempo can also be set
with − and +. Wait for me runs are practice for the notes: they count towards the set's five runs and the day, not towards
evenness or the tempo. A set is five runs. After three, the way on shows at the foot's right: **Done**, back to the drills (or to
Today, from there); with all five played Run goes off, the way on is the orange key, and **Another set** is beside it. From
Today the day's Technique step is the scale and then the arpeggio: the scale's screen has **Next: the arpeggio** from the
start, and the step is done with the arpeggio's third run, not the scale's.

A technique drill's bar has **Loop** (see [Playing to a tempo](#playing-to-a-tempo)): the scale, arpeggio or pattern goes
round whole, each time round a run of the set, and the evenness shown when it's stopped is the last pass's, from that pass's
own keys; its review leads with the passes ("Twice round, every one clean"), and its details say so first, as every screen's
summary does ("2 passes of bars 1 to 2, right in time: 100% and 100%. The rest is the last pass."). Looping isn't held to five runs: it goes until Stop. Beside the key is **Through the keys**: every key one after
another as one piece of music, from the key showing on through the list and round to the one before it, each named over
its first note with its signature where it starts, in the form, octaves and hands chosen, at the tempo showing. A run
through the keys is practice for the notes, summed up as an exercise's run is: it isn't measured for evenness, no key's
tempo moves, and it's kept as a run of the drill. It's remembered for each drill, and can be looped too.

## Exercises

Where a drill measures a few things closely, the exercises are a catalogue to work through, and the drills are a type of it:
83 entries, the twelve drills and 71 exercises in 15 sections, from the owner's own list of technique work for an adult
beginner, from the research in `exercises.md`, and from a lesson the owner likes, a concert pianist's ten daily exercises
(`exercises/the_ten_daily_exercises.md`). The words are Prima's; the patterns are the common stock of piano teaching, and
Prima writes each one's notes itself, in the key and for the hands you choose.

| Type | What's in it |
| --- | --- |
| Drills | Every drill (see [Drills](#drills)): the nine reading drills, and Scales, Arpeggios and Finger independence, which are in their own sections too |
| Sitting and hand shape | Bench height and distance, sitting balanced, the hand's own shape, shoulders down, a tension check, filming yourself from the side |
| Warm-ups away from the keys | Arm swings and shoulder rolls, wrist circles and doorknob turns, shaking out, fan and close, finger taps on a table, gentle forearm stretches, an arm drop onto the knee |
| Arm weight and rotation | Dropping onto one note, rocking between thumb and little finger, smaller turns between fingers a third apart, wrist circles on a held note, and holding one key while the forearm turns to play the next |
| Five-finger patterns | The pattern in every key, major then minor, the hands in mirror, long–short, the pattern then its chord, Hanon's first pattern played quietly, and one finger holding while the other four play |
| Scales | The Scales drill, a scale in contrary motion, the chromatic scale, and a scale that parts in the middle |
| Broken chords and left-hand patterns | The Arpeggios drill, a chord passed between the hands, broken chords through the inversions, an Alberti bass, and an arpeggio counted in fours |
| Chords and cadences | One shape up the scale, up through the inversions, four kinds of chord on one note, I–IV–I–V–I with the hands close, four chords round and round |
| Jumps | An octave with less time to travel on each line, further each time (a third, a fifth, an octave, a tenth), a chord an octave away, the cadence with each chord from its own bottom note, bass note then chord |
| Thumb under | The thumb passing under two, three, four or five fingers |
| Trills | The Finger independence drill, slow trills on fingers 2 and 3, 1 and 2, 1 and 3, and trill bursts up the scale |
| Double notes | Thirds, three up and back; double notes bounced from the wrist; a scale in octaves |
| Hands doing different things | One hand holds while the other moves, two notes against one, smooth against detached, two against three |
| Touch, loud and soft | Two-note slurs, in one place and up the scale, five fingers detached, one note soft to loud, growing and fading |
| Pedal | The pedal by itself, and changed on each chord |
| Ways to practice anything | Slow enough to be right, hands separately then together, small sections, fixing a mistake at once, the metronome ladder, mixing things up, sleeping on it, recording yourself, the hard bit first, when to stop |

There are four kinds. One **played at the piano** (47 of them) shows its music with the fingering under the play bar every
screen with a tempo shares: In time, Wait for me or Listen, with the same summary afterwards, its few lines [a run's
review](#a-runs-review)'s two. Its notes, timing and held notes
are judged as a passage's are, and so, where its music marks them, are the touch (slurs and staccato), loud and soft, and the
pedal. One **away from the keys** (sitting, the warm-ups, the pedal by itself) has nothing for Prima to follow: you do it and
press Done, once a day. A **way to practice** is a few lines to read. And a **drill** opens the drill: all twelve are the
Drills type, and the three technique drills are in the sections they belong to as well.

An exercise's page has the way back at its head, by the name of where it was opened from (Exercises, Today), as a piece's
has; a technique drill's and a drill's have it too, and the metronome's goes back to Tools. One that's played is the practice
screen every screen with music is ([Playing to a tempo](#playing-to-a-tempo)): the head, its music, a run's review, the play bar
with the hands it's written for (Left, Right, Both, one tap each), and the foot, with a card on its left saying what's chosen
(the key lit on a small keyboard and named, a family's choices a word each) and the next in its section on its right.
**Change**, on the card, opens the choices over the music: its keys on an octave of the piano, each
named as the key it picks is written and any key it isn't in off, so any of them is one tap, with Major or Minor beside it
where it has both. Most have
every major and minor key. One that's major by its nature (major then minor, the four kinds of chord, the four-chord loop,
Hanon's pattern, the bounced double notes, the scale in octaves) has the twelve major keys; the chromatic scale starts from
each of the twelve notes ("From E♭") and is written without a signature; and the thumb passes under only in the keys where
it lands on white keys: ten of them under three or four fingers, six under two or five. A minor key's contrary-motion scale
is the harmonic one, and one shape up a minor scale keeps to the scale as its signature has it. A line that climbs more than
an octave starts where it stays near its staff in every key. **Through the keys**, beside the key, plays them all one after
another as one piece of music: from the key showing on through the list and round to the one before it, each key's music
without its close so one runs into the next, its signature where it starts and its name over its first note, and the last
key's close to end on. The play bar's **Loop** (see [Playing to a tempo](#playing-to-a-tempo)) takes an exercise round and
round without its close, or the whole run through the keys round again. It opens as you last played it, at the tempo you
left it, its music in a piece's paper frame, drawn the way a piece's is, taking the rest of the screen and all that scrolls.
Its words are **About**, in its head, shown in the music's place as a piece's About is: what you do in a line, how, what to
notice, what it trains and, for one whose idea
is from a lesson, a link to the lesson at that exercise's place in it; then what's been done of it (how many times and on how
many days, when last, and the fastest clean run In time), **Add to a routine**, which puts it in one of your routines or a new
one (as a card's + does). The next in its section is at the foot's right (in a routine, the routine's time, Skip and Next are
there instead). One to read or to do away from the keys is its words, the next in its section at their end.

A **family** is a main exercise and the side ones a few choices make of it. Its Change has a row for each **variation**,
every option a key: an interval drawn as its two notes on a staff, a touch as its slur or its dots, a count (quick notes,
fingers under, octaves) on numbered pads; the card says what's chosen ("Sixths", "2 quick notes"). What's chosen is kept
with the key and hands, and the main exercise is every variation at its usual. Loop and Through the keys
play it as it's chosen.

| Family | Its variations |
| --- | --- |
| Scale that parts in the middle (both hands up together, apart and back, on up, and the same coming home) | One octave each way, or two; joined or detached; as it comes, soft, loud, or growing and fading |
| Arpeggio counted in fours (a lean on every fourth note, so it falls on a different finger each time) | In fours or in threes; two octaves or four |
| Thumb under | Under 2, 3, 4 or 5 fingers |
| Thirds, three up and back | In one place, or up the scale; with both hands, the same way or in mirror |
| Bounced double notes (a few quick ones that bounce into a longer one, a step higher each bar) | Thirds, fourths, fifths, sixths or octaves; one to four quick notes |
| Scale in octaves | One octave or two |
| Trill bursts up the scale (four quick notes and a stop, a step higher each time) | Fingers 1 and 2, 2 and 3, 3 and 4, or 4 and 5 |
| Hold one, turn to the next (a key held while the forearm turns to play its neighbour three times) | The next finger, or a finger further |
| One finger holds, four play | Each finger in turn, or one of them |
| Two-note slurs up the scale | Fingers 1 and 2, 2 and 3, 3 and 4, or 4 and 5 |

With a pair of fingers, a hand alone uses the pair itself, and with both hands the left takes the opposite pair (5 and 4
against the right's 1 and 2), on the same notes. The long forms travel: a line that goes far from its staff changes clef, or
goes under an octave line, and a held key under moving ones is a second line on its staff, its fingers below it. Their ideas
are from the lesson, each linked from its page; the names, the words and the notes are Prima's
(`exercises/the_ten_daily_exercises.md` says what the lesson shows and where Prima's differ). Octaves, double notes and
held fingers are the kind of work `exercises.md` found hands get overworked by, so their words say to keep them quiet and
short, and the scale in octaves is marked Later.

Each exercise to play also has a **focus**, one to three of thirteen: reading (the reading drills'), finger strength, finger
independence, evenness, the thumb, arm and wrist, rotation, double notes, chords and landing, touch, loud and soft, hands
together, pedal. A section
says what the notes are; a focus says what playing them is for, across the sections.

The catalogue is laid out as the Library is. Down the left: three picks of the ones to play, **Start here** (the first weeks', which the tab opens on), **Not tried yet** and **Played lately** (the last
month's, the latest first); then the catalogue itself, listed by **Types**, **Trains** or **Levels** as the Library lists by
composers, kinds or levels. Types are the Drills first, then the sections to play by short names (Arm and wrist, Five
fingers, Scales, Broken chords, Chords, Jumps, Thumb under, Trills, Double notes, Two hands, Touch, Pedal), and under **To
read** the three with nothing to play (Sitting, Warm-ups, Ways to practice); Trains lists the focuses, Reading first; Levels
are First weeks, Beginner and Later. What's chosen shows beside the lists as **cards** under its name and a line on it: a
type's in its own order, the rest by type, a family among its type's others. A card has the exercise's **shape**, its name,
its tags (Drill, Variations for a family, To read, and Later for when the basics are easy) and when it was last done (Today,
a date, or Not tried). Its **+**, over the corner of its shape, puts it in one of your routines: a menu of them, each ticked
while it's in it (a tap puts it in at the end, or takes it out), and **New routine…** to make one with it in; the + is lit
while it's in one. Any exercise to play, any done away from the keys and the drills whose screens keep a routine's time can be
in one (all but Endless notes and Listen and play back); a way to practice is to read. The shape is how the notes move, drawn small, so what an exercise is shows before a word is
read: each note a bar at its pitch and time, the right hand's in the look's accent and the left's in ink, with a faint line
at each C. It's the exercise's own opening in C, in its first hands, two bars (four where two hold few notes): a scale is a
ramp, a trill a zigzag, a chord a stack, a jump two blocks far apart. A drill's, new every time, is what a set of it is like;
one to read shows its type. The search, in the head beside **New routine**, looks in an exercise's name, its section, what
it trains and all its words, across the catalogue, so "jump", "thumb" or "pedal" finds whatever mentions it. What's shown is
kept in the address, so coming back from an exercise comes back to it. The Drills screen's old address opens the catalogue
at the drills, and a drill's Done goes back there.

What it doesn't do yet: the exercises are fixed patterns, not adapted to how the last run went; they aren't a step of the
daily plan; a routine Claude builds plays a family's main exercise, not a variation of it; one exercise in two of your routines
is the same exercise in both, in the key you last left it, not one key in each; and the measures `exercises.md` proposes for chord changes and jumps (the first landing, how together a chord's
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

The times are for a twenty-minute day. A longer or shorter one shares out its minutes in the same proportions, in whole
minutes that add up to the day's, the pause included. The five-minute plan is the note review and two passages. The plan mixes
scales, rhythms and variants across days instead of repeating one block, and doesn't raise the daily minutes sharply from week
to week.

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
- **Habit**: minutes a week, days practiced out of the last seven, the streak and the freezes banked. The share of clean passages
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
  through the piano itself, and so do a piece's Listen and play along, a staccato note held for half its length. Each note
  goes out a fifth of a second ahead, its time worked out then from the click's beats: the tablet's click knows when its beats
  really sound only once its audio timestamp settles, a moment after it starts, so notes timed at the start came early by the
  speaker's delay.
- **Stopping what the piano plays** (a run, Listen, the metronome) drops the notes still to go and sends All Notes Off just after
  what Android already holds, on Prima's channel and the drums'. It never asks Android to flush its queue: on the tablet
  (Android 16) a flush can collide with Android's own USB MIDI thread, which then crashes Android's system process, and the
  whole tablet seemed to restart when a run or the metronome stopped while the click played on the piano (26 November 2026).
- **Latency** is calibrated once (play along to a click; the median offset is taken out). The piano's own notes are played that
  much after the beat, so they land where yours do: the piano's hand with yours in play along, Listen with the way you hear
  the beat. How much timing jitter the keyboard
  and the tablet add is unknown until measured. Prima estimates the USB and MIDI path's share from the piano's Active Sensing
  messages, which arrive at a fixed interval (our idea, untested); differences smaller than that aren't shown.

## How the data is shaped

Everything lives in one SQLite file on the tablet, with the music imported and what reading a PDF made in folders beside it.

- **Profile**: name, clefs drilled, daily goal, practice cue, look, the keyboard's settings and the timing offset.
- **Levels**: the current reading level and tempo, the rhythm level, and per scale and arpeggio the current tempo and target.
- **Note cards**: one per note and clef, with its review box, when it's next due, its recent answers and response times.
- **Sessions**: each day's practice, its steps and minutes.
- **Practice sessions**: each one on a piece, when it started and finished, its playing and idle time, whether it's paused
  and when something was last played, and the way it was practiced (Simple, Standard, Hands-on, or its own mix).
- **Attempts**: one per passage, rhythm, technique or exercise run, keeping its settings (the level's step, the key it was in and the key it
  avoided), the generator's seed, the notes played with their times and velocities, and the measures worked out from them, so a
  passage can be made again (one spot does) and a result recomputed if the scoring changes. A piece's run keeps the bar it
  started from, whether it was stopped and where, the hand, and round a loop its last bar and each pass's figures; the
  practice session it was played in, or that it was Play's; what went wrong in each bar (wrong, missed, off the beat and how
  many of those ahead of it, let go early, slips put right, chords not together), for the run and for each pass; how long it
  took; In time, its lean against the click and how steady it was, and how each marking was played, as marked or not (each
  dynamic and hairpin, each bar's staccato, tenuto and accents, its gaps under slurs, the pedal); and the marks it left on
  the music, for the in-depth review. A run kept before 15 November has no time, lean or markings, and the look back says
  less of it.
- **Pieces**: the collection's, a local collection's and imported pieces (kept apart, so a newer bundle or a local collection
  built again never takes out the others), with their sources (an uploaded piece's, the name of the file it was imported
  from), licences and tiers (A: public domain or CC BY; SA: share-alike;
  B: non-commercial), levels, and the MEI Verovio made from each notation piece (bars numbered in order, a
  pickup 0, so the numbers printed are the ones Prima says). A PDF keeps its file, its systems found on each page with their
  bars, its metre and tempo, and where you were, and once its notes are read and kept, their MEI as its notation. A notation
  piece keeps where it was left too: its page, the bar it was to start from, its loop's last bar, and the first bar on the
  screen. Imported
  files, and a local collection's PDFs and MIDI, sit beside the data file. Prima's own pieces kept out of the Library (all of
  them after Remove library, and each one removed by hand, by its id) are kept as settings, so a copy and a restore carry them,
  until the Library puts them back; so are the collection's pieces removed by hand, by id, until they're let back in. The
  repertoire is a track of its own (`repertoire`): the pieces kept, by id, each with since when, and the collections, each
  with its name and its pieces in the order they were put in. An uploaded piece's arranger is kept as its work ("arr. …"). What
  Undo puts back (the piece's rows and files as they were) is held in memory only, until the Library is left.
- **Readings**: a PDF's reading by Claude, waiting to be reviewed or kept: each system's step and note, its reading as Claude
  gave it, the doubts by bar (in the piece and in its system), the MEI, the pages it covered, what it cost and how long it took
  (Read again's included), and when it was kept.
- **Excerpts**: each piece's 2-, 4- and 8-bar stretches with their levels, how often each was read, and when last.
- **Exercises**: each one's runs are attempts under its id, with the key and hands and, for a family, what was chosen of its
  variations (a Done away from the keys is one too); and the key, hands, tempo and variations each was last played in. An
  exercise that became a variation of a family (thumb under four fingers) is still opened by its
  own id, and what's played that way stays under it.
- **Streak**: the days practiced and the freezes used.
- **Calls to Claude**: each one's time, the piece it was for, the task and model, how it went, what was asked and answered (no
  images, and never the key), and the tokens and cost, so Settings can show the month's cost.
- **Routines**: each request (what was asked, the evidence it was asked with, the piece's edition, where it is, what came back);
  each routine accepted, kept as accepted, never changed (an adjustment is a routine of its own); each routine of yours, the
  same shape (its activities exercises and drills by id, each with its minutes, under the provider "own"), changed in place as
  you change it, and which of those that come with Prima have been put in, a track ("routine-standard"), so each is put in once;
  which one is followed, a setting; the three starred, a track ("routine-stars"); and each routine's day, one
  per routine and date, with each activity's state, time and runs, which a routine of yours changed keeps for the activities
  still in it.
- **Problems** aren't in the data file: they're `errors.log` beside `claude-key.txt`, plain text, an entry a problem (`===`, the
  time, how bad and what said it, then what it said and the rest), so they're there even when the data file isn't.
- **Copies** of all of it but Claude's key, through the Kit's sync (Kit/Playground.Sync): the data file (`prima.db`, snapshotted
  with SQLite's backup while Prima has it open), the music imported (`library`), what reading a PDF made (`prima.conversion`), and
  the problem log (`errors.log`, `errors.old.log`: copied and exported, but never put back by a restore, which leaves the
  tablet's own as it is). They're in Documents/Prima backups, in a folder for each install named for when
  Prima was installed: Android lets an app read there only what the same install wrote, so a reinstalled Prima opens the copies of
  the one before with the folder picker. A copy is a manifest (when, which version, why, each table's rows, each file's size, time
  and SHA-256) and each file's content under its fingerprint, so only what changed is written; once a week, everything the folder
  holds is read again against its fingerprints, and what no longer matches is written again from the tablet's files (and logged,
  in Problems). The three newest copies are kept;
  besides them, the newest at least a week old, and the oldest younger than that, which comes to be it; and every one held, with
  the content they need. The music imported is kept once for all of them, so each copy costs about a copy of the data file.
  One is taken after practice (a piece's session finished, a routine's activity done) and as Prima starts, when the last is more
  than a day old, but never while a run is being played; before each update, restore, Start over, Remove library and Reset
  settings; and when asked. The
  first start after an update checks at once, before any screen can change anything, that the data file opens cleanly and has at
  least the rows of what's the owner's own (the settings, tracks, runs, practice days and sessions, routines, and the pieces
  imported; not the bundled or collection pieces and their excerpts, which Prima takes out and puts back itself), and that every
  file kept beside it (the music imported, what reading a PDF made) is there. If not, the copy from before is held (kept beside
  the newer ones, and beside any held already, until restored or let go); if it can't be held, Settings says so and no copy takes
  its place until the next start checks again. The copy after the check waits fifteen seconds. A restore, Start over, Remove
  library and Reset settings each hold a copy of what was there just before them too; `held.json` lists every copy held, with
  what it was kept from before and when it was held. Claude's key never goes in one: it's
  in secure storage, and every file a copy reads is searched for it, the copy stopping before it writes anything if it's found.
  A file unchanged since the last copy isn't read again while the key is the same; when it changes, every file is read and
  searched afresh. What was last searched for is kept as a mark beside the data file (`prima.searched`, a keyed hash that gives
  nothing of the key back), never in a copy.
  The look's id in the tablet's preferences isn't copied (it's the data file's Look setting again), nor whether Prima has asked to
  show notifications (the install's own).

## How it's built

Like the workout app: .NET 10 MAUI Blazor, with the logic in a plain library tested with xUnit. [plan.md](plan.md) has the
details.

- **Core**: the music model, the passage generator, the note-review schedule, scoring (matching played notes to the score,
  timing, evenness), levels and progress. No UI, all tested.
- **UI**: the Razor screens.
- **Tablet**: the Android app: USB MIDI in and out through Android's MIDI API, the click through `AudioTrack`, and the tablet's
  screen kept on while Prima is open (every screen is read from the music stand; another app sleeps as usual). Should Android
  close Prima while it's out of sight (it does, to free memory), coming back opens the screen it was on rather than Today, started
  afresh: a drill begins a new set.
- **Preview**: the UI as a Blazor Server page against sample data, for checking screens from the cloud, as `CLAUDE.md`
  describes. Its stand-in piano (`Host/StandInKeyboard.cs`, played by `StandInPlayer.cs`) is the one a build from the PC puts
  in front of the tablet's piano too: Settings › Keyboard › Stand-in piano turns it on there, and DevTools plays it
  (`StandInCalls.cs`), so every exercise and drill can be played on the tablet without the piano. A release has none of it.
- **Notation** is real sheet music, engraved by Verovio in the web view: black on paper, on the grand staff, the same in both
  looks, with the look around it and the cursor and marks drawn on top. A passage is engraved on the page's own thread as
  it's asked for; a whole piece by a second Verovio in a worker (`wwwroot/engraver.js`), a scroll's lines coming a part at a
  time, so the page goes on answering while a long piece is laid out ([Library](#library), "Opening a piece never holds the
  page"). Bars are as wide as their notes need, as in print, and
  each line is spread to the paper's width. A whole piece's lines break where its bars fit, so a busy bar is never squeezed to
  share a line, and a short last line keeps its bars' own widths; a short passage's bars are shared evenly between its lines.
  In the music Prima writes itself (passages, drills, exercises, a piece from a MIDI file, notes read from a PDF), notes
  shorter than a quarter are joined under a beam within the beat, a dotted eighth with its sixteenth as well as plain eighths
  and sixteenths; a bundled piece keeps its own engraving's beams. The brace joining the staves stands as far in from the
  paper's left edge as the last bar line does from its right. A sheet that prints fingering (the technique drills, the
  exercises) keeps a deeper top margin, and a mark's words go above the fingering, so the names of a wrong key and the note it
  was played for sit over their own note without covering the finger numbers.
- **Words in the music** (a song's lyrics, directions, the tempo, fingering) are set in Liberation Serif, the face Verovio
  measures them by and carries with it, so they're drawn as wide as it spaced them for on any device (Android has no Times,
  and its own serif is wider and taller: a song's syllables ran into each other there). The page takes the face from Verovio
  once and each drawing leaves it out. A song's lyrics go under the notes they're sung on, a little smaller than Verovio
  prints them by itself (3.8 half-spaces against its 4.5, about a book's 9.5 point at the printed size; Verovio sets all of a
  piece's words at its lyrics' size, so a song's tempo, directions and part name come down with them), and Verovio spaces the
  notes so the syllables on a line never run together. Each verse and each voice's lyrics have a line of their own: two
  voices' lyrics numbered alike, or verses an exporter names (`part1verse1`) that Verovio reads as one, would otherwise share
  a line and print over each other. The same words in two voices on a staff print once, the first voice's: where a voice
  sings a verse's syllables in a bar as the one before it does, in its own rhythm or not (a hymn's alto under its soprano),
  they aren't printed again; its other words, or the same a bar later, keep a line of their own. A song's lines typed as text
  over or under the music (directions: four or more of three words or more, one every four bars at least) go on a line of their
  own under the notes they start at, each as one syllable on its note, which widens its bar to hold it, since as directions they
  piled up and crushed their bars; a whole verse typed as text (a direction longer than 60 characters, in any piece) is left
  off, with Lyrics on or off. A player's direction made of music's own words, as they're written in Italian and cut short ("D.C.
  al Fine", "poco a poco cresc.", "Allegro ma non troppo", "dim. e rit.", or a "ri - tar - dan - do" spaced out over its bars,
  however long), is never a line of a song: it doesn't count towards a piece being one, isn't moved under the notes, and stays
  with Lyrics off. One in French or German ("un peu en dehors") is read as any other words. Where a line's words still run
  together (less than a pixel apart, measured as drawn), they get more room before they get smaller: Verovio fills each line to
  the brim with bars, and justifying a line a little too full squeezes its words together at any size (a smaller size only lets
  more bars onto the line), so a passage's bars first break where they fit, then the room between words is doubled and then
  tripled (2.4 and 3.6 half-spaces against 1.2), which makes the busy bars wider and fewer a line. Only a bar too wide for the
  line on its own (a very busy song, the tablet upright) makes the words smaller, to 3.3, 2.9 and at the least 2.5, with twice
  the room: small words read better than words run together. This order was tuned with the owner's Numb and stays as it is. A
  song whose words fit is drawn as it was.
  **Lyrics** off (Settings, or a song's More) prints the music alone. Prima's own words under notes (the glossary's note
  names and counts) are printed as written.
- **The look** is Porcelain and Night, chosen on 28 November 2026 from the mock-ups on
  [Prima Looks](https://claude.ai/artifact/2z4qusxAzqdt3VmxzL2ygx) (round two's A4 and A6), laid over the base layer first drawn
  for the [Studio & Vinyl design system](https://claude.ai/artifact/XtgAHfVyw3L94e5DKrj2ii) (both its looks gone the same day):
  Prima's own looks, not the Kit's Logbook and Telemetry, switched in Settings. Each look is
  data-theme on the page (Porcelain's accent data-accent); Porcelain's and Night's rules are at prima.css's end, under
  `html[data-theme^="porcelain"]`, their colours tokens on each one's own data-theme. The fonts (Instrument Serif and
  Instrument Sans) ship with the app as woff2 files, so it works offline; Verovio
  brings its own music fonts.

## Open questions

Only the devices can settle:

- How much timing jitter the keyboard and Android's MIDI path add.
- Where the click sounds (the tablet's speaker, or the piano as a USB audio device) and its delay, and which of the click's sounds
  on the piano the P-225 plays as they're meant (its drums on channel 10, its audio over USB).
- Whether the piano plays the notes Prima sends it, and whether it echoes them back.

Being researched: the Library's public-domain sources, importing PDFs, and page turns (`library.md`).

Partly built from its research: the Exercises tab has a first set of technique exercises as fixed patterns, and the ten
families from a lesson; the landing measures and the adapting steps that `exercises.md` proposes for chord changes and jumps
aren't built. For the owner to settle from the lesson, by ear: the rhythm of the wrist bounce and the lengths in the trill
bursts, which couldn't be read from the video (`exercises/the_ten_daily_exercises.md`).

Open in the research: how best to step difficulty up, whether read-ahead and rhythm-only drills carry over to full reading in
adults, whether spaced note review helps staff reading itself, and whether streaks help adult music learners keep going.
