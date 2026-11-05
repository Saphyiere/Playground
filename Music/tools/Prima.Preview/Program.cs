// Preview host: Prima's real screens (Prima.UI) in a browser, as Blazor Server, against sample data in a SQLite file, so a cloud
// session can render and screenshot them (see CLAUDE.md). /seed/{scenario} sets up the data, and /keys/... plays a simulated piano.
using System.Globalization;
using Microsoft.Data.Sqlite;
using Prima.Core.Keyboard;
using Prima.Preview;
using Prima.Preview.Components;
using Prima.UI;
using Prima.UI.Host;
using Prima.UI.Practice;
using Prima.UI.Storage;

var dbPath = Path.Combine(AppContext.BaseDirectory, "preview.db");
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<IClock, StopwatchClock>();
builder.Services.AddSingleton<SimulatedKeyboard>();
builder.Services.AddSingleton<IKeyboard>(sp => sp.GetRequiredService<SimulatedKeyboard>());
builder.Services.AddPrima(dbPath);

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Routes).Assembly);

// Clears the data, then fills it for a scenario: "empty" is the first run.
app.MapGet("/seed/{scenario}", async (string scenario, PrimaStore store, PrimaSettings settings) =>
{
    await store.GetSettingsAsync(); // ensures the schema exists
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        foreach (var table in new[] { "Setting", "Track", "NoteCard", "NoteAnswer", "Attempt", "PracticeDay" }) new SqliteCommand($"DELETE FROM {table}", connection).ExecuteNonQuery();
    }
    await store.LoadAsync(settings);
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
// Plays the run in progress in one go and ends it: a share of notes right (the rest a key away), a lean and a spread in ms, and
// optionally nothing from a bar on (a stop), so a summary can be screenshotted without waiting.
app.MapGet("/keys/perform", (double? right, double? lean, double? spread, int? stop, SessionRegistry registry) =>
{
    var session = registry.Current;
    if (session?.Plan is null) return Results.Text("no run in progress");

    var random = new Random(9);
    var beatsPerBar = session.Plan.BeatsPerBar;
    var notes = session.Expected
        .Where(e => stop is null || e.Beat < (stop.Value - 1) * beatsPerBar)
        .Select(e =>
        {
            var midi = random.NextDouble() < (right ?? 0.95) ? e.Midi : e.Midi + (random.NextDouble() < 0.5 ? 1 : 2);
            var on = session.TimeAt(e.Beat) + (long)(((lean ?? -15) + (random.NextDouble() - 0.5) * 2 * (spread ?? 12)) * 1e6);
            return new Prima.Core.Scoring.PlayedNote(midi, on, session.TimeAt(e.Beat + e.Beats) - 30_000_000, 60 + random.Next(20));
        }).ToList();
    session.Perform(notes);
    return Results.Text($"performed {notes.Count} notes");
});
// Answers the drill item on screen: right, or wrong.
app.MapGet("/keys/answer", (bool? right, SessionRegistry registry, SimulatedKeyboard keys) =>
{
    if (registry.DrillAnswer is not { } answer) return Results.Text("no drill item");

    foreach (var step in answer(right ?? true)) keys.Play(step, 70, 200);
    return Results.Text("answered");
});
app.MapGet("/keys/sent", (SimulatedKeyboard keys) => Results.Text(string.Join('\n', keys.Sent.Select(s => $"{s.AtNs} {Convert.ToHexString(s.Message)}"))));

app.Run();
