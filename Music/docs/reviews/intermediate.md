# Prima, used for a month by an intermediate player learning pieces

One of four reviews. This one sits in the owner's own seat: hands together at an early-intermediate level, 30 minutes a day, two
pieces to get up to tempo. Everything was done in the preview host (the real screens, a simulated piano) at `main`, two changes
after 1.0.4, in the Studio look at 1280 × 800 unless it says otherwise. Each finding says whether I **saw** it in the preview,
**inferred** it from the code, or it needs **the tablet** to confirm. Nothing in the app was changed.

## 1. In short

I picked Gurlitt's Sonatina (16 bars, level 5) and Petzold's Minuet in G (32 bars), read them in Wait for me, played them In
time, looped the hard bars, and came back to them on later "days".

The three things that most need changing:

1. **The app forgets every pass.** A loop of ten passes comes back as one lump ("196 of 234 right"), a new run wipes the last
   one's summary, and nothing anywhere (the piece, the Library, Today, You) says what I did on a piece yesterday or whether I'm
   better. The runs are stored, 17 of them from my month, but no screen reads them.
2. **After a run the review is ten pixels under the bottom of the screen.** In the normal view the summary starts at 810 px on
   an 800 px screen, so the screen looks as if nothing came back but one figure in the bar. It is a layout slip more than a
   design question, and the fix is small. It does not need a separate summary screen.
3. **Nothing leads you through learning a piece.** There's no target tempo, no sections, no "next: bars 9–12, left hand", and
   Today's twenty minutes never mention a piece. The tools are all here (loops, hands, Wait for me, the tempo, Listen); the
   order to use them in is left to someone who, in the owner's words, doesn't know enough to know.

What's already right: the marks on the music, the bar strip and loops, Wait for me, the four short lines at the top of the
summary with **Loop bars** on them, and Sight-read's rhythm of play, summary, next. More in section 3.

One bug to fix before any of that: **with no pieces stored yet, the Library cannot open** (problem 1 below).

## 2. My month

The clock can't be moved, so the weeks are the seeds plus the runs I played.

**Week 1: finding a piece and reading it.** `seed/today`. Library › Near my level gave 20 pieces, 19 of them Czerny PDFs that
can't be played along with, and one I could use: the Sonatina. I found the Minuet under Everything, where it sits at level 8
among four copies of itself. Sonatina, Wait for me, all 95 notes with six wrong keys on the way: 89 of 95 right first time, 19
seconds. The wrong keys stay on the music in red beside the note, which is exactly what I wanted. Then In time at 60: 79%.

**Week 2: the hard bars.** The summary said bar 15 had the most to fix and offered **Loop bars 13 to 16**. I looped them at 120
for ten passes without stopping, then stopped. Then ten more passes, stopping after each, getting better each time (52, 57, 61,
65, 70, 74, 74, 74, 87, 96% in time). Then the right hand alone with the piano playing the left (Options › Hands › Right, Play
along › The other hand): 66 of 69.

**Week 3: the Minuet.** Opened it, played it through In time at 72: 340 of 408 right, and a summary two screens long. Looped
bars 5 to 8 from the summary's key.

**Week 4: playing through.** The Sonatina in full screen at 120 as "just the sheet music", stopped at bar 9. The same in
portrait (800 × 1280). Then came back to both pieces, the Library, Today and You to see what was left of the month.

**Each day's warm-up.** Today's plan from `seed/today` (Technique and Notes done, Rhythm next), one Sight-read passage, the
play bar's tempo.

## 3. What works

Leave these alone.

- **The marks on the music.** Green, the red key beside the grey note with both named, hollow for missed. After a run the
  music itself is the best review there is. Keep it on screen in any new arrangement.
- **The bar strip and loops.** A bar, **Loop**, the end bar: three taps, and the Play key says "Loop bars 13–16". The
  summary's **Loop bars 13 to 16** does it in one.
- **The top of the summary.** Notes, Timing, Look at, each a line with its bars. This is the right size for "after every
  pass". Everything under **Show details** is the right size for "once, at the end".
- **Wait for me** for a first reading, and the hand named on the Play key ("Play the right hand").
- **Sight-read's flow.** A short passage, the summary on the same screen (it fits: it starts at 393 px), C♯ for the next. The
  piece screen should feel like this.
- **Speed, in the preview.** Nothing I did took over 0.2 s (table in the appendix). The owner's slowdowns are not visible here,
  so they belong to the tablet.

## 4. Problems, the most painful first

### 1. Bug: the Library can't open while it's empty

*Saw it.* A fresh preview data file, `seed/today`, then `library`: an error page, every time, and the collection is never
stored because the page fails before it starts storing. The cause is in `PrimaStore.Library.cs`: the list's query has a
worked-out column (`… AS HasNotes`, added on 8 November with reading a PDF's notes), and with no rows the database can't say
what type it is, so the read fails. I got past it by putting one row in the preview's data file by hand.

The owner's tablet already has its pieces, so it never shows there. A new install would hit it on the first visit to the
Library. *Very sure of the cause; that the tablet behaves the same is inferred* (it runs the same code). Fix: read that column
as a number (`CAST(… AS INTEGER)`), or don't list before storing.

### 2. Gap: a loop's passes aren't counted, compared or kept apart

*Saw it; the storage part read from the code.* This is the owner's "you played this part 70 times, and you improved this much",
and today the app can say neither half.

- A loop is one long run (the bars, fifty times over, until Stop). While it plays the bar shows only "Bar 15": no pass number.
- After ten passes: "196 of 234 right: 34 wrong and 4 missed, most in bars 13, 14 and 15" and "84%". One total. The bar map
  is headed "both times through" and stacks ten passes' dots in each bar until they run off the top of the lane
  ([picture 3](images/intermediate-3.png)). The music shows the last pass's marks only.
- Stopping after each pass instead: each summary replaced the one before. Going from 52% to 96% over ten passes, the screen
  never once said "better than last time".
- What is stored for each run (`Finished` in `Piece.razor`): the time, the piece, In time or Wait for me, the tempo, the hand,
  the bar it started from, whether it was stopped, the totals (right, wrong, missed, held), and every key pressed with its
  time. **Not** stored: where the loop ended, how many passes, anything bar by bar.
- No screen reads these runs. Not the piece, not the Library's row, not Today, not You (which has Reading, Notes, Rhythm,
  Technique, Habit, and no pieces). The minutes do reach the week's total.

To see it: Sonatina, tap bar 13 on the strip, **Loop**, tap bar 16, tempo 120, Play, then
`/keys/perform?right=0.85&spread=40&stop=41&live=true`, wait 82 seconds, **Stop**. (Without `live=true` the preview ends the
run as if all fifty passes were due, and shows 920 missed: a quirk of the preview, not the app.)

What I'd change: score each pass as it ends, show it in the bar, and keep it. See sections 5 and 6.

### 3. Friction: the review is just off the screen

*Saw it.* After a run the music grows to show the whole piece and the summary starts at **810 px on an 800 px screen**
(1228 on 1195 in portrait). Nothing of it shows, and nothing says it's there except one figure in the bar
([picture 1](images/intermediate-1.png)). The owner's "I have to scroll down below the piece".

In **full screen** it's the other way round: the music keeps to its frame and the short summary sits under it, on screen
([picture 5](images/intermediate-5.png)). So the app already has the better arrangement, in the one place the owner thought
had no "how did I do".

The summary's **Loop bars** key then leaves you scrolled down with the Play key off the top of the screen. The piano's C♯
still starts it, which saved me.

What I'd change: after a run keep the piece fitted to the screen as full screen does, with the short lines pinned under the
play bar, above the music, where the eyes already are. *Sure.*

### 4. Bug: the bar you stop in is blamed

*Saw it, twice.* Stop counts every note due up to that instant. Stopping a loop about a second into its eleventh pass gave
"Stopped at bar 13 … Bar 13 had the most to fix: 9 wrong · 4 missed", and bar 13 drawn as missed over the last good pass
([picture 2](images/intermediate-2.png)). Stopping a play-through at bar 9 gave "Bar 9 had the most to fix: 6 missed" and
offered **Loop bar 9**. On the tablet it will be worse: hands off the keys, reach, tap, with the click still running.
*Inferred for the tablet, sure of the cause.* Fix: on Stop, leave out everything after the last key played (or the bar in
progress).

### 5. Gap: nothing says what to do next on a piece

*Saw it.* A piece has no tempo to aim for (every piece opens at 72), no sections, no record of which bars are learned. Reopened
the next day it keeps the tempo, the hand and the view, and nothing else: no loop, no last result, no "you left off at bars
13–16". The Library lists it exactly as before. Today's plan is Technique, Notes, Rhythm, Pause, Sight-reading and One spot,
and One spot is a bar from today's sight-reading, though the concept says "or a piece from the Library". For someone whose
month is two pieces, the plan spends 21 minutes elsewhere and then stops.

### 6. Friction: the full summary is too much, too soon

*Saw it.* The Minuet's first play-through came back with six lines, then three panels, then four red lines of expression (24
staccato notes held long, 19 gaps under slurs, 10 crescendos that didn't change, no change at 6 dynamics), then 32 bars nearly
all red ([picture 4](images/intermediate-4.png)). All true, and none of it useful while I'm still finding the notes. The parts
arrive over about four seconds each time.

Two smaller things in it:

- **"In time 79%" is really the notes.** The headline counts right notes that were also in time, so with 75 of 95 right and
  steady timing, both panels say 79% and the bar shows it in red beside "Steady: nothing more than a tenth of a beat out". I
  read it as bad timing. Show timing as a share of the right notes, or call the headline what it is.
- **The marks change their mind.** While playing with a small early lean, about a third of the notes turned amber with an
  "early" arrow; after the run they were all green, because the lean is taken out afterwards. The concept says so, but at the
  piano it reads as "you were early… no you weren't".

### 7. Friction: finding a piece to learn

*Saw it.* Near my level uses the sight-reading level (4), so it offered 19 PDFs and one playable piece
([picture 6](images/intermediate-6.png)). The filters are one at a time, so "near my level" and "to play along" can't be
combined. The Minuet in G, a first-year piece everywhere, is "level 8, most at 6", and there are four copies of it plus a
"Sample tune - abcm2ps (voices.abc)". A piece to learn should sit a level or two above what you read at sight, and the list
should lead with the pieces you're working on.

### 8. Friction: smaller things

- **Hands** are three taps away in Options, and it's the control I changed most. It belongs on the bar beside the mode.
- **Minutes** count only while a run plays, not the time between runs. My first 17 runs on the Sonatina added under four
  minutes to the day (9 to 12.8).
- **Wait for me's summary** names no bars and offers no loop, though the marks show where the slips were.
- **Today says "125 of 140 minutes this week", You says "this week so far: 52".** Two different weeks, I think. *Saw it; not
  my ground.*
- **Possible hitch, tablet only.** Starting a loop took 103 ms in the preview against 11 ms for a plain run, and re-choosing a
  loop's end 49 ms: each builds the loop fifty times over. On the tablet's processor that could be a noticeable pause when
  dragging a loop's handle or pressing Play. *Inferred; needs the tablet.*

## 5. How I'd arrange it

### What to show when

| | After every pass or run | At the end of a session | Drop, or keep behind Details |
| --- | --- | --- | --- |
| Marks on the music | Yes | The last run's | |
| Notes right, as "22 of 23" | Yes, with ↑ or ↓ against the pass before | First, best and last for each stretch | |
| Timing, as a share of the right notes | Yes, one figure | The lean, and where you rushed | The histogram |
| The bar to look at | Yes | The bars that stayed hard all session | |
| Pass number, and clean passes in a row | Yes | Passes for each stretch | |
| Held too short | Only once the notes are 90% right | Yes | The histogram |
| Loud and soft, touch, pedal | No | Only once the notes are 90% right | The four-second arrival |
| Bar by bar map | No | Yes, over the whole session | "Extra keys", "lost on the keyboard" |
| Minutes, tempo reached, against last time | No | Yes | |

### Three arrangements

**A. One screen, the result on top.** As now, but after a run the piece stays fitted and one line sits under the play bar:
`Pass 6 · 22 of 23 ↑ · timing 96% · look at bar 15 · [Details]`. Details opens today's full summary over the music. In a loop
the line updates as each pass ends, without stopping.

- For: smallest change; the music and its marks never leave; fixes problem 3 outright and half of problem 2.
- Against: still no memory beyond the screen, and no end.

**B. Play mode, then a review screen.** Play takes the whole screen (music, Stop, bar number). Stop or the last bar leads to a
Review screen: the marked music small, the summary beside it, and **Again**, **Loop the worst bars**, **Done**.

- For: clean; reads like "performance, then feedback"; right for a play-through.
- Against: wrong for learning. Ten passes of a loop is ten trips to another screen, or a loop that never reviews. It takes the
  marked music, the best feedback, off the page or shrinks it. And the summary doesn't need a screen: its useful part is four
  lines.

**C. A practice session.** A piece opens on a small card: where you left off, and **Practise**. That starts a session with a
clock. Inside it the screen is arrangement A. **End** (or leaving the piece) shows the session's review, once:

```
Sonatina · 24 min · Wed 11 Nov
Bars 13–16   hands together   32 passes   first 12/23 → best 23/23 → last 22/23   tempo 60 → 84
Bars 9–12    right hand       11 passes   first 15/17 → last 17/17                tempo 72
Whole piece  In time at 72    2 runs      75/95 → 88/95
Still hard: bar 15 (wrong notes in 9 passes of 32).  Against Monday: bars 13–16 were 60, now 84.
Next time: bars 13–16 at 92, then join 9–16.
```

- For: answers "how long, how many times, how much better" directly; gives a place for details that only make sense once
  (expression, the bar map); and "Next time" is where the instructor lives.
- Against: the most to build, and it needs passes kept (below).

**I recommend C, built on A.** A is the first step and worth shipping alone. B I would not build: use full screen as the play
mode it already is.

### The app as instructor

A piece moves through five stages, and the card on the piece (and a step on Today) names the next one:

1. **Read**: Wait for me, each hand, then both. Done when a read-through is 90% right first time.
2. **Sections**: the piece cut into four-bar phrases (the Library already cuts every piece into 4- and 8-bar excerpts at
   phrase starts, so the sections exist). Each looped hands apart, then together, at about 60% of the target tempo. A section
   is done after three clean passes in a row.
3. **Tempo**: each section up the metronome's steps the tempo control already has. Three clean passes, up a step; two rough
   ones, down a step.
4. **Join**: overlapping stretches (1–8, 5–12, 9–16) so the seams get played.
5. **Through**: In time, start to end, at the target, in full screen. This is where expression starts to count.

The owner can always ignore it and tap a bar. The rules are simple ones a teacher would give, not a finding; the teacher's
review should check them.

**From what's stored now** the app could already show: when a piece was last played, how many runs and on how many days, the
last and best whole-piece result at each tempo, the fastest clean tempo, and a "Working on" list in the Library. **It would
have to keep anew**: a loop's last bar and each pass's result, each bar's count for each run, the session (start, end, the
piece), a target tempo for each piece, and each section's stage. Old runs can't be split into passes reliably: the loop's end
wasn't kept, and the keys' times are kept from the first key and not from the beat.

### From sitting down to getting up (30 minutes)

```
Today ─► Warm-up, 8 min ─► Piece session, 20 min ─► The day's line
         a scale, the notes   card: "bars 13–16 at 84"     minutes, what moved
         due, 2 passages      play · line per pass · End
```

Today offers two plans: the reading day it has now, and a piece day (a short warm-up, then the session). Sight-read, the scale
and the notes earn their minutes as a warm-up for me; the Rhythm drill and the full five passages I'd skip on a piece day.
Move Hands onto the play bar. Put "Working on" at the top of the Library. Give You a Pieces part: each piece's stage, tempo
against target, and minutes.

## 6. The five changes I'd make first

1. **Fix the Library on an empty data file** (problem 1). Minutes of work, and a new install is broken without it.
2. **Keep the piece fitted after a run, with the result line under the play bar** (arrangement A), and stop blaming the bar you
   stopped in.
3. **Count and score each pass of a loop**: the number and the line while it plays, each pass kept with the loop's bars.
4. **Show a piece's history**: "last time" on the piece, "Working on" in the Library, Pieces on You. The first version needs
   only what's already stored.
5. **The practice session and its review**, with a target tempo and the five stages, and a piece step on Today.

How sure: 1 to 3 very (seen, and the causes read in the code). 4 sure it's wanted; the exact figures are a choice. 5 is a
design, and the stage rules need the teacher's eye and a week of the owner's own use.

## Appendix

### Times measured in the preview

All on this machine, where the app's code runs far faster than on the tablet. None is a hitch here.

| What | Time |
| --- | --- |
| Library list, 283 pieces, after its first storing | under 2.5 s to full list |
| Open the Sonatina (16 bars) from the Library, to music and strip | 95 ms |
| Open the Minuet (32 bars) | 44 ms |
| Open Kinderszenen (339 bars) | 127 ms to music, 148 ms to strip |
| Play pressed to count-in, plain run | 11 ms |
| Play pressed to count-in, a 31-bar loop | 103 ms |
| Changing a loop's end bar (bars 10–40) | 49 ms |
| Last note to summary, Minuet (408 notes) | 111 ms |
| Stop to summary, loop | 165 ms |
| Show details: until all its parts are in | about 4 s, by design |

### What a run keeps

From `Finished` in `Pages/Piece.razor` and `Storage/PrimaStore.Practice.cs`, one row for each run:

- When, the day, "piece", "in time" or "wait", the piece, its level, the tempo (In time only).
- Every key: its note and its time in ms from the first key. (The click's beats aren't kept for pieces.)
- In time: in-time share, right, wrong, missed, extra, kept going, holds judged, let go early, the bar it started from,
  stopped or not, the bar it stopped at, the hand.
- Wait for me: right first time, notes, wrong keys, chords not together, seconds, and the same last four.
- The headline (right ÷ expected) and whether it was clean.
- Minutes are added to the day from Play to the end of the run.

Not kept: the loop's last bar, the passes, anything bar by bar, play along on or off, the session.

### The ten passes

Sonatina, bars 13–16, both hands, In time at 120, each pass stopped at its end. The bar's figure after each:
52, 57, 61, 65, 70, 74, 74, 74, 87, 96%. Notes right: 17 of 23 for the first eight, then 20, then 22. Each summary named only
its own pass.

### Pictures

1. [After a run](images/intermediate-1.png): the whole screen, and no review on it.
2. [After ten passes of a loop](images/intermediate-2.png): one figure, the last pass's marks, bar 13 marked missed by Stop.
3. [Ten passes in "both times through"](images/intermediate-3.png).
4. [The Minuet's details](images/intermediate-4.png) after a first play-through.
5. [Full screen after a run](images/intermediate-5.png): the short summary fits.
6. [Near my level](images/intermediate-6.png): PDFs.
