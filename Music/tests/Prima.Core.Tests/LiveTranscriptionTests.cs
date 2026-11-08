using Playground.AI.Assist;
using Prima.Core.Music;
using Prima.Core.Transcription;

namespace Prima.Core.Tests;

/// <summary>
/// Reads a printed system with the real API, when PLAYGROUND_CLAUDE_KEY holds a Claude API key, and is skipped otherwise. The system
/// is the first of Czerny's Op. 821 No. 13 from the library (Mutopia, public domain), and the notes it should give are the piece's
/// MIDI's. About 20 cents a run (Opus at high effort thinks before it writes); the test prints what it cost.
/// </summary>
public class LiveTranscriptionTests
{
    private const string Skip = "Set PLAYGROUND_CLAUDE_KEY to a Claude API key to ask the real API.";
    private static readonly string? Key = Environment.GetEnvironmentVariable("PLAYGROUND_CLAUDE_KEY");

    private sealed class KeyFromEnvironment : IAssistKeyStore
    {
        public Task<string?> GetAsync() => Task.FromResult(Key);

        public Task SetAsync(string key) => Task.CompletedTask;

        public Task RemoveAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task ReadSystem_ReadsCzernysFirstSystemAsItsMidiPlaysIt()
    {
        Assert.SkipWhen(Key is null, Skip);
        var ct = TestContext.Current.CancellationToken;
        var png = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "czerny-821-13-system-1.png"), ct);
        var claude = new Claude(new KeyFromEnvironment(), NoAssistLog.Instance, TimeProvider.System);

        var answer = await claude.AskAsync(new ReadSystem(png), new SystemToRead(1, 1, true, 0, 0), ct);

        Assert.True(answer.Ok, $"{answer.Kind}: {answer.Summary} {string.Join(" ", answer.Notes)}");
        TestContext.Current.TestOutputHelper?.WriteLine($"Read in {answer.Usage.Took.TotalSeconds:0} s for ${answer.Usage.Cost:0.00} " +
            $"({answer.Usage.InputTokens} tokens in, {answer.Usage.OutputTokens} out). Doubts: {answer.Value.Doubts.Count}.");
        var (score, _) = ReadingScore.Build([answer.Value]);
        Assert.Equal(new Key(-2), score.Key);
        Assert.Equal(new Meter(4, 4), score.Meter);
        Assert.Equal([1, 2], score.Bars.Select(b => b.Number));

        // Each bar: D6, B♭5 and F5 on the first three beats above, after a grace note each, and sixteenths below.
        int[] lower = [58, 60, 58, 57, 58, 60, 62, 60, 58, 60, 58, 57, 58, 60, 62, 60, 58, 60, 58, 57, 58, 60, 62, 60, 58, 60, 58, 57, 58, 60, 62, 58];
        List<(int Staff, long Tick, int Midi)> played =
        [
            .. new[] { 0L, 1920L }.SelectMany(bar => new[] { (1, bar, 86), (1, bar + 480, 82), (1, bar + 960, 77) }),
            .. lower.Select((midi, i) => (2, i * 120L, midi)),
        ];
        List<(int Bar, int Start, int Midi)> graces = [(1, 0, 77), (1, 480, 74), (1, 960, 74), (2, 0, 77), (2, 480, 74), (2, 960, 74)];

        Assert.Equal(played.Order(), score.Expected().Select(n => (n.Staff, n.Tick, n.Midi)).Order());
        Assert.Equal(graces, score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == 1).SelectMany(v => v.Events.OfType<Notes>().Where(n => n.Grace)
            .SelectMany(n => n.Tones.Select(t => (Bar: b.Number, n.Start, t.Pitch.Midi))))).Order());
    }
}
