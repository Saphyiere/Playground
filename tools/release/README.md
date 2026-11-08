# Releases

Every app is released from one click: the repo's **Actions › Release › Run workflow**
([the workflow](../../.github/workflows/release.yml)). Each app that changed since its last release gets a GitHub release of its
own, **Prima 1.0.N** or **Workout 1.0.N**, with its app files, and the changes since the last one in its notes. An app that didn't
change is skipped, Markdown aside; tick **Release every app** to release them all anyway. The apps update themselves from these
releases, through the Kit's [Playground.Updates](../../Kit/Playground.Updates/README.md).

Every file is signed with the key Visual Studio signs with on the PC. So a release installs over a copy from Visual Studio as an
update and keeps its data (and the other way round), and the watch app still pairs with the phone's, which needs the same key.

## Once: give GitHub the key

It's the PC's debug key, `%LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore`.

1. Copy it as text, in PowerShell:

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("$env:LOCALAPPDATA\Xamarin\Mono for Android\debug.keystore")) | Set-Clipboard
   ```

2. On GitHub, in the repo's **Settings › Secrets and variables › Actions**, add a **New repository secret** named
   `PLAYGROUND_KEYSTORE_BASE64` and paste it in. GitHub never shows it again, and only this repo's workflows can use it. (One
   named `PRIMA_KEYSTORE_BASE64`, from before, works too.)

Optional: under **Variables**, `PLAYGROUND_SIGNING_SHA256` with the key's SHA-256 fingerprint, so a file signed by any other key
is never published. The notes of every release give the fingerprint it was signed with.

## How an app describes its release

`release.json` in the app's folder, read by [release-app.sh](release-app.sh); a new app adds one, and the workflow finds it:

```json
{
  "name": "Workout",
  "tag": "workout",
  "tests": ["Workout/tests/AdaptiveWorkout.Core.Tests"],
  "builds": [
    { "project": "Workout/src/AdaptiveWorkout.Phone/AdaptiveWorkout.Phone.csproj", "asset": "workout.apk", "runtimes": ["android-arm64"] },
    { "project": "Workout/src/AdaptiveWorkout.Watch/AdaptiveWorkout.Watch.csproj", "asset": "workout-watch.apk", "runtimes": ["android-arm", "android-arm64"] }
  ],
  "paths": ["Workout", "Kit", "Directory.Build.props", "Directory.Build.targets", "global.json"]
}
```

- **tests** run first; a failing one stops the app's release.
- **builds** are Release builds, signed, with versionName 1.0.N (the release) and versionCode 1, so any build installs over any
  other. **runtimes** are the chips: arm64 for the phone and the tablet, and 32-bit ARM too for the Galaxy Watch, which runs
  32-bit Android. Leaving out x86, which only emulators use, makes Prima's file 53 MB rather than 83.
- **paths** are what the app is built from: a change under one since the last release means a new one.

## Trying it without publishing

The same script, with the debug key on this machine, builds and checks everything but doesn't publish:

```bash
VERSION=1.0.0 KEYSTORE="$HOME/.local/share/Xamarin/Mono for Android/debug.keystore" STORE_PASS=android KEY_PASS=android KEY_ALIAS=androiddebugkey ANDROID_HOME=/opt/android-sdk JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64 OUT=/tmp/release DRY_RUN=true bash tools/release/release-app.sh release Music
```

## Installing a release by hand

The apps install their own updates. By hand: open the release on the device's browser, download its file and open it; Android
offers **Update**. Or from the PC, with `adb install -r` and the file, which replaces the app and keeps its data, or stops with an
error and changes nothing. The watch, over Wi-Fi: pair once with **Wireless debugging** on the watch, then `adb connect` and
`adb install -r workout-watch.apk`.
