// Preview host: the real AdaptiveWorkout.UI screens in a browser, as Blazor Server, against sample data in a SQLite file, so a
// cloud session can render and screenshot them (see CLAUDE.md). /seed/{scenario} fills the data; the other endpoints stand in for
// the watch and the phone's notification.
using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using AdaptiveWorkout.Preview;
using AdaptiveWorkout.UI;
using AdaptiveWorkout.UI.Assist;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Data.Sqlite;
using AdaptiveWorkout.Preview.Components;
using Playground.AI.Assist;
using Playground.Updates;

var dbPath = Path.Combine(AppContext.BaseDirectory, "preview.db");
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Stand-ins for the phone's notification and watch link that write down what they were told, for /notice-log.
var noticeLog = new List<string>();
builder.Services.AddSingleton<WorkoutNotice>(new LogNotice(noticeLog));
builder.Services.AddSingleton<WatchLink>(sp => new LogWatchLink(sp.GetRequiredService<WorkoutStore>(), noticeLog));
builder.Services.AddSingleton<FlowRegistry>();
// Claude answers from the workout itself (PreviewClaude), never the API; the key is kept in memory (AddAdaptiveWorkout's default).
builder.Services.AddSingleton<PreviewClaude>();
builder.Services.AddSingleton<IClaude>(sp => sp.GetRequiredService<PreviewClaude>());
builder.Services.AddAdaptiveWorkout(dbPath);
// Updates: GitHub and the phone stood in for (Kit/Playground.Updates); /updates/{scenario} sets what's installed and what's out.
builder.Services.AddStandInUpdates(new UpdateOptions("workout", "workout.apk") { DataFiles = [dbPath], BackupFolder = "Workout backups" }, "Workout");
// fresh per page load here, so reseeding between screenshots isn't masked by a cached workout; the newest is kept for /watch-next
builder.Services.AddScoped<WorkoutFlow>(sp => sp.GetRequiredService<FlowRegistry>().Latest = ActivatorUtilities.CreateInstance<WorkoutFlow>(sp));

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Routes).Assembly);

async Task<string> Seed(string scenario, WorkoutStore store)
{
    await store.GetRoutinesAsync(); // ensures the schema exists
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        foreach (var table in new[] { "WeekLook", "Question", "DataCheck", "LookBack", "ClaudeCall", "SessionExercise", "WorkoutSet", "WorkoutSession", "TemplateExercise", "RoutineDay", "SessionTemplate", "ExerciseMuscle", "Exercise", "Routine", "Setting", "FoodEntry", "BodyWeightEntry", "HeartRateSample" })
            new SqliteCommand($"DELETE FROM {table}", connection).ExecuteNonQuery();
    }
    if (scenario == "empty") return "empty";

    var now = DateTimeOffset.Now;
    DateTimeOffset At(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, now.Offset);

    var rows = await store.GetOrCreateExerciseAsync("Overhand rows", ExerciseMeasure.Weight);
    var pullUps = await store.GetOrCreateExerciseAsync("Pull-ups", ExerciseMeasure.Weight);
    var dips = await store.GetOrCreateExerciseAsync("Dips", ExerciseMeasure.Weight);
    var pulldown = await store.GetOrCreateExerciseAsync("Lat pulldown", ExerciseMeasure.Weight);
    var pushdown = await store.GetOrCreateExerciseAsync("Cable rope pushdown", ExerciseMeasure.Weight);
    var bench = await store.GetOrCreateExerciseAsync("Flat chest press", ExerciseMeasure.Weight);
    var squat = await store.GetOrCreateExerciseAsync("Squats", ExerciseMeasure.Weight);
    var curls = await store.GetOrCreateExerciseAsync("Curls", ExerciseMeasure.Weight);
    var walk = await store.GetOrCreateExerciseAsync("Walk", ExerciseMeasure.Time);
    var skulls = await store.GetOrCreateExerciseAsync("Skull crushers", ExerciseMeasure.Weight);

    var push = new SessionTemplate { Name = "Push day" };
    await store.SaveTemplateAsync(push, [new() { ExerciseId = bench.Id, Order = 0, PlannedSets = 3 }]);
    var back = new SessionTemplate { Name = "Back day" };
    await store.SaveTemplateAsync(back,
    [
        new() { ExerciseId = rows.Id, Order = 0, PlannedSets = 3 },
        new() { ExerciseId = pullUps.Id, Order = 1, PlannedSets = 3, SupersetGroup = 2 },
        new() { ExerciseId = dips.Id, Order = 2, SupersetGroup = 2 },
        new() { ExerciseId = pulldown.Id, Order = 3 },
        new() { ExerciseId = pushdown.Id, Order = 4, PlannedSets = 3 },
    ]);
    var legs = new SessionTemplate { Name = "Leg day" };
    await store.SaveTemplateAsync(legs, [new() { ExerciseId = squat.Id, Order = 0, PlannedSets = 3 }]);
    var arms = new SessionTemplate { Name = "Arm day" };
    await store.SaveTemplateAsync(arms, [new() { ExerciseId = curls.Id, Order = 0 }, new() { ExerciseId = skulls.Id, Order = 1 }]);
    var sixDays = new Routine { Name = "Six-day split" };
    await store.SaveRoutineAsync(sixDays, [push.Id, back.Id, legs.Id, arms.Id, push.Id, back.Id, null]);
    await store.SaveRoutineAsync(new Routine { Name = "Upper lower" }, [back.Id, legs.Id, null, push.Id, legs.Id, null, null]);
    if (scenario != "noroutine") await store.SetActiveRoutineAsync(sixDays.Id);

    if (scenario == "years")
    {
        // Three years of training until three weeks ago: four workouts a week, 15 sets each; four meals a day; a weigh-in a day.
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var addSession = new SqliteCommand("INSERT INTO WorkoutSession (TemplateId, StartedAt, FinishedAt, LoggedAfterward, Improvement, ExercisesCompared) VALUES ($t, $s, $f, 0, 0.01, 3); SELECT last_insert_rowid();", connection, transaction);
        var addSet = new SqliteCommand("""INSERT INTO WorkoutSet (SessionId, ExerciseId, "Order", StartedAt, StoppedAt, Weight, WeightUnit, Reps, DurationSeconds, HitGoal) VALUES ($s, $e, $o, $a, $b, $w, 0, $r, NULL, 0)""", connection, transaction);
        var addFood = new SqliteCommand("INSERT INTO FoodEntry (At, Calories) VALUES ($a, $c)", connection, transaction);
        var addWeight = new SqliteCommand("INSERT INTO BodyWeightEntry (At, Weight, WeightUnit) VALUES ($a, $w, 0)", connection, transaction);
        // The watch's heart rate, about once a second through each workout.
        var addBeat = new SqliteCommand("INSERT INTO HeartRateSample (At, Bpm) VALUES ($a, $b)", connection, transaction);
        var beatAt = addBeat.Parameters.Add("$a", SqliteType.Text);
        var beatBpm = addBeat.Parameters.Add("$b", SqliteType.Integer);
        string O(DateTimeOffset at) => at.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var plans = new[] { (back.Id, new[] { rows, pullUps, dips, pulldown, pushdown }), (legs.Id, new[] { squat, squat, squat, curls, skulls }), (push.Id, new[] { bench, bench, dips, pushdown, skulls }), (arms.Id, new[] { curls, skulls, curls, skulls, rows }) };
        var day = now.Date.AddDays(-3 * 365);
        for (var i = 0; day < now.Date.AddDays(-21); day = day.AddDays(1), i++)
        {
            var at = new DateTimeOffset(day, now.Offset);
            addWeight.Parameters.Clear(); addWeight.Parameters.AddWithValue("$a", O(at.AddHours(7))); addWeight.Parameters.AddWithValue("$w", 200 - i * 0.015);
            addWeight.ExecuteNonQuery();
            foreach (var hour in new[] { 8, 12, 15, 19 })
            {
                addFood.Parameters.Clear(); addFood.Parameters.AddWithValue("$a", O(at.AddHours(hour))); addFood.Parameters.AddWithValue("$c", 400 + hour * 10);
                addFood.ExecuteNonQuery();
            }
            if (day.DayOfWeek is not (DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Friday)) continue;

            var (templateId, lifts) = plans[i % plans.Length];
            var start = at.AddHours(18);
            addSession.Parameters.Clear(); addSession.Parameters.AddWithValue("$t", templateId); addSession.Parameters.AddWithValue("$s", O(start)); addSession.Parameters.AddWithValue("$f", O(start.AddMinutes(50)));
            var sessionId = (long)addSession.ExecuteScalar()!;
            for (var second = 0; second < 50 * 60; second++)
            {
                beatAt.Value = O(start.AddSeconds(second));
                beatBpm.Value = 100 + second % 45;
                addBeat.ExecuteNonQuery();
            }
            var order = 1;
            foreach (var lift in lifts)
            {
                for (var set = 0; set < 3; set++, order++)
                {
                    var setStart = start.AddMinutes(order * 3);
                    addSet.Parameters.Clear();
                    addSet.Parameters.AddWithValue("$s", sessionId); addSet.Parameters.AddWithValue("$e", lift.Id); addSet.Parameters.AddWithValue("$o", order);
                    addSet.Parameters.AddWithValue("$a", O(setStart)); addSet.Parameters.AddWithValue("$b", O(setStart.AddSeconds(45))); addSet.Parameters.AddWithValue("$w", 60 + i % 50); addSet.Parameters.AddWithValue("$r", 10);
                    addSet.ExecuteNonQuery();
                }
            }
        }
        transaction.Commit();
    }

    async Task<WorkoutSession> Session(long? templateId, DateTimeOffset start, bool finish, params (Exercise Exercise, double? Weight, int? Reps, int? Seconds, bool Goal)[] sets)
    {
        var session = new WorkoutSession { TemplateId = templateId, StartedAt = start };
        await store.StartSessionAsync(session);
        var at = start;
        var order = 1;
        foreach (var set in sets)
        {
            await store.InsertSetAsync(session, new WorkoutSet { ExerciseId = set.Exercise.Id, Order = order++, StartedAt = at, StoppedAt = at.AddSeconds(set.Seconds ?? 45), Weight = set.Weight, Reps = set.Reps, DurationSeconds = set.Seconds, HitGoal = set.Goal });
            at = at.AddMinutes(3);
        }
        if (finish)
        {
            session.FinishedAt = at;
            await store.FinishSessionAsync(session);
        }
        return session;
    }

    // Five weeks before October: steady progress with one bad back day.
    if (scenario == "trend")
    {
        (Exercise, double?, int?, int?, bool) W(Exercise exercise, double weight, int reps) => (exercise, weight, reps, null, false);
        await Session(back.Id, At(9, 4, 18), true, W(rows, 85, 10), W(rows, 85, 10), W(rows, 85, 10), W(pulldown, 100, 10), W(pulldown, 100, 10), W(pulldown, 100, 10), W(pushdown, 40, 12), W(pushdown, 40, 12), W(pushdown, 40, 12));
        await Session(legs.Id, At(9, 7, 18), true, W(squat, 115, 8), W(squat, 115, 8), W(squat, 115, 8));
        await Session(push.Id, At(9, 9, 18), true, W(bench, 115, 10), W(bench, 115, 10), W(bench, 115, 10));
        await Session(back.Id, At(9, 11, 18), true, W(rows, 90, 10), W(rows, 90, 10), W(rows, 90, 10), W(pulldown, 105, 10), W(pulldown, 105, 10), W(pulldown, 105, 9), W(pushdown, 45, 12), W(pushdown, 45, 12), W(pushdown, 45, 10));
        await Session(legs.Id, At(9, 14, 18), true, W(squat, 125, 8), W(squat, 125, 8), W(squat, 125, 8));
        await Session(push.Id, At(9, 16, 18), true, W(bench, 125, 10), W(bench, 125, 10), W(bench, 125, 8));
        await Session(back.Id, At(9, 18, 18), true, W(rows, 90, 10), W(rows, 90, 8), W(rows, 90, 7), W(pulldown, 105, 9), W(pulldown, 105, 8), W(pulldown, 105, 8), W(pushdown, 45, 10), W(pushdown, 45, 10), W(pushdown, 45, 9));
        await Session(legs.Id, At(9, 21, 18), true, W(squat, 125, 8), W(squat, 125, 8), W(squat, 125, 8));
        await Session(push.Id, At(9, 23, 18), true, W(bench, 125, 10), W(bench, 125, 10), W(bench, 125, 10));
        await Session(back.Id, At(9, 25, 18), true, W(rows, 95, 10), W(rows, 95, 10), W(rows, 95, 10), W(pulldown, 110, 10), W(pulldown, 110, 10), W(pulldown, 110, 10), W(pushdown, 50, 12), W(pushdown, 50, 10), W(pushdown, 50, 10));
        await Session(legs.Id, At(9, 28, 18), true, W(squat, 130, 8), W(squat, 130, 8), W(squat, 130, 8));
        await Session(push.Id, At(9, 30, 18), true, W(bench, 130, 10), W(bench, 130, 10), W(bench, 130, 8));
    }

    await Session(back.Id, At(10, 2, 18), true, (rows, 95, 10, null, false), (rows, 95, 10, null, false), (rows, 95, 8, null, false), (pullUps, null, 10, null, false), (dips, null, 12, null, false), (pullUps, null, 9, null, false), (dips, null, 12, null, false), (pullUps, null, 8, null, false), (dips, null, 10, null, false), (pulldown, 115, 10, null, false), (pulldown, 115, 10, null, false), (pulldown, 115, 9, null, false), (pushdown, 50, 12, null, false), (pushdown, 50, 10, null, false), (pushdown, 50, 10, null, false));
    var heartRateSession = await Session(back.Id, At(10, 9, 18), true, (rows, 95, 10, null, true), (rows, 95, 10, null, true), (rows, 95, 10, null, true), (pullUps, null, 10, null, false), (dips, null, 12, null, false), (pullUps, null, 9, null, false), (dips, null, 12, null, false), (pullUps, null, 9, null, false), (dips, null, 10, null, false), (pulldown, 120, 10, null, false), (pulldown, 120, 10, null, false), (pulldown, 120, 10, null, false), (pushdown, 50, 12, null, false), (pushdown, 50, 12, null, false), (pushdown, 50, 12, null, false));
    await Session(legs.Id, At(10, 12, 18), true, (squat, 135, 8, null, false), (squat, 135, 8, null, false), (squat, 135, 8, null, false));
    await Session(push.Id, At(10, 14, 18), true, (bench, 135, 10, null, false), (bench, 135, 10, null, false), (bench, 135, 10, null, false));
    await Session(null, At(10, 15, 7), true, (walk, null, null, 1800, false));
    await store.AddHeartRateAsync([.. Enumerable.Range(0, 45 * 12).Select(i => new HeartRateSample { At = heartRateSession.StartedAt.AddSeconds(i * 5), Bpm = 110 + (int)(30 * Math.Abs(Math.Sin(i / 20.0))) })]);
    await store.AddFoodAsync(1240, new DateTimeOffset(now.Date.AddHours(8), now.Offset));
    await store.AddFoodAsync(650, new DateTimeOffset(now.Date.AddHours(13), now.Offset));
    await store.AddBodyWeightAsync(184.2, WeightUnit.Pounds, now.AddDays(-28));
    await store.AddBodyWeightAsync(182.4, WeightUnit.Pounds, now.AddDays(-2));

    // Every session with a warm-up and a cool-down, and no workout open.
    if (scenario == "warmup")
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        new SqliteCommand("UPDATE SessionTemplate SET WarmUp = 1, CoolDown = 1", connection).ExecuteNonQuery();
    }

    // Ten weeks of food, some days not logged, a weigh-in every other day drifting down slower than the logs say, and body details.
    if (scenario is "food" or "food-nobody" or "checks")
    {
        var random = new Random(7);
        for (var d = 70; d >= 1; d--)
        {
            var day = now.Date.AddDays(-d);
            if (d % 9 == 4 || d % 13 == 0) continue;
            var total = 1650 + random.Next(0, 700);
            await store.AddFoodAsync(total * 3 / 10, new DateTimeOffset(day.AddHours(8), now.Offset));
            await store.AddFoodAsync(total * 3 / 10, new DateTimeOffset(day.AddHours(13), now.Offset));
            await store.AddFoodAsync(total - 2 * (total * 3 / 10), new DateTimeOffset(day.AddHours(19), now.Offset));
        }
        for (var d = 70; d >= 4; d -= 2)
            await store.AddBodyWeightAsync(Math.Round(187.5 - (70 - d) * 0.075 + (random.NextDouble() - 0.5) * 1.6, 1), WeightUnit.Pounds, new DateTimeOffset(now.Date.AddDays(-d).AddHours(7), now.Offset));
        if (scenario is "food" or "checks")
        {
            await store.SetSettingAsync("Sex", "Male");
            await store.SetSettingAsync("BirthYear", "1992");
            await store.SetSettingAsync("HeightCm", "178");
            await store.SetSettingAsync("DailyActivity", "Sitting");

            // A back day this morning: an hour of sets ending an hour ago, the watch reading about 125 bpm through it.
            var morning = await Session(back.Id, now.AddHours(-2), false, [.. Enumerable.Range(0, 15).Select(_ => (rows, (double?)95, (int?)10, (int?)null, false))]);
            var sets = await store.GetSessionSetsAsync(morning.Id);
            for (var i = 0; i < sets.Count; i++)
            {
                sets[i].StartedAt = morning.StartedAt.AddMinutes(4 * i);
                sets[i].StoppedAt = morning.StartedAt.AddMinutes(4 * i + 4);
                await store.UpdateSetAsync(sets[i]);
            }
            morning.FinishedAt = morning.StartedAt.AddHours(1);
            await store.FinishSessionAsync(morning);
            await store.AddHeartRateAsync([.. Enumerable.Range(0, 720).Select(i => new HeartRateSample { At = morning.StartedAt.AddSeconds(i * 5), Bpm = 125 + (i % 2 == 0 ? 8 : -8) })]);
        }
    }

    // A back day with every planned set done, for Finish to open its look back: rows hit the goal, the superset held, one pulldown ran
    // long (Stop tapped late) and the last pushdowns came after a short rest. The watch read through it, every 5 seconds.
    if (scenario == "lookback")
    {
        var workout = new WorkoutSession { TemplateId = back.Id, StartedAt = now.AddMinutes(-48) };
        await store.StartSessionAsync(workout);
        var at = workout.StartedAt.AddMinutes(1);
        var order = 1;
        var beats = new List<HeartRateSample>();
        void Beat(DateTimeOffset from, int seconds, int bpmFrom, int bpmTo)
        {
            for (var s = 0; s < seconds; s += 5) beats.Add(new HeartRateSample { At = from.AddSeconds(s), Bpm = bpmFrom + (bpmTo - bpmFrom) * s / Math.Max(1, seconds) });
        }
        async Task Round(int seconds, int restAfter, params (Exercise Exercise, double? Weight, int Reps, bool Goal)[] sets)
        {
            foreach (var set in sets)
                await store.InsertSetAsync(workout, new WorkoutSet { ExerciseId = set.Exercise.Id, Order = order++, StartedAt = at, StoppedAt = at.AddSeconds(seconds), Weight = set.Weight, Reps = set.Reps, HitGoal = set.Goal });
            Beat(at, seconds, 112, 146);
            Beat(at.AddSeconds(seconds), restAfter, 146, 108);
            at = at.AddSeconds(seconds + restAfter);
        }
        await Round(40, 135, (rows, 100, 10, false));
        await Round(40, 135, (rows, 100, 10, false));
        await Round(42, 150, (rows, 100, 10, true));
        await Round(45, 120, (pullUps, null, 10, false), (dips, null, 12, false));
        await Round(45, 120, (pullUps, null, 10, false), (dips, null, 12, false));
        await Round(48, 150, (pullUps, null, 9, false), (dips, null, 11, false));
        await Round(42, 135, (pulldown, 120, 10, false));
        await Round(280, 90, (pulldown, 120, 10, false));
        await Round(40, 150, (pulldown, 120, 8, false));
        await Round(38, 120, (pushdown, 50, 12, false));
        await Round(36, 20, (pushdown, 50, 12, false));
        await Round(35, 0, (pushdown, 50, 11, false));
        await store.AddHeartRateAsync(beats);
        return "lookback";
    }

    // Rows stuck below Oct 9's 10, 10, 10 at 95 for four back days since, and a back day open with two sets of rows done.
    if (scenario == "plateau")
    {
        foreach (var (month, day, reps) in new[] { (10, 16, 9), (10, 23, 8), (10, 30, 9), (11, 6, 9) })
            await Session(back.Id, At(month, day, 18), true, (rows, 95, 10, null, false), (rows, 95, 10, null, false), (rows, 95, reps, null, false));
    }

    // The food weeks, with two slips to ask about: three days ago only breakfast was logged, and a weigh-in five days ago reads 9 lb heavy.
    if (scenario == "checks")
    {
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            var day = now.Date.AddDays(-3);
            new SqliteCommand($"DELETE FROM FoodEntry WHERE At >= '{new DateTimeOffset(day, now.Offset):O}' AND At < '{new DateTimeOffset(day.AddDays(1), now.Offset):O}'", connection).ExecuteNonQuery();
        }
        await store.AddFoodAsync(850, new DateTimeOffset(now.Date.AddDays(-3).AddHours(8), now.Offset));
        await store.AddBodyWeightAsync(191.4, WeightUnit.Pounds, new DateTimeOffset(now.Date.AddDays(-5).AddHours(7), now.Offset));
    }

    // Last week and this week of the six-day split, to today, with last week's leg day missed. Claude tagged the exercises (Pull-ups
    // are mine), and Hanging leg raises, made since, aren't tagged yet. The week scenario adds food on most days and weigh-ins.
    if (scenario is "muscles" or "week")
    {
        var raises = await store.GetOrCreateExerciseAsync("Hanging leg raises", ExerciseMeasure.Weight);
        var made = (await store.GetExercisesAsync()).Where(e => e.Id != raises.Id).Select(e => new ExerciseToTag(e.Id, e.Name, e.Measure, ""));
        await store.SaveMuscleTagsAsync(TagExercises.Tags(PreviewClaude.Tag(new ExercisesToTag([.. made], []))));
        await store.SaveMuscleTagsAsync([new MuscleTags(pullUps.Id, [Muscle.Lats, Muscle.Biceps], [Muscle.UpperBack, Muscle.Forearms], true)]);
        (Exercise, double?, int?, int?, bool)[] Sets(Exercise exercise, double? weight, int count) => [.. Enumerable.Repeat((exercise, weight, (int?)10, (int?)null, false), count)];
        var days = new (long TemplateId, (Exercise, double?, int?, int?, bool)[] Sets)[]
        {
            (push.Id, [.. Sets(bench, 135, 4), .. Sets(dips, null, 3), .. Sets(skulls, 40, 3)]),
            (back.Id, [.. Sets(rows, 100, 4), .. Sets(pullUps, null, 3), .. Sets(pulldown, 120, 3), .. Sets(pushdown, 50, 3)]),
            (legs.Id, Sets(squat, 135, 5)),
            (arms.Id, [.. Sets(curls, 30, 4), .. Sets(skulls, 40, 3)]),
            (push.Id, [.. Sets(bench, 135, 4), .. Sets(dips, null, 3), .. Sets(skulls, 40, 3)]),
            (back.Id, [.. Sets(rows, 100, 4), .. Sets(pullUps, null, 3), .. Sets(pulldown, 120, 3), .. Sets(pushdown, 50, 3)]),
        };
        var today = DateOnly.FromDateTime(now.Date);
        var thisWeek = Consistency.WeekStart(today);
        for (var day = thisWeek.AddDays(-7); day <= today; day = day.AddDays(1))
        {
            var index = Schedule.DayIndex(day);
            var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(18, 0)), now.Offset);
            if (index == 6 || (day < thisWeek && index == 2) || start > now) continue;
            // The week scenario's lifts went up a step this week.
            await Session(days[index].TemplateId, start, true, [.. days[index].Sets.Select(set => scenario == "week" && day >= thisWeek && set.Item2 is double w ? set with { Item2 = w + 5 } : set)]);
        }
    }

    if (scenario == "week")
    {
        for (var d = 1; d <= 28; d++)
        {
            var day = now.Date.AddDays(-d);
            if (d % 7 == 3) continue;
            await store.AddFoodAsync(1850 + d % 4 * 60, new DateTimeOffset(day.AddHours(12), now.Offset));
        }
        for (var d = 26; d >= 4; d -= 2)
            await store.AddBodyWeightAsync(Math.Round(184.0 - (28 - d) * 0.06, 1), WeightUnit.Pounds, new DateTimeOffset(now.Date.AddDays(-d).AddHours(7), now.Offset));
        await store.SetSettingAsync("Sex", "Male");
        await store.SetSettingAsync("BirthYear", "1992");
        await store.SetSettingAsync("HeightCm", "178");
    }

    if (scenario is "today" or "sessions" or "noroutine" or "trend" or "years" or "warmup" or "food" or "food-nobody" or "checks" or "muscles" or "week") return $"seeded {scenario}";

    // Open workouts, one per scenario. Sets stopped a little while ago.
    var open = scenario switch { "track" => null, "auto" or "auto-last" => (long?)arms.Id, "done" => legs.Id, _ => back.Id };
    var session = new WorkoutSession { TemplateId = open, StartedAt = now.AddMinutes(-16) };
    await store.StartSessionAsync(session);
    var n = 1;
    async Task Done(Exercise exercise, double? weight, int reps, int secondsAgo) =>
        await store.InsertSetAsync(session, new WorkoutSet { ExerciseId = exercise.Id, Order = n++, StartedAt = now.AddSeconds(-secondsAgo - 45), StoppedAt = now.AddSeconds(-secondsAgo), Weight = weight, Reps = reps });

    switch (scenario)
    {
        case "auto": await Done(curls, 30, 12, 300); await Done(curls, 30, 10, 40); break;              // first time, no counts: offer "move on"
        case "auto-last": await Done(curls, 30, 12, 300); await Done(skulls, 40, 12, 40); break;     // last exercise, no count: offer "finish"
        case "done": await Done(squat, 135, 8, 600); await Done(squat, 135, 8, 300); await Done(squat, 140, 8, 40); break; // plan complete
        case "long": await Done(rows, 100, 10, 1500); await Done(rows, 100, 10, 725); break; // a 12-minute rest
        case "plateau": await Done(rows, 95, 10, 300); await Done(rows, 95, 10, 48); break;
        case "track": await Done(walk, null, 0, 300); await Done(walk, null, 0, 40); break;
        default: await Done(rows, 100, 10, 600); await Done(rows, 100, 10, 400); await Done(rows, 100, 10, 48); break;
    }
    return scenario;
}

app.MapGet("/seed/{scenario}", async (string scenario, WorkoutStore store, PreviewClaude claude) =>
{
    claude.Reset();
    return Results.Text(await Seed(scenario, store));
});

// The stand-in Claude: a key on the phone or not (one ending a1B2), a key file for Settings to find, and how it answers: slower, or
// with a kind of trouble (/claude/script?delay=60000, ?fail=offline).
// What Settings › Updates shows: "ready" (1.0.7 installed, 1.0.9 out), "pc" (a build from the PC), "current" (1.0.9, the newest),
// or "none" (no release yet). It checks again at once, as the Check button does.
app.MapGet("/updates/{scenario}", async (string scenario, StandInUpdates standIn, Updater updates) =>
{
    const string notes = """
        Changes since workout-1.0.7:

        - Workout: Calories asks once about a half-logged day or a weigh-in far off the line
        - Workout: Calories offers maintenance by the scale, when the logs and the scale disagree
        - Workout: a lift that's gone flat says so, and Claude's look back reads reps left, the heart rate fall and plateaus
        - Workout: the rest screen says how far heart rate has fallen since the set stopped
        - Workout: Claude looks back at each workout as it finishes, and its plan for next time is a tap away
        - Workout: the Logbook and Telemetry looks, their fonts and the shared screen pieces

        Built from e70ce42. Signed by the key with SHA-256 968f7e9ccea0277bb04bd467b9a2e87b7217a5a2e10bff76d5a0bff119cc7169.
        """;
    switch (scenario)
    {
        case "ready": standIn.Show("1.0.7", 9, notes); break;
        case "pc": standIn.Show("1.0", 9, notes); break;
        case "current": standIn.Show("1.0.9", 9, notes); break;
        case "none": standIn.Show("1.0", 0); break;
        default: return Results.Text("ready, pc, current or none");
    }
    await updates.CheckAsync(evenIfRecent: true);
    return Results.Text($"{updates.Status.Step}: {updates.Status.Message}");
});
app.MapGet("/claude/key", async (bool? clear, IAssistKeyStore keys) =>
{
    if (clear == true) await keys.RemoveAsync();
    else await keys.SetAsync("sk-ant-api03-preview-000000000000000000a1B2");
    return Results.Text(clear == true ? "no key" : "key set");
});
app.MapGet("/claude/file", async (string? text, ClaudeKeyFile file) =>
{
    await File.WriteAllTextAsync(file.Path, text ?? "sk-ant-api03-preview-000000000000000000c3D4");
    return Results.Text($"wrote {file.Path}");
});
app.MapGet("/claude/script", (int? delay, string? fail, PreviewClaude claude) =>
{
    claude.Reset();
    if (delay is { } ms) claude.DelayMs = ms;
    if (fail is not null) claude.Fail = Enum.Parse<AnswerKind>(fail, ignoreCase: true);
    return Results.Text($"delay {claude.DelayMs} ms, failing with {claude.Fail?.ToString() ?? "nothing"}");
});

app.MapGet("/notice-log", (string? clear) =>
{
    lock (noticeLog)
    {
        var text = string.Join('\n', noticeLog);
        if (clear is not null) noticeLog.Clear();
        return Results.Text(text);
    }
});

// Turns the rest countdown on with this exercise's rest, as the ±15s buttons would have saved it.
app.MapGet("/countdown/{name}/{seconds:int}", async (string name, int seconds, WorkoutStore store) =>
{
    var exercise = await store.GetOrCreateExerciseAsync(name, ExerciseMeasure.Weight);
    await store.SetSettingAsync("RestCountdown", "on");
    await store.SetSettingAsync($"RestTarget:{exercise.Id}", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    return Results.Text($"{name} #{exercise.Id}: {seconds}s");
});

// Pretends the watch's Start button was tapped: what the phone's WatchListenerService does with /workout/next.
app.MapGet("/watch-next", (FlowRegistry flows, WatchLink watch) =>
{
    if (flows.Latest is not { } workout) return Results.Text("no workout");
    workout.StartNext();
    if (workout.Status is { } status) watch.Show(status);
    return Results.Text(LogNotice.Describe(workout.Status));
});

// Pretends the watch's Stop button was tapped: what the phone's WatchListenerService does with /workout/stop-set.
app.MapGet("/watch-stop", (FlowRegistry flows, WatchLink watch) =>
{
    if (flows.Latest is not { } workout) return Results.Text("no workout");
    workout.StopSet();
    if (workout.Status is { } status) watch.Show(status);
    return Results.Text(LogNotice.Describe(workout.Status));
});

// Pretends the watch's rest alarm went: what the phone's WatchListenerService does with /workout/rest-over. The countdown's end
// is the current rest's, unless `late` seconds are added, as when the watch heard an old countdown.
app.MapGet("/watch-rest-over", (FlowRegistry flows, WatchLink watch, int? late) =>
{
    if (flows.Latest is not { } workout) return Results.Text("no workout");
    if (workout.Status?.RestEndsAt is not { } end) return Results.Text($"no countdown: {LogNotice.Describe(workout.Status)}");
    workout.RestIsUp(WatchMessages.DecodeTime(WatchMessages.EncodeTime(end.AddSeconds(late ?? 0))));
    if (workout.Status is { } status) watch.Show(status);
    return Results.Text(LogNotice.Describe(workout.Status));
});

// Pretends to be the watch: one reading, through the same encoding the watch app sends.
// A minute of readings, one a second, going from one rate to another, for the heart rate line.
app.MapGet("/heart-rate-minute/{from:int}/{to:int}", (int from, int to, WatchLink watch) =>
{
    var now = DateTimeOffset.Now;
    watch.Receive([.. Enumerable.Range(0, 61).Select(i => new HeartRateSample { At = now.AddSeconds(i - 60), Bpm = from + (to - from) * i / 60 + (i % 7 == 0 ? 1 : 0) })]);
    return Results.Text($"latest {watch.Latest?.Bpm}, recent {watch.Recent.Count}");
});

app.MapGet("/heart-rate/{bpm:int}", async (int bpm, WatchLink watch, WorkoutStore store) =>
{
    var payload = WatchMessages.EncodeReadings(DateTimeOffset.Now.AddMinutes(-16), [new HeartRateSample { At = DateTimeOffset.Now.AddSeconds(-1), Bpm = bpm - 3 }, new HeartRateSample { At = DateTimeOffset.Now, Bpm = bpm }]);
    var (startedAt, readings) = WatchMessages.DecodeReadings(payload);
    watch.Receive(readings);
    await store.GetRoutinesAsync(); // waits for the queued save
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    var saved = new SqliteCommand("SELECT COUNT(*) || ' rows, last ' || MAX(Bpm) || ' at ' || MAX(At) FROM HeartRateSample", connection).ExecuteScalar();
    return Results.Text($"payload: {System.Text.Encoding.UTF8.GetString(payload).Replace('\n', '|')}\ndecoded start: {startedAt:O}, {readings.Count} readings, latest {watch.Latest?.Bpm}\nsaved: {saved}");
});

app.Run();

sealed class LogNotice(List<string> log) : WorkoutNotice
{
    public static string Describe(WorkoutStatus? s) => s is null ? "none"
        : $"{(s.Resting ? "rest" : "set")} '{s.Title}' since {s.Since:HH:mm:ss}{(s.RestEndsAt is { } e ? $" ends {e:HH:mm:ss}" : "")}{(s.Next is { } n ? $" next '{n}'" : "")}";

    public override void Show(WorkoutStatus? status)
    {
        lock (log) log.Add($"{DateTimeOffset.Now:HH:mm:ss.fff} notification: {Describe(status)}");
    }

    public override void Buzz()
    {
        lock (log) log.Add($"{DateTimeOffset.Now:HH:mm:ss.fff} PHONE BUZZ");
    }
}

// Logs what would go to the watch, through the same encoding.
sealed class LogWatchLink(WorkoutStore store, List<string> log) : WatchLink(store)
{
    public override void Show(WorkoutStatus status)
    {
        var payload = WatchMessages.EncodeStatus(status);
        lock (log) log.Add($"{DateTimeOffset.Now:HH:mm:ss.fff} to watch: {LogNotice.Describe(WatchMessages.DecodeStatus(payload))}");
    }
}

sealed class FlowRegistry
{
    public WorkoutFlow? Latest { get; set; }
}
