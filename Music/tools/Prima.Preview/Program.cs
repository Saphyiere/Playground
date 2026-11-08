// Preview host: Prima's real screens (Prima.UI) in a browser, as Blazor Server, against sample data in a SQLite file, so a cloud
// session can render and screenshot them (see CLAUDE.md). /seed/{scenario} sets up the data, and /keys/... plays a simulated piano.
using System.Globalization;
using Microsoft.Data.Sqlite;
using Playground.AI.Assist;
using System.Text.Json;
using Prima.Core.Generation;
using Prima.Core.Keyboard;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.Preview;
using Prima.Preview.Components;
using Prima.UI;
using Prima.UI.Host;
using Prima.UI.Practice;
using Prima.UI.Storage;

var dbPath = Path.Combine(AppContext.BaseDirectory, "preview.db");
var builder = WebApplication.CreateBuilder(args);
// The MEI the browser makes from a MusicXML file comes back over SignalR, and a piece's is far bigger than its 32 KB default.
builder.Services.AddRazorComponents().AddInteractiveServerComponents().AddHubOptions(o => o.MaximumReceiveMessageSize = 16 * 1024 * 1024);
builder.Services.AddSingleton<IClock, StopwatchClock>();
builder.Services.AddSingleton<SimulatedKeyboard>();
builder.Services.AddSingleton<IKeyboard>(sp => sp.GetRequiredService<SimulatedKeyboard>());
// The Library's music (Music/library), and a file picker /files/next fills (?path=…, repeated for several files).
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
builder.Services.AddSingleton<ILibraryBundle>(new FolderBundle(Path.Combine(root?.FullName ?? ".", "Music", "library")));
builder.Services.AddSingleton<PreviewFiles>();
builder.Services.AddSingleton<IFiles>(sp => sp.GetRequiredService<PreviewFiles>());
builder.Services.AddSingleton<ISystemBars, NoSystemBars>();
// Claude answers from saved replies (PreviewClaude), never the API; the key is kept in memory (AddPrima's default).
builder.Services.AddSingleton<PreviewClaude>();
builder.Services.AddSingleton<IClaude>(sp => sp.GetRequiredService<PreviewClaude>());
builder.Services.AddPrima(dbPath);

var app = builder.Build();
// An address no page has shows the app's own not-found page, as the tablet's router does.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Routes).Assembly);

// Clears the data, then fills it for a scenario (in Seeds, below): "empty" is the first run, "fresh" just after the welcome,
// "today" six weeks in with today half done, "summary" today finished, "months" three months in.
app.MapGet("/seed/{scenario}", async (string scenario, PrimaStore store, PrimaSettings settings) =>
{
    await store.GetSettingsAsync(); // ensures the schema exists
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        foreach (var table in new[] { "Setting", "Track", "NoteCard", "NoteAnswer", "Attempt", "PracticeDay", "ClaudeCall" }) new SqliteCommand($"DELETE FROM {table}", connection).ExecuteNonQuery();
    }
    await store.LoadAsync(settings);
    await Seeds.Fill(scenario, store, settings);
    return Results.Text(scenario);
});

// The simulated piano: plugged in or out, keys and pedals, Active Sensing, and taps on the click's beats for the timing calibration.
app.MapGet("/keys/connect", (SimulatedKeyboard keys) => { keys.Plug(true); return Results.Text("connected"); });
app.MapGet("/keys/disconnect", (SimulatedKeyboard keys) => { keys.Plug(false); return Results.Text("disconnected"); });
app.MapGet("/keys/play", (string notes, int? velocity, int? hold, SimulatedKeyboard keys) =>
{
    keys.Play(notes.Split(',').Select(n => int.Parse(n, CultureInfo.InvariantCulture)), velocity ?? 80, hold ?? 300);
    return Results.Text($"played {notes}");
});
app.MapGet("/keys/pedal", (int controller, int value, SimulatedKeyboard keys) => { keys.Pedal(controller, value); return Results.Text("pedal"); });
app.MapGet("/keys/sense", (int? seconds, double? jitter, SimulatedKeyboard keys) => { keys.Sense(seconds ?? 70, jitter ?? 1.2); return Results.Text("sensed"); });
app.MapGet("/keys/tap-beats", (int from, int count, double? offset, double? spread, Click click, SimulatedKeyboard keys) =>
{
    var random = new Random(5);
    for (var beat = from; beat < from + count; beat++)
    {
        var at = click.BeatTime(beat) + (long)((offset ?? -20) * 1e6 + (random.NextDouble() - 0.5) * 2 * (spread ?? 15) * 1e6);
        keys.PlayAt(NoteName.MiddleC, 70, at, at + 150_000_000);
    }
    return Results.Text($"tapped {count} beats");
});
// Plays the run in progress: a share of notes right (the rest a key away), a lean and a spread in ms, and optionally nothing from
// a bar on (a stop), a share of notes with a slip (the key above, just before the right one), and a share let go at 60% of their
// length. In one go, ending the run, so a summary can be screenshotted without waiting; or with "live", each note when it's due,
// as from the piano, so the sheet marks them during the run.
app.MapGet("/keys/perform", (double? right, double? lean, double? spread, int? stop, bool? live, double? slips, double? early, SessionRegistry registry, SimulatedKeyboard keys, IClock clock) =>
{
    var session = registry.Current;
    if (session?.Plan is null) return Results.Text("no run in progress");

    var random = new Random(9);
    var beatsPerBar = session.Plan.BeatsPerBar;
    var notes = session.Expected
        .Where(e => stop is null || e.Beat < (stop.Value - 1) * beatsPerBar)
        .SelectMany(e =>
        {
            var midi = random.NextDouble() < (right ?? 0.95) ? e.Midi : e.Midi + (random.NextDouble() < 0.5 ? 1 : 2);
            var on = session.TimeAt(e.Beat) + (long)(((lean ?? -15) + (random.NextDouble() - 0.5) * 2 * (spread ?? 12)) * 1e6);
            var (start, end) = (session.TimeAt(e.Beat), session.TimeAt(e.Beat + e.Beats));
            var off = early is { } share && random.NextDouble() < share ? on + (long)((end - start) * 0.6) : end - 30_000_000;
            var note = new Prima.Core.Scoring.PlayedNote(midi, on, off, 60 + random.Next(20));
            return slips is { } slip && midi == e.Midi && random.NextDouble() < slip ? [new(midi + 1, on - 60_000_000, on - 20_000_000, 60), note] : new[] { note };
        }).ToList();
    if (live != true)
    {
        session.Perform(notes);
        return Results.Text($"performed {notes.Count} notes");
    }

    _ = Task.Run(async () =>
    {
        foreach (var note in notes.OrderBy(n => n.OnNs))
        {
            var wait = (note.OnNs - clock.NowNs) / 1e6;
            if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait));
            keys.PlayAt(note.Midi, note.Velocity, note.OnNs, note.OffNs);
        }
    });
    return Results.Text($"playing {notes.Count} notes live");
});
// Answers the drill item on screen: right, or wrong.
app.MapGet("/keys/answer", (bool? right, SessionRegistry registry, SimulatedKeyboard keys) =>
{
    if (registry.DrillAnswer is not { } answer) return Results.Text("no drill item");

    foreach (var step in answer(right ?? true)) keys.Play(step, 70, 200);
    return Results.Text("answered");
});
// The glossary's examples, each entry's id and the MEI of its example, for checking they engrave.
app.MapGet("/glossary/examples", () => Results.Json(Prima.UI.Glossary.GlossaryEntries.All.Select(e => new { e.Id, e.Marks, e.Example })));
app.MapGet("/keys/sent", (SimulatedKeyboard keys) => Results.Text(string.Join('\n', keys.Sent.Select(s => $"{s.AtNs} {Convert.ToHexString(s.Message)}"))));
// The next file "picked" for an import: a file on this machine, by path.
// The stand-in Claude: a key on the tablet or not (one ending a1B2, as in the mock-ups), a key file for Settings to find, how it
// answers (?delay=ms, ?fail=OutOfCredit&after=2), and three pieces read this month for Settings' ledger.
app.MapGet("/claude/key", async (bool? clear, IAssistKeyStore keys) =>
{
    if (clear == true) await keys.RemoveAsync();
    else await keys.SetAsync("sk-ant-api03-preview-only-not-a-real-key-a1B2");
    return Results.Text(clear == true ? "no key" : "key set");
});
app.MapGet("/claude/file", async (string? text, ClaudeKeyFile file) =>
{
    await File.WriteAllTextAsync(file.Path, text ?? "sk-ant-api03-preview-only-not-a-real-key-c3D4");
    return Results.Text(file.Path);
});
app.MapGet("/claude/script", (int? delay, string? fail, int? after, PreviewClaude claude) =>
{
    claude.Reset();
    if (delay is { } ms) claude.DelayMs = ms;
    if (fail is not null) (claude.Fail, claude.FailAfter) = (Enum.Parse<AnswerKind>(fail, ignoreCase: true), after ?? 0);
    return Results.Text($"delay {claude.DelayMs} ms, failing with {claude.Fail?.ToString() ?? "nothing"} after {claude.FailAfter}");
});
app.MapGet("/claude/spend", async (PrimaStore store) =>
{
    var pieces = await store.GetPiecesAsync();
    var today = DateTimeOffset.Now;
    foreach (var (number, cost, daysAgo) in new[] { ("No. 13", 0.74m, 0), ("No. 17", 0.81m, 2), ("No. 8", 0.62m, 6) })
    {
        if (pieces.FirstOrDefault(p => p.IsPdf && p.Title.EndsWith(number, StringComparison.Ordinal)) is not { } piece) continue;
        await store.AddClaudeCallAsync(new AssistCall(today.AddDays(-Math.Min(daysAgo, today.Day - 1)), "prima.read-system", 1, "preview", 1, AnswerKind.Answered,
            "Read.", "{}", "{}", 1, new Usage(0, 0, 0, 0, cost, TimeSpan.Zero), "preview", ""), piece.Id);
    }
    return Results.Text("spent");
});

app.MapGet("/files/next", (string[] path, PreviewFiles files) =>
{
    files.Next = [.. path.Where(File.Exists).Select(p => (Path.GetFileName(p), File.ReadAllBytes(p)))];
    return Results.Text(files.Next.Count == 0 ? "no such file" : $"next pick: {string.Join(", ", files.Next.Select(f => f.Name))}");
});
// Plays a piece's notes in order at a tempo, as if from the piano: for following along. From event "from", "count" of them.
app.MapGet("/keys/piece", async (string id, int? from, int? count, double? bpm, int? skip, PrimaStore store, SimulatedKeyboard keys) =>
{
    if (await store.GetMeiAsync(id) is not { } mei) return Results.Text("no such piece");

    var events = Prima.Core.Library.ScoreEvents.From(Prima.Core.Music.MeiReader.Read(mei)).Skip(from ?? 0).Take(count ?? 1000).ToList();
    if (events.Count == 0) return Results.Text("nothing to play");

    // Each chord when it's due, as a player would: the follower takes notes as they come.
    var start = events[0].Beat;
    var secondsPerBeat = 60 / (bpm ?? 72);
    var chords = events.Where((_, i) => skip is null || i % skip != skip - 1).Select(e => (At: TimeSpan.FromSeconds((e.Beat - start) * secondsPerBeat + 0.2), e.Pitches)).ToList();
    _ = Task.Run(async () =>
    {
        var began = DateTime.UtcNow;
        foreach (var (at, pitches) in chords)
        {
            var wait = at - (DateTime.UtcNow - began);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            keys.Play(pitches, 70, (int)(secondsPerBeat * 800));
        }
    });
    return Results.Text($"playing {chords.Count} chords from bar {events[0].Bar}");
});

app.Run();

/// <summary>Sample history for the preview: weeks of practice that get gradually better, the way the screens expect it.</summary>
static class Seeds
{
    public static async Task Fill(string scenario, PrimaStore store, PrimaSettings settings)
    {
        if (scenario == "empty") return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        (settings.Welcomed, settings.Cue, settings.TimingOffsetMs, settings.TappingSpreadMs, settings.CalibratedOn) = (true, "When I sit down after dinner, I’ll open Prima", 18, 14, today.AddDays(-20));
        await store.SaveAsync(settings);
        if (scenario == "fresh") return;

        var weeks = scenario == "months" ? 13 : 6;
        var rng = new Rng(11);
        var first = today.AddDays(-7 * weeks);
        var notes = new (Clef Clef, int Midi)[] { (Clef.Treble, 60), (Clef.Treble, 67), (Clef.Treble, 72), (Clef.Bass, 53), (Clef.Bass, 48), (Clef.Bass, 43), (Clef.Treble, 64), (Clef.Treble, 65), (Clef.Treble, 69), (Clef.Treble, 71), (Clef.Bass, 50), (Clef.Bass, 52), (Clef.Bass, 55), (Clef.Treble, 74), (Clef.Treble, 76), (Clef.Bass, 45), (Clef.Treble, 62), (Clef.Bass, 57), (Clef.Treble, 77), (Clef.Bass, 41) };
        var cards = new Dictionary<(Clef, int), NoteCard>();
        var scale = Technique.Scale(new Key(0), ScaleKind.Major, 2, HandUse.Right);
        var arpeggio = Technique.Arpeggio(new Key(1), 2, HandUse.Right);
        for (var day = first; day < today; day = day.AddDays(1))
        {
            // Five or six days a week, missing the odd one.
            if (rng.Chance(0.24)) continue;

            var progress = (day.DayNumber - first.DayNumber) / (double)(7 * weeks);
            var at = day.ToDateTime(new TimeOnly(19, 10));
            await store.AddMinutesAsync(day, 13 + rng.Int(11));
            await Passages(store, rng, day, at.AddMinutes(12), progress, 4);
            await Rhythm(store, rng, day, at.AddMinutes(8), progress);
            await TechniqueRuns(store, rng, day, at, progress, scale, "scale", "scales");
            if (day.DayNumber % 3 == 0) await TechniqueRuns(store, rng, day, at.AddMinutes(3), progress, arpeggio, "arpeggio", "arpeggios");
            await NoteAnswers(store, rng, day, at.AddMinutes(5), progress, notes, cards);
        }

        var level = weeks > 6 ? 5 : 4;
        await store.SaveTrackAsync("reading", new ReadingState(level, 3, weeks > 6 ? 76 : 72, [(0.86, true), (0.91, true), (0.83, true)], 0, 1.5));
        await store.SaveTrackAsync("drill:intervals", new DrillTrack(3, 0.92, 0.92, today.AddDays(-2)));
        await store.SaveTrackAsync("drill:chords", new DrillTrack(2, 0.84, 0.84, today.AddDays(-4)));
        await store.SaveTrackAsync("drill:keys", new DrillTrack(4, 0.95, 0.95, today.AddDays(-1)));
        await store.SaveTrackAsync("drill:ledger", new DrillTrack(1, 0.78, 0.78, today.AddDays(-6)));
        if (scenario is not ("today" or "summary")) return;

        // Today: the scale and the notes done, and one passage read with a bar to fix.
        var start = today.ToDateTime(new TimeOnly(DateTime.Now.Hour < 9 ? 7 : 8, 30));
        await store.AddMinutesAsync(today, 9);
        await TechniqueRuns(store, rng, today, start, 1, scale, "scale", "scales");
        await NoteAnswers(store, rng, today, start.AddMinutes(4), 1, notes, cards);
        await Passages(store, rng, today, start.AddMinutes(7), 1, 1, worstBar: 3);
        var plan = DailyPlan.Build(today, settings.DailyMinutes, level, pianoPlays: false);
        await store.SavePlanAsync(today, JsonSerializer.Serialize(plan, PrimaStore.Json));
        var done = scenario == "summary" ? plan.Steps.Select(s => s.Kind) : [StepKind.Technique, StepKind.Notes];
        foreach (var step in done) await store.MarkStepAsync(today, step, shortPlanDone: scenario == "summary");
        if (scenario == "summary") await store.AddMinutesAsync(today, 12);
    }

    private static async Task Passages(PrimaStore store, Rng rng, DateOnly day, DateTime at, double progress, int count, int? worstBar = null)
    {
        for (var i = 0; i < count; i++)
        {
            var level = 2 + (int)(progress * 2.6);
            var tempo = 60 + Math.Round(progress * 12);
            var inTime = Math.Clamp(0.72 + 0.17 * progress + (rng.Double() - 0.5) * 0.12, 0.45, 1);
            var keptGoing = rng.Chance(0.75 + 0.2 * progress);
            var noteCount = 22 + 4 * level;
            var wrong = (int)Math.Round((1 - inTime) * noteCount * 0.45);
            var errors = worstBar is null ? 1 + rng.Int(2) : 3;
            var bar = worstBar ?? 1 + rng.Int(8);
            var measures = new
            {
                inTime, right = noteCount - wrong, wrong, missed = keptGoing ? rng.Int(2) : 3 + rng.Int(3), extra = rng.Int(3), audible = rng.Int(5), off = (int)Math.Round((1 - inTime) * noteCount * 0.5),
                leanMs = -14 + rng.Int(9), leanPercent = -2 - rng.Double() * 3, steadiness = 7 - 2.5 * progress + rng.Double(), perMinute = 26 + 22 * progress + rng.Int(6), keptGoing,
                drift = (rng.Double() - 0.4) * 4, rmsMs = 64 - 22 * progress + rng.Int(10), lost = rng.Int(3), worstBar = bar, worstErrors = errors, bars = 8, notes = noteCount,
            };
            var key = new Key(rng.Int(5) - 2);
            var settings = JsonSerializer.Serialize(new { step = 3, style = "Melody", avoid = (object?)null, key = new { fifths = key.Fifths, minor = key.Minor } });
            var variant = i == count - 1 && day.DayNumber % 4 == 0 ? "ReadAhead" : null;
            await store.AddAttemptAsync(new AttemptRecord(0, at.AddMinutes(i * 1.5), day, "passage", variant, null, level, tempo, (long)(rng.Next() >> 2), settings, "[]", null, JsonSerializer.Serialize(measures), inTime, inTime > 0.97 && keptGoing, null));
        }
    }

    private static async Task Rhythm(PrimaStore store, Rng rng, DateOnly day, DateTime at, double progress)
    {
        foreach (var stage in new[] { "Throughout", "CountInOnly" })
        {
            var accuracy = Math.Clamp(0.8 + 0.12 * progress + (rng.Double() - 0.5) * 0.08, 0.5, 1);
            var measures = new { accuracy, lean = -3.5 + 1.5 * progress + rng.Double(), steadiness = 6.5 - 2 * progress + rng.Double(), drift = stage == "Throughout" ? 0 : 2.5 - 1.5 * progress + rng.Double(), missed = rng.Int(2), extra = rng.Int(2), stage };
            await store.AddAttemptAsync(new AttemptRecord(0, at, day, "rhythm", stage, null, 3, 80, null, null, "[]", null, JsonSerializer.Serialize(measures), accuracy, measures.missed == 0 && measures.extra == 0, null));
            at = at.AddMinutes(1);
        }
    }

    private static async Task TechniqueRuns(PrimaStore store, Rng rng, DateOnly day, DateTime at, double progress, TechniqueItem item, string kind, string drill)
    {
        var tempo = Math.Round(76 + 12 * progress);
        for (var run = 0; run < 3; run++)
        {
            var sd = 9.5 - 3.5 * progress + rng.Double();
            var bpm = tempo * 2;
            var hand = new { IoiSdMs = sd / 100 * 60_000 / bpm, IoiSdPercent = sd, PlainIoiSdMs = sd / 100 * 60_000 / bpm * 1.2, Bpm = bpm, CrossingMs = 22 - 12 * progress + rng.Int(6), SeamMs = 8 + rng.Int(5), VelocitySd = 9 - 3 * progress + rng.Double(), LegatoMs = -6 + rng.Int(9) };
            var measures = new { valid = true, right = hand, left = (object?)null, sync = (double?)null, item = item.Id, hands = "Right" };
            await store.AddAttemptAsync(new AttemptRecord(0, at.AddSeconds(run * 40), day, kind, "Right", item.Id, 3, tempo, null, null, "[]", null, JsonSerializer.Serialize(measures), sd, true, null));
        }
        await store.SaveTrackAsync($"technique:{drill}:{item.Id}:Right", new TechniqueState(tempo, 92, [9.6, 10.1, 9.2], DateOnly.FromDateTime(DateTime.Now).AddDays(-40), [7.1, 6.8, 6.4], 0, tempo - 4));
    }

    private static async Task NoteAnswers(PrimaStore store, Rng rng, DateOnly day, DateTime at, double progress, (Clef Clef, int Midi)[] notes, Dictionary<(Clef, int), NoteCard> cards)
    {
        // New notes come in over the weeks; the low bass ones stay the hardest.
        var known = Math.Min(notes.Length, 3 + (int)(progress * notes.Length));
        for (var i = 0; i < 12; i++)
        {
            var (clef, midi) = notes[rng.Int(known)];
            var hard = clef == Clef.Bass && midi < 46 || midi > 75;
            var right = rng.Chance(hard ? 0.62 : 0.9);
            var ms = (hard ? 2600 : 1700) - 700 * progress + rng.Int(500);
            var card = NoteSchedule.Answer(cards.TryGetValue((clef, midi), out var c) ? c : new NoteCard(clef, midi, 1, day, day, null), right, ms < 1500, day);
            cards[(clef, midi)] = card;
            await store.SaveAnswerAsync(card, new NoteAnswer(new DateTimeOffset(at.AddSeconds(i * 6)), clef, midi, right, ms), right ? midi : midi + 2, "notes");
        }
    }
}

/// <summary>The preview's file picker: it hands over whatever /files/next set (one path or several), once.</summary>
sealed class PreviewFiles : IFiles
{
    public List<(string Name, byte[] Data)> Next { get; set; } = [];

    public Task<bool> OfferAsync(string path, string title) => Task.FromResult(false);

    public Task<(string Name, byte[] Data)?> PickAsync()
    {
        (string Name, byte[] Data)? next = Next.Count > 0 ? Next[0] : null;
        Next = [];
        return Task.FromResult(next);
    }

    public Task<IReadOnlyList<(string Name, byte[] Data)>> PickManyAsync()
    {
        IReadOnlyList<(string Name, byte[] Data)> next = Next;
        Next = [];
        return Task.FromResult(next);
    }
}
