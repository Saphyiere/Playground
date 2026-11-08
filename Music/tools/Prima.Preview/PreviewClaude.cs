using System.Text.Json;
using Playground.AI.Assist;
using Prima.Core.Transcription;

namespace Prima.Preview;

/// <summary>
/// Claude for the preview: answers from saved replies and never calls the API, so the screens can be checked without a key. With a
/// key set (/claude/key), the key's test passes and each system is read from Readings/czerny-821-13.json, by its place on page 1,
/// whatever the PDF. Its comparison corrects nothing and leaves system 3's long high note in doubt, read as E6; read again, the
/// system comes back with F6, as printed. Each answer waits a moment and is logged with a price, as the real one is.
/// /claude/script slows it, or makes it fail with a kind after a number of systems.
/// </summary>
public sealed class PreviewClaude(IAssistKeyStore keys, IAssistLog log) : IClaude
{
    private sealed record Saved(string Note, List<SystemReading> Systems, Dictionary<string, SystemReading> Reread);

    private static readonly Saved Readings = JsonSerializer.Deserialize<Saved>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Readings", "czerny-821-13.json")), AssistSchema.Json)!;

    private readonly Lock gate = new();
    private readonly Dictionary<int, int> timesRead = [];
    private int read;

    /// <summary>How long each answer takes, in ms.</summary>
    public int DelayMs { get; set; } = 1500;

    /// <summary>The kind of trouble to answer with once <see cref="FailAfter"/> systems have been read; null for none.</summary>
    public AnswerKind? Fail { get; set; }

    public int FailAfter { get; set; }

    public void Reset() => (DelayMs, Fail, FailAfter) = (1500, null, 0);

    public async Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default)
    {
        if (await keys.GetAsync() is null) return new(AnswerKind.NoKey, default, "There's no Claude key on this device yet.", [], Usage.None);
        await Task.Delay(DelayMs, ct);
        var (answer, cost) = input switch
        {
            SystemToRead system => (Read(system), 0.11m),
            SystemToCompare system => ((object)new Answer<SystemComparison>(AnswerKind.Answered, Compare(system), "Compared.", [], Usage.None), 0.07m),
            _ => throw new InvalidOperationException($"The preview has no answers for {task.Name}."),
        };
        var typed = (Answer<TAnswer>)answer;
        var usage = typed.Ok ? new Usage(9_000, 2_500, 0, 0, cost, TimeSpan.FromMilliseconds(DelayMs)) : Usage.None;
        await log.WriteAsync(new AssistCall(DateTimeOffset.Now, task.Name, task.Version, "preview", 1, typed.Kind, typed.Summary,
            JsonSerializer.Serialize(input, AssistSchema.Json), typed.Ok ? JsonSerializer.Serialize(typed.Value, AssistSchema.Json) : "", 1, usage, "preview", ""));
        return typed with { Usage = usage };
    }

    public async Task<Answer<string>> TestAsync(CancellationToken ct = default)
    {
        if (await keys.GetAsync() is null) return new(AnswerKind.NoKey, null, "There's no Claude key on this device yet.", [], Usage.None);
        await Task.Delay(600, ct);
        return new(AnswerKind.Answered, "Claude Opus 5.5", "The key works, and Claude Opus 5.5 is ready.", [], Usage.None);
    }

    private object Read(SystemToRead system)
    {
        int times;
        lock (gate)
        {
            if (Fail is { } kind && read >= FailAfter) return Trouble(kind, system);
            read++;
            times = timesRead[system.Index] = timesRead.GetValueOrDefault(system.Index) + 1;
        }

        var reading = times > 1 && Readings.Reread.TryGetValue(system.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var again)
            ? again
            : Readings.Systems[(system.Index - 1) % Readings.Systems.Count];
        return new Answer<SystemReading>(AnswerKind.Answered, reading, "Read.", [], Usage.None);
    }

    // The long high note in system 3's first bar stays in doubt while it's read as E6.
    private static SystemComparison Compare(SystemToCompare system) =>
        system.Reading.Bars[0].Staves[0].Voices[0].Events.Any(e => e.Pitches.Any(p => p is { Step: Prima.Core.Music.Step.E, Octave: 6 }) && e.Value == NoteValue.Half)
            ? new([], [new ReadDoubt(1, 1, "The long high note after the two grace notes: I read it as E6, on the third ledger line, but beside the print it may sit a step higher, on F6.")])
            : new([], []);

    private static object Trouble(AnswerKind kind, SystemToRead system) => new Answer<SystemReading>(kind, null, kind switch
    {
        AnswerKind.KeyRejected => "Claude didn't accept the key on this device: it may be wrong, disabled or expired.",
        AnswerKind.SpendLimit => "The spend limit for this key's workspace is reached.",
        AnswerKind.OutOfCredit => "This month's API credits are used up.",
        AnswerKind.Busy => "Claude is busy right now; try again in a minute.",
        AnswerKind.Offline => "Claude couldn't be reached: check the connection.",
        _ => $"System {system.Index}: the lower staff in its last bar is too faint to make out, so its beats can't be read.",
    }, [], Usage.None);
}
