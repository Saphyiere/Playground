# Playground.Sync

Keeps a full, restorable copy of an app's data somewhere that outlives the app, so an uninstall, a broken update or a bad install
from the PC can't take it. An app says what it keeps, and the `Syncer` does the rest: copies it, only what changed, in the
background; checks the app after an update; and puts a copy back, all of it or none. Plain .NET, no UI: each app shows
`Syncer.Status` in its own look. Android's side is [Playground.Sync.Android](../Playground.Sync.Android/AndroidSync.cs).

## What an app keeps

`SyncOptions.Items`, each copied whole, under its name in the copy (the same on every device):

- **`SyncItem.Database(name, path)`**: a SQLite database, copied with SQLite's online backup while the app has it open, so the
  copy is whole and consistent whatever the app is writing. Never a copy of the file, which could catch a write half done.
  `RequiredTables` are the tables that tell it's this app's: a restore refuses a database without them. `Counted` is what in it is
  the person's own (`SyncCount`: a table's rows, or those a condition picks), which the check after an update counts (below).
- **`SyncItem.Folder(name, path)`**: every file in a folder and the folders in it.
- **`SyncItem.File(name, path)`**: one file, which needn't be there yet.
- **`SyncItem.Custom(name, export)`**: anything else (preferences, say), which the app writes out as files for a copy and reads
  back on restore (`ISyncExport`).

**Secrets never go in a copy.** `Exclude` names files never read, wherever they turn up (a key file waiting to be taken in), and
files on their way (`.tmp`, `.partial`) are left out too. `Secrets` gives the secrets themselves (an API key): every file a copy
reads is searched for each, and a copy that would hold one stops before it writes anything, saying which file held it.

## How a copy is kept

A store holds the copies, laid out the same way wherever it is:

```
copies/20261115T153000123Z.json   a copy's manifest, named for when it was taken (UTC), so the newest sorts last
blobs/3f1c…e9                     each file's content, named by its SHA-256
held.json                         the copies kept from before an update that didn't check out (below)
```

The manifest says what the copy holds: the app and its version, when and why it was taken (`Asked`, `Scheduled`, `BeforeUpdate`,
`AfterUpdate`, `AfterRestore`), each item (a database with each table's rows), and each file with its size, time and SHA-256. A
copy is named a millisecond after the newest before it when the clock is behind that one, so the newest still sorts last, but its
manifest dates it now: a copy once taken with the clock ahead doesn't date the ones after it.

**Only what changed is written.** A file whose size and time are the ones the last copy had keeps its fingerprint without being
read again; a database is snapshotted each time and fingerprinted; and content the store holds already is never written again.
So a copy after a day's practice writes the database and the manifest, and a copy with nothing changed writes the manifest alone.

**A copy that stops part way changes nothing.** Content goes first, each piece whole or not at all (written under a temporary name,
then renamed), then the manifest, then the copies before and the content only they needed. Until the manifest is there, the copy
before is the copy; whatever the one that stopped had written is found and kept by the next. A file that changes as it's being
copied (a log written to) is fingerprinted again and copied as it is now, and one that goes meanwhile (the log turned over, a
piece taken out of the Library) is left out of it, the rest copied. A manifest without its lists of items and files isn't a copy.

**In the background, one at a time.** Each piece of work runs on the thread pool; `Status` says what it's doing and how far
(`Progress`, a sentence and a fraction), and `Changed` is raised from whatever thread the work is on. A copy asked for while
another runs waits its turn.

## Restoring

`RestoreAsync(copy)` puts a copy's data in place of the app's, all of it or none:

1. Every file is fetched beside where it goes and checked against its fingerprint; each database is checked too (it opens
   cleanly, has its `RequiredTables`, and holds the rows its manifest says). Nothing of the app's is touched yet.
2. Then, while the app leaves its data alone (`WhileRestoring`: Prima runs it on its database's lane with its connections closed),
   each item's copy goes in at once, and if one can't, those before it go back. A database's journal goes with it.
3. Only the items the copy has are replaced: a database on its own replaces the database alone. A fresh copy is taken after.

`RestoreAsync` throws only when nothing was restored. Once the copy is in place it's restored, whatever comes after: when the copy
restored was held and can't be let go, or the app's side of `WhileRestoring` fails once the work is done, the status says the copy
was restored, and why it's still held.

Copies to restore come from the app's own store (`FindCopiesAsync(Store)`), a folder the person picks (`ISyncPicker`: a store of
an install before, or one pushed from a PC), an export, or a database file on its own, such as a backup from before copies were
kept whole (`OpenFileAsync`).

`ExportAsync(folder)` writes everything into one archive to send on: the files under their own names and the manifest beside
them, each fingerprinted and searched for the secrets again as it goes in, so one written to meanwhile goes in as it is then. It's
named for the time on the device (`Prima 2026-11-15 1530.zip`), in the same digits whatever its calendar, and restores like any
copy.

## Updates

The updater ([Playground.Updates](../Playground.Updates/README.md)) takes a full copy through the app's `IUpdateSafeguard`, which
`AddSync` registers, before it hands an update to Android's installer: if the copy can't be kept, nothing is installed. Then
`StartAsync`, called as the app starts:

- **The first start after an update** (the newest copy was taken before an update, on another version) checks at once, before
  the app's screens can change anything, that its data holds all of the person's the copy did: each database opens cleanly and
  has at least the rows of what's `Counted` as theirs (every table, without it), and every file in its folders is there. What the
  app puts in and takes out itself (Prima's bundled pieces) isn't counted, so it's never taken for data lost; a copy taken before
  counts were kept is checked by its tables' rows for what's counted whole. Only then, after the wait, is a fresh copy taken,
  in its place. If it doesn't hold all of it, that copy is **held** (`held.json`, `Status.Held` with why): kept
  whatever copies come after, until it's restored or let go (`ReleaseHeldAsync`). A fresh copy is taken all the same, beside it.
  A later update that loses data too holds its own beside it: a store holds as many as there are (`Status.Holding`, newest
  first, `Status.Held` the newest's), and restoring one lets go of it only when it's held in this install's own store, so one
  found in another install's folder leaves this install's held. If a copy can't be held (the store can't be written to just
  then), the status and the log say so, and no copy is taken in its place until the next start checks again.
- **Otherwise**, a copy is taken when there's none yet, the last is older than `RefreshAfter` (two days), or it's dated after now
  (taken with the clock ahead, which would otherwise hold off every copy until the real date caught up with it).

A copy waits `StartDelay` (15 seconds) first, so the app's first screen isn't slowed; the check after an update doesn't.

## Where copies go

An `ISyncStore`: named objects, each written whole or not at all. The first store is the app's own, whose newest copy the status
shows; copies go to each, and one that can't be used just then (a cloud drive, not signed in) is left out that time.

- **`FolderStore`**: a folder. On Android (`AddAndroidSync`), it's this install's folder in the device's shared storage,
  `Documents/<App> backups/Installed <when>`, named for when the app was installed (UTC). Android 11 and later let an app write
  there without asking and read back what it wrote, and an uninstall doesn't reach it. An app installed again isn't the app that
  wrote the files before: Android won't let it read or replace them by path. So each install keeps its copies in a folder of its
  own, and the copies of an install before are opened with the system's folder picker (`DocumentTreeStore`, through Android's
  documents provider), which reads whatever folder the person picks. The app keeps its access to a picked folder (a persisted
  grant): what the picker gives lasts only while its screen is up, and a copy is read later, when it's restored. Android 10 and
  earlier keep copies in the app's own folder, which an uninstall deletes, and say so.
- **A PC.** Not a store of its own: the device's folder is already one a PC reaches over USB, and adb is the PC's to drive, so the
  PC pulls. [tools/sync/pull-copy.ps1](../../tools/sync/pull-copy.ps1) finds the newest copy over adb, pulls only what the PC
  doesn't have yet, and writes the copy's files out under their own names; `-From` reads a folder copied off in Explorer. A second
  copy on the device itself, in `Android/data/<app>` where `errors.log` is, would guard against nothing new: an uninstall deletes
  that folder with the app. To put a copy back, push its store folder into `Documents/<App> backups` and pick it in the app.
- **Google Drive, later.** It fits the same interface: `ConnectAsync` signs in (Android's Credential Manager, then an
  authorization for Drive's app data folder, which may ask the person, and false until it's given), `ListAsync` lists the app's
  folder, `WriteAsync` uploads (one request, so whole or not at all), `OpenReadAsync` downloads, `DeleteAsync` deletes. Registered
  as a second store, it gets each copy after the device's own, and its copies are found and restored like any other's.

## Using it in an app

1. **References.** The app's UI project references `Kit/Playground.Sync`; its host references `Kit/Playground.Sync.Android`, which
   brings the picker's activity into the app's manifest by itself.
2. **The app** says what it keeps, from its services (Prima's is `Music/src/Prima.UI/Storage/PrimaSync.cs`):

   ```csharp
   services.AddSync(sp => new SyncOptions("Prima")
   {
       Items =
       [
           SyncItem.Database("prima.db", databasePath) with { RequiredTables = ["Setting", "Attempt"], Counted = [SyncCount.Rows("Attempt"), new("Piece (imported)", "Piece", "Origin = 'import'")] },
           SyncItem.Folder("library", filesPath),
       ],
       Exclude = [AssistKey.FileName],
       Secrets = async _ => [.. new[] { await sp.GetRequiredService<IAssistKeyStore>().GetAsync() }.OfType<string>()],
       Version = sp.GetService<IInstaller>()?.InstalledVersion ?? "",
       WhileRestoring = store.WhileClosedAsync,
   });
   ```

3. **The host** registers the device's store and pickers with its other services, and starts the work on start:

   ```csharp
   builder.Services.AddAndroidSync("Prima backups");
   // after builder.Build():
   _ = app.Services.GetRequiredService<Syncer>().StartAsync();
   ```

   Its `UpdateOptions` need no `DataFiles`: the updater takes the full copy instead.
4. **The screens** show `Status` (the last copy, progress, a held copy, a sentence on how it went) and redraw on `Changed`; Back up
   now is `CopyAsync()`, Restore lists `FindCopiesAsync(Store)` and what `ISyncPicker` and `OpenFileAsync` find, then
   `RestoreAsync`, and Export is `ExportAsync` handed to the share sheet. Prima's are in `Music/src/Prima.UI/Components/DataCopies.razor`.
5. **The preview host** registers a `FolderStore` in a folder of its own and a stand-in `ISyncPicker` (Prima's, in its Program.cs).

Workout, next: `adaptiveworkout.db` and its Claude key's file excluded and key searched for, `AddAndroidSync("Workout backups")`
in its phone host, its `DataFiles` gone, and its Settings showing the copies in Logbook and Telemetry.

## Build and test

```bash
dotnet test --project Kit/Playground.Sync.Tests
```
