# Playground

Personal .NET 10 apps for the owner's devices, not for app stores: a Pixel phone (Android 17), a Lenovo Android tablet, and a
Galaxy Watch5 (Wear OS). Each app is a folder at the root with its own solution, README and `docs/concept.md`; they share the
build settings at the root. The owner reads the repo's docs and works in it from Visual Studio on Windows.

## Tools

The cloud environment's setup script, [tools/cloud-setup.sh](tools/cloud-setup.sh), installs .NET 10 at `/opt/dotnet` with the
`maui-android` workload and the Android SDK at `/opt/android-sdk` (API 36, build tools 36.0.0), and points every shell at them,
so a plain `dotnet build` works. If `dotnet` is missing, the script hasn't run in this environment: run it (`bash
tools/cloud-setup.sh`, about 90 seconds) and tell the owner to paste it into the environment's settings. The machine already has
Java 21, Node 22, Python 3 with Pillow, and headless Chromium under `/opt/pw-browsers`.

## Build and test

- Build the highest-level project a change touches, not a solution: `dotnet build Workout/src/AdaptiveWorkout.Phone/AdaptiveWorkout.Phone.csproj`
  builds the Core and UI under it too. Leave no warnings.
- Tests are xUnit v3 on Microsoft.Testing.Platform, for each app's business logic (`*.Core`) only:
  `dotnet test --project Workout/tests/AdaptiveWorkout.Core.Tests`.
- All output goes to `artifacts/` at the root.

## Checking screens

A change the owner will see is checked rendered before it's done, in both looks (Logbook and Telemetry) at phone width, 390 px
(and tablet width for a tablet app), with before-and-after screenshots when it shouldn't change anything else. Only the devices
can test the watch, a MIDI keyboard or the microphone: say what's left for the owner to try.

Each app's screens have a preview host: its real UI as a Blazor Server page, against sample data in a SQLite file. For the
workout app, `Workout/tools/AdaptiveWorkout.Preview`:

```bash
TZ=America/New_York dotnet run --project Workout/tools/AdaptiveWorkout.Preview > <scratch>/preview.log 2>&1 &   # http://127.0.0.1:5199
curl -s http://127.0.0.1:5199/seed/today    # the scenarios are in Seed, in its Program.cs
node tools/screens/screens.mjs steps.json <scratch>/shots
```

The time zone is the owner's, so days turn over where the owner's do. [tools/screens/screens.mjs](tools/screens/screens.mjs) drives
the machine's headless Chromium over the DevTools protocol (not Playwright); its steps are listed at the top. To switch the look,
go to `settings` and tap `Telemetry` or `Logbook`. Telemetry writes dates "Sat 31 Oct", Logbook "Sat, Oct 31". A tap from a
script doesn't blur a field the way a finger does, so tap a form's Save with `realTap`. A new app gets a preview host of its own
the same way.

## Working alongside other sessions

There's a session per app or task, and they all push to `main`. Before pushing, `git pull --rebase origin main`. Stay in the app
the task is about; a change to the shared root files or another app's folder needs saying so in the commit message.

## Commits and docs

- Commit to `main` and push after each verified part, with a message that says what changed for the owner.
- An app's `docs/concept.md` describes what it does and how its data is shaped: change it with the behavior, and its README with
  how to build, run or deploy it.

## The look

The apps share two looks, **Logbook** (a printed training log: warm paper, serif print, a red pen) and **Telemetry** (a dark
instrument panel, mono readouts on rulers), switched in each app's Settings. How they're built is under **Design** in
[Workout/README.md](Workout/README.md).
