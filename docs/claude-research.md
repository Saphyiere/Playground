# Claude in the apps: research

What it would take for the apps to ask Claude things: a **trainer** that reads your workouts and says what to lift next, a
**practice teacher** for Prima, and **PDF sheet music read into notation** you can play along with. It also covers how the
devices reach Claude, what it costs, and how every question gets the same JSON shape back. This is research only: nothing here is
built yet. Prices, plans and API details are as of November 2026.

**How sure we can be.** Anthropic's own documentation is cited for everything about the API, plans and keys. **Measured** means
tested in this research session. **Estimate** means worked out from prices and token counts, not measured.

---

## In short

- **Call Claude straight from the phone and the tablet.** You don't need a server or a container at home. You paste the API key
  into Settings once. It's kept in Android's secure storage, and its workspace has a monthly spend limit.
- **Your plan's monthly tokens.** **Max includes $100 of API credits a month** (Ultra $200, Pro none). They're separate from
  your usage in Claude and Claude Code. Link a Console organization in claude.ai under **Settings → Billing**, and every API key
  in it draws on them. When they run out, requests stop until next month. Nothing is charged to the plan.
- **The same JSON every time.** Each kind of question is a C# record in and a C# record out. The API's structured outputs hold
  Claude to the record's JSON schema while it writes, so the answer always parses. Every answer comes in the same envelope, and
  the app checks one thing: did it answer?
- **PDFs into notation works, on the tablet.** Claude read printed piano music from three Library pages, a bar or two at a time,
  and got **549 of 551 notes** right, checked against each piece's MIDI (**measured**, part 3). Claude can't listen to audio, so checking
  is done by eye: Prima's own Verovio engraves what was read, and Claude compares that with the page. About $0.50 to $2 a page
  (**estimate**).
- **The trainer and the teacher: the app does the arithmetic, Claude the judgement.** Core works out set times, rests, heart
  rate and anything odd, like a 6-minute set or a 15-second rest. Claude decides what that means and what comes next. Its answer
  names your own exercises and Prima's own drills, so the app can pre-fill or run it, and you confirm.

---

## 1. From the devices to Claude

### What you set up once

1. **A Claude Console organization** at [platform.claude.com](https://platform.claude.com). On Max or Ultra, link it to your plan
   first: claude.ai → **Settings → Billing → API credits → Link organization**. The monthly credits then arrive there; new
   subscribers can claim them after 7 days on the plan. Each plan links to one organization, and changing it later takes a
   message to support, so link the one you'll build in.
2. **A workspace for the apps** (or one each: Workout, Prima) with a **monthly spend limit**, say $30. Keys in that workspace can
   never spend more than that.
3. **A personal API key** scoped to that workspace. Choose an expiry when you make it (a preset from 3 hours to 30 days, a custom
   length, or never). It can be disabled and turned back on in the Console at any time.
4. **On each device**, the app's Settings → Claude: paste the key and tap **Test**.

### Your plan and the API

| Plan | API credits a month |
| --- | --- |
| Pro | None: buy credits in the Console, pay as you go |
| Max | $100 |
| Ultra | $200 |

- The credits cover the Claude API (any model), Managed Agents, the Agent SDK and the Console's playground. They don't cover
  Claude Code or extra usage in the Claude apps, and your limits there don't change.
- They refresh each billing cycle and don't roll over. You don't need a payment method in the Console.
- **When they run out, requests stop** with "Your credit balance is too low…" until the next month's credits arrive. Nothing is
  charged to your plan. Credits you buy yourself are spent after the included ones. So with no purchased credits, a runaway bug
  costs at most this month's $100.

**Why not use the subscription itself, through Claude Code on a PC** (`claude -p --json-schema`)? It works for your own scripts,
but it's the wrong tool for this:

- Anthropic's terms say signing in with a subscription is for Claude Code and Anthropic's own apps, and that apps should use API
  keys.
- The PC would have to be on, and reachable from the gym.
- Each question runs Claude Code's whole agent loop. That's slower, and its schema is checked after writing instead of enforced
  while writing.
- How `claude -p` counts against plans has already been in flux: a separate credit for it was announced for June 2026, then
  paused.

The API credits are meant for exactly this.

### Where the key lives

|  | Key on the device | Server at home (a container on the PC) | Server in the cloud |
| --- | --- | --- | --- |
| What calls Claude | The app, directly | The PC, for the app | A hosted function (a Cloudflare Worker, say) |
| Away from home | Works | Needs the PC on and a tunnel (Tailscale) | Works |
| What the devices hold | The key | A token for the PC | A token for the function |
| Moving parts | None | The PC, the container, the tunnel | A hosted service to keep up |

**Recommendation: keep the key on the device.** Anthropic's advice against keys in client apps is about apps that strangers
install, since anyone can dig a key out of an app they have. These apps live on your devices only. A server would add a computer
to keep running, and the devices would still need a secret to reach it. Anthropic's keyless option for apps, App Attest, is for
iOS and macOS only.

What keeps the key safe:

- **It's never in the repo.** The repo is public. GitHub's push protection has blocked Anthropic keys by default since October
  2026, and Anthropic revokes keys found in public repos, so a slip costs you a new key, not money. Still: no key in an
  `appsettings.json` in the project, and no key in a build.
- **It's kept in Android's secure storage**, not in the app's SQLite file. MAUI's `SecureStorage` encrypts with AES-256, using a
  key held in the Android Keystore. The SQLite file is the wrong place because Prima's **Back up** shares that file to Drive or
  the PC.
- **It's left out of Android's Auto Backup.** Both apps have `allowBackup="true"`, and a restored secure-storage file can't be
  decrypted on another device. Add Microsoft's backup rule that excludes
  `${applicationId}.microsoft.maui.essentials.preferences.xml`. After a restore, the app then simply asks for the key again.
- **It stays in C#.** The screens run in a WebView, and the key is never handed to JavaScript. Prima's WebView also draws
  imported PDFs with pdf.js.
- **Spending has limits everywhere.** The workspace has its spend limit, every request has a token limit, and the app retries at
  most once, so a bug can't loop.
- **If a device is lost**, disable the key in the Console and make a new one.

Your idea of a settings file pushed to the tablet works as a way to **deliver** the key. Copy a small file with the key to the
tablet's Downloads over USB; **Settings → Claude → Read key from a file** then moves it into secure storage and deletes the file.
Building the key into the app, as an `appsettings.json` packed into the APK, isn't worth it: the key would sit in every APK and in
`artifacts/`, readable by anyone who gets a copy. Simplest of all: open the Console in the tablet's browser, make the key there,
copy it and paste it.

**One device detail.** Prima's manifest has no `INTERNET` permission. Debug builds add it on their own for the debugger, but
Release builds don't, so asking Claude would work from Visual Studio and fail on the installed copy. Add
`<uses-permission android:name="android.permission.INTERNET" />`. The workout app already has it.

### What leaves the device

- Only what a question needs, and only when you press its button. Nothing runs in the background.
  - The trainer gets your sets, their times, heart-rate summaries (not the raw stream) and body weight.
  - The teacher gets your practice results.
  - Reading a PDF sends the page images.
- Anthropic doesn't train on API data. For Opus 5.5 and Sonnet 5.5 it doesn't keep prompts and answers by default; the Fable and
  Mythos models are the exception, kept 30 days. Anything its safety systems flag can be kept for up to 2 years.
- Prima's concept says nothing leaves the tablet unless you back it up. That would become "unless you back it up or ask Claude".

---

## 2. The framework: one shape for every question

### The rule

Every question an app asks is a **task**: a C# record in, a C# record out. The API's **structured outputs** take the answer
record's JSON schema and hold Claude to it while it writes (constrained decoding), so the reply always parses into the record.
The two exceptions are a refusal and running out of tokens, and the framework turns both into a plain failure. Nothing is
free-form, and nothing needs "does this field exist?" checks.

### The envelope

Every answer, whatever the task, looks like this:

```json
{
  "status": "answered",
  "summary": "Up to 105 on the chest press; rows stay at 120.",
  "notes": ["Set 3 of rows ran 4:40. I counted it as about a minute: the rest looks like walking to the next machine."],
  "answer": { "the task's own record": "the same shape every time" }
}
```

- `status` is `answered`, `needMore` (the input wasn't enough, and `notes` says what's missing) or `cannot`.
- `answer` holds the task's record when the status is `answered`, and is null otherwise. That's the only null in the envelope.
- Inside a task's record, nothing is optional. Lists can be empty, choices are enums, and no field is "maybe there".

What the app sees:

```csharp
Answer<NextSession> a = await assist.AskAsync(Tasks.NextSession, input, ct);
if (a.Ok) Prefill(a.Value);  // Value, Summary, Notes and Cost are all filled in
else Show(a.Summary);         // what went wrong, in a sentence
```

`Ok` is true only when Claude answered, the reply matched the schema, and the task's own checks passed.

### A task is four things

1. **An input record**: what the app sends. Core builds it from its data. Its properties' `[Description]` attributes are the
   field notes Claude reads.
2. **An answer record**: its JSON schema is generated from the type with System.Text.Json's `JsonSchemaExporter`, then
   tightened to the API's rules.
3. **Instructions**: a markdown file per task, embedded in the assembly, with a version number. Changing it, or either record,
   means a new version.
4. **Checks**: what a schema can't say. For example, the exercise must exist, the weight may change by at most 10%, and the bars
   must add up.

### What happens on every call

1. **Build the request.** The system prompt is a short shared preamble plus the task's instructions. There's one user message:
   any attachments (images), then the input as JSON, like `{"task": "workout.next-session", "version": 2, "today":
   "2026-11-08", "input": {…}}`. The serializer options are always the same, so the same data always makes the same bytes.
2. **Fix the answer's shape.** `output_config.format` is the envelope's schema, with the task's answer record inside it. Claude
   can't write anything else.
3. **Send it** through the official C# SDK, on its beta endpoint so that on Opus and Sonnet a refused request is answered by
   another model. If reading a PDF runs into the request timeout, its answers will need streaming.
4. **Read the stop reason.** A finished answer is parsed. `max_tokens` is a failure ("answer too long": raise the limit).
   `refusal` is a failure that gives Anthropic's reason.
5. **Run the task's checks.** If any fail, ask once more with the problems listed. If they fail again, `Ok` is false.
6. **Log it** in the app's SQLite: task, version, model, input, answer, tokens, cost, time and request id. That shows what was
   asked and what it cost, and lets a new prompt be tried on old questions before it ships.

### Schema rules

These come from the API's limits on structured outputs:

- **Every property is required**, and every object has `additionalProperties: false`. The generator adds both.
- **No recursive types, and no `minimum`, `maximum` or `minLength`.** Put bounds in the field's description and in the task's
  checks instead.
- **Few nullable fields.** A request allows at most 24 optional properties and 16 union-typed ones, and `string?` (string or
  null) counts as a union. Prefer `""`, `[]` and an enum value like `none` to null.
- **Compare enum values without case.** They can come back capitalized differently.
- **No field that asks for Claude's reasoning or step-by-step thinking.** It can trigger a refusal. Use a short `why` field
  instead; Opus 5.5 always thinks before it answers anyway.
- The first request with a new schema is a little slower while it's compiled. After that it's cached for 24 hours.

**About consistency:** there's no temperature to turn down any more. Current models take only the default, and the current API
version (2026-10-21) rejects `temperature` outright. Consistency comes from the schema, the versioned instructions and the checks.

### Where it lives

- **`Kit/Playground.AI.Assist`**: a plain .NET 10 library both apps share, holding the envelope, `AssistTask`, the client and
  the log. Its [README](../Kit/Playground.AI.Assist/README.md) covers writing a task, wiring it into an app, and getting the key
  onto a device.
- **Each app's tasks** go in its Core, next to the logic they use (`AdaptiveWorkout.Core/Assist/NextSession.cs`). Their input
  building and checks are then tested with xUnit like the rest of Core, without calling Claude.
- **The hosts** wire in the key from secure storage. The preview hosts get a stand-in that returns saved answers, so screens can
  be checked in the cloud without a key.
- **The same contract could be served over HTTP** later, if you ever want a PC in the middle. The apps wouldn't change.

### The SDK

- It's the official package: `dotnet add package Anthropic`, version 10 and up. Versions 3.x and older were a community package,
  now named `tryAGI.Anthropic`. It targets .NET Standard 2.0, so it runs in MAUI on Android.
- Release builds compile ahead of time. Try anything that leans on reflection, like generating schemas from types, in a Release
  build on the device early.

### Models and cost per question

Prices per million tokens: Opus 5.5 $4 in and $20 out, Sonnet 5.5 $2 and $10, Haiku 5.5 $0.10 and $0.50. An image costs one
token per 28 × 28 pixels: about 1,500 for one system of a page, and at most 4,784 for any single image.

| Task | Model to start with | Per question (estimate) | A month of daily use |
| --- | --- | --- | --- |
| Next workout | Opus 5.5 (try Sonnet 5.5) | $0.10–0.25 | $3–8 |
| Practice plan | Opus 5.5 (try Sonnet 5.5) | $0.10–0.20 | $3–6 |
| PDF to notation | Opus 5.5, high effort | $0.50–2 a page | Depends on the pages |

Most of an answer's cost is Claude's thinking, which only measuring will pin down; the log will show the real numbers. Work that
can wait up to a day, like converting a pile of PDFs, can go through Message Batches at half price.

---

## 3. Reading PDFs into notation (Prima)

### Why

Today Prima shows a PDF as pages, and plays along only when a matching MIDI file came with it. As notation (MusicXML or MEI), a
PDF piece would get everything Prima does with Library pieces: Wait for me, In time, the piano playing the other hand, Listen,
loops and a level. [library.md](../Music/docs/library.md) (§5) decided recognition wasn't a tablet feature, because research OMR
gets 18% of symbols wrong on real piano scans. A model that reads a few bars at a time changes that for clean PDFs.

### Can Claude read sheet music? (measured)

No published test of Claude on sheet music turned up. The benchmarks found (MusiXQA, MuSP-Bench) report that general multimodal
models find scores hard, but they ask **questions about** whole scores, which is a different task from transcribing a few cropped
bars at a time. So it was tested here, on the Library's own Mutopia PDFs. Each comes with a MIDI file exported from the same
engraving, which makes a ready answer key.

**How:**

- Each page was rendered at 300 dpi and cut into images of one or two bars.
- Claude Opus 5.5, the model the app would call, read each image directly in the research session (not through the API). It
  wrote down every note in each staff: pitch, where it starts and how long it lasts.
- The MIDI's notes were extracted without being looked at, and compared afterwards by pitch and start.
- A third page was degraded to look like a scan: tilted 0.6°, down to 200 dpi, blurred, darkened paper, noise and JPEG. Its bars
  were enlarged 2×, and 3–4× for a few dense chords.

| Page | Notes in the score | Read right | Wrong or extra |
| --- | --- | --- | --- |
| Czerny Op. 821 No. 13, clean: 8 bars with grace notes, high ledger lines, accidentals that carry through the bar | 162 | 162 | None |
| Joplin, *Sugar Cane*, page 1, clean: pickup and 14 bars, 4-note chords in both hands, ties, two clef changes | 247 | 247 | 1 extra note |
| Czerny Op. 821 No. 17, made to look scanned: 8 bars, two voices on the lower staff, an 8va passage | 142 | 140 | 1 chord misread (2 notes) |

Every note that was found also had the right length (535 of 535), and so did every tie, every grace note and the 8va.

**What went wrong.** All the mistakes were chords stacked above the staff, among ledger lines. In *Sugar Cane* bar 8, an empty
ledger line was read as an extra E6 in a G5–C♯6–G6 chord. In the scan-like Czerny, bar 4, two notes of a blurred three-note chord
were read a step too high: G5–B5–D6 instead of F♯5–A5–D6.

**What it means:**

- **Clean engraved PDFs** read well enough to play along with straight away; a wrong note is rare enough to fix by hand.
- **Scans** will do worse. The test scan was mild; old scans like IMSLP's, and phone photos, weren't tested.
- **Spelling isn't checked** (F♯ or G♭), and neither are slurs, dynamics, pedalling or fingering, because MIDI doesn't carry
  them. Playing along mostly needs pitch and rhythm.
- **It's a strong sign, not a benchmark:** one reader, about 30 bars. The first job in building this is measuring it properly
  (below).

### Checking a reading: by eye, not by ear

Claude can't listen. The API takes text, images and PDFs, not audio. So the check you described (make the MIDI, play it, compare)
becomes three checks:

1. **The app checks the arithmetic.** Each voice's notes must fill its bar, every pitch must be on the piano, and a tie must join
   equal pitches. That cheaply catches misread rhythms.
2. **Claude compares pictures.** Prima's Verovio engraves the reading. Claude gets the page's system and the engraving side by
   side and lists the differences it sees, bar by bar.
   - On the scan-like page, this pass found the misread chord, and the second reading got two of its three notes right.
   - The third note was "corrected" to fit the harmony (a D7 chord) and was wrong. So the instructions must say to report only
     what's printed. A bar still in doubt is read again from a closer crop, or marked for you.
3. **You listen.** Listen has the piano play the reading, and a wrong note usually sounds wrong.

### The pipeline on the tablet

1. **Pages to images.** pdf.js draws each page, and Prima's `PdfLayout` already finds its systems. Each system is cut out at up
   to 2,576 px wide, the largest image Claude takes without shrinking it. The clefs, key and metre carry over from the system
   before.
2. **Read.** One request per system; several can run at once. The answer lists bars, then staves, then voices, then notes and
   rests. Each note has its pitch spelled out (step, alteration, octave), its note value (type, dots, tuplet), ties and grace
   notes, plus chords, clef changes, 8va, repeats and endings. That's close to Prima's own `Score` record, which already has all
   of these.
3. **Check and build.** The app checks the bars, builds a `Score` and writes MEI with `MeiWriter`. The app works out the ticks;
   Claude doesn't.
4. **Compare.** Verovio engraves each system, Claude compares it with the page, and flagged bars are read again zoomed in. One
   round, two at most.
5. **Keep it** as an imported piece, with the PDF beside it so you can switch between the print and the notation. Bars Claude
   still had doubts about are listed.

Start on demand: a **Convert** in a PDF piece's Options, one piece at a time.

### What's built

The endpoint is in `Music/src/Prima.Core/Transcription`. The Prima session designs the screens around it.

- **`ReadSystem`** asks Claude to read one system's image. Claude writes pitches as printed: the line or space, and the
  accidental printed before the note. `ReadingScore` turns them into the pitches that sound: the key signature, the bar's
  earlier accidentals on the same line or space, ties across barlines and systems, and 8va lines. It does this in code, not by
  asking Claude.
- **`ReadingChecks`** check every reading before it's taken:
  - each voice fills its bar (only a pickup, or a bar cut short by a repeat or the end, is short);
  - every pitch is on the piano;
  - every tie lands on its pitch;
  - both staves are there.

  A reading that fails is asked for once more, with the problems listed.
- **`CompareSystem`** gets the print and Verovio's engraving of the reading, side by side, and reads again the bars that differ.
  It's told to report only what's printed, never what the harmony suggests.
- **`PdfToNotation`** runs it all:
  - the first system alone, since only it prints the time signature, then the rest four at a time;
  - progress for each system;
  - doubts by bar number;
  - failed systems named, and read again one at a time;
  - a stop when the key, the credit or the connection fails.
- **Two simplifications for now.** Notes under an 8va line keep the pitch they sound, and are engraved an octave above the print
  without the line. Slurs, dynamics, pedalling and fingering aren't read.
- **Tests:** 20, against a scripted stand-in for Claude. One more asks the real API to read the first system of Czerny's Op. 821
  No. 13 and checks its notes against the piece's MIDI, for about 20 cents. It's skipped unless `PLAYGROUND_CLAUDE_KEY` holds a
  key: set it as the [framework's README](../Kit/Playground.AI.Assist/README.md#build-and-test) says, then run
  `dotnet test --project Music/tests/Prima.Core.Tests`. What Claude reads across many pieces is measured with the tool below,
  which needs a key too.

### Measuring it properly

`Music/library` holds 104 Mutopia pieces as a PDF and its MIDI. A small tool on the PC (or a test) can run the converter on a set
of pages and score it against the MIDI as was done here. It reports the notes right, the notes wrong, missing or extra in each
bar, and the cost and time per page. Every prompt or model change gets measured on the same pages before it reaches the tablet.
For the harder case, add a few scans of your own PDFs whose notation you've checked by hand.

### Cost and time (estimate)

A system costs roughly $0.15–0.60 to read and compare with Opus 5.5 at high effort, most of it for the reading Claude writes and
thinks through. A page of 6 systems is about $1–3.60 and takes about 7.5 minutes: the first system alone, then the rest four at
a time. `PdfToNotation.Estimate` gives this range for a PDF before it's read, for the screen to show. Ultra's $200 a month
covers about 55 to 220 pages. Convert pieces as you want them, or send a big pile through Message Batches at half price.
Sonnet 5.5 is half the price; whether it reads as well is the first thing to measure.

### Your PDFs and the repo

Notation made from PDFs you own stays on the tablet as imports, like the local collection, and never goes into `Music/library`.

### And Audiveris?

library.md planned Audiveris on the PC for the rare piece that needed note-level following. It's still an option as a first draft
for Claude to correct, if scans turn out hard. Start without it: it's one less tool, and it doesn't run on Android.

---

## 4. The workout trainer

### What it's for

**Ask the trainer** on Today, beside Start, says what to do next session: weight, reps and sets for each exercise, each with a
line on why. After **Finish**, a short look back at the workout says what went up, and what the data says about any set that
looks wrong.

### Your method stays the rule

[concept.md](../Workout/docs/concept.md): pick a weight and aim for 3 × 10. Hit 10, 10, 10 and go up next time; miss reps and
stay. The trainer is told this, along with the ACSM's guidance to add 2–10% once you can do one or two reps more than the
target. Claude adds judgement where the rule needs it:

- a lift that has stalled for weeks,
- a set that shouldn't count,
- a day the heart rate says you were tired,
- an exercise you haven't done in a month.

### Messy data: Core flags it, Claude decides what it means

Core already works out set times, rests and heart rate during sets and rests. It would add a list of oddities for each set,
tested like the rest of Core:

| Flag | How Core finds it | What it probably means |
| --- | --- | --- |
| Long set | Over 3× your usual set time for that exercise, or over 3 minutes | You forgot to stop, or the time includes walking to the next machine |
| Short rest | Under 30 s, or far under the countdown | You didn't wait the full rest, or tapped Start late on the set before |
| Effort during rest | Heart rate climbed during the rest and fell during the set | You did the set before tapping Start: the times are off, the reps are fine |
| Odd number | A weight far from the last times (100 → 1000) | A typo |
| Goal box | Hit goal ticked with fewer reps than last time, or every rep hit and not ticked | A missed tick |

Claude gets a summary of each set (not the raw heart-rate samples), its flags, and the last few times you did each exercise in
this session's template. Its notes say which flags it acted on.

### What comes back

```json
{
  "exercises": [
    { "exerciseId": 12, "sets": 3, "reps": 10, "weight": 105, "unit": "lb", "change": "up", "why": "10, 10, 10 at 100 on Monday" }
  ],
  "rest": [ { "exerciseId": 12, "seconds": 90 } ],
  "dataNotes": [ { "setId": 5531, "flag": "long_set", "treatedAs": "ignored_time", "note": "Ran into the walk to the rows" } ]
}
```

- `change` is one of `up`, `same`, `down`, `new` or `skip`.
- `exerciseId` must be one of yours, because the schema lists them as an enum. A weight change of more than 10%, or one that
  isn't a whole number of your hit-goal steps, fails the checks.
- The app uses the answer to pre-fill. For that workout it takes the place of the +5 after a hit goal, and **Next** shows
  "Trainer: 105 × 10". You can always change it.

### Evidence

- **ACSM position stand on progression (2009):** raise the load 2–10% once you can do one or two reps more than the target.
- **Two studies of training plans written by GPT and Gemini** (Havers et al. 2025; a 2026 preprint): the plans were of moderate
  quality and weakest on fitting the person and on progression. That's the argument for sending your history and your rules,
  not asking for a plan from nothing.

---

## 5. The practice teacher (Prima)

### Toward a piece

**I want to play this.** The teacher gets what Prima already knows about the piece and about you.

- **The piece:** its level and the reasons for it (keys, metre, rhythm figures, hands, chords, range, ledger lines), and its
  excerpts with their levels.
- **You:** reading level and tempo, your scales and their tempos, the notes you miss, the rhythm figures you miss, and your
  minutes a day.

It returns steps Prima can already run, and roughly how many days it will take:

- a drill and its settings: Key signatures in E♭, Rhythm with dotted eighth-sixteenths, Scales in E♭ major over two octaves;
- the piece itself, with bars, hands, tempo and mode: bars 9–16, right hand, Wait for me, ♩ = 60;
- sight-reading at a level.

The drills are a fixed list, so the schema lists them as an enum. Bars and excerpts change from piece to piece, so the checks
confirm them.

### Today's plan, from the teacher

Today shows the day's plan from `DailyPlan`: technique, notes, rhythm, a pause, sight-reading and one spot. **Ask the teacher**
would return a plan in the same shape, the same step kinds and minutes, shaped by your recent results and the piece you're working
toward. Prima runs it with the screens it already has. The research rules go into the instructions: about 85% success, keep
going, short technique.

---

## 6. Decided

1. **The plan:** Ultra, so $200 of API credits a month. (Ultra is the tier above Max, which brings $100.)
2. **The data:** sending workout data (heart rate, body weight) and practice data to Anthropic is fine, a question at a time.
3. **The key:** it reaches each device as a file, copied over USB, that the app moves into secure storage and deletes. The steps
   are in the framework's README, under Getting the key onto a device.
4. **The framework:** `Kit/Playground.AI.Assist`, named so it says what it's for. It's built, with its tests.
5. **First:** PDF to notation, with Prima's screens designed by the Prima session, against the shapes the framework session
   sent it.

Still open: the models. Opus 5.5 is the default, and each task can try Sonnet 5.5 once it measures as well.

## 7. A build order

1. **The framework.** Done: `Kit/Playground.AI.Assist`. Still to do: Settings → Claude (key and Test) and the log on the
   devices, with a task working end to end in a Release build.
2. **PDF to notation.** The endpoint is built (above). Next: the measuring tool on the PC, then the tablet's screens (the Prima
   session) and wiring.
3. **The trainer.** Core's flags and inputs first, tested, then the question itself.
4. **The teacher.**

---

## How the reading test was scored

- **The answer key.** The MIDI's notes were taken from each file with their pitch, start and length, in quarter notes. Only a
  count and the time signature were printed, so the reading stayed blind.
- **Matching.** A note counts as right when the MIDI has the same pitch starting at the same time. The *Sugar Cane* and scan-like
  Czerny MIDI started exactly on the beat. Czerny No. 13's MIDI started 43/384 of a beat late, pushed by its opening grace note,
  so it was shifted back by that amount first. Grace notes, which MIDI moves a little, could be up to half a beat off.
- **Two exceptions.** Where two voices on one staff share a pitch at the same moment, MIDI holds one note, so the reading counted
  one too. Spelling isn't judged, since a MIDI pitch is a number.
- **The comparison images** were the reading engraved by Verovio 6.4 (through music21 and MusicXML), one bar at a time, under the
  page's own bar.

---

## Sources

The Claude API and plans (Anthropic's documentation):

- API credits for Max, Ultra and Team plans: https://platform.claude.com/docs/en/about-claude/api-credits-for-subscribers ·
  https://support.claude.com/en/articles/17154008-monthly-api-credits-for-max-ultra-and-team-plans
- Authentication, API keys, workspaces and expiry: https://platform.claude.com/docs/en/manage-claude/authentication
- Structured outputs and their limits: https://platform.claude.com/docs/en/build-with-claude/structured-outputs
- Vision (image sizes and token cost): https://platform.claude.com/docs/en/build-with-claude/vision
- PDF support: https://platform.claude.com/docs/en/build-with-claude/pdf-support
- Data retention: https://platform.claude.com/docs/en/manage-claude/api-and-data-retention
- Claude Code's terms on subscriptions and API keys: https://code.claude.com/docs/en/legal-and-compliance
- `claude -p` and `--json-schema`: https://code.claude.com/docs/en/headless
- Using the Agent SDK with a plan, including the paused June 2026 change:
  https://support.claude.com/en/articles/15036540-use-the-claude-agent-sdk-with-your-claude-plan
- The C# SDK: https://github.com/anthropics/anthropic-sdk-csharp

Keys and Android:

- GitHub push protection for Anthropic keys (October 2026):
  https://github.blog/changelog/2026-10-13-secret-scanning-blocks-anthropic-keys-by-default-adds-salesforce
- Anthropic as a GitHub secret-scanning partner (2024):
  https://github.blog/changelog/2024-08-20-anthropic-is-now-a-github-secret-scanning-partner/
- .NET MAUI secure storage and Auto Backup: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage

Sheet music and models:

- Mayer et al. 2024, practical end-to-end OMR for pianoform music (17.7% symbol errors on real scans):
  https://arxiv.org/abs/2403.13763
- MusiXQA, visual music understanding in multimodal LLMs: https://arxiv.org/pdf/2506.23009
- MuSP-Bench, music understanding across score and performance (2026): https://arxiv.org/abs/2608.28212

Training:

- ACSM position stand, progression models in resistance training (2009): https://pubmed.ncbi.nlm.nih.gov/19204579/
- Havers et al. 2025, training plans by generative AI assessed by coaches: https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12492345/
- Plans by GPT-4 and Gemini, reproducibility and quality: https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11963122/
- Silva, Colaço and Mota, resistance training prescriptions by ChatGPT and Gemini (preprint): https://preprints.jmir.org/preprint/93865
