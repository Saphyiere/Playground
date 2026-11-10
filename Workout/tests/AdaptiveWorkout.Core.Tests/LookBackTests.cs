using System.Text.Json;
using System.Text.Json.Nodes;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Core.Tests;

public class LookBackTests
{
    private const long Rows = 1, Curls = 2, Plank = 3, PullUps = 4, Dips = 5;

    private static readonly DateTimeOffset Today = new(2026, 11, 8, 17, 0, 0, TimeSpan.FromHours(-8));

    private static readonly Dictionary<long, Exercise> Exercises = new()
    {
        [Rows] = new() { Id = Rows, Name = "Overhand rows", Measure = ExerciseMeasure.Weight },
        [Curls] = new() { Id = Curls, Name = "Curls", Measure = ExerciseMeasure.Weight },
        [Plank] = new() { Id = Plank, Name = "Plank", Measure = ExerciseMeasure.Time },
        [PullUps] = new() { Id = PullUps, Name = "Pull-ups", Measure = ExerciseMeasure.Weight },
        [Dips] = new() { Id = Dips, Name = "Dips", Measure = ExerciseMeasure.Weight },
    };

    private static readonly Dictionary<long, int> Countdowns = new() { [Rows] = 120, [Curls] = 90 };

    private sealed record Done(long Exercise, double? Weight, int Reps, int Seconds = 45, int RestAfter = 120, bool HitGoal = false);

    // A finished workout from `at`: each set runs its seconds, then rests before the next. Ids are the workout's id × 100 plus the set's order.
    private static (WorkoutSession Session, List<WorkoutSet> Sets) Workout(long id, DateTimeOffset at, params Done[] sets)
    {
        var result = new List<WorkoutSet>();
        var time = at;
        foreach (var set in sets)
        {
            result.Add(new WorkoutSet
            {
                Id = id * 100 + result.Count + 1, SessionId = id, ExerciseId = set.Exercise, Order = result.Count + 1, StartedAt = time,
                StoppedAt = time.AddSeconds(set.Seconds), Weight = set.Weight, Reps = set.Reps, HitGoal = set.HitGoal,
                DurationSeconds = Exercises[set.Exercise].Measure == ExerciseMeasure.Time ? set.Seconds : null,
            });
            time = time.AddSeconds(set.Seconds + set.RestAfter);
        }

        return (new WorkoutSession { Id = id, StartedAt = at, FinishedAt = result.Count == 0 ? at : result[^1].StoppedAt }, result);
    }

    // Heart rate every 5 seconds, going steadily from one number to another.
    private static IEnumerable<HeartRateSample> Ramp(DateTimeOffset from, DateTimeOffset to, int bpmFrom, int bpmTo)
    {
        var seconds = (to - from).TotalSeconds;
        for (var s = 0; s <= seconds; s += 5)
            yield return new HeartRateSample { At = from.AddSeconds(s), Bpm = (int)Math.Round(bpmFrom + (bpmTo - bpmFrom) * (s / seconds)) };
    }

    private static LookBackContext Context(Dictionary<long, TrainerTarget>? trainer = null) =>
        new(5, Countdowns, trainer ?? [], new BodyWeightEntry { At = Today.AddDays(-1), Weight = 212.4, WeightUnit = WeightUnit.Pounds }, WeightUnit.Pounds, 1800, 2450);

    private static LookBackInput Input((WorkoutSession Session, List<WorkoutSet> Sets) today, IEnumerable<(WorkoutSession Session, List<WorkoutSet> Sets)>? before = null,
        IEnumerable<HeartRateSample>? samples = null, IReadOnlyList<TemplateExercise>? plan = null, LookBackContext? context = null)
    {
        var earlier = (before ?? []).ToList();
        return LookBackFacts.For(today.Session, "Back day", today.Sets, plan ?? [], Exercises, [.. earlier.Select(w => w.Session), today.Session],
            [.. earlier.SelectMany(w => w.Sets), .. today.Sets], [.. samples ?? []], context ?? Context());
    }

    private static List<SetFlagKind> Flags(LookBackInput input, int setNumber, long exercise = Rows) =>
        [.. input.Workout.Sets.Single(s => s.ExerciseId == exercise && s.Number == setNumber).Flags.Select(f => f.Kind)];

    [Fact]
    public void For_GivesEachSetItsTimeRestAndHeartRate()
    {
        var today = Workout(9, Today, new Done(Rows, 100, 10, 40, 90), new Done(Rows, 100, 10, 40, 90), new Done(Rows, 100, 9, 40, 0));
        var samples = Ramp(Today, Today.AddSeconds(40), 100, 140).Concat(Ramp(Today.AddSeconds(45), Today.AddSeconds(125), 135, 105));

        var sets = Input(today, samples: samples).Workout.Sets;

        Assert.Equal([1, 2, 3], sets.Select(s => s.Number));
        Assert.All(sets, s => Assert.Equal("Overhand rows", s.Exercise));
        Assert.Equal([40, 40, 40], sets.Select(s => s.Seconds));
        Assert.Equal([null, 90, 90], sets.Select(s => s.RestBeforeSeconds));
        Assert.Equal((100, 120, 140), (sets[0].BpmAtStart, sets[0].BpmAverage, sets[0].BpmPeak));
        Assert.Equal(105, sets[1].RestLowBpm);
        Assert.Null(sets[2].BpmAtStart);
        Assert.All(sets, s => Assert.Empty(s.Flags));
    }

    [Fact]
    public void For_ASupersetRoundRestsTogether_AndOnlyItsFirstSetCanBeShort()
    {
        var round = Today.AddSeconds(60);
        var sets = new List<WorkoutSet>
        {
            new() { Id = 1, SessionId = 9, ExerciseId = PullUps, Order = 1, StartedAt = Today, StoppedAt = Today.AddSeconds(40), Reps = 8 },
            new() { Id = 2, SessionId = 9, ExerciseId = Dips, Order = 2, StartedAt = Today, StoppedAt = Today.AddSeconds(50), Reps = 10 },
            new() { Id = 3, SessionId = 9, ExerciseId = PullUps, Order = 3, StartedAt = round, StoppedAt = round.AddSeconds(40), Reps = 7 },
            new() { Id = 4, SessionId = 9, ExerciseId = Dips, Order = 4, StartedAt = round, StoppedAt = round.AddSeconds(45), Reps = 9 },
        };
        var session = new WorkoutSession { Id = 9, StartedAt = Today, FinishedAt = round.AddSeconds(45) };

        var facts = Input((session, sets), context: Context() with { RestTargets = new Dictionary<long, int> { [Dips] = 120 } }).Workout.Sets;

        Assert.Equal([false, true, false, true], facts.Select(s => s.WithPrevious));
        Assert.Equal([null, null, 10, 10], facts.Select(s => s.RestBeforeSeconds));
        Assert.Equal([SetFlagKind.ShortRest], facts[2].Flags.Select(f => f.Kind));
        Assert.Equal("0:10 rest; the countdown after Dips was 2:00", facts[2].Flags[0].Detail);
        Assert.Empty(facts[3].Flags);
    }

    [Fact]
    public void For_LastTimesAreTheThreeBeforeThisWorkout_NewestFirst()
    {
        var weeks = Enumerable.Range(1, 4).Select(w => Workout(w, Today.AddDays(-7 * (5 - w)), new Done(Rows, 90 + 5 * w, 10, RestAfter: 105), new Done(Rows, 90 + 5 * w, 10, RestAfter: 105),
            new Done(Rows, 90 + 5 * w, 10, HitGoal: w == 4))).ToList();
        var later = Workout(20, Today.AddDays(2), new Done(Rows, 200, 10));
        var today = Workout(9, Today, new Done(Rows, 115, 10));

        var rows = Input(today, [.. weeks, later]).Exercises.Single();

        Assert.Equal(["3 × 10 at 110", "3 × 10 at 105", "3 × 10 at 100"], rows.LastTimes.Select(t => t.Sets));
        Assert.Equal([new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 18)], rows.LastTimes.Select(t => t.Date));
        Assert.Equal([true, false, false], rows.LastTimes.Select(t => t.HitGoal));
        Assert.Equal([110.0], rows.LastTimes[0].Weights);
        Assert.Equal(105, rows.LastTimes[0].TypicalRestSeconds);
        Assert.Equal(120, rows.RestCountdownSeconds);
    }

    [Fact]
    public void For_SaysWhatTheTrainerSaid_OnlyWhenItWasForThisWorkout()
    {
        var older = Workout(1, Today.AddDays(-7), new Done(Rows, 100, 10));
        var last = Workout(2, Today.AddDays(-3), new Done(Rows, 100, 10, HitGoal: true));
        var today = Workout(9, Today, new Done(Rows, 105, 10));

        var forToday = Input(today, [older, last], context: Context(new() { [Rows] = new TrainerTarget(2, 105, WeightUnit.Pounds, 10, 3, 135) }));
        var stale = Input(today, [older, last], context: Context(new() { [Rows] = new TrainerTarget(1, 105, WeightUnit.Pounds, 10, 3, 135) }));

        Assert.Equal("3 × 10 at 105 lb, rest 2:15", forToday.Exercises.Single().TrainerSaid);
        Assert.Equal("", stale.Exercises.Single().TrainerSaid);
    }

    [Fact]
    public void For_PlannedSetsComeFromTheEntry_OrItsSupersetsFirst()
    {
        var today = Workout(9, Today, new Done(PullUps, null, 8), new Done(Dips, null, 10), new Done(Curls, 30, 12));
        var plan = new List<TemplateExercise>
        {
            new() { ExerciseId = PullUps, Order = 0, PlannedSets = 3, SupersetGroup = 1 },
            new() { ExerciseId = Dips, Order = 1, SupersetGroup = 1 },
            new() { ExerciseId = Curls, Order = 2 },
        };

        var exercises = Input(today, plan: plan).Exercises;

        Assert.Equal([3, 3, null], exercises.Select(e => e.PlannedSets));
    }

    [Fact]
    public void For_BodyWeightAndCaloriesTravelWithIt()
    {
        var input = Input(Workout(9, Today, new Done(Rows, 100, 10)));

        Assert.Equal(new LookBackBody(212.4, WeightUnit.Pounds, 1800, 2450), input.Body);
        Assert.Equal(("Back day", new DateOnly(2026, 11, 8), 5.0), (input.Workout.Name, input.Workout.Date, input.HitGoalStep));
    }

    [Fact]
    public void For_CarriesRepsLeft_AndHowFarHeartRateFellInTheMinuteAfter()
    {
        var today = Workout(9, Today, new Done(Rows, 100, 10, 40, 120), new Done(Rows, 100, 10, 40, 0));
        today.Sets[0].RepsLeft = 2;
        var samples = Ramp(Today.AddSeconds(30), Today.AddSeconds(50), 140, 150).Append(new HeartRateSample { At = Today.AddSeconds(100), Bpm = 118 });

        var sets = Input(today, samples: samples).Workout.Sets;

        Assert.Equal([2, null], sets.Select(s => s.RepsLeft));
        Assert.Equal([32, null], sets.Select(s => s.BpmDropFirstMinute));
    }

    [Fact]
    public void For_SaysWhenAnExerciseHasGoneFlat_CountingToday()
    {
        var before = new[] { 105.0, 100, 100, 100 }.Select((w, i) => Workout(i + 1, Today.AddDays(-7 * (4 - i)), new Done(Rows, w, 10), new Done(Rows, w, 10), new Done(Rows, w, 10))).ToList();

        var flat = Input(Workout(9, Today, new Done(Rows, 100, 10), new Done(Rows, 100, 10), new Done(Rows, 100, 10)), before).Exercises.Single();
        var moving = Input(Workout(9, Today, new Done(Rows, 110, 10), new Done(Rows, 110, 10), new Done(Rows, 110, 10)), before).Exercises.Single();

        Assert.Equal((4, "3 × 10 at 105 on 2026-10-11"), (flat.FlatWorkouts, flat.Best));
        Assert.Equal((0, ""), (moving.FlatWorkouts, moving.Best));
    }

    [Fact]
    public void Input_KeptBeforeVersion2_StillReads()
    {
        var input = Input(Workout(9, Today, new Done(Rows, 100, 10)));
        var kept = JsonSerializer.SerializeToNode(input, AssistSchema.Json)!;
        foreach (var set in kept["workout"]!["sets"]!.AsArray()) set!.AsObject().Remove("repsLeft");
        foreach (var set in kept["workout"]!["sets"]!.AsArray()) set!.AsObject().Remove("bpmDropFirstMinute");
        foreach (var set in kept["workout"]!["sets"]!.AsArray()) set!.AsObject().Remove("warmUp");
        foreach (var exercise in kept["exercises"]!.AsArray()) { exercise!.AsObject().Remove("flatWorkouts"); exercise.AsObject().Remove("best"); }
        kept.AsObject().Remove("muscles");

        var read = kept.Deserialize<LookBackInput>(AssistSchema.Json)!;

        Assert.Equal(input.Workout.Sets[0] with { Flags = read.Workout.Sets[0].Flags }, read.Workout.Sets[0]);
        Assert.Equal((0, ""), (read.Exercises[0].FlatWorkouts, read.Exercises[0].Best));
        Assert.Null(read.Muscles);
        Assert.False(read.Workout.Sets[0].WarmUp);
    }

    [Fact]
    public void AWarmUpSet_HasNoFlags_AndIsntOneOfTheExercisesSets()
    {
        // Rows warmed up at 30, far from its 100s, with 20 seconds' rest after; curls only got a warm-up.
        var before = Workout(1, Today.AddDays(-7), new Done(Rows, 100, 10), new Done(Rows, 100, 10), new Done(Rows, 100, 10));
        var today = Workout(9, Today, new Done(Rows, 30, 12, RestAfter: 20), new Done(Rows, 100, 10), new Done(Rows, 100, 10), new Done(Rows, 100, 10), new Done(Curls, 15, 12));
        today.Sets[0].WarmUp = today.Sets[4].WarmUp = true;

        var input = Input(today, [before]);
        var warmUp = input.Workout.Sets[0];

        Assert.True(warmUp.WarmUp);
        Assert.Equal((0, 0), (warmUp.Number, warmUp.Flags.Count));
        Assert.Equal([0, 1, 2, 3, 0], input.Workout.Sets.Select(s => s.Number));
        Assert.All(input.Workout.Sets, s => Assert.Empty(s.Flags));
        Assert.Equal(["Overhand rows"], input.Exercises.Select(e => e.Name));
        Assert.Contains("\"warmUp\":true", JsonSerializer.Serialize(input, AssistSchema.Json));
    }

    [Fact]
    public void Problems_AWarmUpsWeightIsntOneToPlanFrom()
    {
        var today = Workout(9, Today, new Done(Rows, 30, 12), new Done(Rows, 100, 10), new Done(Rows, 100, 10));
        today.Sets[0].WarmUp = true;
        var input = Input(today);
        LookBack Plan(double weight) => new(["Rows held."], [new NextLift(Rows, 3, 10, weight, 120, "10, 10 at 100")], []);

        Assert.Empty(LookBackPlan.Problems(input, Plan(105)));
        Assert.Equal(["Overhand rows: 35 isn't whole steps of 5 from a weight used today or in its last times (100), by at most a tenth of it or one step."],
            LookBackPlan.Problems(input, Plan(35)));
        Assert.Equal(new TrainerTarget(9, 105, WeightUnit.Pounds, 10, 3, 120), LookBackPlan.Targets(input, Plan(105))[0].Target);
    }

    // The six-day split: a session every day but Sunday.
    private static readonly long?[] SixDays = [11, 12, 13, 14, 15, 16, null];

    private static readonly Dictionary<long, MuscleTags> MuscleTagsOf = new()
    {
        [Rows] = new(Rows, [Muscle.Lats, Muscle.UpperBack], [Muscle.RearDelts, Muscle.Biceps], false),
        [Curls] = new(Curls, [Muscle.Biceps], [Muscle.Forearms], true),
    };

    [Fact]
    public void WeekMuscles_OnTheWeeksLastPlannedWorkout_CountsMondayToIt()
    {
        // Saturday is the routine's last day. Last Sunday's rows aren't this week's; Thursday's pull-ups aren't tagged yet.
        var saturday = Today.AddDays(-1);
        var lastWeek = Workout(1, Today.AddDays(-7), new Done(Rows, 100, 10), new Done(Rows, 100, 10));
        var monday = Workout(2, Today.AddDays(-6), new Done(Rows, 100, 10), new Done(Rows, 100, 10), new Done(Curls, 30, 12));
        var thursday = Workout(3, Today.AddDays(-3), new Done(PullUps, null, 10));
        var today = Workout(4, saturday, new Done(Rows, 100, 10));
        var all = new[] { lastWeek, monday, thursday, today };

        var muscles = LookBackFacts.WeekMuscles(today.Session, [.. all.Select(w => w.Session)], [.. all.SelectMany(w => w.Sets)], Exercises, MuscleTagsOf, SixDays)!;

        Assert.Equal(3, muscles.Week.Single(m => m.Muscle == Muscle.Lats).Sets);
        Assert.Equal(2.5, muscles.Week.Single(m => m.Muscle == Muscle.Biceps).Sets);
        Assert.Equal(0.5, muscles.Week.Single(m => m.Muscle == Muscle.Forearms).Sets);
        Assert.DoesNotContain(Muscle.Lats, muscles.Missing);
        Assert.Contains(Muscle.Calves, muscles.Missing);
        Assert.Equal(["Pull-ups"], muscles.Untagged);
    }

    [Fact]
    public void WeekMuscles_IsNull_WhileAPlannedDayIsToCome_OrWithoutARoutine()
    {
        var wednesday = Workout(4, Today.AddDays(-4), new Done(Rows, 100, 10));
        var sunday = Workout(5, Today, new Done(Rows, 100, 10));

        Assert.Null(LookBackFacts.WeekMuscles(wednesday.Session, [wednesday.Session], wednesday.Sets, Exercises, MuscleTagsOf, SixDays));
        Assert.Null(LookBackFacts.WeekMuscles(sunday.Session, [sunday.Session], sunday.Sets, Exercises, MuscleTagsOf, null));
        Assert.NotNull(LookBackFacts.WeekMuscles(sunday.Session, [sunday.Session], sunday.Sets, Exercises, MuscleTagsOf, SixDays));
    }

    [Fact]
    public void WeekMuscles_GoWithTheInput()
    {
        var today = Workout(9, Today, new Done(Rows, 100, 10));
        var muscles = LookBackFacts.WeekMuscles(today.Session, [today.Session], today.Sets, Exercises, MuscleTagsOf, SixDays);

        var input = Input(today, context: Context() with { Muscles = muscles });

        Assert.Equal(muscles, input.Muscles);
        Assert.Contains("\"missing\":[\"chest\",", JsonSerializer.Serialize(input, AssistSchema.Json));
    }

    [Fact]
    public void Flags_ALongSet_OverThreeMinutesOrThreeTimesTheUsual()
    {
        var today = Workout(9, Today, new Done(Rows, 100, 10, 45), new Done(Rows, 100, 10, 280), new Done(Rows, 100, 10, 150), new Done(Rows, 100, 10, 100));

        var input = Input(today);

        Assert.Equal([SetFlagKind.LongSet], Flags(input, 2));
        Assert.Equal("Ran 4:40; its sets usually run 1:40", input.Workout.Sets[1].Flags[0].Detail);
        Assert.Empty(Flags(input, 3));
        Assert.Empty(Flags(input, 4));
        Assert.Equal([SetFlagKind.LongSet], Flags(Input(Workout(9, Today, new Done(Rows, 100, 10, 30), new Done(Rows, 100, 10, 30), new Done(Rows, 100, 10, 95))), 3));
    }

    [Fact]
    public void Flags_AShortRest_UnderThirtySecondsOrHalfTheCountdown()
    {
        var input = Input(Workout(9, Today, new Done(Rows, 100, 10, RestAfter: 20), new Done(Rows, 100, 10, RestAfter: 55), new Done(Rows, 100, 10, RestAfter: 70),
            new Done(Rows, 100, 10)));

        Assert.Equal([SetFlagKind.ShortRest], Flags(input, 2));
        Assert.Equal([SetFlagKind.ShortRest], Flags(input, 3));
        Assert.Empty(Flags(input, 4));
    }

    [Fact]
    public void Flags_EffortDuringTheRest_WhenHeartRateClimbsInTheRestAndFallsInTheSet()
    {
        var today = Workout(9, Today, new Done(Rows, 100, 10, 40, 120), new Done(Rows, 100, 10, 60, 0));
        var restStart = Today.AddSeconds(40);
        var setStart = restStart.AddSeconds(120);
        var samples = Ramp(restStart, setStart, 100, 132).Concat(Ramp(setStart.AddSeconds(5), setStart.AddSeconds(60), 128, 112));

        var input = Input(today, samples: samples);

        Assert.Equal([SetFlagKind.EffortDuringRest], Flags(input, 2));
        Assert.Equal("Heart rate rose 100 → 132 during the rest, then fell to 112 during the set", input.Workout.Sets[1].Flags[0].Detail);
    }

    [Fact]
    public void Flags_AnOddNumber_FarFromTheExercisesOthers()
    {
        var before = Workout(1, Today.AddDays(-4), new Done(Rows, 100, 10), new Done(Rows, 100, 10));
        var input = Input(Workout(9, Today, new Done(Rows, 100, 10), new Done(Rows, 1000, 10), new Done(Rows, 100, 100), new Done(Rows, 105, 10)), [before]);

        Assert.Equal([SetFlagKind.OddNumber], Flags(input, 2));
        Assert.Equal("1000 lb; its other sets are around 100", input.Workout.Sets[1].Flags[0].Detail);
        Assert.Equal("100 reps; its other sets are around 10", input.Workout.Sets[2].Flags[0].Detail);
        Assert.Empty(Flags(input, 4));
    }

    [Fact]
    public void Flags_AGoalBox_TickedOnFewerReps_OrLeftEmptyWhenEverySetReachedLastTimesGoal()
    {
        var ticked = Input(Workout(9, Today, new Done(Rows, 100, 10), new Done(Rows, 100, 10), new Done(Rows, 100, 8, HitGoal: true)));
        var last = Workout(1, Today.AddDays(-4), new Done(Rows, 100, 10), new Done(Rows, 100, 10), new Done(Rows, 100, 10, HitGoal: true));
        var unticked = Input(Workout(9, Today, new Done(Rows, 105, 10), new Done(Rows, 105, 10), new Done(Rows, 105, 11)), [last]);
        var shortOfIt = Input(Workout(9, Today, new Done(Rows, 105, 10), new Done(Rows, 105, 10), new Done(Rows, 105, 9)), [last]);

        Assert.Equal([SetFlagKind.GoalBox], Flags(ticked, 3));
        Assert.Equal("Hit goal ticked on 8 reps, after 10 at the same weight", ticked.Workout.Sets[2].Flags[0].Detail);
        Assert.Equal([0, 0, 1], unticked.Workout.Sets.Select(s => s.Flags.Count));
        Assert.Equal([SetFlagKind.GoalBox], Flags(unticked, 3));
        Assert.Equal("Every set reached last time's goal (10 at 100 lb), but Hit goal isn't ticked", unticked.Workout.Sets[2].Flags[0].Detail);
        Assert.All(shortOfIt.Workout.Sets, s => Assert.Empty(s.Flags));
    }

    [Fact]
    public void Flags_TimedExercisesHaveNone()
    {
        var input = Input(Workout(9, Today, new Done(Plank, null, 0, 60, 10), new Done(Plank, null, 0, 600, 0)));

        Assert.All(input.Workout.Sets, s => Assert.Empty(s.Flags));
        Assert.Equal([60, 600], input.Workout.Sets.Select(s => s.Seconds));
    }

    // Today: rows at 100 for 10, 10, 10 (last time 95 and 100); curls at 30; pull-ups at bodyweight; a plank.
    private static LookBackInput Planned()
    {
        var last = Workout(1, Today.AddDays(-4), new Done(Rows, 95, 10), new Done(Curls, 25, 12));
        return Input(Workout(9, Today, new Done(Rows, 100, 10), new Done(Rows, 100, 10), new Done(Rows, 100, 10, HitGoal: true), new Done(Curls, 30, 12),
            new Done(PullUps, null, 8), new Done(Plank, null, 0, 60)), [last]);
    }

    private static NextLift Lift(long exercise, double weight, int rest = 120, int sets = 3, int reps = 10) => new(exercise, sets, reps, weight, rest, "10, 10, 10: up a step");

    private static LookBack Answer(params NextLift[] lifts) => new(["Rows went up to 100 and held 10, 10, 10."], lifts, []);

    [Fact]
    public void Problems_NoneForAGoodAnswer()
    {
        var input = Planned();

        Assert.Empty(LookBackPlan.Problems(input, Answer(Lift(Rows, 105), Lift(Curls, 30, 90, reps: 12), Lift(PullUps, 0, 90, reps: 8))));
        Assert.Empty(new LookBackTask().Check(input, Answer(Lift(Rows, 100), Lift(Curls, 25, 90), Lift(PullUps, 0, 90))));
    }

    [Fact]
    public void Problems_EachWeightExerciseOnce_AndNothingElse()
    {
        var problems = LookBackPlan.Problems(Planned(), Answer(Lift(Rows, 105), Lift(Rows, 105), Lift(Plank, 0), Lift(77, 50)));

        Assert.Equal(
        [
            "Overhand rows is in nextTime 2 times; give it once.",
            "Plank is timed: leave it out of nextTime.",
            "nextTime has exercise 77, which isn't in today's workout.",
            "Curls is missing from nextTime.",
            "Pull-ups is missing from nextTime.",
        ], problems);
    }

    [Fact]
    public void Problems_AWeightIsWholeStepsNearAWeightUsed()
    {
        var input = Planned();

        Assert.Empty(LookBackPlan.Problems(input, Answer(Lift(Rows, 95), Lift(Curls, 35, 90), Lift(PullUps, 0, 90))));
        Assert.Equal(
        [
            "Overhand rows: 107.5 isn't whole steps of 5 from a weight used today or in its last times (95, 100), by at most a tenth of it or one step.",
            "Curls: 40 isn't whole steps of 5 from a weight used today or in its last times (25, 30), by at most a tenth of it or one step.",
            "Pull-ups is done at bodyweight: its weight should be 0, not 10.",
        ], LookBackPlan.Problems(input, Answer(Lift(Rows, 107.5), Lift(Curls, 40, 90), Lift(PullUps, 10, 90))));
    }

    [Fact]
    public void Problems_AssistanceMovesByStepsToo()
    {
        var input = Input(Workout(9, Today, new Done(Dips, -40, 8), new Done(Dips, -40, 8)));

        Assert.Empty(LookBackPlan.Problems(input, new LookBack([], [new NextLift(Dips, 2, 8, -35, 90, "8, 8: less help")], [])));
        Assert.Single(LookBackPlan.Problems(input, new LookBack([], [new NextLift(Dips, 2, 8, -25, 90, "8, 8: less help")], [])));
    }

    [Fact]
    public void Problems_RangesWhyAndNotes()
    {
        var input = Planned();
        var answer = new LookBack(["a", "b", "c", "d", "e", "f"], [Lift(Rows, 105, rest: 100, sets: 0, reps: 60) with { Why = " " }, Lift(Curls, 30, 20), Lift(PullUps, 0, 90)],
            [new SetNote(902, SetTreatment.TimeIgnored, "Ran into the walk to the rows"), new SetNote(5, SetTreatment.Counted, "?"), new SetNote(902, SetTreatment.Counted, "again")]);

        Assert.Equal(
        [
            "Overhand rows: sets should be 1 to 10, not 0.",
            "Overhand rows: reps should be 1 to 50, not 60.",
            "Overhand rows: rest should be 30 to 300 seconds in steps of 15, not 100.",
            "Overhand rows: why is empty.",
            "Curls: rest should be 30 to 300 seconds in steps of 15, not 20.",
            "setNotes has set 5, which isn't one of today's sets.",
            "setNotes has set 902 2 times; give it one note.",
            "highlights has 6 lines; keep it to five at most.",
        ], LookBackPlan.Problems(input, answer));
    }

    [Fact]
    public void Targets_KeepTodaysUnit_AndBodyweightStaysBodyweight()
    {
        var input = Input(Workout(9, Today, new Done(Rows, 50, 10), new Done(PullUps, null, 8)));
        input = input with { Workout = input.Workout with { Sets = [.. input.Workout.Sets.Select(s => s.ExerciseId == Rows ? s with { Unit = WeightUnit.Kilograms } : s)] } };

        var targets = LookBackPlan.Targets(input, new LookBack([], [new NextLift(Rows, 3, 10, 55, 150, "up"), new NextLift(PullUps, 3, 9, 0, 90, "more reps")], []));

        Assert.Equal([(Rows, new TrainerTarget(9, 55, WeightUnit.Kilograms, 10, 3, 150)), (PullUps, new TrainerTarget(9, null, WeightUnit.Pounds, 9, 3, 90))], targets);
    }

    [Fact]
    public void Schema_IsOneTheApiTakes()
    {
        var schema = JsonSerializer.SerializeToElement(AssistSchema.For(typeof(Reply<LookBack>)));
        var answer = schema.GetProperty("properties").GetProperty("answer").GetProperty("properties");
        var note = answer.GetProperty("setNotes").GetProperty("items").GetProperty("properties");

        Assert.Equal(["highlights", "nextTime", "setNotes"], answer.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["counted", "timeIgnored", "setIgnored", "likelyTypo"], note.GetProperty("treatedAs").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Input_TravelsAsCamelCaseJson_AndAReplyReadsBack()
    {
        var json = JsonSerializer.Serialize(Input(Workout(9, Today, new Done(Rows, 100, 10, 280))), AssistSchema.Json);
        var reply = JsonSerializer.Deserialize<Reply<LookBack>>("""
            {"status":"answered","summary":"Rows held.","notes":[],"answer":{"highlights":["Rows held at 100."],
            "nextTime":[{"exerciseId":1,"sets":3,"reps":10,"weight":100,"restSeconds":120,"why":"10 at 100: stay"}],
            "setNotes":[{"setId":901,"treatedAs":"TimeIgnored","note":"A late tap on Stop"}]}}
            """, AssistSchema.Json);

        Assert.Contains("\"flags\":[{\"kind\":\"longSet\",\"detail\":\"Ran 4:40\"}]", json);
        Assert.Contains("\"unit\":\"pounds\"", json);
        Assert.Equal(SetTreatment.TimeIgnored, reply!.Answer!.SetNotes[0].TreatedAs);
    }
}
