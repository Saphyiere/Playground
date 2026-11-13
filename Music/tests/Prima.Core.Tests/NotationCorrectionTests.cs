using System.Text.Json;
using Prima.Core.Music;
using Prima.Core.Transcription;

namespace Prima.Core.Tests;

public class NotationCorrectionTests
{
    private static ConversionSource Source(int count = 3) => new(new string('a', 64), "source-r1",
        Enumerable.Range(0, count).Select(i => new SourceBar($"b{i}", 1, 1, i, "1", new(i / (double)count, 0, 1d / count, 0.5))).ToArray(), true);

    private static ReadBar Bar(Step step = Step.C, NoteValue value = NoteValue.Whole, bool tie = false) => new(99, 0, 4, 4, false, BarEnd.Normal, 0,
    [
        new(1, ReadClef.Treble, [new(1, [new(ReadKind.Note, [new(step, Accidental.None, 4, tie, 0)], value, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]),
        new(2, ReadClef.Bass, [new(1, [new(ReadKind.Rest, [], value, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]),
    ]);

    private static BarCorrectionPatch Patch(NotationReview state, params BarReplacement[] replacements) => new(state.Source.Sha256,
        state.Source.Revision, state.Current.Revision, replacements.Select(b => b.SourceBarId).ToArray(), replacements, CorrectionOrigin.Model);

    private static NotationReview Fill(NotationReview state, params string[] ids) =>
        NotationCorrections.Apply(state, Patch(state, ids.Select(id => new BarReplacement(id, Bar(), [])).ToArray()));

    [Fact]
    public void AChunkCanPopulateMissingBarsWithoutChangingItsNeighbours()
    {
        var empty = NotationCorrections.Create(Source());

        var filled = Fill(empty, "b1");

        Assert.Null(empty.Current.Bars[1].Written);
        Assert.NotNull(filled.Current.Bars[1].Written);
        Assert.False(filled.Current.Bars[1].Accepted);
        Assert.Same(empty.Current.Bars[0], filled.Current.Bars[0]);
        Assert.Same(empty.Current.Bars[2], filled.Current.Bars[2]);
        Assert.Equal(["b1"], Assert.Single(filled.History).TargetBarIds);
        Assert.NotEqual(empty.Current.Revision, filled.Current.Revision);
    }

    [Fact]
    public void OnlyTheActualFirstSourceBarCanUseThePickupAllowance()
    {
        var empty = NotationCorrections.Create(Source());
        var pickup = Bar(value: NoteValue.Quarter);

        Assert.NotNull(NotationCorrections.Apply(empty, Patch(empty, new BarReplacement("b0", pickup, []))).Current.Bars[0].Written);
        var error = Assert.Throws<CorrectionRejectedException>(() => NotationCorrections.Apply(empty, Patch(empty, new BarReplacement("b1", pickup, []))));
        Assert.Equal(CorrectionRejection.InvalidNotation, error.Code);
        Assert.All(empty.Current.Bars, b => Assert.Null(b.Written));
    }

    [Fact]
    public void AcceptedBarsRequireAnExplicitReopenBeforeAnyCorrection()
    {
        var state = Fill(NotationCorrections.Create(Source()), "b0");
        state = NotationCorrections.Accept(state, state.Current.Revision, "b0");
        var patch = Patch(state, new BarReplacement("b0", Bar(Step.D), []));

        Assert.Equal(CorrectionRejection.ProtectedBar, Assert.Throws<CorrectionRejectedException>(() => NotationCorrections.Apply(state, patch)).Code);
        Assert.Equal(CorrectionRejection.ProtectedBar, Assert.Throws<CorrectionRejectedException>(() =>
            NotationCorrections.Apply(state, patch with { Origin = CorrectionOrigin.Manual })).Code);
        var reopened = NotationCorrections.Reopen(state, state.Current.Revision, "b0");
        var corrected = NotationCorrections.Apply(reopened, patch with { BaseNotationRevision = reopened.Current.Revision });
        Assert.False(corrected.Current.Bars[0].Accepted);
        Assert.Equal(Step.D, corrected.Current.Bars[0].Written!.Staves[0].Voices[0].Events[0].Pitches[0].Step);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("source-revision")]
    [InlineData("notation-revision")]
    public void ProposalsMustMatchTheirExactSourceAndNotationRevision(string changed)
    {
        var state = NotationCorrections.Create(Source());
        var patch = Patch(state, new BarReplacement("b0", Bar(), []));
        patch = changed switch
        {
            "hash" => patch with { SourceSha256 = new string('b', 64) },
            "source-revision" => patch with { SourceRevision = "source-r2" },
            _ => patch with { BaseNotationRevision = "old" },
        };

        var error = Assert.Throws<CorrectionRejectedException>(() => NotationCorrections.Apply(state, patch));
        Assert.Equal(changed == "notation-revision" ? CorrectionRejection.StaleNotation : CorrectionRejection.StaleSource, error.Code);
        Assert.Empty(state.History);
    }

    [Theory]
    [InlineData("duplicate-target")]
    [InlineData("duplicate-replacement")]
    [InlineData("missing")]
    [InlineData("foreign-replacement")]
    [InlineData("foreign-target")]
    [InlineData("empty")]
    public void ReplacementOwnershipMustMatchTheTargetsExactly(string invalid)
    {
        var state = NotationCorrections.Create(Source());
        var replacement = new BarReplacement("b0", Bar(), []);
        var patch = Patch(state, replacement);
        patch = invalid switch
        {
            "duplicate-target" => patch with { TargetBarIds = ["b0", "b0"] },
            "duplicate-replacement" => patch with { Replacements = [replacement, replacement] },
            "missing" => patch with { TargetBarIds = ["b0", "b1"] },
            "foreign-replacement" => patch with { Replacements = [replacement with { SourceBarId = "b2" }] },
            "foreign-target" => patch with { TargetBarIds = ["elsewhere"], Replacements = [replacement with { SourceBarId = "elsewhere" }] },
            _ => patch with { TargetBarIds = [], Replacements = [] },
        };

        Assert.Equal(CorrectionRejection.InvalidTargets, Assert.Throws<CorrectionRejectedException>(() => NotationCorrections.Apply(state, patch)).Code);
        Assert.All(state.Current.Bars, b => Assert.Null(b.Written));
    }

    [Fact]
    public void MalformedNotationIsRejectedAtomically()
    {
        var state = Fill(NotationCorrections.Create(Source()), "b0");
        var patch = Patch(state, new BarReplacement("b0", Bar(Step.D), []), new BarReplacement("b1", Bar() with { Staves = null! }, []));

        Assert.Equal(CorrectionRejection.InvalidNotation, Assert.Throws<CorrectionRejectedException>(() => NotationCorrections.Apply(state, patch)).Code);
        Assert.Equal(Step.C, state.Current.Bars[0].Written!.Staves[0].Voices[0].Events[0].Pitches[0].Step);
        Assert.Null(state.Current.Bars[1].Written);
        Assert.Single(state.History);
    }

    [Fact]
    public void ATargetCannotBreakAnAcceptedNeighboursTie()
    {
        var state = NotationCorrections.Create(Source());
        state = NotationCorrections.Apply(state, Patch(state, new BarReplacement("b0", Bar(tie: true), []), new BarReplacement("b1", Bar(), [])));
        state = NotationCorrections.Accept(state, state.Current.Revision, "b0");

        var error = Assert.Throws<CorrectionRejectedException>(() => NotationCorrections.Apply(state, Patch(state, new BarReplacement("b1", Bar(Step.D), []))));

        Assert.Equal(CorrectionRejection.InvalidNotation, error.Code);
        Assert.True(state.Current.Bars[0].Accepted);
    }

    [Fact]
    public void MissingBarsAreNotJoinedAcrossAGap()
    {
        var state = NotationCorrections.Create(Source());

        state = NotationCorrections.Apply(state, Patch(state, new BarReplacement("b0", Bar(tie: true), []), new BarReplacement("b2", Bar(Step.D), [])));

        Assert.Null(state.Current.Bars[1].Written);
        Assert.True(state.Current.Bars[0].Written!.Staves[0].Voices[0].Events[0].Pitches[0].Tie);
        Assert.Equal(CorrectionRejection.InvalidNotation, Assert.Throws<CorrectionRejectedException>(() =>
            NotationCorrections.Apply(state, Patch(state, new BarReplacement("b1", Bar(Step.D), [])))).Code);
    }

    [Fact]
    public void ModelDoubtsNeverReplaceUserFlagsOrImplyAcceptance()
    {
        var state = Fill(NotationCorrections.Create(Source()), "b0");
        state = NotationCorrections.SetUserFlags(state, state.Current.Revision, "b0", [new("f1", "pitch", "Check the ledger line")]);
        state = NotationCorrections.Apply(state, Patch(state, new BarReplacement("b0", Bar(Step.D), [new(1, "Crowded notes")])));

        Assert.Single(state.Current.Bars[0].UserFlags);
        Assert.Single(state.Current.Bars[0].ModelDoubts);
        Assert.False(state.Current.Bars[0].Accepted);
        Assert.False(state.Current.Bars[0].UserCorrected);
        state = NotationCorrections.Apply(state, Patch(state, new BarReplacement("b0", Bar(), [])) with { Origin = CorrectionOrigin.Manual });
        Assert.Single(state.Current.Bars[0].UserFlags);
        Assert.Empty(state.Current.Bars[0].ModelDoubts);
        Assert.True(state.Current.Bars[0].UserCorrected);
    }

    [Fact]
    public void UndoPreservesAcceptedNeighboursAndNeverReusesAnOldRevision()
    {
        var state = Fill(NotationCorrections.Create(Source()), "b0", "b1");
        state = NotationCorrections.Accept(state, state.Current.Revision, "b0");
        var oldPatch = Patch(state, new BarReplacement("b1", Bar(Step.D), []));
        var changed = NotationCorrections.Apply(state, oldPatch);

        var undone = NotationCorrections.UndoLastAction(changed, changed.Current.Revision);

        Assert.True(undone.Current.Bars[0].Accepted);
        Assert.Same(state.Current.Bars[0], undone.Current.Bars[0]);
        Assert.Equal(state.Current.Bars[1].Written, undone.Current.Bars[1].Written);
        Assert.NotEqual(state.Current.Revision, undone.Current.Revision);
        Assert.NotEqual(changed.Current.Revision, undone.Current.Revision);
        Assert.Equal(NotationChangeKind.Undo, undone.History[^1].Kind);
        Assert.Equal(CorrectionRejection.StaleNotation, Assert.Throws<CorrectionRejectedException>(() => NotationCorrections.Apply(undone, oldPatch)).Code);
    }

    [Fact]
    public void InputArraysCannotMutateCurrentOrHistoricalNotation()
    {
        var source = Source();
        var state = NotationCorrections.Create(source);
        var pitches = new[] { new ReadPitch(Step.C, Accidental.None, 4, false, 0) };
        var events = new[] { Bar().Staves[0].Voices[0].Events[0] with { Pitches = pitches } };
        var voices = new[] { new ReadVoice(1, events) };
        var staves = new[] { new ReadStaff(1, ReadClef.Treble, voices), Bar().Staves[1] };
        var doubts = new[] { new ModelBarDoubt(1, "Source is faint") };
        state = NotationCorrections.Apply(state, Patch(state, new BarReplacement("b0", Bar() with { Staves = staves }, doubts)));
        var flags = new[] { new UserBarFlag("u1", "pitch", "Inspect") };
        state = NotationCorrections.SetUserFlags(state, state.Current.Revision, "b0", flags);

        pitches[0] = pitches[0] with { Step = Step.D };
        events[0] = events[0] with { Value = NoteValue.Quarter };
        voices[0] = voices[0] with { Voice = 4 };
        staves[0] = staves[0] with { Staff = 9 };
        doubts[0] = new(2, "Changed");
        flags[0] = new("u2", "rhythm", "Changed");
        ((SourceBar[])source.Bars)[0] = source.Bars[0] with { Id = "changed" };

        var saved = state.Current.Bars[0];
        Assert.Equal(Step.C, saved.Written!.Staves[0].Voices[0].Events[0].Pitches[0].Step);
        Assert.Equal(NoteValue.Whole, saved.Written.Staves[0].Voices[0].Events[0].Value);
        Assert.Equal(1, saved.Written.Staves[0].Voices[0].Voice);
        Assert.Equal(1, saved.Written.Staves[0].Staff);
        Assert.Equal(1, saved.ModelDoubts[0].Staff);
        Assert.Equal("u1", saved.UserFlags[0].Id);
        Assert.Equal("b0", state.Source.Bars[0].Id);
        Assert.Same(state.History[0].After.Bars[0].Written, saved.Written);
    }

    [Fact]
    public void ReviewHistoryAndUndoSurviveSerialization()
    {
        var state = Fill(NotationCorrections.Create(Source()), "b0");
        state = NotationCorrections.SetUserFlags(state, state.Current.Revision, "b0", [new("u1", "pitch", "Inspect")]);
        state = NotationCorrections.Accept(state, state.Current.Revision, "b0");

        var restored = NotationCorrections.Restore(JsonSerializer.Deserialize<NotationReview>(JsonSerializer.Serialize(state))!);
        var undone = NotationCorrections.UndoLastAction(restored, restored.Current.Revision);

        Assert.False(undone.Current.Bars[0].Accepted);
        Assert.Single(undone.Current.Bars[0].UserFlags);
        Assert.NotNull(undone.Current.Bars[0].Written);
        Assert.Equal(4, undone.History.Length);
        Assert.Equal(2, undone.UndoStack.Length);
    }

    [Fact]
    public void RestoringDeepCopiesMutableDeserializedLists()
    {
        var state = Fill(NotationCorrections.Create(Source()), "b0");
        var loaded = JsonSerializer.Deserialize<NotationReview>(JsonSerializer.Serialize(state))!;
        var restored = NotationCorrections.Restore(loaded);

        ((IList<ReadPitch>)loaded.Current.Bars[0].Written!.Staves[0].Voices[0].Events[0].Pitches)[0] = new(Step.D, Accidental.None, 4, false, 0);

        Assert.Equal(Step.C, restored.Current.Bars[0].Written!.Staves[0].Voices[0].Events[0].Pitches[0].Step);
        Assert.Equal(Step.C, restored.History[0].After.Bars[0].Written!.Staves[0].Voices[0].Events[0].Pitches[0].Step);
    }
}
