# Practice and Play: the plan

The owner's design, 12 November, put together from the four reviews and the mock-ups on the canvas
([Prima: Practise and Play through](https://claude.ai/artifact/BDoxbjCTPokP5ZWP1ZkfAo)): every screen there is used, and how
much the app says while you practise is yours to set. Nothing here is built yet. Where the owner didn't say, my picks are
marked *(pick)* and listed again at the end to confirm.

## The piece's front: Practice or Play

- A piece opens on its music, with About this piece under it (what's been done on it), and two big keys on its bar:
  **Practice** and **Play**. Nothing else is on the bar but the piece's level. The music scrolls, and a tap on it does
  nothing yet.
- Both are taps on the screen. The piano's buttons (the low C♯ and D♯) never press them, nor Finish practice, Keep
  practising or Done: starting and finishing are always deliberate. Inside Practice and Play the piano's go key still starts
  a run, as now, since your hands are on the keys by then.

## Practice

- **Practice** starts a practice session on the piece, and the page is the piece as it is now, with all its tools: loops,
  bars to start from, hands, In time and Wait for me, the tempo, Listen, Options, full screen.
- **A timer** at the top, beside the piece's name, counts the session from the tap. It pauses when you leave the piece or the
  tablet sleeps, and after five minutes with nothing played or tapped *(pick)*, saying Paused; it goes on when you come back
  or play.
- **Finish practice**, beside the timer, ends the session and shows the look back.
- A session you leave without finishing goes on when you come back to the piece that day; the next day it's finished by
  itself, as of the last thing you played, and its look back is in the piece's history *(pick)*.
- Today and You count practice time from the sessions (and Play's time), not only while a run plays.

## How much Practice tells you

In Settings › Practice, and on the piece's Options for a quick change. Three ways, each a set of the settings under it,
which you can change one by one:

| | Simple | Standard (as now) | Hands-on |
| --- | --- | --- | --- |
| After a run | nothing: it's ready to go again | its summary under the music | the review takes the screen |
| Round a loop, each pass | nothing | its figure in the bar | the loop stops and shows the pass, then waits for you *(pick)* |
| A run that ends | waits for you to go again, or goes again by itself after a bar's count-in (a setting) | waits | waits |

- **Simple** is for learning: loops, In time, Wait for me, hands, everything but the reviews. Nothing tells you how it went.
- **Hands-on** stops after each pass of a loop to show what happened, and gives each run the full review.
- **Standard** is today's app, kept as a third way *(pick: the owner named two)*.
- **The marks on the music** (the wrong and missed notes, while you play and afterwards) and **the cursor** stay settings of
  their own, as now, whichever way you practise.
- Whatever you choose, every run and every pass is kept, so the look back has them all.

## The look back (Finish practice)

- As on the canvas, taking the page: the session's time; each stretch you practised (its bars, the hand and the mode) with
  its passes or runs as a line, first to last and the best named, and its tempo or pace; what's still hard; and how it
  compares with last time.
- Keys: **Keep practising** (the session goes on), **In-depth review** beside it, and **Done** (back to the piece's front).
- No "next time" line: it suggests, and the instructor work is on hold.

## The in-depth review

- The session as a whole, taking the screen as on the canvas: its lines (notes, timing, held, the bar to look at) over every
  run and pass; the bars shaded by what went wrong; and the music of the bar you tap, marked as you last played it there.
- **A long piece**: up to 24 bars every bar has its cell. Past that, only the bars that had something wrong, the most to fix
  first, up to 16, each with its number, and over them a thin strip of the whole piece with each of them marked on it, so
  you can see where they are. More than 16 with something wrong: the 16 with the most, and "and 9 more" to show them all.
- Play's quick review opens it too, for its one run.

## Play

- **Play** opens the piece full screen, the page and nothing else: a slim strip at the top with the piece's name, the tempo
  (tap it to change it) and **Start** *(pick: a Start, so the tempo can be set first)*. Start, or the piano's go key, counts
  in; the strip then says the bar and the time, with Stop.
- No cursor and no marks while it plays, whatever the settings say. It's In time, with the click, from the first bar to the
  end, both hands. Following you without a click is the advanced reviewer's ask, on hold with the other seats.
- **At the end** (or Stop): a very quick review at the bottom: the headline and a few words, like "91% right in time · 88 of
  95 notes · look at bar 15", with **Again**, **In-depth review** and **Done**. The marks show on the music if that setting
  is on.
- A play-through is kept as a run, and counts in About's "played through" and its best.

## What it takes to build

- A practice session in the data: the piece, when it started and finished, its minutes, the way it was practised; each run
  of it carries the session. Each run also keeps what went wrong in each bar, for the in-depth review.
- The piece page gets three states: the front, Practice and Play. Play is full screen with its strip; Practice is today's
  page with the timer and Finish practice at the top.
- The three ways in Settings and Options, and the run and the loop following them: what shows after a run, after each pass,
  and whether a run goes again by itself.
- The look back and the in-depth review from the session's runs and passes (step 2 keeps the passes). Play's quick review is
  the summary's lines, cut down.
- Today's and You's minutes from the sessions.
- Checked in the preview in both looks and at both tablet sizes, and on the tablet with the piano.

## To confirm

1. Three ways (Simple, Standard, Hands-on), Standard being today's, or just the two named?
2. Hands-on: after each pass the loop waits for you (a tap or the piano's go key), or goes on by itself after a few seconds?
3. Play: a Start key first, so the tempo can be set, or straight into the count-in?
4. The timer pauses after five minutes with nothing played or tapped.
5. A session left unfinished goes on the same day and finishes by itself the next.
