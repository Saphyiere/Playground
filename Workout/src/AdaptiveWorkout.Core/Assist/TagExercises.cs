using System.ComponentModel;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Core.Assist;

/// <summary>
/// The exercises to tag (<paramref name="Exercises"/>), and the ones I tagged myself (<paramref name="OwnerTagged"/>), for Claude to
/// follow where an exercise is alike.
/// </summary>
public sealed record ExercisesToTag(IReadOnlyList<ExerciseToTag> Exercises, IReadOnlyList<OwnerTag> OwnerTagged);

/// <summary>An exercise to tag: its name as I typed it, whether it's weights or time, and how I did it last ("3 × 10 at 100"; empty when never).</summary>
public sealed record ExerciseToTag(long Id, string Name, ExerciseMeasure Measure, string LastTime);

/// <summary>An exercise I tagged myself: its main muscles (primary) and the ones that help (secondary).</summary>
public sealed record OwnerTag(string Name, IReadOnlyList<Muscle> Primary, IReadOnlyList<Muscle> Secondary);

/// <summary>The muscles one exercise trains.</summary>
public sealed record ExerciseTag(
    [property: Description("The exercise's id, from the input.")]
    long ExerciseId,
    [property: Description("The muscles the exercise is done for: one to three. Empty only for cardio, like walking, running or cycling.")]
    IReadOnlyList<Muscle> Primary,
    [property: Description("Muscles that help, without being what it's done for: none to three, and never one already in primary.")]
    IReadOnlyList<Muscle> Secondary);

public sealed record ExerciseTags(
    [property: Description("Each exercise in the input's exercises, once.")]
    IReadOnlyList<ExerciseTag> Exercises);

/// <summary>
/// Tags the owner's exercises with the muscles they train, for the muscle map (see <see cref="Muscles"/>). Asked for the exercises not
/// tagged yet; the owner can change any tag, and theirs stays.
/// </summary>
public sealed class TagExercises : AssistTask<ExercisesToTag, ExerciseTags>
{
    public override string Name => "workout.tag-exercises";

    public override int Version => 1;

    // Naming what a row or a curl trains takes knowledge, not long thought.
    public override EffortLevel Effort => EffortLevel.Low;

    public override int MaxTokens => 8_000;

    public override string Instructions => """
        Tag each of the owner's exercises with the muscles it trains, from the list the schema gives. The names are the owner's own,
        typed as they like ("Overhand rows", "Cable rope pushdown", "Flat chest press"), so read each as someone at the gym would, and
        where a name could mean several exercises, take the most common. Each comes with how the owner last did it, which says whether
        it's done with weight, at bodyweight (no weight) or assisted (a negative one), or for time.

        - primary: the muscles the exercise is done for, one to three, as a coach would list them. Rows: lats and upper back. Squats:
          quads and glutes. Chest press: chest. Curls: biceps. Pushdowns and skull crushers: triceps. A plank: abs.
        - secondary: muscles that help, none to three, never one already in primary. Rows: rear delts and biceps. Chest press: front
          delts and triceps. Squats: hamstrings and lower back.
        - Cardio, like walking, running or cycling, trains none of these as strength work: leave both lists empty.
        - ownerTagged are exercises the owner tagged themselves. Where one of these is alike (a variation, or the same movement on
          another machine), tag it the way the owner did.

        The summary: one sentence on what you tagged.
        """;

    public override IReadOnlyList<string> Check(ExercisesToTag input, ExerciseTags answer)
    {
        var problems = new List<string>();
        var names = input.Exercises.ToDictionary(e => e.Id, e => e.Name);
        foreach (var group in answer.Exercises.GroupBy(t => t.ExerciseId).Where(g => g.Count() > 1))
            problems.Add($"{names.GetValueOrDefault(group.Key, $"Exercise {group.Key}")} is tagged {group.Count()} times; tag it once.");
        foreach (var tag in answer.Exercises)
        {
            if (!names.TryGetValue(tag.ExerciseId, out var name))
            {
                problems.Add($"Exercise {tag.ExerciseId} isn't in the input.");
                continue;
            }

            if (tag.Primary.Count > 3) problems.Add($"{name}: give at most three primary muscles, not {tag.Primary.Count}.");
            if (tag.Secondary.Count > 3) problems.Add($"{name}: give at most three secondary muscles, not {tag.Secondary.Count}.");
            if (tag.Primary.Count == 0 && tag.Secondary.Count > 0) problems.Add($"{name}: secondary muscles need a primary one; for cardio leave both empty.");
            foreach (var both in tag.Primary.Intersect(tag.Secondary)) problems.Add($"{name}: {Muscles.Name(both)} is in both lists; keep it in primary only.");
            if (tag.Primary.Distinct().Count() < tag.Primary.Count || tag.Secondary.Distinct().Count() < tag.Secondary.Count) problems.Add($"{name}: a muscle is listed twice.");
        }

        foreach (var missing in input.Exercises.Where(e => answer.Exercises.All(t => t.ExerciseId != e.Id)))
            problems.Add($"{missing.Name} is missing; tag every exercise.");
        return problems;
    }

    /// <summary>Claude's tags, as the app keeps them: each muscle once, in the muscle map's order.</summary>
    public static IReadOnlyList<MuscleTags> Tags(ExerciseTags answer) =>
        [.. answer.Exercises.Select(t => new MuscleTags(t.ExerciseId, [.. Muscles.All.Where(t.Primary.Contains)], [.. Muscles.All.Where(m => t.Secondary.Contains(m) && !t.Primary.Contains(m))], false))];
}
