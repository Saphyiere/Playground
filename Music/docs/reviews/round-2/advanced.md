# Prima, second round: an advanced player's month, with the hitches measured

*Written 12 November 2026, on `main` as of Practice and Play ("Prima: Practice and Play on a piece"). Everything here was played
in the preview host with its simulated piano. The preview is not the tablet: here the app's .NET code runs on a fast machine and
reaches the browser over a socket, while on the tablet it all runs inside Android's WebView on a much slower processor. To stand
in for the tablet I slowed the browser's processor four and six times with the DevTools setting for that. Each finding says
whether I **saw** it in the preview, **inferred** it from the code, or whether only **the tablet** can confirm it.*

## 1. In short

I played the advanced seat: a strong amateur (about grade 7–8) who wants Prima mostly as clever sheet music. That means the
big pieces full screen, played through at tempo, the odd loop on a hard passage, and my own scores imported. Over the month I
lived in the eight longest pieces in the Library, from Träumerei (24 bars) to Chopin's Op. 28 No. 17 (90 bars and 2,501 notes,
which the app draws with 20,000 shapes). I also sight-read at the top level, ran drills at speed and imported a score I wrote.

**What most needs changing:**

1. **Every change of screen draws the whole piece again, and you wait for it.** Finish practice, In-depth review, Done, Play,
   turning the tablet and opening a piece each engrave the whole piece from scratch, all in one go, with the app frozen until
   it's done. On Op. 45 that is about 1 s each on this machine and **6–8 s each at a sixth of the speed** (Finish practice → look
   back: 8.1 s; In-depth review: 7.8 s; Done: 6.3 s; Play: 6.0 s). I'm sure of the cause: the profiler puts all of it in one
   function. Three of the four new screens hit it. This is very likely most of "slowdowns that happen, but it's hard to pin
   down": they come at the moments you move between screens, not while you play.
2. **In Practice, the music gets less than half the screen in landscape, and after a run about a third.** The frame is 430 px of
   the tablet's 800 before a run, and 258 px once the summary is up (Standard). In the Raindrop Prelude that is **one staff**. For
   someone who reads the music while playing, it's the biggest thing in the way. Portrait with full screen is fine (714 px).
3. **Play is the right answer, but it stops one step short.** It's clean, big and full screen, and it judges correctly. But its
   quick review says "look at bar 74" with no way to go there. And Play is In time to a click, so for Chopin, Schumann or
   anything with rubato the timing it judges isn't the timing I meant.

**What's already right:** playing itself is quick. A key reaches its mark in 10–70 ms even at a sixth of the speed, in both
modes, at the start of a long piece and deep into it. Page turns, the Library filter, drills and Wait for me are all quick. The
judging of notes and timing on real-time playing is accurate: a note-perfect player with ±30 ms of human scatter gets every note
right in all eight pieces. The look back and the in-depth review are the screens I'd have asked for. And the round 1 hitches that
grew with the length of a piece while you played (marks repainting the music, the cursor) are gone.

## 2. My month

The month is the `months` seed plus the runs I played myself. The clock can't be moved, so the "weeks" are what I did, in
order.

**Week 1: the Library and the long pieces.** I opened the Library and filtered it ("chopin", then cleared), then opened each of
the eight long pieces from it and turned the tablet both ways on each, timing everything at full speed and at a quarter speed
(the table is in the appendix). Für Elise, Op. 45 and No. 17 opened in about a second here and in 2.8–5 s at a quarter speed.
Every turn of the tablet cost the same again. I started a run at the start and deep in (bar 84 of Für Elise) in both modes, and
played wrong keys into it to time key → mark.

**Week 2: practice sessions.** On Op. 45 I did what I'd really do. **Practice**, a run from bar 1 at tempo with marks as I play
(97% right, ±15 ms), Stop at bar 8, then the summary's **Loop bars 5 to 8**, played live round twice. Then the whole piece once
through, **Show details**, **Finish practice**, the look back, **In-depth review**, a bar tapped in it, and **Done**. I did the
whole session at full speed and again at a sixth of the speed, with the profiler on the slow moments. I did the same on No. 17
in the **Pages** view, turning pages before and during a run.

**Week 3: Play, and checking what's judged.** I played Op. 45 through in **Play**, at full speed and at a sixth, and read its
quick review. Then I checked the judge itself. A note-perfect player went through all eight pieces in a whole-piece run, with
timing scatter of 0, 15 and 30 ms. Then the same in real time from bar 1 and bar 5 of Op. 45, and a 90%-right player to see
whether wrong notes were named right. I loop-tested bars 5–8 and a one-bar loop (both counted and scored correctly).

**Week 4: my own score, sight-reading, drills.** I wrote a 16-bar study (sixteenths over chords) as a MIDI file, imported it
(**Import files**), and played it: it was written out cleanly, graded level 8, and a perfect run scored 352 of 352. Sight-read
at level 12 (C♯ major, 28 bars, hands together) at 92% right. The scales drill at a quarter speed. I spot-checked the look
(Studio) in portrait at 800 × 1280, with and without full screen.

A note on the preview, so nobody chases ghosts: three things looked like scoring bugs at first and weren't. Wrong notes were
named against the wrong note of a chord; the first pass of a loop scored 19%; played notes showed as missed in a loop. Each came
from the simulated piano (keys sent all at once rather than in time, keys from an earlier run still arriving, the simulator
stopping after its last pass). With real-time keys and a fresh host they all came out right. The owner won't meet them on the
tablet.

## 3. What works (leave it alone)

- **Key → mark is fast everywhere.** In time and Wait for me, bar 1 or bar 84, 9–26 ms at full speed and 31–71 ms at a sixth
  (seen). Round 1's main worry, the live marks repainting the whole piece on every note, is fixed: marks now go on a layer of
  their own.
- **Page turns** in the Pages view: 160–290 ms at a quarter speed on the heaviest piece (seen).
- **The judging of right notes and timing** on real-time playing. All eight pieces came out note-perfect for a note-perfect
  player, with up to ±30 ms of scatter. Wrong notes are named in pairs that make sense ("D♯4 → E4"). Loop passes are counted and
  scored correctly (89% then 96% for a 90% player).
- **Play's screen.** The music alone with a slim strip, the tempo first, then Start. At the end, every note marked and a
  one-line review (picture 2).
- **The look back** reads at a glance: stretches, passes, first → last, Still hard. **The in-depth review** on a long piece shows
  the 16 worst bars with a strip of the whole piece over them, and tapping a bar shows its music marked (31 ms).
- **Portrait with full screen in Practice** gives the music 714 of 1280 px with the summary under it. That's how I'd practise on
  this tablet (picture 4).
- **Importing a MIDI file of my own** worked first time (once my file was valid). It came out cleanly notated and correctly
  judged.
- **Memory** stays flat (11–15 MB). Leaving a piece takes the page back from 15,000–20,000 shapes to 1,800 (seen), so nothing
  piles up over a long session.

## 4. Problems, the most painful first

Each is marked a **bug** (broken), **friction** (works, but in the way) or a **gap** (missing). The times are in the appendix.

### 4.1 Bug: Finish practice, In-depth review, Done and Play each draw the whole piece again (6–8 s on a slow processor)

**What happens.** The music sits under the look back hidden, not removed, so that it needn't be drawn again. But the sheet
watches its own size (`Sheet.razor.js`, the `ResizeObserver` in `render`), and hidden means 0 px wide. So it lays the whole
piece out again, at its narrowest width (600 units), for nobody to see. Done shows it again at full width, so it's laid out
again. The in-depth review and Play go full screen, which is another width, and Back or Done leaves it, which is another. Each
is a full Verovio layout and drawing of every bar on the page's one thread, and nothing on the screen moves while it runs.

| Op. 45, 99 bars | full speed | ×6 slower |
| --- | --- | --- |
| Finish practice → look back | 1.26 s (one 1.17 s freeze) | **8.1 s** (one 7.9 s freeze) |
| In-depth review opens | 0.98 s | **7.8 s** |
| Done → front (shows, then freezes) | 0.83 s | **6.3 s** freeze |
| Play → its strip | 0.61 s freeze | **6.0 s** |

The profiler puts 8.11 of the 8.12 s in `draw` called from the size watcher (`Sheet.razor.js:125`). I'm sure of this: seen and
read in the code.

**How to see it in the preview.** `seed/months`, then open Op. 45 (`library/pdmx-QmfNseniSwST8r2CM6MKccHfJ4BB5rEu1MiZdmrP9kh2q2`),
tap Practice, start a run, `/keys/perform?right=0.97&spread=15`, then tap Finish practice. Watch the 1 s pause; slow the
processor six times in DevTools to feel the tablet.

**On the tablet.** On No. 17 at the tablet's speed I'd expect 4–10 s for each. Practice a bar of Op. 28 No. 17, then tap
Finish practice and count until Keep practising responds to a tap. Do the same with In-depth review, then Done. If each takes
seconds and the screen won't respond in between, it's this.

**What I'd change.** The sheet shouldn't draw itself again while it's hidden or about to be shown at the size it had. Either
skip the redraw when the width is 0 and redraw only if the width really changed when it comes back, or keep one drawing per
width and swap them. The review and Play could also keep the frame the width it had. This one change takes away four of the
slowest moments in the app.

### 4.2 Friction: opening a piece, and turning the tablet, freeze the app for seconds

**What happens.** A notation piece is drawn whole, as one long page of every bar, all at once, before anything else can happen.
Turning the tablet does it again from scratch for the new width. For the heavy pieces at a quarter speed:

| | opens | turned upright | turned back |
| --- | --- | --- | --- |
| No. 17 (20,145 shapes) | 5.0 s | 6.6 s | 6.6 s |
| Op. 45 (14,494) | 4.2 s | 4.1 s | 4.2 s |
| Weeping Willow (11,352) | 3.4 s | 3.1 s | 2.9 s |
| Für Elise (8,748) | 2.8 s | 2.5 s | 1.9 s |
| Träumerei (4,037) | 1.2 s | 1.5 s | 1.2 s |

The profile splits it roughly half Verovio laying the piece out (`load`) and half drawing and the browser taking in the
picture. Seen; sure.

**What I'd change.** Draw the first screenful first and the rest after, or move the engraving off the page's one thread (a
worker), so the screen stays alive while it works. Keep one layout per orientation, so turning back is free. This is the round 1
hitch "a big piece opening", still there. It's a fair cost once, but it's paid again at every turn and (4.1) at every new
screen.

### 4.3 Bug: starting a run in the Pages view draws the piece twice and can hold the count-in for up to 4 s

**What happens.** Runs always play in the scrolling view (`Piece.razor`, `Starting`). So in Pages, Start switches the layout to
one long scroll, which is a full new drawing (the sheet went from 1064 × 430 to 1064 × 1952 in 288 ms here). Then it waits for
the drawing, up to 4 s, before counting in. On No. 17 at a quarter speed the run start froze for 5.3 s and then 2.3 s. The
Pages view then stays in Scroll for the rest of the visit. Seen and read in the code; sure.

**How to see it.** Open No. 17 (`…UHM-17`), Practice, Options → Pages, close Options, tap Play.

**On the tablet.** In Pages, tap Play and listen. If the click starts seconds late, or not where you expect, it's this.

**What I'd change.** If runs must scroll, say so on the Pages button ("Pages: between runs"), or draw the scroll layout once in
the background when Pages is chosen. Better for this seat: let a run play in pages with page turns (the gap in 4.6).

### 4.4 Friction: in landscape, Practice leaves the music a third of the screen once the summary is up

**What happens.** On the 1280 × 800 screen, Practice gives the music a frame 430 px tall: the title (120 px), the play bar,
the bar strip and About take the rest. After a run, with Standard's summary under the music, the frame is **258 px**. In the
Raindrop Prelude that is one staff (picture 1). You can't read ahead or see where you are in the line. Full screen helps, but
the summary still takes its half. Seen; sure.

**What I'd change.** In landscape, put the summary beside the music (a column on the right) or make it a strip one line tall
that opens on a tap. Drop the big title in Practice: the piece's name is already on the play bar. The bar strip could fold into
the play bar. The goal is two systems of music at all times, three in full screen.

### 4.5 Friction: Play's quick review can't take you to the bar it names

**What happens.** At the end of a play-through: "97% right in time · 1314 of 1356 notes · look at bar 74", with **Again**,
**In-depth review** and **Done** (picture 2). To practise bar 74 you go Done → Practice → find 74 on the strip → Loop. That's four
steps, and the bar number has to be remembered on the way. Seen.

**What I'd change.** A fourth key, **Practise bar 74** (or **Loop 73–75**), that opens Practice on that bar with a loop set. The
in-depth review's bar cells could do the same. That's the bridge between "how did I do" and "now fix it", which is the point of
having both modes.

### 4.6 Gap: Play is a click-track run, not sheet music you play from

Judged from the seat the owner described: "pretend like it's the actual sheet music… then a screen after: how did I do?"

- **It does the second half well.** The quick review is fast (0.17 s, 0.95 s at a sixth). Every note is marked. The in-depth
  review is one tap away. It counts in About's "played through" and its best.
- **The first half isn't sheet music yet.** It's In time with a click from start to end, so it judges you against a metronome.
  Op. 45 is marked *Sostenuto*; Träumerei is all rubato. A good performance of either will come out as "early" and "late" all
  over. Following you without a click is on hold, and for this seat it's the one that matters most.
- **No page turns.** Play scrolls a line at a time, and the Pages view is not used in a run (4.3). A printed-music player expects
  to turn pages, by a pedal or the piano's top and bottom keys, which the app already supports outside runs.
- **No time on the review.** It knows the minutes; one more number on the line ("4:12") would answer "how long was I playing".
- Opening Play costs a full redraw (4.1): 6 s at a sixth of the speed, before the strip responds.

**What I'd change, in order:** the 4.1 fix; a "Practise bar N" key; the time on the line; page turns in Play; then, when the
instructor work comes back, Follow me (no click, judged on notes and evenness rather than a metronome).

### 4.7 Bug (probable): "let go early" on notes no hand can hold

**What happens.** A note-perfect simulated player who holds every key to 30 ms before its end still gets "let go early" in the
summary: Raindrop (No. 15) 26 notes, Op. 45 20, Tchaikovsky No. 8 8, Träumerei 7, Weeping Willow 4 (seen, with ±15 ms scatter;
picture 1, the Held line). The scorer judges a note without pedal by its own key's release (`PassageScorer.cs`, where `sounded`
is worked out). When the music strikes the same key again in another voice while the first is still written as held (Raindrop's
repeated A♭s under the melody), the first must be released, and it's counted short. The code's own comment ("unless the key was
struck again") only covers the pedalled case.

**How sure.** Medium. The simulated piano's handling of a key pressed again while down differs from a real one, and with the
pedal down (as Raindrop is played) the rule doesn't fire. **On the tablet:** play Raindrop bars 1–5 legato *without* pedal, note
right, and see whether Held says notes were let go early. If it does, the rule should excuse a note whose key is struck again
before its written end.

### 4.8 Friction: smaller stalls during a run, and while the details come in

At a sixth of the speed, a live run with marks on Op. 45 had about one stall a second of 50–280 ms (24 in 30 s, 2.2 s in all).
A loop had 22 of up to 140 ms. The profile puts most of it in the browser laying out and painting the 15,000-shape page (11.5 s
of "program" in 20 s), plus each mark's `getScreenCTM` and drawing (`tint` in `Sheet.razor.js`). Show details at a sixth: 29
stalls of up to 308 ms in the 4 s its panels take to come in. At full speed none of this shows. **On the tablet:** play No. 17
In time with Marks as you play on, then off (Settings). If the cursor glides with them off and stutters with them on, it's the
marks. Seen; fairly sure of the size, less of the share between marks and the page.

### 4.9 Small things

- **Friction: two Practice clocks tick, one hidden.** The strip is in the header and again in the play bar (for portrait); both
  run a one-second timer, so the session is saved every 15 s, not 30. It's harmless, but it's double work every second (seen:
  10 changes in 5 s, not 5).
- **Friction: an imported MIDI file's tempo is ignored.** My file was at 100; the piece opened at 72. Its title is the file's
  name ("etude3"). Seen.
- **Gap: sight-reading at the top level has a left hand of block chords only** (picture 3). The right hand is a fair level 12
  (seven sharps, trills, triplets), but at grade 7–8 the left hand needs moving lines: Alberti, broken chords, a bass that walks.
- **Gap (still): no fingering or marks of my own on the score**, and **no free tempo**. They're unchanged since round 1.
- **Not timed: the Library storing its pieces the first time.** In my host the 237 pieces were already stored, so it opened in
  1.2 s. The first store on a new data file wasn't something I could time.
- **Fine: the Library filter.** Typing waits 250 ms by design, then filters in about 30 ms. Clearing back to all 237 rows is a
  285 ms stall at a sixth of the speed: noticeable, not a problem.

## 5. How I'd arrange it for this player

What I do when I sit down: open a piece, play it through, fix two or three spots, play it through again, get up. Today that's
the front → Practice (or Play) → screens that each redraw the piece. I'd make the piece the one constant screen and let
everything else come and go over it:

```
Library ─▶ Piece (music full height, slim bar: Practice · Play · tempo · page)
              │
              ├─ Play ──▶ count-in ─▶ music only, page turns by pedal ─▶ end
              │                                                            │
              │          quick review strip (bottom, one line):            │
              │          "96% · 1314/1356 · 4:12 · bar 74"  [Practise bar 74] [Review] [Again] [Done]
              │                                                            │
              └─ Practice ◀──────────── Practise bar 74 (loop 73–75 set) ◀─┘
                    │  clocks in the bar, not the title
                    │  runs and loops; summary in a side column (landscape) or a one-line strip
                    └─ Finish practice ─▶ look back over the music (sheet kept as drawn) ─▶ Done
```

- **Move**: in landscape, the summary to a column beside the music; the clocks and Finish practice into the play bar; the bar
  strip into the play bar too, or hidden until tapped.
- **Merge**: Play's review and Practice's "look at bar N" into one key that opens Practice on that bar with a loop.
- **Cut**: the page title in Practice and Play (it's on the bar); About under the music while practising (it's for the front).
- **Keep**: the front with two keys; the look back; the in-depth review; Simple, Standard and Hands-on.
- **Never**: draw the music again just because a screen came over it.

## 6. The five changes I'd make first

1. **Stop the sheet drawing itself again when it's hidden or shown at the size it had** (4.1). That fixes the 6–8 s freezes on
   Finish practice, In-depth review, Done and Play, and costs nothing visible.
2. **Keep the page alive while a piece is engraved, and keep each orientation's layout** (4.2): draw the first screen first, or
   engrave in a worker.
3. **Give the music the screen in landscape Practice** (4.4): summary beside it, chrome cut, two systems always.
4. **"Practise bar N" from Play's quick review and the in-depth review** (4.5), and the time on Play's line.
5. **Make Pages honest** (4.3): either runs play in pages with page turns, or the layout for runs is ready before Start, so
   the count-in is never late.

Then, from the on-hold list, Follow me for Play: for this player it's what turns Play from a test into sheet music.

## 7. Since the first round

*(Written after the rest, having then read the first round's `../advanced.md`.)*

The first round's three big hitches are fixed: the frozen end of a run, the cursor making the browser go over the whole piece,
and pieces never let go of. What's left is the engraving, which round 1 ranked fourth and which the new screens now set off
far more often.

| Round 1 problem | Now | Round 1 timing | Now |
| --- | --- | --- | --- |
| 1. The end of a run freezes (every mark searched the page) | **Fixed** (seen) | No. 17 whole run → summary 3.3 s, ×4 17.5 s; Für Elise 0.34 s, ×4 2.3 s | Op. 45 whole run → summary 0.21 s, ×6 1.15 s |
| 2. The cursor keeps the browser busy; marks wait | **Fixed** as far as I measured: key → mark stays short deep in a piece. I didn't re-measure the busy share | Für Elise key → mark, Wait for me, ×6 67 / 65 ms | 40–52 ms ×6; In time 31–71 ms ×6 |
| 3. Pieces never let go of (memory leak) | **Fixed** (seen): 8 long pieces opened in turn, script memory flat at 11–15 MB, the Library back to 1,791 elements | +11 MB per visit to Für Elise | no growth |
| 4a. Opening and turning engrave the whole piece | **Still there**, and **slower in my runs**, by about 2× (seen, though on a different session's machine, so treat the ratio with care) | Für Elise open 0.25 s, ×4 1.1 s; No. 17 open 0.47 s, ×4 2.6 s; No. 17 turn 0.6 s, ×4 3.2 s | Für Elise 0.55 s frozen, ×4 2.3 s frozen; No. 17 0.8 s frozen, ×4 4.2 s frozen; No. 17 turn 1.0–1.15 s, ×4 6.6 s |
| 4b. A page turn in Pages lays out the whole piece | **Fixed** (seen): only the page is drawn | No. 17 0.4 s | No. 17 0.16–0.29 s at ×4 |
| 4c. Play from Pages switches to Scroll, for good | **Still there** (4.3), now with a wait of up to 4 s before the count-in | Kinderszenen 2.3 s frozen | No. 17 ×4: 5.3 s + 2.3 s frozen |
| 5. ‹ Bar restarts the run slowly | Not re-tested | 3.4 s | – |
| 6. Can't follow a real performance | **Partly fixed**: In time follows the piece's own tempo and metre changes, and a marked roll is a roll (from the log; my perfect runs through Op. 45's rolled chords came out right). **Follow me still missing**, and Play makes it matter more (4.6) | – | – |
| 7. After a play-through, one figure | **Fixed** by Play's quick review and Standard's summary on screen, and loops get a figure per pass. But in landscape Practice the summary now squeezes the music to 258 px (4.4) | – | – |
| 8. Doesn't remember where I was | **Fixed**: a piece opens where it was left; the Library has Working on. Fingering and my own marks: **still missing** | – | – |
| 9. "Tempo Infinity" on a scale | Fixed in the log; not re-tested | – | – |
| 10. The Library on an empty file; bare tempo numbers; two lines of music in the normal view | The first two **fixed**; the frame is still 430 px of 800 | 430 px | 430 px, 258 px after a run |

**New or worse since round 1:**

- **The new screens draw the piece again** (4.1): Finish practice, In-depth review, Done and Play cost 0.6–1.3 s each here and
  6–8 s at a sixth of the speed. This is new with Practice and Play, and it's now the biggest hitch in the app.
- **Opening and turning measured about twice as slow** as round 1's figures. It may be the machine; it's worth the owner
  timing No. 17 opening on the tablet against round 1's estimate.
- **"Let go early" on notes no hand can hold** (4.7) may well have been there before; round 1 didn't test with a note-perfect
  player.
- **Play's review names a bar it can't take you to** (4.5), and Play adds a second full-screen redraw on entering.

---

## Appendix A: timings

Measured in the preview's headless Chromium, 1280 × 800, Studio look, from the tap (a script's click) to the screen having
changed and painted, with long tasks (main-thread work over 50 ms) and frame gaps logged. "×4" and "×6" are the browser's
processor slowed four and six times. The tablet's own speed isn't known here; a mid-range Android tablet's WebView is usually
somewhere in that range of this machine. Key → mark times include the simulated piano's trip over HTTP.

**Opening and turning** (the Library tap until the music is drawn; the size change until the music is drawn again):

| Piece | bars drawn | shapes | open ×1 | open ×4 | upright / back ×1 | upright / back ×4 |
| --- | --- | --- | --- | --- | --- | --- |
| Für Elise | 106 | 8,748 | 0.80 s | 2.76 s (×6: ~5.1 s) | 0.45 / 0.38 s | 2.54 / 1.88 s (×6: 3.87 / 3.24) |
| Chopin Op. 45 | 99 | 14,494 | 0.96 s | 4.17 s (×6: 7.7 s) | 0.82 / 0.71 s | 4.14 / 4.23 s |
| Op. 28 No. 15 | 89 | 14,914 | 0.77 s | 3.33 s | 0.82 / 0.77 s | 3.86 / 3.77 s |
| Op. 28 No. 17 | 90 | 20,145 | 1.02 s | 4.99 s | 1.15 / 0.96 s | 6.57 / 6.60 s |
| Tchaikovsky No. 8 | 85 | 7,249 | 0.27 s | 1.84 s | 0.31 / 0.31 s | 1.70 / 1.53 s |
| Träumerei | 25 | 4,037 | 0.25 s | 1.23 s | 0.19 / 0.19 s | 1.47 / 1.20 s |
| The Entertainer | 78 | 7,221 | 0.39 s | 2.25 s | 0.37 / 0.31 s | 2.10 / 1.66 s |
| Weeping Willow | 88 | 11,352 | 0.47 s | 3.45 s | 0.72 / 0.67 s | 3.13 / 2.92 s |

Leaving a piece for the Library: 60–150 ms ×1, 200–870 ms ×4.

**A practice session on Op. 45** (×1 / ×6):

| Step | ×1 | ×6 |
| --- | --- | --- |
| Practice → session page | 54 ms | 402 ms |
| Idle 6 s on the page (clocks ticking) | no stalls | one 55 ms stall |
| Start a run (bar 1) | 68 ms | 742 ms |
| Live run 30 s, marks as you play | no stalls | 24 stalls, worst 282 ms, 2.2 s in all |
| Stop → summary | 261 ms | 680 ms |
| Start a loop (bars 5–8) | 42 ms | 594 ms |
| Loop, live, 30 s | no stalls | 22 stalls, worst 139 ms |
| Whole piece played → summary | 207 ms | 1.15 s (426 ms stall) |
| Show details → first panel | 67 ms | 482 ms; then 29 stalls (worst 308 ms) over 4 s |
| Finish practice → look back | 1.26 s | **8.12 s** |
| In-depth review opens | 0.98 s | **7.77 s** |
| Tap a bar in the review | 31 ms | 235 ms |
| Done → front | 0.83 s (787 ms stall) | 0.32 s, then a **6.27 s** stall |
| Play → its strip | 43 ms, then a 612 ms stall | **6.02 s** |
| Play: Start | 31 ms | 209 ms |
| Play: played through → quick review | 170 ms | 949 ms |
| Leave → Library | 89 ms | 725 ms |

**Key → mark**, Für Elise (a key sent to the simulated piano until its mark or the cursor changed on the page):

| | ×1 | ×6 |
| --- | --- | --- |
| In time, bars 1–3 | 9, 16, 26 ms | 31, 35, 68 ms |
| In time, from bar 84 | 11, 12, 15 ms | 34, 47, 71 ms |
| Wait for me, from bar 84 | 14, 15, 26 ms | 40, 51, 52 ms |
| Start → run going | 31–73 ms | 133–438 ms |
| Scales drill, ×4: answer → screen | | 23–44 ms |

**Pages view, No. 17, ×4**: switching to Pages 3.3–4.0 s (13 pages at 1280 × 800); a page turn 156–290 ms; starting a run in
Pages: freezes of 5.3 s and 2.3 s (two full drawings; 25 s of run had 8.6 s of stalls).

**Library**: filtering ("chopin") 279 ms ×1, 364 ms ×6, including its 250 ms wait for typing to stop; clearing back to 237
rows 327 ms ×1, 707 ms ×6 (a 285 ms stall); reopening ×6 300 ms.

**Memory**: the page holds 11–15 MB of script memory throughout; 8,800–20,400 elements with a piece open, back to 1,791 in the
Library afterwards; event listeners 22 → 33 over the visits.

## Appendix B: what the profiler says, slowest first

- **Finish practice ×6** (8.1 s): `draw` (Sheet.razor.js:143) 8,110 ms inclusive, of which `load` (Verovio's layout) 3,819 ms;
  called from the size watcher's callback (Sheet.razor.js:125), not from .NET. The same for the review, Done and Play.
- **Opening Für Elise ×6**: `render` → `draw` 3,398 ms, `load` 1,640 ms; the rest is Verovio drawing SVG and the browser
  parsing and laying it out.
- **Turning upright ×6**: `draw` from the size watcher, 2,927 ms; `load` 1,460 ms.
- **A run started in Pages ×4**: `render` → `draw` 5,456 ms (two drawings); `bars.js` measuring every bar's place (`drawn`)
  1,458 ms, of which `getBoundingClientRect` 1,462 ms self.
- **A live run ×6, 20 s**: 11.5 s of the browser's own layout and paint ("program"), `markLive` → `drawMark` 251 ms, `tint`
  163 ms, `getScreenCTM` 39 ms self. Script is small; the page's size is what costs.

## Appendix C: the suspects, ranked, and how the owner can check each on the tablet

| # | Suspect (where) | Evidence | Cost on the tablet (estimate) | How to check on the tablet |
| --- | --- | --- | --- | --- |
| 1 | The sheet drawn again when hidden or resized for another screen (`Sheet.razor.js`, the `ResizeObserver` in `render`; `Piece.razor`'s `piece-stage-gone` and full screen) | profiled ×6: 6–8 s each; seen ×1: ~1 s each | 4–10 s frozen at Finish practice, In-depth review, Done, Play, Back | Practise a bar of No. 17, tap Finish practice, then tap Keep practising straight away: count until it responds. The same with In-depth review and Done |
| 2 | Whole piece engraved at once on opening and on turning (`Sheet.razor.js` `draw`) | measured on 8 pieces ×1/×4 | 2–7 s per open and per turn on the long pieces | Open No. 17 and count; turn the tablet and count again |
| 3 | A run in Pages switches to Scroll and waits up to 4 s (`Piece.razor` `Starting`) | seen ×4: 5.3 s + 2.3 s | the count-in starts late, sometimes by seconds | Pages view, tap Play: does the click start at once? |
| 4 | Live marks and the size of the page during a run (`Sheet.razor.js` `tint`, the page's 15,000–20,000 shapes) | ×6: ~1 stall/s, up to 280 ms | the cursor and marks stutter in dense music | No. 17 In time, Marks as you play on, then off |
| 5 | Show details' staged panels (`PlaySummary`) | ×6: 29 stalls in 4 s | scrolling sticks for a few seconds after Show details | After a long run, Show details, then scroll at once |
| 6 | Two Practice clocks ticking (`PracticeStrip` twice in `Piece.razor`) | seen: 2 updates a second | small: a save every 15 s | none needed |

Not suspects (measured quick at ×6): key → mark in both modes, deep in a long piece; Wait for me; drills; page turns; the
Library filter; MIDI input (`AndroidKeyboard.cs` parses each message and hands it on, with no work that grows with the piece);
memory over a session.

## Pictures

1. `images/advanced-1.png`: the Raindrop Prelude in landscape Practice after a note-perfect run. One staff of music, the summary
   taking the rest, and "26 notes let go early".
2. `images/advanced-2.png`: Play's quick review on Op. 45: "look at bar 74", and no way there.
3. `images/advanced-3.png`: sight-reading at level 12: a fair right hand over block chords.
4. `images/advanced-4.png`: Practice upright with full screen. This is how it should feel.
