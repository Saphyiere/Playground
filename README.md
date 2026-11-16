# Playground

Personal apps for my Android phone, tablet and Galaxy Watch, built with .NET 10. Not meant for app stores.

| Folder | What it is |
|---|---|
| [Workout](Workout/README.md) | Workout tracker: routines, sets with heart rate from the watch, rest timer, calories, body weight and stats. Phone and watch. |
| [Music](Music/README.md) | Prima: sight-reading and piano practice with the P-225B over USB MIDI, in its own Studio and Vinyl looks. Tablet. |
| [Kit](Kit/README.md) | What the apps share: the Logbook and Telemetry looks (Workout's), their fonts, the common pieces of a screen, the way back, asking Claude, updates, and copies of their data. |

## Layout

```
Directory.Build.props     Build settings every project shares: output to artifacts/, nullable, implicit usings, the devices'
                          signing key, and app files (.apk) rather than app bundles.
Directory.Build.targets   Signs every app with the devices' key, brings in the install guard, and keeps Visual Studio's
                          background builds from breaking Android resource ids.
global.json               SDK settings: the test runner and MSBuild SDK versions.
CLAUDE.md                 How Claude works in this repo: build, test, check screens, commit.
tools/
  build/InstallGuard.*    The install guard: stops an install from the PC that would erase an app's data on the device.
  cloud-setup.sh          The cloud environment's setup script.
  release/                The Release workflow's script, and how keys and installing from the PC work (its README).
  screens/screens.mjs     Takes screenshots of an app's preview host, for checking screens from a cloud session.
  sync/pull-copy.ps1      Pulls an app's newest copy of its data off a phone or tablet onto the PC, over adb.
docs/
  claude-research.md      Research on the apps asking Claude: a workout trainer, a practice teacher, PDFs into notation.
Kit/                      The shared looks and screen pieces, a Razor class library every app's UI references, and
                          Playground.AI.Assist, how the apps ask Claude.
Workout/                  The workout app, with its own solution, README and docs.
Music/                    Prima, the sight-reading and piano practice app, with its own solution, README and docs.
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

**Keys and installing.** Every build of every app, Debug or Release, from Visual Studio or the command line, is signed with one
key, the PC's .NET for Android debug key, and so are the GitHub releases: any of them installs over any other as an update and
keeps the app's data. Before anything is installed from the PC, the install guard compares it with the copy on the device and
stops when installing would erase that copy's data. Release is never built or installed on the PC: releases come only from the
GitHub workflow, and a change is tried on a device with a Debug build, after backing up the app's data.
[tools/release/README.md](tools/release/README.md#one-key-for-every-build) says how it all works, and why.

## Adding an app

A folder at the root named for the app, with its own solution, README and `docs/concept.md`, and projects named after it:
`<App>.Core` for the business logic (tested), `<App>.UI` for the screens, and a host per device (`<App>.Phone`, `<App>.Watch`,
`<App>.Tablet`). Its screens use the [Kit](Kit/README.md), its looks or the app's own, and a preview host
(`<App>/tools/<App>.Preview`) shows them for checking from the cloud.
