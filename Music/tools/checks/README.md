# Checks: every exercise and drill, played in the preview

Scripts that go through Prima's exercises and drills in the [preview host](../Prima.Preview) with its stand-in piano, the way
they'd be gone through by hand: open each one, see that its music is on the staff, and play it. They're for after a change to
the play bar, the sheet, the exercise catalogue or a drill, and for a new exercise: it's in the catalogue, so it's played too.

They drive a headless Chrome over the DevTools protocol, one that's left running, so a script starts at once.

```bash
# The preview (CLAUDE.md has its ports and seeds), with any data: /seed/fresh is enough.
TZ=America/Los_Angeles dotnet run --project Music/tools/Prima.Preview &
curl -s http://127.0.0.1:5299/seed/fresh

# A headless Chrome with its own profile, left running. On the PC:
"C:/Program Files/Google/Chrome/Application/chrome.exe" --headless=new --user-data-dir=<a scratch folder> --remote-debugging-port=9341 --hide-scrollbars --disable-gpu about:blank &
# In the cloud environment, the machine's headless shell takes the same flags (without --headless=new).

node Music/tools/checks/exercises.mjs <folder for pictures of anything wrong> [one exercise's id] [what's chosen of its variations] [hands]
node Music/tools/checks/drills.mjs <folder for pictures>
```

`BASE` (default `http://127.0.0.1:5299/`) and `SHOTS_PORT` (default `9341`) say where the preview and the browser are.

## What each plays

- **exercises.mjs**: every exercise to play, in its first key and hands. Once through In time (the stand-in plays every note
  right, at once: the summary must say all of them were right); once through in Wait for me, each step answered as the cursor
  reaches it (every note right first time, and the run ends); looped In time at the fastest tempo, the stand-in playing each
  note as it's due, until the bar counts a third pass, then Stop (the summary must name its passes); and through the keys, drawn
  and played once. A line for each exercise, `BAD` with a picture for anything that fails, and a count at the end. About half
  a minute an exercise. A family is played as its main exercise; with its id and a choice of its variations after it
  (`bounce "interval=octave;quick=3" Together`) it's that form that's played, in C, with its first hands or the ones named.
  Every form's notes are checked without the browser by the tests (`ExerciseFamilyTests`); the preview's
  `/exercises/forms/{id}` lists them.
- **drills.mjs**: Scales, Arpeggios and Finger independence, a run In time, a looped run and a run through the keys; then the
  reading drills with their Loop on, a set played right and the next beginning by itself.

`drive.mjs` is the driver they share: `open()` gives a page to `go` to, `tap`, read with `evaluate`, wait on with `until`, and
photograph with `shot`; `get` asks the preview itself (`keys/perform`, `seed/…`).

`keys/perform` plays the run in progress, its knobs in the query: `right` (the share of notes right, the rest a key or two
away), `lean` and `spread` (ms), `stop` (nothing from that bar on), `slips` and `early` (shares of notes), `roll` (ms between a
chord's keys), `drift` (ms a beat from the first note on: `-10` a run that gets ahead of the click, `10` one that falls behind)
and `hold` (each key down for this much of its note: `1.1` legato into the next, `0.5` detached); `live=true` plays each note
as it's due. A run's review (concept, "A run's review") answers to each: rushed, uneven, wrong notes, detached.

A line the engraver says it squeezed ("Justification is highly compressed": the slow trills' bar of sixteenths with a
finger on each, at the tablet's width) is noted at the end of the exercise's line and not counted: it still reads.

Two things worth knowing. The stand-in's live player (`/keys/perform?live=true`) stops when its run does; before it did, a
looped run's player went on for half an hour into every run after it. And nothing else that loads the machine (a test run, a
build) should go on beside a looped check: its notes are played as they're due.
