using Bunit;
using Playground.AI.Assist;
using Prima.Core.Music;
using Prima.Core.Transcription;
using Prima.UI.Transcription;

namespace Prima.UI.Tests;

public class NotationReviewTests
{
    [Fact]
    public async Task MovingANoteToAnEmptyVoicePreservesItsBeatAndTheSourceVoiceLength()
    {
        await using var host = new ComponentHost();
        var first = Bar();
        var events = new[] { Step.C, Step.D, Step.E, Step.F }.Select(step => first.Staves[0].Voices[0].Events[0] with
        {
            Value = NoteValue.Quarter, Pitches = [new(step, Accidental.None, 4, false, 0)]
        }).ToArray();
        first = first with { Staves = [first.Staves[0] with { Voices = [new(1, events)] }, first.Staves[1]] };
        ReadBar? saved = null;
        var view = host.Context.Render<BarEditor>(p => p.Add(c => c.Bar, first).Add(c => c.OnSave, b => saved = b));
        view.Find("[data-staff='1'][data-voice='1'][data-event='1']").Click();
        view.Find("[data-action=move-voice]").Click();
        view.Find("[data-action=save-bar]").Click();

        Assert.NotNull(saved);
        Assert.Empty(ReadingChecks.Problems(new([saved], []), true));
        var score = ReadingScore.Build([new([saved], [])]).Score;
        var moved = Assert.Single(score.Bars[0].Voices.Single(v => v.Staff == 1 && v.Layer == 2).Events.OfType<Notes>());
        Assert.Equal(Ticks.Quarter, moved.Start);
        Assert.Equal(Step.D, moved.Tones[0].Pitch.Step);
        Assert.Equal(Ticks.Whole, score.Bars[0].Voices.Single(v => v.Staff == 1 && v.Layer == 1).Events.Max(e => e.Start + e.Length));
    }

    [Fact]
    public async Task ReloadAfterAConflictKeepsConcurrentFlagsOnTheNextAction()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var source = new ConversionSource(new string('a', 64), "r1", [new("bar1", 1, 1, 0, "1", new(0, 0, 1, 1))], true);
        NotationReview? shown = null;
        var view = host.Context.Render<NotationReviewPanel>(p => p.Add(c => c.PieceId, piece.Id).Add(c => c.Source, source)
            .Add(c => c.Initial, [new BarReplacement("bar1", Bar(), [])]).Add(c => c.SelectedBarId, "bar1").Add(c => c.OnChanged, s => shown = s));
        view.WaitForElement("[data-action=flag-bar]");
        var initial = (await host.Store.GetNotationReviewAsync(piece.Id, source.Revision))!;
        var concurrent = NotationCorrections.SetUserFlags(initial, initial.Current.Revision, "bar1", [new("other", "pitch", "Another review flag")]);
        Assert.True(await host.Store.SaveNotationReviewAsync(piece.Id, concurrent, initial.Current.Revision));
        view.Find("[data-review-hint]").Input("My flag");
        view.Find("[data-action=flag-bar]").Click();
        view.WaitForElement("[role=alert]");
        Assert.Equal(concurrent.Current.Revision, shown!.Current.Revision);

        view.Find("[data-action=flag-bar]").Click();
        view.WaitForAssertion(() => Assert.Equal(2, shown!.Current.Bars[0].UserFlags.Length));
        Assert.Contains(shown!.Current.Bars[0].UserFlags, f => f.Id == "other");
    }

    [Fact]
    public async Task DismissingACompletionNoticePreservesTheSavedResult()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var source = new ConversionSource(new string('a', 64), "r1", [new("bar1", 1, 1, 0, "1", new(0, 0, 1, 1))], true);
        var job = new ConversionJob(1, "job1", piece.Id, piece.Title, source, "pdf", new("test", new InferenceSettings("test-model"), new()),
            ConversionState.Queued, 0, DateTimeOffset.UtcNow, [], []);
        await host.Store.CreateConversionJobAsync(job);
        Assert.True(await host.Store.SaveConversionJobAsync(job with { State = ConversionState.Ready, Revision = 1, Mei = "saved-result" }, 0));
        var view = host.Context.Render<ConversionNotice>();
        view.WaitForElement("[data-conversion-notice=job1]");
        view.Find("[data-action=dismiss-conversion]").Click();
        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-conversion-notice]")));

        var saved = await host.Store.GetConversionJobAsync(job.Id);
        Assert.Equal(ConversionState.Ready, saved!.State);
        Assert.Equal("saved-result", saved.Mei);
        Assert.Equal("1", (await host.Store.GetSettingsAsync())["Conversion.Notice.job1"]);
    }

    [Fact]
    public async Task EditorChangesWrittenPitchOnlyAfterExplicitSave()
    {
        await using var host = new ComponentHost();
        var original = Bar();
        ReadBar? saved = null;
        var view = host.Context.Render<BarEditor>(p => p.Add(c => c.Bar, original).Add(c => c.OnSave, b => saved = b));
        view.Find("[data-pitch='0']").Change("F#4");
        Assert.Null(saved);
        Assert.Equal(Step.C, original.Staves[0].Voices[0].Events[0].Pitches[0].Step);

        view.Find("[data-action=save-bar]").Click();

        Assert.NotNull(saved);
        Assert.Equal(new ReadPitch(Step.F, Accidental.Sharp, 4, false, 0), saved.Staves[0].Voices[0].Events[0].Pitches[0]);
        Assert.Equal(original.Staves[1], saved.Staves[1]);
        Assert.All(view.FindAll("button, input"), control => Assert.Equal("off", control.GetAttribute("data-piano")));
    }

    [Fact]
    public async Task AcceptedBarCannotOpenTheEditorUntilExplicitlyReopened()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var source = new ConversionSource(new string('a', 64), "r1", [new("bar1", 1, 1, 0, "1", new(0, 0, 1, 1))], true);
        var view = host.Context.Render<NotationReviewPanel>(p => p.Add(c => c.PieceId, piece.Id).Add(c => c.Source, source)
            .Add(c => c.Initial, [new BarReplacement("bar1", Bar(), [])]).Add(c => c.SelectedBarId, "bar1"));
        view.WaitForElement("[data-action=accept-bar]").Click();
        view.WaitForElement("[data-action=reopen-bar]");
        Assert.Empty(view.FindAll("[data-action=edit-bar]"));
        Assert.True((await host.Store.GetNotationReviewAsync(piece.Id, source.Revision))!.Current.Bars[0].Accepted);

        view.Find("[data-action=reopen-bar]").Click();
        view.WaitForElement("[data-action=edit-bar]").Click();
        Assert.Single(view.FindComponents<BarEditor>());
    }

    [Fact]
    public async Task ReviewCompareAndSwapRejectsAnOldEditAfterAcceptance()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var source = new ConversionSource(new string('a', 64), "r1", [new("bar1", 1, 1, 0, "1", new(0, 0, 1, 1))], true);
        var initial = NotationCorrections.Create(source);
        initial = NotationCorrections.Apply(initial, new(source.Sha256, source.Revision, initial.Current.Revision, ["bar1"], [new("bar1", Bar(), [])], CorrectionOrigin.Model));
        Assert.True(await host.Store.SaveNotationReviewAsync(piece.Id, initial, null));
        var accepted = NotationCorrections.Accept(initial, initial.Current.Revision, "bar1");
        Assert.True(await host.Store.SaveNotationReviewAsync(piece.Id, accepted, initial.Current.Revision));

        var stale = NotationCorrections.SetUserFlags(initial, initial.Current.Revision, "bar1", [new("f1", "pitch", "Check this note")]);
        Assert.False(await host.Store.SaveNotationReviewAsync(piece.Id, stale, initial.Current.Revision));
        var loaded = await host.Store.GetNotationReviewAsync(piece.Id, source.Revision);
        Assert.True(loaded!.Current.Bars[0].Accepted);
        Assert.Empty(loaded.Current.Bars[0].UserFlags);
    }

    private static ReadBar Bar() => new(1, 0, 4, 4, false, BarEnd.Normal, 0,
        [new(1, ReadClef.Treble, [new(1, [new(ReadKind.Note, [new(Step.C, Accidental.None, 4, false, 0)], NoteValue.Whole, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]),
         new(2, ReadClef.Bass, [new(1, [new(ReadKind.Note, [new(Step.C, Accidental.None, 3, false, 0)], NoteValue.Whole, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])])]);
}
