using Playground.AI.Assist;
using Prima.Core.Music;
using Prima.Core.Transcription;
using Prima.UI.Transcription;

namespace Prima.UI.Tests;

public class ReadingDraftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadyChunksRejoinBySourceIdentityAndRestoreSystemLocalIndexes(bool pickup)
    {
        var source = new ConversionSource(new string('a', 64), "source-revision",
            [new("a", 1, 1, 0, "1", new(0, 0, 0.5, 0.4)), new("b", 1, 1, 1, "2", new(0.5, 0, 0.5, 0.4)), new("c", 1, 2, 2, "3", new(0, 0.5, 1, 0.4))], true);
        var chunks = source.Bars.Select((bar, index) =>
        {
            var value = pickup && index == 0 ? NoteValue.Quarter : NoteValue.Whole;
            var written = new ReadBar(1, 0, 4, 4, false, BarEnd.Normal, 0,
                [new(1, ReadClef.Treble, [new(1, [new(ReadKind.Note, [new(Step.C, Accidental.None, 4 + index, false, 0)], value, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])])]);
            return new ConversionChunkProgress(new("chunk-" + index, source.Revision, [bar.Id], null, 2000), ConversionPhase.Complete,
                new SystemReading([written], index == 0 ? [] : [new(1, 1, "Check pitch")])) ;
        }).ToArray();
        // Callback/completion order is not a source identity. Join by the source map even if records arrive out of order.
        var job = new ConversionJob(1, "job", "piece", "Synthetic", source, "pdf", new("test", new("model"), new()),
            ConversionState.Ready, 7, DateTimeOffset.UtcNow, chunks.Reverse().ToArray(), []);

        var draft = ReadingDraft.FromJob(job);

        Assert.Equal(2, draft.Readings.Count);
        Assert.Equal(new[] { 1, 2 }, draft.Readings[0]!.Bars.Select(b => b.Index));
        Assert.Equal(1, draft.Readings[1]!.Bars[0].Index);
        Assert.Equal(new[] { 4, 5, 6 }, draft.Readings.SelectMany(r => r!.Bars).Select(b => b.Staves[0].Voices[0].Events[0].Pitches[0].Octave));
        Assert.Equal(new[] { (1, 2, pickup ? 1 : 2), (2, 1, pickup ? 2 : 3) }, draft.Doubts.Select(d => (d.System, d.BarInSystem, d.Bar)));
        Assert.Equal(source.Revision, draft.SourceRevision);
        Assert.Equal(job.Id, draft.ConversionJobId);
        Assert.Equal(job.Revision, draft.ConversionRevision);
        Assert.Equal(pickup ? 0 : 1, MeiReader.Read(draft.Mei).Bars[0].Number);

        Assert.Throws<ArgumentException>(() => ReadingDraft.FromJob(job with { Chunks = chunks.Take(2).ToArray() }));
        Assert.Throws<ArgumentException>(() => ReadingDraft.FromJob(job with { State = ConversionState.Running }));
    }
}
