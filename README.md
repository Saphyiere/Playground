# Playground

Personal apps for my Android phone, tablet and Galaxy Watch, built with .NET 10 and sharing one look. Not meant for app stores.

| Folder | What it is |
|---|---|
| [Workout](Workout/README.md) | Workout tracker: routines, sets with heart rate from the watch, rest timer, calories, body weight and stats. Phone and watch. |
| Music (coming) | Sheet-music reading practice with a MIDI keyboard or the microphone. Tablet and phone. |
| [Kit](Kit/README.md) | What every app shares: the Logbook and Telemetry looks, their fonts, and the common pieces of a screen. |

## Layout

```
Directory.Build.props     Build settings every project shares: output to artifacts/, nullable, implicit usings.
Directory.Build.targets   Keeps Visual Studio's background builds from breaking Android resource ids.
global.json               SDK settings: the test runner and MSBuild SDK versions.
CLAUDE.md                 How Claude works in this repo: build, test, check screens, commit.
tools/
  cloud-setup.sh          The cloud environment's setup script.
  screens/screens.mjs     Takes screenshots of an app's preview host, for checking screens from a cloud session.
Kit/                      The shared looks and screen pieces, a Razor class library every app's UI references.
Workout/                  The workout app, with its own solution, README and docs.
artifacts/                All build output (git-ignored).
```

## Setup

**Cloud sessions (Claude Code on the web).** The environment's setup script installs the .NET 10 SDK with its Android workload,
and the Android SDK. Paste [tools/cloud-setup.sh](tools/cloud-setup.sh) into the environment's settings (the environment menu in
the session's title bar, then **Edit**, then **Setup script**); it takes about a minute and a half, and later sessions reuse the
result. Start a session per app or task, with this repo selected.

**Windows.** Visual Studio 2026 with the **.NET Multi-platform App UI development** workload, which brings the .NET 10 SDK, the
Android SDK and a JDK. Open an app's solution, e.g. `Workout/AdaptiveWorkout.slnx`. Each app's README covers putting it on the
phone, tablet or watch.

## Adding an app

A folder at the root named for the app, with its own solution, README and `docs/concept.md`, and projects named after it:
`<App>.Core` for the business logic (tested), `<App>.UI` for the screens, and a host per device (`<App>.Phone`, `<App>.Watch`). Its
screens use the [Kit](Kit/README.md), and a preview host (`<App>/tools/<App>.Preview`) shows them for checking from the cloud.
