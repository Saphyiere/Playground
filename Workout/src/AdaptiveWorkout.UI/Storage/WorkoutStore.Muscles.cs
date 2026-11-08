using AdaptiveWorkout.Core;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AdaptiveWorkout.UI.Storage;

public sealed partial class WorkoutStore
{
    private const string ByClaude = "claude", ByOwner = "owner";

    /// <summary>Every tagged exercise's muscles, by exercise id; an exercise not tagged yet isn't in it.</summary>
    public Task<Dictionary<long, MuscleTags>> GetMuscleTagsAsync() => RunAsync(async c =>
    {
        var tagged = await c.QueryAsync<(long Id, string By)>("SELECT Id, TaggedBy FROM Exercise WHERE TaggedBy IS NOT NULL");
        var rows = (await c.QueryAsync<(long ExerciseId, string Muscle, bool Main)>("SELECT ExerciseId, Muscle, Main FROM ExerciseMuscle"))
            .Select(r => (r.ExerciseId, Muscle: Enum.TryParse<Muscle>(r.Muscle, out var muscle) ? muscle : (Muscle?)null, r.Main))
            .Where(r => r.Muscle is not null).ToLookup(r => r.ExerciseId);
        return tagged.ToDictionary(t => t.Id, t => new MuscleTags(t.Id,
            [.. Muscles.All.Where(m => rows[t.Id].Any(r => r.Muscle == m && r.Main))],
            [.. Muscles.All.Where(m => rows[t.Id].Any(r => r.Muscle == m && !r.Main))], t.By == ByOwner));
    });

    /// <summary>
    /// Saves exercises' muscles in place of their old ones. Claude's (not <see cref="MuscleTags.ByOwner"/>) never replace mine, even
    /// ones I saved while Claude was being asked.
    /// </summary>
    public Task SaveMuscleTagsAsync(IReadOnlyList<MuscleTags> tags) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        foreach (var tag in tags)
        {
            var by = tag.ByOwner ? ByOwner : ByClaude;
            var changed = await c.ExecuteAsync("UPDATE Exercise SET TaggedBy = @by WHERE Id = @ExerciseId AND (@by = @ByOwner OR TaggedBy IS NULL OR TaggedBy <> @ByOwner)", new { by, tag.ExerciseId, ByOwner }, transaction);
            if (changed == 0) continue;

            await c.ExecuteAsync("DELETE FROM ExerciseMuscle WHERE ExerciseId = @ExerciseId", tag, transaction);
            await c.ExecuteAsync("INSERT INTO ExerciseMuscle (ExerciseId, Muscle, Main) VALUES (@ExerciseId, @Muscle, @Main)",
                tag.Main.Select(m => new { tag.ExerciseId, Muscle = m.ToString(), Main = true }).Concat(tag.Helps.Select(m => new { tag.ExerciseId, Muscle = m.ToString(), Main = false })), transaction);
        }

        await transaction.CommitAsync();
    });
}
