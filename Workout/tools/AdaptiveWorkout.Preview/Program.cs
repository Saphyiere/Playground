// Preview host: the real AdaptiveWorkout.UI screens in a browser, as Blazor Server, against sample data in a SQLite file, so a
// cloud session can render and screenshot them (see CLAUDE.md). /seed/{scenario} fills the data; the other endpoints stand in for
// the watch and the phone's notification.
using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Data.Sqlite;
using AdaptiveWorkout.Preview.Components;

var dbPath = Path.Combine(AppContext.BaseDirectory, "preview.db");
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Stand-ins for the phone's notification and watch link that write down what they were told, for /notice-log.
var noticeLog = new List<string>();
builder.Services.AddSingleton<WorkoutNotice>(new LogNotice(noticeLog));
builder.Services.AddSingleton<WatchLink>(sp => new LogWatchLink(sp.GetRequiredService<WorkoutStore>(), noticeLog));
builder.Services.AddSingleton<FlowRegistry>();
builder.Services.AddAdaptiveWorkout(dbPath);
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
        foreach (var table in new[] { "SessionExercise", "WorkoutSet", "WorkoutSession", "TemplateExercise", "RoutineDay", "SessionTemplate", "Exercise", "Routine", "Setting", "FoodEntry", "BodyWeightEntry", "HeartRateSample" })
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
    if (scenario is "food" or "food-nobody")
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
        if (scenario == "food")
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

    if (scenario is "today" or "sessions" or "noroutine" or "trend" or "years" or "warmup" or "food" or "food-nobody") return $"seeded {scenario}";

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
        case "track": await Done(walk, null, 0, 300); await Done(walk, null, 0, 40); break;
        default: await Done(rows, 100, 10, 600); await Done(rows, 100, 10, 400); await Done(rows, 100, 10, 48); break;
    }
    return scenario;
}

app.MapGet("/seed/{scenario}", async (string scenario, WorkoutStore store) => Results.Text(await Seed(scenario, store)));

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
