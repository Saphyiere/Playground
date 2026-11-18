using System.Text.Json;
using System.Text.RegularExpressions;
using Playground.AI.Assist;
using Prima.Core.Music;
using Prima.Core.Transcription;

namespace Prima.Core.Tests;

/// <summary>
/// A chord's sharps printed just after the clefs, read as a key signature, once made most of a conversion's wrong notes: the wrong
/// key went on into systems that print none. Synthetic bars only: a G♯ whole note over a bar rest, in whatever key is asked for.
/// </summary>
public class KeySignatureTests
{
    private static readonly InferenceSettings Settings = new(Model: "test-model");

    private static ReadBar Bar(int index, int fifths = 0, BarEnd end = BarEnd.Normal, bool repeatStart = false) =>
        new(index, fifths, 4, 4, repeatStart, end, 0,
        [
            new(1, ReadClef.Treble, [new(1, [new(ReadKind.Note, [new(Step.G, Accidental.Sharp, 4, false, 0)], NoteValue.Whole, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]),
            new(2, ReadClef.Bass, [new(1, [new(ReadKind.BarRest, [], NoteValue.Quarter, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]),
        ]);

    private static SystemReading Reading(params ReadBar[] bars) => new(bars, []);

    /// <summary>A request for two bars after the given one, at a system's start or inside one.</summary>
    private static ChunkToRead Chunk(ReadBar? before, bool startsSystem, string? feedback = null) =>
        new(1, 2, false, startsSystem, ["b5", "b6"], before, ValidationFeedback: feedback);

    /// <summary>Two systems of three bars on page 1, and one of two on page 2, each bar a third of its system wide.</summary>
    private static ConversionSource Source()
    {
        (int Page, int System)[] systems = [(1, 1), (1, 1), (1, 1), (1, 2), (1, 2), (1, 2), (2, 1), (2, 1)];
        var bars = systems.Select((s, i) => new SourceBar($"b{i}", s.Page, s.System, i, $"{i + 1}",
            new(i % 3 / 3.0, (s.System - 1) * 0.3, 1 / 3.0, 0.25))).ToArray();
        return new(new string('a', 64), "r1", bars, true);
    }

    [Fact]
    public void KeyChanges_SayWhereEachChangeIs()
    {
        ReadBar[] bars = [Bar(1, 2), Bar(2, 2, BarEnd.Double), Bar(3), Bar(4, end: BarEnd.RepeatEnd), Bar(5, -3), Bar(6, -3), Bar(7, 1, repeatStart: true), Bar(8)];

        var changes = ReadingChecks.KeyChanges(Bar(9), startsSystem: true, bars);

        Assert.Equal(
        [
            new KeyChange(1, 0, 2, KeyChangePlace.SystemStart),
            new KeyChange(3, 2, 0, KeyChangePlace.DoubleBarline),
            new KeyChange(5, 0, -3, KeyChangePlace.DoubleBarline),
            new KeyChange(7, -3, 1, KeyChangePlace.DoubleBarline),
            new KeyChange(8, 1, 0, KeyChangePlace.Barline),
        ], changes);
    }

    [Fact]
    public void KeyChanges_AtTheFirstBarInsideASystemComeAfterTheBarBefore()
    {
        Assert.Equal(KeyChangePlace.Barline, Assert.Single(ReadingChecks.KeyChanges(Bar(9), false, [Bar(1, 2)])).Place);
        Assert.Equal(KeyChangePlace.DoubleBarline, Assert.Single(ReadingChecks.KeyChanges(Bar(9, end: BarEnd.Final), false, [Bar(1, 2)])).Place);
        Assert.Empty(ReadingChecks.KeyChanges(null, false, [Bar(1, 2)]));
        Assert.Empty(ReadingChecks.KeyChanges(Bar(9, 2), false, [Bar(1, 2), Bar(2, 2)]));
    }

    [Theory]
    [InlineData(0, "none")]
    [InlineData(1, "1 sharp")]
    [InlineData(2, "2 sharps")]
    [InlineData(-1, "1 flat")]
    [InlineData(-7, "7 flats")]
    public void Signature_InWords(int fifths, string words) => Assert.Equal(words, ReadingChecks.Signature(fifths));

    [Fact]
    public void Check_SendsBackAKeyChangeAfterAnOrdinaryBarlineInsideASystem()
    {
        var task = new ReadChunk([], Settings);

        var fromTheBarBefore = Assert.Single(task.Check(Chunk(Bar(4), startsSystem: false), Reading(Bar(1, 2), Bar(2, 2))));
        var between = Assert.Single(task.Check(Chunk(Bar(4), startsSystem: false), Reading(Bar(1), Bar(2, -1))));

        Assert.StartsWith("Bar 1: the key signature changes from none to 2 sharps after an ordinary barline, inside the system.", fromTheBarBefore);
        Assert.Contains("a sharp or flat right before a notehead is that note's accidental", fromTheBarBefore);
        Assert.StartsWith("Bar 2: the key signature changes from none to 1 flat after an ordinary barline", between);
    }

    [Fact]
    public void Check_LetsRealKeyChangesThrough()
    {
        var task = new ReadChunk([], Settings);

        // At a system's start, where every system prints its key signature: the key isn't carried on, so it may differ.
        Assert.Empty(task.Check(Chunk(Bar(4), startsSystem: true), Reading(Bar(1, 2), Bar(2, 2))));
        Assert.Empty(task.Check(Chunk(Bar(4, 2), startsSystem: true), Reading(Bar(1), Bar(2))));
        // After a double barline, a final one or a repeat, before the bar or inside the batch.
        Assert.Empty(task.Check(Chunk(Bar(4, end: BarEnd.Double), startsSystem: false), Reading(Bar(1, 2), Bar(2, 2))));
        Assert.Empty(task.Check(Chunk(Bar(4), startsSystem: false), Reading(Bar(1, end: BarEnd.Final), Bar(2, -2))));
        Assert.Empty(task.Check(Chunk(Bar(4), startsSystem: false), Reading(Bar(1), Bar(2, 4, repeatStart: true))));
        // And a key carried on inside its system.
        Assert.Empty(task.Check(Chunk(Bar(4, 2), startsSystem: false), Reading(Bar(1, 2), Bar(2, 2))));
    }

    [Fact]
    public void Check_AResentBatchKeepsAChangeItWasAskedAbout_ButNotANewOne()
    {
        var task = new ReadChunk([], Settings);
        var feedback = Assert.Single(task.Check(Chunk(Bar(4), startsSystem: false), Reading(Bar(1, 2), Bar(2, 2))));

        Assert.Empty(task.Check(Chunk(Bar(4), startsSystem: false, feedback), Reading(Bar(1, 2), Bar(2, 2))));
        Assert.StartsWith("Bar 2: the key signature changes from 2 sharps to 3 sharps", Assert.Single(task.Check(Chunk(Bar(4), startsSystem: false, feedback), Reading(Bar(1, 2), Bar(2, 3)))));
        Assert.Single(task.Check(Chunk(Bar(4), startsSystem: false, "Bar 1, upper staff, voice 1 adds up to 3 quarter notes."), Reading(Bar(1, 2), Bar(2, 2))));
    }

    [Fact]
    public void Check_KeepsItsOtherProblems()
    {
        var task = new ReadChunk([], Settings);

        Assert.Equal(["The returned bars do not cover the exact owned source range."], task.Check(Chunk(Bar(4), startsSystem: false), Reading(Bar(1, 2))));
        Assert.Contains(task.Check(Chunk(Bar(4), startsSystem: false), Reading(Bar(1, 9), Bar(2, 9))), p => p.Contains("7 sharps or flats at most", StringComparison.Ordinal));
    }

    [Fact]
    public void Requests_SayWhereTheirFirstBarIs_AndWhetherItStartsTheSourceOrAPrintedSystem()
    {
        var source = Source();
        var chunks = ConversionChunks.Plan(source, new ChunkLimits(MaxBars: 2));

        var inputs = chunks.Select(c => ChunkToRead.For(source, c, null)).ToArray();

        Assert.Equal([["b0", "b1"], ["b2"], ["b3", "b4"], ["b5"], ["b6", "b7"]], inputs.Select(i => i.OwnedSourceBars));
        Assert.Equal([true, false, true, false, true], inputs.Select(i => i.StartsSystem));
        Assert.Equal([true, false, false, false, false], inputs.Select(i => i.First));
        Assert.Equal([(1, 1), (1, 1), (1, 2), (1, 2), (2, 1)], inputs.Select(i => (i.Page, i.System)));
        // A batch split for length keeps its first half's start; its second half goes on inside the system.
        Assert.Equal([true, false], ConversionChunks.Split(chunks[2]).Select(c => ChunkToRead.For(source, c, null).StartsSystem));
    }

    [Fact]
    public void Requests_ForARecheckCarryTheContextAndTheSystemStart()
    {
        var source = Source();
        var before = Bar(3, 2);
        var current = Bar(1, 2);

        var chunk = new ConversionChunk("recheck", "r1", ["b3"], "b2", 1_000);

        var input = ChunkToRead.For(source, chunk, before, current, "The sharps are the chord's", "feedback");

        Assert.Equal(new ChunkToRead(1, 2, false, true, chunk.BarIds, before, current, "The sharps are the chord's", "feedback"), input);
        Assert.False(ChunkToRead.For(source, new ConversionChunk("recheck", "r1", ["b4"], "b3", 1_000), current).StartsSystem);
        Assert.Throws<ArgumentException>(() => source.StartsSystem(source.Bars[0] with { Id = "elsewhere" }));
    }

    [Fact]
    public void Requests_NameTheSystemStartAsTheInstructionsDo()
    {
        var input = ChunkToRead.For(Source(), new ConversionChunk("c", "r1", ["b6"], "b5", 1_000), Bar(3, 2));
        var instructions = Regex.Replace(new ReadChunk([], Settings).Instructions, @"\s+", " ");

        Assert.Contains("\"startsSystem\":true", JsonSerializer.Serialize(input, AssistSchema.Json));
        Assert.Contains("StartsSystem is true", instructions);
        Assert.Contains("never from PreviousBar", instructions);
        Assert.Contains("Count one staff's sharps or flats", instructions);
    }

    [Fact]
    public void Review_FlagsEveryKeyChange_SayingWhereItIs()
    {
        // The second system starts in two sharps (a chord's sharps, perhaps), drops them after an ordinary barline, and takes one
        // flat after a double barline. After a system that wasn't read, nothing is compared.
        SystemReading?[] readings =
        [
            Reading(Bar(1), Bar(2), Bar(3)),
            Reading(Bar(1, 2), Bar(2, 0, BarEnd.Double), Bar(3, -1), Bar(4, -1)),
            null,
            Reading(Bar(1, 3), Bar(2, 3)),
        ];

        var issues = ReadingFidelity.Find(readings);

        Assert.All(issues, i => Assert.Equal((ReadingFidelityKind.KeyChange, 0, 0, 0), (i.Kind, i.Staff, i.Voice, i.EventIndex)));
        Assert.Equal([(1, 1), (1, 2), (1, 3)], issues.Select(i => (i.SystemIndex, i.BarIndex)));
        Assert.Equal("The key signature changes from none to 2 sharps at the start of this system. Check the print after the clefs: a chord's sharps or flats there are only its accidentals.", issues[0].Message);
        Assert.StartsWith("The key signature changes from 2 sharps to none after an ordinary barline, inside the system", issues[1].Message);
        Assert.StartsWith("The key signature changes from none to 1 flat after a double barline.", issues[2].Message);
        Assert.Empty(ReadingFidelity.Find([Reading(Bar(1, 2), Bar(2, 2)), Reading(Bar(1, 2))]));
    }

    [Fact]
    public void Review_FlagsAKeyChangeAfterAnOrdinaryBarlineAsAChordsSharpsPerhaps()
    {
        var issue = Assert.Single(ReadingFidelity.Find([Reading(Bar(1), Bar(2, 2), Bar(3, 2))]));

        Assert.Equal((0, 2), (issue.SystemIndex, issue.BarIndex));
        Assert.StartsWith("The key signature changes from none to 2 sharps after an ordinary barline, inside the system", issue.Message);
    }
}
