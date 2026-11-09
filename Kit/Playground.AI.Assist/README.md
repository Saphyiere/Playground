# Playground.AI.Assist

How the apps ask Claude things: a trainer for the workout app, a practice teacher and PDF reading for Prima. It's a plain .NET
library with no UI. [docs/claude-research.md](../../docs/claude-research.md) has the research behind it.

- **Every question is a task:** an input record in, an answer record out, its instructions and its checks.
- **The reply always parses.** The API holds Claude's reply to a JSON schema made from the answer record (structured outputs).
- **The app gets one shape back,** `Answer<T>`. Check `Ok`: when it's true, `Value` is the answer; when it's false, `Summary`
  says why in a sentence for the screen, and `Kind` says what kind of trouble it was (no key, out of credit, offline…).

```csharp
public sealed record Lift([property: Description("One of the owner's exercises, by id.")] int ExerciseId, double Weight, int Reps, string Why);
public sealed record NextSession(IReadOnlyList<Lift> Lifts);

public sealed class AskTheTrainer : AssistTask<TrainerInput, NextSession>
{
    public override string Name => "workout.next-session";
    public override int Version => 1;
    public override string Instructions => "…";  // what Claude is told, after the shared preamble
    public override IReadOnlyList<string> Check(TrainerInput input, NextSession answer) => [.. /* problems, as sentences */];
}

Answer<NextSession> answer = await claude.AskAsync(new AskTheTrainer(), input, ct);
if (answer.Ok) Prefill(answer.Value); else Show(answer.Summary);
```

## What's in it

```
AssistTask.cs     A task: its name and version, instructions, model, effort, token limit, attachments (images, PDFs) and checks.
Answer.cs         What comes back: Answer<T> (Ok, Kind, Value, Summary, Notes, Usage), and Reply<T>, the envelope Claude writes.
AssistSchema.cs   The JSON options records travel with, and the schema made from a record, by the API's rules.
Claude.cs         IClaude and Claude, which asks; ClaudeModels, the models and their prices.
AssistKey.cs      IAssistKeyStore, where a device keeps its key, and getting the key in from a file.
AssistLog.cs      IAssistLog, where a host keeps its calls, and two simple ones.
```

## What happens when an app asks

1. **The request.** The system prompt is the shared preamble (`Claude.Preamble`) and then the task's instructions. One user
   message holds the task's attachments, then `{"task", "version", "today", "input"}` as JSON. Records travel with
   `AssistSchema.Json`: camelCase names and enum values, and the same bytes for the same data.
2. **The answer's shape.** `output_config.format` is the schema of `Reply<TAnswer>`: `status` (answered, needMore or cannot),
   `summary`, `notes` and the task's `answer`.
3. **Fallbacks.** On Opus and Sonnet, a refused request is answered by another model on the API's side (the
   `server-side-fallback-2026-07-01` beta). Haiku has no such fallback.
4. **Reading the reply.** The reply streams, and the SDK puts it back together (`CreateStreaming(...).Aggregate()`): a long
   answer, thinking and all, can take longer than a request may wait for a whole reply, and a stream keeps arriving. A task that
   needs more than the 32,000 tokens it gets says so in `MaxTokens` (Prima's reading of a system takes 64,000). A refusal, a
   reply that ran out of tokens, and a reply that doesn't parse are each a kind of their own. `needMore` and `cannot` come back
   with Claude's summary and notes.
5. **The checks.** An answer that fails the task's checks is asked for once more, with its problems listed. If it fails again,
   the kind is `CheckFailed`.
6. **The log.** Every call goes to `IAssistLog`: the task, model, question, reply, tokens, cost and the API's message id.
7. **When the request fails.** The API's errors become kinds: `KeyRejected`, `OutOfCredit`, `SpendLimit`, `Busy`, `Offline` and
   `Failed`, and a stream that breaks off with an error partway is `Busy`. The client retries a busy or dropped request twice on
   its own.

## Records Claude answers in

These are the API's rules for structured outputs. `AssistSchema.For` refuses a record that breaks one, and says where.

- **Every property is required, and objects are closed.** The schema does both. A property that may be missing is a list that
  may be empty, or an enum with a `none`, rather than null.
- **No dictionaries, no `object` or `JsonElement`, and no record that contains itself.**
- **At most 16 properties that can be one of several types**, and a nullable property counts. The envelope's `answer` is one.
- **No bounds in the schema.** Number and length bounds aren't taken: write them in the `[Description]` and check them in the
  task's `Check`.
- **Describe properties with `[Description]`.** That's what Claude reads about a field. Don't ask for its reasoning in a field,
  which can trip a refusal; a short `why` is fine.
- **Enum values** arrive in camelCase and are read back ignoring case.

## Using it in an app

1. **References.** The app's Core references this project, for its tasks. The host references it to wire it in. Add it to the
   app's solution.
2. **A key store** in the host, on MAUI's secure storage:

   ```csharp
   sealed class SecureKeyStore : IAssistKeyStore
   {
       const string Name = "claude-api-key";

       public async Task<string?> GetAsync()
       {
           try { return await SecureStorage.Default.GetAsync(Name); }
           catch (Exception) { SecureStorage.Default.Remove(Name); return null; }  // restored from a backup this device can't decrypt
       }

       public Task SetAsync(string key) => SecureStorage.Default.SetAsync(Name, key);

       public Task RemoveAsync() => Task.FromResult(SecureStorage.Default.Remove(Name));
   }
   ```
3. **Registration:** `builder.Services.AddSingleton<IAssistKeyStore, SecureKeyStore>()`, the app's `IAssistLog` (kept in its
   SQLite store, so Settings can show what this month cost), then `builder.Services.AddAiAssist()`.
4. **The key file at start.** Take a key file pushed to the device and show what happened in Settings:

   ```csharp
   var folder = Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath;
   var import = await AssistKey.ImportAsync(Path.Combine(folder, AssistKey.FileName), keys);
   ```
5. **The internet.** Add `<uses-permission android:name="android.permission.INTERNET" />` to the manifest if it isn't there.
   Debug builds add it on their own and Release builds don't, so without it, asking Claude works from Visual Studio and fails on
   the installed copy.
6. **Backups.** Leave secure storage out of Android's backups, and nothing else. It's encrypted to the device, so a restored copy
   can't be read anyway. On `<application>`, set `android:fullBackupContent="@xml/auto_backup_rules"` (Android 11 and before)
   and `android:dataExtractionRules="@xml/data_extraction_rules"` (Android 12 on, for cloud backups and moving to a new
   device). In `Platforms/Android/Resources/xml/`, write the package name out in full: `${applicationId}` isn't filled in there.
   Don't add an `<include>`, which would back up only what it names and leave out the app's own data. For Prima:

   `auto_backup_rules.xml`:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <full-backup-content>
       <exclude domain="sharedpref" path="com.saphyiere.prima.microsoft.maui.essentials.preferences.xml" />
   </full-backup-content>
   ```

   `data_extraction_rules.xml`:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <data-extraction-rules>
       <cloud-backup>
           <exclude domain="sharedpref" path="com.saphyiere.prima.microsoft.maui.essentials.preferences.xml" />
       </cloud-backup>
       <device-transfer>
           <exclude domain="sharedpref" path="com.saphyiere.prima.microsoft.maui.essentials.preferences.xml" />
       </device-transfer>
   </data-extraction-rules>
   ```

   For the phone, the name is `com.saphyiere.adaptiveworkout.microsoft.maui.essentials.preferences.xml`.
7. **The key stays in C#.** It never goes in the app's SQLite file or settings (Prima's Back up shares that file), and never to
   the web view or JavaScript.
8. **Asking from the screens.** Screens take `IClaude`. A preview host registers a stand-in that answers from saved replies, so
   the screens can be checked without a key.

## Getting the key onto a device

The key goes onto the device once, as a file, and the app takes it from there.

1. **Make a key.** In the [Claude Console](https://platform.claude.com):
   - Link the organization to your plan (claude.ai → Settings → Billing → API credits), so Ultra's $200 a month lands there.
   - Make a workspace for the apps with a monthly spend limit.
   - Make a personal API key scoped to that workspace.

   The Console shows the key once: keep it in your password manager.
2. **Put it in a file** named `claude-key.txt`, holding nothing but the key, in a folder outside the repo (and outside
   OneDrive).
3. **Copy it to the device** over USB, after opening the app once so its folder exists:

   ```bash
   adb push claude-key.txt /sdcard/Android/data/com.saphyiere.prima/files/
   ```

   For the phone, the folder is `com.saphyiere.adaptiveworkout`.
4. **Delete the file on the PC** with Shift+Delete, so it skips the Recycle Bin.
5. **Open the app.** It moves the key into secure storage and deletes the file on the device. Settings shows the key masked,
   like `sk-ant-…a1B2`, with a Test.
6. **If a device is lost or the key leaks,** disable the key in the Console (it stops at once), make a new one, and push that.

Two things to know:

- **Keep the key out of the repo.** It's public; GitHub's push protection blocks Anthropic keys, and Anthropic revokes keys found
  in public repos.
- **Push it again after a reinstall or a restore.** Secure storage isn't backed up, so a restored app asks for the key again.

## Build and test

```bash
dotnet build Kit/Playground.AI.Assist.Tests/Playground.AI.Assist.Tests.csproj
```

```bash
dotnet test --project Kit/Playground.AI.Assist.Tests
```

The tests stand a fake API in for the network: the request's shape, the replies and their kinds, the checks and the second ask,
the schema rules, and the key file. `ClaudeModels` holds the API's list prices from November 2026; change them when Anthropic
does.

Three more tests ask the real API, and are skipped unless `PLAYGROUND_CLAUDE_KEY` holds a key. They check that the key works,
and that the API takes the request and answers in its schema, on Haiku and on Opus with its fallback. A run costs a few cents.
A cloud session can't hold a key for Anthropic's own API, so run them on the PC, in a PowerShell window you close afterwards.
Paste the key at the prompt, not into the command: PowerShell saves each command line to a history file on disk.

```powershell
$env:PLAYGROUND_CLAUDE_KEY = Read-Host "Claude API key"
```

```powershell
dotnet test --project Kit/Playground.AI.Assist.Tests
```
