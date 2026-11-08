# AdaptiveWorkout

Personal workout app for an Android phone, with heart rate coming from a Samsung Galaxy Watch5 (Wear OS; Wear OS 6 once updated to One UI 8 Watch). Not meant for app stores.

## Layout

The app lives in the `Workout` folder of the [Playground](../README.md) repo; the build settings it shares with the other apps
(`Directory.Build.props`, `Directory.Build.targets`, `global.json`) are at the repo root. Commands below run from `Workout`.

```
build/
  AdaptiveWorkout.Build/    Solution items project (compiles nothing). Shows this folder's top-level files and the repo's
                            shared ones (README, CLAUDE.md, Directory.Build.props, global.json, ...) in Solution Explorer.
                            Put local-dev files here.
docs/                       The app concept and data model (concept.md).
src/
  AdaptiveWorkout.Core/     Business logic (plain .NET 10): next step, plan changes, suggestions, history, schedule, consistency, improvement, calories, maintenance and what workouts burn.
                            Assist/ has Claude's look back: what it's sent about a workout, what it answers, and the checks on it.
  AdaptiveWorkout.UI/       The app's screens (Razor) and storage (SQLite via Dapper). Any host can show them.
  AdaptiveWorkout.Phone/    Phone app: .NET MAUI Blazor Hybrid host for the UI, Android only.
  AdaptiveWorkout.Watch/    Watch app: .NET for Android targeting Wear OS. Shows heart rate and sends it to the phone.
tests/
  AdaptiveWorkout.Core.Tests/   xUnit v3 tests for Core. Business logic only.
```

All bin/obj output goes to `artifacts/` at the repo root (git-ignored, set in the root `Directory.Build.props`).

Open `Workout/AdaptiveWorkout.slnx` in Visual Studio 2026 or Rider.

What the app does and how its data is shaped: [docs/concept.md](docs/concept.md).

## One-time setup (Windows)

1. Install **Visual Studio 2026** with the **.NET Multi-platform App UI development** workload. That brings the .NET 10 SDK, the Android SDK, and a JDK.
   - CLI-only alternative: install the .NET 10 SDK, then `dotnet workload install maui-android`, then
     `dotnet build src/AdaptiveWorkout.Phone -t:InstallAndroidDependencies -p:AndroidSdkDirectory=C:\Android\sdk -p:JavaSdkDirectory=C:\Android\jdk -p:AcceptAndroidSDKLicenses=True`
     (downloads the Android SDK and a JDK into those folders).
2. `adb` lives in the Android SDK's `platform-tools` folder (Visual Studio: `C:\Program Files (x86)\Android\android-sdk\platform-tools`, or **Tools > Android > Android Adb Command Prompt**).

## Hello world on the phone

1. Phone (Pixel): **Settings > About phone**, tap **Build number** 7 times. Newer Pixels show a short Developer options menu even before this, but **USB debugging** and **Wireless debugging** only appear after tapping Build number. Then **Settings > System > Developer options**, turn on **USB debugging** (or **Wireless debugging** if you'd rather not plug in).
2. Plug the phone in and accept the "Allow USB debugging" prompt. `adb devices` should list it.
3. Visual Studio: set **AdaptiveWorkout.Phone** as the startup project, pick the phone in the device dropdown, and press F5.
   CLI: `dotnet build src/AdaptiveWorkout.Phone -t:Run`

The app opens on Today. F5 runs a Debug build, which opens slowly: it compiles each method on the phone the first time it runs, and with the debugger attached it also waits for Visual Studio. (`UseInterpreter` is off in both apps, so Debug builds at least run compiled code rather than Mono's interpreter, which gives up C# Hot Reload on the devices.) **For the copy you use day to day, install Release**: pick **Release** in Visual Studio's configuration dropdown and press **Ctrl+F5**, or run `dotnet build src/AdaptiveWorkout.Phone -t:Run -c Release`. Release compiles the whole app ahead of time (`AndroidEnableProfiledAot` is off), so it opens quickly. It takes a few minutes to build, installs over a Debug build, and keeps the app's data.

Each start writes how long it took to the phone's log, as one line that `adb logcat -s AdaptiveWorkout` shows, like this (numbers made up):

```
Opened in 1200 ms: .NET +300, app +50, window +100, web view +200, Blazor +350, settings +50, Today's data +50, drawn +100
```

Each step's number is the time since the step before it, counting from when Android started the app: `.NET` until the app's own code runs, `app` building the MAUI app, `window` its window and page, `web view` creating the web view, `Blazor` loading the page and starting Blazor in it, `settings` opening the database and reading the theme, `Today's data` Today's reads, and `drawn` Today rendered with them. Android times the start too, from the tap: `adb logcat -s ActivityTaskManager` shows `Fully drawn …: +1s200ms`.

If the app stops as it opens with *No view found for id 0x… for fragment NavigationRootManager_ElementBasedFragment*, the build deployed resource ids that don't match the app's resources. `Directory.Build.targets` stops Visual Studio's background builds from causing that; if it happens anyway, delete the `artifacts` folder and build again. Don't uninstall the app to fix it: that deletes its data.

## Hello world on the watch (no USB needed)

Galaxy Watch4 and newer run Wear OS, so they install apps over Wi-Fi with `adb`.

1. Watch: **Settings > About watch > Software information**, tap **Software version** until developer mode turns on.
2. **Settings > Developer options**: turn on **ADB debugging** and **Wireless debugging**.
3. Put the watch on the same Wi-Fi network as the PC. Wear OS turns the watch's Wi-Fi off whenever it has a Bluetooth link to the phone (the Watch4 and newer have no "always on" Wi-Fi option), so while deploying, turn Bluetooth off on the phone. **Settings > Connections > Wi-Fi** on the watch should then show the network as connected.
4. **Wireless debugging > Pair new device** shows an IP, a pairing port, and a code:
   ```
   adb pair 192.168.1.50:37000      # pairing port, then enter the code
   adb connect 192.168.1.50:41234   # the *other* port shown on the Wireless debugging screen
   adb devices
   ```
5. Visual Studio: set **AdaptiveWorkout.Watch** as the startup project, pick the watch, press F5.
   CLI (with the phone also connected, name the watch): `dotnet build src/AdaptiveWorkout.Watch -t:Run -p:AdbTarget="-s 192.168.1.50:41234"`

Pairing is a one-time step. The connect port usually changes when the watch reboots or Wireless debugging is toggled, so check the screen and repeat `adb connect` when that happens.

## Heart rate: first run

The phone opens the watch app when a workout starts, and the watch sends heart rate back over the Wear OS Data Layer.

1. Deploy both apps from the same PC. The Data Layer only connects apps with the same id and signing key, and Debug and Release builds on one PC both use its debug key. If you ever set up a release keystore, sign both apps with it.
2. Turn Bluetooth back on on the phone after deploying to the watch: the watch and phone talk over that link.
3. Open **AdaptiveWorkout** on the watch once by hand and allow heart rate and notifications. It shows your heart rate right away. Swipe it away when done.
4. Start a workout on the phone (allow its notifications, for the rest timer on the lock screen). The watch wakes up on the heart rate screen, and within a few seconds the phone shows the bpm under the timer. **Finish** closes the watch app.
5. On a break, tap the rest time for a countdown. At zero the watch buzzes and the next set starts, even with the phone locked in a pocket: the watch keeps the time and tells the phone. The watch comes up showing the set; while a set runs, its button is **Stop**, and between sets **Start <next>**.
6. The watch screen stays up for the whole workout, dimmed between glances, instead of going back to the watch face: the workout is an ongoing activity, and its icon on the watch face taps back in.

If the watch doesn't open, it should at least show a "Workout started" notification; tap it. Then check the phone's log with `adb logcat -s AdaptiveWorkout`: `Opening the watch app: done` or `failed (n)` says what the watch's companion app answered. **Open on watch** on the phone tries again.

If an Android build ever fails over output paths, temporarily remove the two artifacts lines in the repo root's `Directory.Build.props` to rule out the `artifacts/` output folder.

## Claude's look back: first run

Finishing a workout opens Claude's look back at it, asked with your Anthropic API key. The key lives in the phone's secure storage,
never in the app's data or the repo; the [AI framework's README](../Kit/Playground.AI.Assist/README.md#getting-the-key-onto-a-device)
has the whole story, from the Console's workspace and spend limit to what to do if the phone is lost.

1. In the Claude Console, make an API key in the workspace for your apps, and save it alone in a file named `claude-key.txt`,
   outside the repo.
2. Open the app once, so its folder is there, plug the phone in and copy the file across:
   `adb push claude-key.txt /sdcard/Android/data/com.saphyiere.adaptiveworkout/files/`. Delete the copy on the PC with Shift+Delete.
3. Open the app: it takes the key into secure storage and deletes the file on the phone. **Settings › Claude** shows the key
   masked; **Test** checks it with Anthropic without spending anything.
4. Finish a workout. The look back takes about half a minute; **Use for next time** takes its plan.
5. Try it in a **Release** build once: Release builds trim the app, and the answer's JSON schema is made from its records, so check
   that a look back still arrives there.

Android's backups leave the key out (`Platforms/Android/Resources/xml`), so after a restore or a reinstall, push the file again.

## Updates from GitHub

The phone and watch apps are released with the other apps, from the repo's **Actions › Release › Run workflow**: a release,
**Workout 1.0.N**, with `workout.apk` (the phone) and `workout-watch.apk`, whenever the app changed since its last one.
[tools/release/README.md](../tools/release/README.md) has the one-time setup and how a release is made. Both are signed with the
key Visual Studio signs with, so a release installs over a copy from Visual Studio as an update, keeping its data, and the watch
still pairs with the phone.

- **The phone** updates itself: **Settings › Updates** shows the newest release, and **Install** checks it, keeps a copy of the
  data in Download/Workout backups, and hands it to Android to confirm (the Kit's
  [Playground.Updates](../Kit/Playground.Updates/README.md)).
- **The watch**, for now, from the PC over Wi-Fi: `adb connect` to the watch, then `adb install -r workout-watch.apk` from the
  release. Whether the watch can install its own updates, the way the phone does, is still to be tried on it.

## Design

The app wears the [Kit](../Kit/README.md)'s two looks, Logbook and Telemetry, switched in **Settings** (the icon at the top right of Today, after Stats). The Kit has the themes' tokens, fonts and shared pieces, and how they work; `src/AdaptiveWorkout.UI/wwwroot/app.css` has this app's own screens, in the same tokens.

- **Logbook** here: the pen ticks each gym day, circles today, and keeps tally strokes for the streak and the sets done; it ticks Hit goal, and brackets a superset down its left edge.
- **Telemetry** here: lime for training, amber for food, blue for body weight, coral for heart rate. The set's time fills a minute of seconds, the rest drains its countdown, calories sit on a ruler with the goal and maintenance marked, and the heart rate shows its last minute as a line.
- **Pieces that draw differently** in this app branch on the cascading theme inside their own component: `SecondsRuler`, the rest and calorie rulers, the heart rate line, the workout header, Today's tiles, the improvement chart (`TrendChart`, on Gym and Stats), and the calorie chart.
- **The phone around the page** follows too (`PhoneThemeSetting`): the status and navigation bar icons go light on a dark theme, and the background behind the page while it loads matches. Both start in the last theme, kept in the phone's preferences. The splash and app icon stay Logbook's (vermilion on paper), since Android draws them before the app can choose. They're SVGs in `src/AdaptiveWorkout.Phone/Resources`.

The watch wears the phone's theme too. The phone sends its theme's id with each workout, when the watch says hello, and when it changes; the watch keeps the last one. `src/AdaptiveWorkout.Watch/WatchTheme.cs` has each theme's colors (from the Kit's `kit.css` tokens) and fonts: the same four, cut down to Latin as TTFs in `Resources/font` (Newsreader at a fixed optical size), with their licenses in `src/AdaptiveWorkout.Watch/OFL-*.txt`. Dimmed, the watch goes to light print on black, whatever the theme, to spare the screen. To add a theme there, add its `WatchTheme` and the fonts it needs.

## Tests

```
dotnet test --project tests/AdaptiveWorkout.Core.Tests
```

Tests are only for business logic in `Core`: no UI, wording, or plumbing tests. The look back's tests never call Claude: they check
what the app works out and sends, and the checks on an answer.

The preview host (`tools/AdaptiveWorkout.Preview`, see the repo's CLAUDE.md) has a stand-in Claude that answers a look back from
the workout itself after a moment: `/claude/key` puts a key on the "phone" (`?clear=true` takes it off), `/claude/file` leaves a key
file to be found, and `/claude/script?delay=8000&fail=offline` slows it down or makes it fail. `/seed/lookback` sets up a back day
ready for **Finish**; `/seed/plateau` has rows gone flat, with a back day open; `/seed/checks` has a half-logged day and a weigh-in far off the line.

## Why these choices

**Phone: .NET MAUI Blazor Hybrid.** MAUI is Microsoft's supported C# phone framework; it replaced Xamarin, which was retired in 2024. The Blazor Hybrid flavor writes the UI as Razor components (HTML/CSS) inside the native app. That's what makes a web version cheap later: move `Components/` into a Razor class library and host it from a Blazor web app too (the `maui-blazor-web` template shows the setup).

**Watch: C#, not Kotlin.** .NET for Android can build Wear OS apps (`dotnet new androidwear`), so the watch app stays in C# and shares `Core` with the phone. Kotlin with Jetpack Compose is Google's first-class path and has better samples. Switch only if the C# bindings get in the way.

**Getting heart rate off the watch**, best first:

| Option | Live? | Watch code? | Notes |
|---|---|---|---|
| Own watch app reads the HR sensor and sends it to the phone over the Wear OS Data Layer | Yes, about 1/sec | Yes (small) | **Built.** Sensor via Android `SensorManager` in a foreground service (Health Services' .NET binding is still prerelease); phone link via `Xamarin.GooglePlayServices.Wearable`. Both apps share an `ApplicationId` and signing key, which the Data Layer requires. The phone opens the watch app the way `RemoteActivityHelper` does (no .NET binding for it): a broadcast to the watch's companion app on the phone. |
| Third-party watch app (e.g. *Heart for Bluetooth*) that makes the watch act like a Bluetooth chest strap; phone reads the standard BLE Heart Rate service | Yes | No | Fastest "just a connector". The phone code would also work with any HR strap. Depends on someone else's app. |
| Health Connect on the phone (Samsung Health syncs into it) | No | No | Data arrives in batches, often roughly per minute and only after Samsung Health syncs. Fine for history, not for a live workout. |
| Samsung Health Sensor SDK | Yes | Yes | Needs Samsung partner approval. Skip. |

Heart rate permissions: Wear OS 6 (Android 16) replaces `BODY_SENSORS` with `android.permission.health.READ_HEART_RATE` for apps targeting API 36. The watch app declares both and asks for whichever the watch uses.

`Xamarin.GooglePlayServices.Wearable` is pinned to 119.0.0.4 in both apps: newer versions pull in AndroidX libraries newer than MAUI 10's, and the phone build fails on duplicate classes.

## Next steps

[docs/plan.md](docs/plan.md) has what's next, in order. **Build order** in [docs/concept.md](docs/concept.md) has what's built.
