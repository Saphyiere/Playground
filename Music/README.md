# Prima

Sight-reading and piano practice for the owner's Lenovo Idea Tab Pro, played on a Yamaha P-225B over USB MIDI. New music at
your level every day, read in time with a click that never waits, short drills for what reading leans on and for even
technique, and progress as honest numbers over weeks. Local data only: no accounts, no cloud.

- How it works: [docs/concept.md](docs/concept.md)
- How it's built, part by part: [docs/plan.md](docs/plan.md)
- The evidence behind it: [docs/research.md](docs/research.md), and for the Library, [docs/library.md](docs/library.md)

## Layout

```
Prima.slnx                The solution. Open it in Visual Studio.
build/Prima.Build/        Solution items: this folder's and the repo's top-level files and docs.
src/
  Prima.Core/             The music, the generator, scoring and practice logic. No UI; tested.
  Prima.UI/               The screens, the look (wwwroot/prima.css and its fonts) and the store (SQLite).
  Prima.Tablet/           The Android app: hosts the screens in a web view, and talks to the piano.
tools/Prima.Preview/      The screens in a browser against sample data, for checking them from the cloud.
tests/Prima.Core.Tests/   Tests for the Core.
docs/                     How it works, the plan and the research.
```

## One-time setup (Windows)

Visual Studio 2026 with the **.NET Multi-platform App UI development** workload, which brings the .NET 10 SDK, the Android SDK
and a JDK. The repo's [README](../README.md) has the details, and the Workout app's README has the CLI-only route.

## Putting it on the tablet

1. Tablet: **Settings > About tablet**, tap **Build number** 7 times, then turn on **USB debugging** (or **Wireless debugging**)
   in **Developer options**.
2. Plug the tablet into the PC and accept the "Allow USB debugging" prompt. `adb devices` should list it. With wireless
   debugging, pair once with `adb pair` and the code the tablet shows, then `adb connect`.
3. Visual Studio: set **Prima.Tablet** as the startup project, pick the tablet in the device dropdown, and press F5.
   CLI: `dotnet build src/Prima.Tablet -t:Run`

For the copy you use day to day, install **Release** (Ctrl+F5 with Release picked, or `dotnet build src/Prima.Tablet -t:Run -c
Release`). It compiles the whole app ahead of time, so it opens quickly, and it installs over a Debug build without losing your
data. Don't uninstall the app to fix a build problem: that deletes your practice history.

## Connecting the piano

The P-225B's **USB TO HOST** port (a square USB-B socket on the back) goes to the tablet's USB-C port, with a USB-B to USB-C
cable or a USB-B cable and an adapter. The tablet then has no free port, so it practises on battery unless a USB-C hub with power
delivery sits in between. Prima finds the piano by itself, and its name lights up at the foot of the rail.

Then try, in **Tools**:

- **Keyboard check**: play a few keys and the pedals, and the lowest and highest keys (A0 and C8). Leave it a minute for the
  MIDI path's jitter, from the piano's Active Sensing. **Play middle C** checks the piano plays notes Prima sends it, on channel
  16, with the grand piano voice.
- **Metronome**: does the click sound on time, and where (the tablet's speaker, or the piano if Android sends audio there)? In
  **Settings → Click**, "On the piano" plays it as short high notes on the piano instead, for headphones in the piano.
- **Timing**: play a key with the click for 16 beats and keep the offset. Redo it after changing where the click sounds.

Worth noting for the next parts: whether the piano echoes the notes Prima sends back to it, which channel a split or dual voice
sends on, the jitter Keyboard check shows, and the timing offset and spread.

## The looks

Prima has its own two looks, **Studio** (a music workstation: warm grey keys, an amber screen, one orange key) and **Vinyl** (a
record sleeve: deep teal, cream type, mustard), switched in Settings. They come from the
[Studio & Vinyl design system](https://claude.ai/artifact/XtgAHfVyw3L94e5DKrj2ii): its tokens and components are in
`src/Prima.UI/wwwroot/prima.css`, with Prima's own screens after them. Sheet music is black on paper in both. Prima uses the
Kit only for the way back and its settings store, not its Logbook and Telemetry looks.

## Checking screens from the cloud

The preview host runs the real screens against sample data on http://127.0.0.1:5299, and `tools/screens/screens.mjs` at the
repo's root screenshots them (CLAUDE.md has the details):

```bash
TZ=America/Los_Angeles dotnet run --project Music/tools/Prima.Preview
```

```bash
curl -s http://127.0.0.1:5299/seed/empty
```

## Licences

The fonts (Space Grotesk, Space Mono, Bebas Neue and Work Sans) are under the SIL Open Font License; their licences are next to
them in `src/Prima.UI/wwwroot/fonts`.
