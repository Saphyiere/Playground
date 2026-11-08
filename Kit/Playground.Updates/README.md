# Playground.Updates

How every app updates itself from the repo's GitHub releases, which the Release workflow makes
([tools/release/README.md](../../tools/release/README.md)). Plain .NET, no UI; Android's side is
[Playground.Updates.Android](../Playground.Updates.Android/AndroidInstaller.cs). Each app shows the updater in its own look.

- **One `Updater` per app**, holding the `Status` its screens show: what's installed, the newest release, and a sentence on how it
  stands. A copy built on the PC is version 1.0, older than any release (1.0.N), so it's offered the newest.
- **`CheckAsync()`** asks GitHub for the newest release tagged `{tag}-1.0.N` that carries the app's file, drafts and pre-releases
  aside, so another app's releases are never taken for this one's. It asks at most once in 20 hours, unless `evenIfRecent`, so an
  app calls it each time it starts. GitHub's API is used without signing in: 60 requests an hour from one address.
- **`InstallAsync()`** downloads the release with progress, checks it against the size and SHA-256 GitHub gives, checks it's
  signed with the installed app's key, copies the app's data files to Downloads, and hands it to Android's installer, which asks
  the person to confirm. Each step that fails stops it with nothing installed, and a sentence for the screen.

**Why an update can't lose the app's data:** Android replaces an app and keeps its data only when the new copy is signed with the
same key; the updater checks that before installing, and refuses otherwise. The copy in Downloads is there in case of anything
else: an uninstall doesn't reach it.

## Using it in an app

1. **References.** The app's UI project references `Kit/Playground.Updates`, for its screens; its host references
   `Kit/Playground.Updates.Android`, which brings the permission to install apps and its installer's receiver into the app's
   manifest by itself.
2. **The host** registers it with what the app releases as, and checks at start:

   ```csharp
   builder.Services.AddAndroidUpdates(new UpdateOptions("prima", "prima.apk") { DataFiles = [databasePath], BackupFolder = "Prima backups" });
   // after builder.Build():
   _ = app.Services.GetRequiredService<Updater>().CheckAsync();
   ```

3. **The screens** take `Updater`, show its `Status` with Check and Install, and redraw on `Changed`, which can come from any
   thread.
4. **The preview host** registers `AddStandInUpdates(options, "Prima")` instead: made-up releases, and installs that only pretend.
   `StandInUpdates.Show("1.0", 12, notes)` sets what's installed and the newest release, for each screen to be checked.

The first time, Android asks to let the app install apps: the updater opens that setting, and Install works once it's allowed.

## Build and test

```bash
dotnet test --project Kit/Playground.Updates.Tests
```
