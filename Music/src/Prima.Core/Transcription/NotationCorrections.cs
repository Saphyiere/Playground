using System.Collections.Immutable;
using System.Text.Json;

namespace Prima.Core.Transcription;

public enum CorrectionOrigin { Model, Manual }
public enum NotationChangeKind { ModelCorrection, ManualCorrection, Accepted, Reopened, UserFlags, Undo }
public enum CorrectionRejection { InvalidSource, StaleSource, StaleNotation, InvalidTargets, ProtectedBar, InvalidNotation, InvalidReview, NothingToUndo }

public sealed class CorrectionRejectedException(CorrectionRejection code, string message) : InvalidOperationException(message)
{
    public CorrectionRejection Code { get; } = code;
}

public sealed record ModelBarDoubt(int? Staff, string Note);
public sealed record UserBarFlag(string Id, string Category, string Note);
public sealed record BarReplacement(string SourceBarId, ReadBar Written, IReadOnlyList<ModelBarDoubt> ModelDoubts);

/// <summary>A proposal owns exactly its targets. ReadBar.Index is notation data, never the source identity.</summary>
public sealed record BarCorrectionPatch(string SourceSha256, string SourceRevision, string BaseNotationRevision,
    IReadOnlyList<string> TargetBarIds, IReadOnlyList<BarReplacement> Replacements, CorrectionOrigin Origin);

public sealed record ReviewedBar(string SourceBarId, ReadBar? Written, ImmutableArray<ModelBarDoubt> ModelDoubts,
    bool Accepted, bool UserCorrected, ImmutableArray<UserBarFlag> UserFlags);
public sealed record NotationSnapshot(string Revision, ImmutableArray<ReviewedBar> Bars);
public sealed record NotationChange(NotationChangeKind Kind, ImmutableArray<string> TargetBarIds, NotationSnapshot Before, NotationSnapshot After);

/// <summary>
/// Immutable written notation and separate human/model review state. History is append-only; UndoStack is the previous state
/// of each still-applied action. Persistence and concurrency claims belong to the caller.
/// </summary>
public sealed record NotationReview(ConversionSource Source, NotationSnapshot Current, ImmutableArray<NotationChange> History,
    ImmutableArray<NotationSnapshot> UndoStack);

public static class NotationCorrections
{
    public static NotationReview Create(ConversionSource source)
    {
        var frozen = FreezeSource(source);
        var bars = frozen.Bars.Select(b => new ReviewedBar(b.Id, null, [], false, false, [])).ToImmutableArray();
        return new(frozen, new(Guid.NewGuid().ToString("N"), bars), [], []);
    }

    /// <summary>Reestablishes deep immutability after loading records whose ReadBar lists were deserialized as mutable lists.</summary>
    public static NotationReview Restore(NotationReview state)
    {
        var source = FreezeSource(state.Source);
        return state with
        {
            Source = source,
            Current = FreezeSnapshot(source, state.Current),
            History = state.History.Select(h => h with
            {
                Before = FreezeSnapshot(source, h.Before), After = FreezeSnapshot(source, h.After),
            }).ToImmutableArray(),
            UndoStack = state.UndoStack.Select(s => FreezeSnapshot(source, s)).ToImmutableArray(),
        };
    }

    public static NotationReview Apply(NotationReview state, BarCorrectionPatch patch)
    {
        if (!string.Equals(state.Source.Sha256, patch.SourceSha256, StringComparison.OrdinalIgnoreCase) || state.Source.Revision != patch.SourceRevision)
            throw new CorrectionRejectedException(CorrectionRejection.StaleSource, "The proposal belongs to a different source or segmentation revision.");
        RequireRevision(state, patch.BaseNotationRevision);
        if (patch.TargetBarIds is null || patch.Replacements is null)
            throw new CorrectionRejectedException(CorrectionRejection.InvalidTargets, "The proposal is missing targets or replacements.");
        var targets = patch.TargetBarIds.ToImmutableArray();
        var known = state.Current.Bars.Select((b, i) => (b.SourceBarId, Index: i)).ToDictionary(b => b.SourceBarId, b => b.Index, StringComparer.Ordinal);
        if (targets.Length == 0 || targets.Distinct(StringComparer.Ordinal).Count() != targets.Length || targets.Any(t => t is null || !known.ContainsKey(t)))
            throw new CorrectionRejectedException(CorrectionRejection.InvalidTargets, "The proposal has empty, duplicate, or foreign targets.");
        var replacements = patch.Replacements.ToImmutableArray();
        if (replacements.Any(r => r is null) || replacements.Length != targets.Length ||
            replacements.Select(r => r.SourceBarId).Distinct(StringComparer.Ordinal).Count() != replacements.Length ||
            !targets.ToHashSet(StringComparer.Ordinal).SetEquals(replacements.Select(r => r.SourceBarId)))
            throw new CorrectionRejectedException(CorrectionRejection.InvalidTargets, "Each target must have exactly one replacement, with no other bars.");
        if (!Enum.IsDefined(patch.Origin)) throw new CorrectionRejectedException(CorrectionRejection.InvalidTargets, "Unknown correction origin.");
        if (state.Current.Bars.Any(b => b.Accepted && targets.Contains(b.SourceBarId)))
            throw new CorrectionRejectedException(CorrectionRejection.ProtectedBar, "Reopen an accepted target before correcting it.");

        var updated = state.Current.Bars.ToBuilder();
        try
        {
            foreach (var replacement in replacements)
            {
                var at = known[replacement.SourceBarId];
                var doubts = replacement.ModelDoubts.ToImmutableArray();
                if (doubts.Any(d => d is null || d.Staff is not (null or 1 or 2) || string.IsNullOrWhiteSpace(d.Note)))
                    throw new FormatException("A model doubt has an invalid staff or no explanation.");
                updated[at] = updated[at] with
                {
                    Written = Freeze(replacement.Written),
                    ModelDoubts = doubts,
                    UserCorrected = patch.Origin == CorrectionOrigin.Manual,
                };
            }
            ValidateWritten(updated.ToImmutable());
        }
        catch (Exception e) when (e is not CorrectionRejectedException)
        {
            throw new CorrectionRejectedException(CorrectionRejection.InvalidNotation, $"The proposal's notation is invalid: {e.Message}");
        }
        return Commit(state, updated.ToImmutable(), targets,
            patch.Origin == CorrectionOrigin.Manual ? NotationChangeKind.ManualCorrection : NotationChangeKind.ModelCorrection);
    }

    public static NotationReview Accept(NotationReview state, string baseRevision, string barId)
    {
        var at = RequireBar(state, baseRevision, barId);
        var bar = state.Current.Bars[at];
        if (bar.Written is null) throw new CorrectionRejectedException(CorrectionRejection.InvalidReview, "An unread bar cannot be accepted.");
        return bar.Accepted ? state : Commit(state, state.Current.Bars.SetItem(at, bar with { Accepted = true }), [barId], NotationChangeKind.Accepted);
    }

    public static NotationReview Reopen(NotationReview state, string baseRevision, string barId)
    {
        var at = RequireBar(state, baseRevision, barId);
        var bar = state.Current.Bars[at];
        return !bar.Accepted ? state : Commit(state, state.Current.Bars.SetItem(at, bar with { Accepted = false }), [barId], NotationChangeKind.Reopened);
    }

    public static NotationReview SetUserFlags(NotationReview state, string baseRevision, string barId, IReadOnlyList<UserBarFlag> flags)
    {
        var at = RequireBar(state, baseRevision, barId);
        var frozen = flags.ToImmutableArray();
        if (frozen.Any(f => f is null || string.IsNullOrWhiteSpace(f.Id) || string.IsNullOrWhiteSpace(f.Category) || f.Note is null) ||
            frozen.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != frozen.Length)
            throw new CorrectionRejectedException(CorrectionRejection.InvalidReview, "User flags need unique identities and a category.");
        var bar = state.Current.Bars[at];
        return bar.UserFlags.SequenceEqual(frozen) ? state
            : Commit(state, state.Current.Bars.SetItem(at, bar with { UserFlags = frozen }), [barId], NotationChangeKind.UserFlags);
    }

    /// <summary>
    /// Deliberately undoes the entire last action, including that action's review changes. Unrelated accepted bars remain as
    /// they were. The restored content receives a fresh revision, so proposals against an old head never become current again.
    /// </summary>
    public static NotationReview UndoLastAction(NotationReview state, string baseRevision)
    {
        RequireRevision(state, baseRevision);
        if (state.UndoStack.IsEmpty) throw new CorrectionRejectedException(CorrectionRejection.NothingToUndo, "There is no action to undo.");
        var before = state.UndoStack[^1];
        for (var i = 0; i < state.Current.Bars.Length; i++)
        {
            var current = state.Current.Bars[i];
            if (current.Accepted && !SameData(current.Written, before.Bars[i].Written))
                throw new CorrectionRejectedException(CorrectionRejection.ProtectedBar, "Reopen an accepted bar before undoing its notation.");
        }
        var targets = state.Current.Bars.Where((b, i) => !SameData(b, before.Bars[i])).Select(b => b.SourceBarId).ToImmutableArray();
        var restored = new NotationSnapshot(Guid.NewGuid().ToString("N"), before.Bars);
        return state with
        {
            Current = restored,
            History = state.History.Add(new(NotationChangeKind.Undo, targets, state.Current, restored)),
            UndoStack = state.UndoStack.RemoveAt(state.UndoStack.Length - 1),
        };
    }

    private static NotationReview Commit(NotationReview state, ImmutableArray<ReviewedBar> bars, ImmutableArray<string> targets, NotationChangeKind kind)
    {
        var next = new NotationSnapshot(Guid.NewGuid().ToString("N"), bars);
        return state with { Current = next, History = state.History.Add(new(kind, targets, state.Current, next)), UndoStack = state.UndoStack.Add(state.Current) };
    }

    private static void RequireRevision(NotationReview state, string revision)
    {
        if (state.Current.Revision != revision) throw new CorrectionRejectedException(CorrectionRejection.StaleNotation, "The notation or review state changed after this action was prepared.");
    }

    private static int RequireBar(NotationReview state, string revision, string id)
    {
        RequireRevision(state, revision);
        var at = state.Source.Bars.ToList().FindIndex(b => b.Id == id);
        if (at < 0) throw new CorrectionRejectedException(CorrectionRejection.InvalidTargets, "The target is not a bar in this source.");
        return at;
    }

    // ReadBar contains nested collections: record/reference equality changes after a JSON round trip.
    private static bool SameData<T>(T left, T right) => ReferenceEquals(left, right) || JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private static ConversionSource FreezeSource(ConversionSource source)
    {
        var problems = source.Problems();
        if (problems.Count > 0) throw new CorrectionRejectedException(CorrectionRejection.InvalidSource, string.Join(" ", problems));
        return source with { Bars = source.Bars.ToImmutableArray() };
    }

    private static NotationSnapshot FreezeSnapshot(ConversionSource source, NotationSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Revision) || snapshot.Bars.IsDefault || snapshot.Bars.Any(b => b is null) ||
            !snapshot.Bars.Select(b => b.SourceBarId).SequenceEqual(source.Bars.Select(b => b.Id)))
            throw new CorrectionRejectedException(CorrectionRejection.InvalidReview, "A saved snapshot does not match its source bar identities.");
        try
        {
            var bars = snapshot.Bars.Select(b => b with { Written = b.Written is null ? null : Freeze(b.Written) }).ToImmutableArray();
            if (bars.Any(b => b.Accepted && b.Written is null || b.ModelDoubts.IsDefault || b.UserFlags.IsDefault))
                throw new CorrectionRejectedException(CorrectionRejection.InvalidReview, "A saved snapshot has invalid review state.");
            ValidateWritten(bars);
            return snapshot with { Bars = bars };
        }
        catch (Exception e) when (e is not CorrectionRejectedException)
        {
            throw new CorrectionRejectedException(CorrectionRejection.InvalidNotation, $"Saved notation is invalid: {e.Message}");
        }
    }

    private static ReadBar Freeze(ReadBar bar) => bar with
    {
        Staves = bar.Staves.Select(s => s with
        {
            Voices = s.Voices.Select(v => v with
            {
                Events = v.Events.Select(e => e with { Pitches = e.Pitches.ToImmutableArray(), Marks = e.Marks.ToImmutableArray() }).ToImmutableArray(),
            }).ToImmutableArray(),
        }).ToImmutableArray(),
    };

    private static void ValidateWritten(ImmutableArray<ReviewedBar> bars)
    {
        // A gap is not a join. Only the actual first source bar may use the opening-pickup allowance.
        for (var at = 0; at < bars.Length;)
        {
            if (bars[at].Written is null) { at++; continue; }
            var first = at;
            var run = new List<ReadBar>();
            while (at < bars.Length && bars[at].Written is { } written) { run.Add(written with { Index = run.Count + 1 }); at++; }
            var problems = ReadingChecks.Problems(new SystemReading(run, []), first == 0);
            if (problems.Count > 0) throw new CorrectionRejectedException(CorrectionRejection.InvalidNotation, string.Join(" ", problems));
        }
    }
}
