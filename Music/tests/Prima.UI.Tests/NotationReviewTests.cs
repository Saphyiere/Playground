using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
    public async Task EditorSetsTheKeySignature_SayingWhichBarsTakeIt()
    {
        await using var host = new ComponentHost();
        ReadBar? saved = null;
        var view = host.Context.Render<BarEditor>(p => p.Add(c => c.Bar, Bar() with { Fifths = 2 }).Add(c => c.FollowingInKey, 3).Add(c => c.OnSave, b => saved = b));

        Assert.Equal("2", view.Find("[data-key-signature]").GetAttribute("data-key-signature"));
        Assert.Empty(view.FindAll("[data-key-run]"));
        await view.InvokeAsync(() => view.Find("[data-action=key-flatter]").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-action=key-flatter]").ClickAsync(new MouseEventArgs()));

        Assert.Equal("No sharps or flats", view.Find("[data-key-signature] .control-value").TextContent);
        Assert.Equal("Saving also sets it for the next 3 bars of this system, read in the same key as this one.", view.Find("[data-key-run]").TextContent);
        await view.InvokeAsync(() => view.Find("[data-action=key-flatter]").ClickAsync(new MouseEventArgs()));
        Assert.Equal("1 flat", view.Find("[data-key-signature] .control-value").TextContent);
        await view.InvokeAsync(() => view.Find("[data-action=save-bar]").ClickAsync(new MouseEventArgs()));

        Assert.Equal(-1, saved!.Fifths);
        Assert.Equal(Bar().Staves.Select(s => s.Clef), saved.Staves.Select(s => s.Clef));
    }

    [Fact]
    public async Task EditorKeepsTheKeyWithinSevenSharpsOrFlats()
    {
        await using var host = new ComponentHost();
        var sharps = host.Context.Render<BarEditor>(p => p.Add(c => c.Bar, Bar() with { Fifths = 7 }));
        var flats = host.Context.Render<BarEditor>(p => p.Add(c => c.Bar, Bar() with { Fifths = -7 }));

        Assert.True(sharps.Find("[data-action=key-sharper]").HasAttribute("disabled"));
        Assert.False(sharps.Find("[data-action=key-flatter]").HasAttribute("disabled"));
        Assert.True(flats.Find("[data-action=key-flatter]").HasAttribute("disabled"));
        Assert.Equal("7 flats", flats.Find("[data-key-signature] .control-value").TextContent);
    }

    [Fact]
    public async Task SavingAKeyPutsTheRestOfItsSystemInIt_AndUndoTakesItAllBack()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        // Three bars on the first system, read in two sharps, and one on the next, read in two sharps too.
        var source = new ConversionSource(new string('a', 64), "r1",
        [
            new("bar1", 1, 1, 0, "1", new(0, 0, 0.3, 0.4)), new("bar2", 1, 1, 1, "2", new(0.3, 0, 0.3, 0.4)),
            new("bar3", 1, 1, 2, "3", new(0.6, 0, 0.3, 0.4)), new("bar4", 1, 2, 3, "4", new(0, 0.5, 0.3, 0.4)),
        ], true);
        var view = host.Context.Render<NotationReviewPanel>(p => p.Add(c => c.PieceId, piece.Id).Add(c => c.Source, source)
            .Add(c => c.Initial, [.. source.Bars.Select(b => new BarReplacement(b.Id, Bar() with { Fifths = 2 }, []))]).Add(c => c.SelectedBarId, "bar2"));
        view.WaitForElement("[data-action=edit-bar]");
        Assert.Equal("Edit bar", view.Find("[data-action=edit-bar]").TextContent);
        await view.InvokeAsync(() => view.Find("[data-action=edit-bar]").ClickAsync(new MouseEventArgs()));
        view.WaitForElement("[data-key-signature]");
        await view.InvokeAsync(() => view.Find("[data-action=key-flatter]").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-action=key-flatter]").ClickAsync(new MouseEventArgs()));
        Assert.Equal("1", view.Find("[data-key-run]").GetAttribute("data-key-run"));

        await view.InvokeAsync(() => view.Find("[data-action=save-bar]").ClickAsync(new MouseEventArgs()));

        view.WaitForElement("[data-action=undo-review]:not([disabled])");
        var saved = (await host.Store.GetNotationReviewAsync(piece.Id, source.Revision))!;
        Assert.Equal([2, 0, 0, 2], saved.Current.Bars.Select(b => b.Written!.Fifths));
        Assert.Equal([false, true, true, false], saved.Current.Bars.Select(b => b.UserCorrected));
        await view.InvokeAsync(() => view.Find("[data-action=undo-review]").ClickAsync(new MouseEventArgs()));
        view.WaitForElement("[data-action=undo-review][disabled]");
        var undone = (await host.Store.GetNotationReviewAsync(piece.Id, source.Revision))!;
        Assert.Equal([2, 2, 2, 2], undone.Current.Bars.Select(b => b.Written!.Fifths));
    }

    [Fact]
    public async Task AcceptedBarCannotOpenTheEditorUntilExplicitlyReopened()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var source = new ConversionSource(new string('a', 64), "r1", [new("bar1", 1, 1, 0, "1", new(0, 0, 1, 1))], true);
        var view = host.Context.Render<NotationReviewPanel>(p => p.Add(c => c.PieceId, piece.Id).Add(c => c.Source, source)
            .Add(c => c.Initial, [new BarReplacement("bar1", Bar(), [])]).Add(c => c.SelectedBarId, "bar1"));
        // Each tap waited for, so what's checked after it is what the tap did, not a render still under way.
        view.WaitForElement("[data-action=accept-bar]");
        await view.InvokeAsync(() => view.Find("[data-action=accept-bar]").ClickAsync(new MouseEventArgs()));
        view.WaitForElement("[data-action=reopen-bar]");
        Assert.Empty(view.FindAll("[data-action=edit-bar]"));
        Assert.True((await host.Store.GetNotationReviewAsync(piece.Id, source.Revision))!.Current.Bars[0].Accepted);

        await view.InvokeAsync(() => view.Find("[data-action=reopen-bar]").ClickAsync(new MouseEventArgs()));
        view.WaitForElement("[data-action=edit-bar]");
        await view.InvokeAsync(() => view.Find("[data-action=edit-bar]").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Single(view.FindComponents<BarEditor>()));
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
