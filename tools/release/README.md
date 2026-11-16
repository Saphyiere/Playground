# Releases

Every app is released from one click: the repo's **Actions › Release › Run workflow**
([the workflow](../../.github/workflows/release.yml)). Each app that changed since its last release gets a GitHub release of its
own, **Prima 1.0.N** or **Workout 1.0.N**, with its app files, and the changes since the last one in its notes. An app that didn't
change is skipped, Markdown aside; tick **Release every app** to release them all anyway. The apps update themselves from these
releases, through the Kit's [Playground.Updates](../../Kit/Playground.Updates/README.md).

To release one app, set **Release only this app folder** to its folder, for example **Music** for Prima. Leave it empty to
keep the normal all-changed-apps behavior. The folder selector also applies when **Release every app** is checked.

Every file is signed with the devices' key, the one every build on the PC is signed with too
([One key for every build](#one-key-for-every-build)). So a release installs over a copy from Visual Studio as an update and keeps
its data (and the other way round), and the watch app still pairs with the phone's, which needs the same key.

## One key for every build

Android installs an app over the copy already on a device, as an update that keeps its data, only when both are signed with the
same key. So every build of every app is signed with one key, the devices' key: the PC's .NET for Android debug key,
`%LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore` (alias `androiddebugkey`, password `android`), whose certificate's
SHA-256 is `c22578f9e77d3b4c1bae27db455535dce8aff61aa8ca9337429c336ceae07148`. That holds for Debug and Release, from Visual
Studio or the command line, and for the releases here, which the workflow signs with the same file, from its secret.

The shared [Directory.Build.targets](../../Directory.Build.targets) sets it for every app, present and future, over anything an
app's project says. Only signing given on a build's command line, as the workflow gives it, is used instead. .NET for Android
would sign with that file anyway, but it makes a new key in its place when the file is missing, and nothing a new key signs
installs over the apps on the devices. Here a build on Windows stops instead: put the file back from your backup. Keep a copy of
it somewhere safe, since GitHub never shows its secret again. (A cloud session's builds are signed with a debug key of that
machine's own; they never reach the devices.)

## Installing from the PC

On 15 November 2026, a Release build installed from the PC (`dotnet build -c Release -t:Install`) erased Prima's data on the
tablet, though it was signed with the right key. A Release build makes an app bundle (`.aab`) by default, and before installing a
bundle, .NET for Android uninstalls the app. It asks adb to keep the data (`adb uninstall -k`), adb refuses, and it falls back to
`adb shell cmd package uninstall`, which erases it ([dotnet/android#8472](https://github.com/dotnet/android/issues/8472), closed
without a fix). .NET for Android also uninstalls an app, data and all, when Android refuses a build as an update: one signed with
another key, or with a lower versionCode than the device's copy. No setting turns that off. So:

- **Every build makes an app file** (`.apk`), as the workflow always has, not a bundle
  ([Directory.Build.props](../../Directory.Build.props)).
- **The install guard** ([tools/build/InstallGuard.targets](../build/InstallGuard.targets)) runs before every install from the
  PC: Visual Studio's Deploy and Run, `dotnet build -t:Install` and `dotnet run`. It compares the build with the app on the
  connected device and stops the build, with nothing installed and a message saying why, when installing would uninstall the app:
  when the device's copy is signed with another key (the message names both), has a newer versionCode, or the build would install
  a bundle or has `AndroidPreserveUserData` set to false. Before any of that, and whatever the device holds, it stops an install
  that isn't signed with the devices' key (its certificate's SHA-256 against `PlaygroundSigningSha256`), naming both keys: a key
  store made afresh while the file was missing would otherwise put a copy on a device that no release could update. Beyond that,
  with no device connected, or the app not on it, it does nothing. It
  reads the device's copy with `adb shell dumpsys package`, which gives its key as a short hash of the certificate
  (`signatures:[12828f8f]` for the devices' key), and the build's key with `apksigner`. When those don't match, it pulls the app
  file from the device and reads its key with `apksigner` before deciding. If it stops a deploy, build with the key the device's
  copy has; to replace the app anyway, back up its data, uninstall it from the device yourself, and deploy again.
- **Release is never built or installed on the PC.** A release is what the workflow builds, tests, signs and tags from what's on
  GitHub, and the apps update themselves from it. To try a change on a device, back up the app's data, then deploy a Debug build
  from Visual Studio: it installs over a release and keeps the data, and the next release installs over it the same way.

## Once: give GitHub the key

It's the PC's debug key, `%LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore`.

1. Copy it as text, in PowerShell:

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("$env:LOCALAPPDATA\Xamarin\Mono for Android\debug.keystore")) | Set-Clipboard
   ```

2. On GitHub, in the repo's **Settings › Secrets and variables › Actions**, add a **New repository secret** named
   `PLAYGROUND_KEYSTORE_BASE64` and paste it in. GitHub never shows it again, and only this repo's workflows can use it. (One
   named `PRIMA_KEYSTORE_BASE64`, from before, works too.)

Optional: under **Variables**, `PLAYGROUND_SIGNING_SHA256` with the key's SHA-256 fingerprint
(`c22578f9e77d3b4c1bae27db455535dce8aff61aa8ca9337429c336ceae07148`), so a file signed by any other key is never published. The
notes of every release give the fingerprint it was signed with.

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

In a cloud session, the same script, with that machine's debug key, builds and checks everything but doesn't publish (on the PC,
Release isn't built: [Installing from the PC](#installing-from-the-pc)):

```bash
VERSION=1.0.0 KEYSTORE="$HOME/.local/share/Xamarin/Mono for Android/debug.keystore" STORE_PASS=android KEY_PASS=android KEY_ALIAS=androiddebugkey ANDROID_HOME=/opt/android-sdk JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64 OUT=/tmp/release DRY_RUN=true bash tools/release/release-app.sh release Music
```

## Installing a release by hand

The apps install their own updates. By hand: open the release on the device's browser, download its file and open it; Android
offers **Update**. Or from the PC, with `adb install -r` and the file, which replaces the app and keeps its data, or stops with an
error and changes nothing. The watch, over Wi-Fi: pair once with **Wireless debugging** on the watch, then `adb connect` and
`adb install -r workout-watch.apk`.
