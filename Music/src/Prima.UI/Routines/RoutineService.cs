using System.Collections.Concurrent;
using System.Text.Json;
using Playground.AI.Assist;
using Prima.Core.Generation;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.Core.Routines;
using Prima.UI.Host;
using Prima.UI.Storage;
using Prima.UI.Transcription;

namespace Prima.UI.Routines;

/// <summary>Owns routine preparation independently of a page. Requests run only after an explicit start.</summary>
public sealed class RoutineService(PrimaStore store, IInferenceProvider provider, PrimaSettings settings, ILibraryBundle bundle)
{
    private readonly Task recovered = RecoverAsync(store);
    private const string StarsTrack = "routine-stars";

    /// <summary>The routines that came with Prima and have been put in, by their keys: each only once.</summary>
    public const string StandardTrack = "routine-standard";

    /// <summary>How many routines can be starred, to keep them on top of the routines.</summary>
    public const int MostStarred = 3;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> active = new();
    private readonly SemaphoreSlim lane = new(1);
    public event Action<RoutineGenerationJob>? Changed;
    public string ProviderId => provider.ProviderId;
    public int DailyMinutes => settings.DailyMinutes;

    public async Task<PracticeEvidenceSnapshot> EvidenceAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var attempts = await store.GetAttemptsAsync(DateOnly.FromDateTime(DateTime.Now).AddDays(-60));
        var pieceRuns = (await store.GetPieceRunsAsync()).ToDictionary(p => p.Run.Id, p => p.Run);
        var skills = new List<SkillEvidence>();
        foreach (var group in attempts.Where(a => a.Kind != "geography-look" && a.Variant != "done" && a.Headline is not null)
            .GroupBy(a => (a.Kind, a.Variant, a.SetId, a.Settings, a.Tempo, a.Level, Hands: pieceRuns.GetValueOrDefault(a.Id)?.Hands))
            .OrderByDescending(g => g.Max(a => a.At)).Take(40))
        {
            var runs = group.OrderByDescending(a => a.At).Take(20).ToArray();
            var measured = runs.Where(a =>
            {
                using var document = JsonDocument.Parse(a.Measures);
                var root = document.RootElement;
                if (pieceRuns.TryGetValue(a.Id, out var piece) && piece.Passes.Count > 0) return piece.Reached > 0;
                if (root.TryGetProperty("stopped", out var stopped) && stopped.ValueKind == JsonValueKind.True) return false;
                return !root.TryGetProperty("notes", out var count) || count.ValueKind != JsonValueKind.Number || count.GetInt32() > 0;
            }).ToArray();
            if (measured.Length == 0) continue;
            var days = measured.Select(a => a.Day).Distinct().Count();
            var timed = measured.Where(a => a.Tempo is > 0).Select(a => a.Tempo!.Value).ToArray();
            // Technique's timed headline is spread. Other timed players keep pitch and timing accuracy separately.
            var techniqueTiming = group.Key.Kind is "scale" or "arpeggio" or "independence" && group.Key.Variant != "wait";
            var accuracy = techniqueTiming ? (double?)null : measured.Average(a =>
            {
                using var document = JsonDocument.Parse(a.Measures);
                return document.RootElement.TryGetProperty("inTime", out var value) && value.TryGetDouble(out var timedAccuracy) ? timedAccuracy : a.Headline!.Value;
            });
            var last = measured[0];
            var context = last.Settings is { Length: <= 500 } kept ? kept : "";
            var detail = $"Mode: {group.Key.Variant ?? "set"}; material: {group.Key.SetId ?? "mixed"}; recorded level/step: {group.Key.Level}; last settings: {context}. " +
                (measured.Length < 3 || days < 2 ? "Limited evidence; do not treat this as established ability." : "Repeated practice; compare only the same mode and material.");
            if (group.Key.Level is not null) detail += " A recorded step can be manually selected; older drill records may contain the next step rather than the performed step.";
            var hands = group.Key.Hands;
            if (hands is null && last.Settings is { } chosen)
            {
                using var document = JsonDocument.Parse(chosen);
                if (document.RootElement.TryGetProperty("hands", out var value) && value.ValueKind == JsonValueKind.String) hands = value.GetString();
            }
            hands = hands?.ToLowerInvariant() switch { "together" => "both", var known => known };
            IReadOnlyList<int> trouble = [];
            if (group.Key.Kind == "piece")
            {
                // The run summary repeats its final pass. Use completed passes OR the summary, then one vote per run/bar.
                trouble = measured.Where(a => pieceRuns.ContainsKey(a.Id)).Select(a => pieceRuns[a.Id]).DistinctBy(r => r.Id)
                    .SelectMany(run => (run.Passes.Count > 0 ? run.Passes.SelectMany(p => p.Trouble) : run.Trouble)
                        .Where(t => t.Weight > 0).GroupBy(t => t.Bar).Select(g => (Bar: g.Key, Weight: g.Max(t => t.Weight))))
                    .GroupBy(t => t.Bar).Where(g => g.Count() >= 2).OrderByDescending(g => g.Count())
                    .ThenByDescending(g => g.Sum(t => t.Weight)).ThenBy(g => g.Key).Take(6).Select(g => g.Key).ToArray();
                detail += " Trouble bars recur in separate past runs. This history does not retain source revisions; verify its bar numbering against the current piece before targeting it.";
            }
            skills.Add(new(group.Key.Kind, measured.Length, days, measured.Max(a => a.At), accuracy,
                timed.Length == 0 ? null : timed.Order().ElementAt(timed.Length / 2), detail)
                { Hands = hands, PieceId = group.Key.Kind == "piece" ? group.Key.SetId : null, TroubleBars = trouble });
        }
        return new(now, skills);
    }

    /// <summary>Prefer written notation. MIDI-derived hand assignments require an explicit user confirmation.</summary>
    public async Task<RoutineSource> PrepareSourceAsync(string pieceId, bool confirmHands = false)
    {
        var piece = await store.GetPieceAsync(pieceId) ?? throw new InvalidOperationException("The library piece was not found.");
        var mei = await store.GetMeiAsync(pieceId);
        var fromMidi = piece.Format.Equals("midi", StringComparison.OrdinalIgnoreCase) || piece.Format.Equals("mid", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(piece.File)?.ToLowerInvariant() is ".mid" or ".midi";
        if (string.IsNullOrWhiteSpace(mei))
        {
            var file = piece.Midi ?? (fromMidi ? piece.File : null);
            if (file is null) throw new InvalidOperationException("This piece needs playable notation or MIDI before it can be used for a routine.");
            byte[]? bytes;
            if (piece.Origin == "bundle") bytes = await bundle.ReadAsync(file);
            else
            {
                var folder = Path.GetFullPath(store.FilesPath) + Path.DirectorySeparatorChar;
                var path = Path.GetFullPath(Path.Combine(folder, file));
                if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The source file is outside the library.");
                bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
            }
            if (bytes is null) throw new InvalidOperationException("The piece's MIDI file is unavailable.");
            mei = MidiNotation.Mei(bytes, piece.Title);
            fromMidi = true;
        }
        var score = MeiReader.Read(mei);
        var unresolved = false;
        if (piece.IsPdf && await store.GetReadingAsync(pieceId) is { } reading)
        {
            var draft = JsonSerializer.Deserialize<ReadingDraft>(reading.Result, AssistSchema.Json);
            unresolved = draft is null || !draft.Ok || draft.Doubts.Count > 0;
            if (draft is not null && (draft.ConversionJobId ?? draft.SourceRevision) is { } reviewId && await store.GetNotationReviewAsync(pieceId, reviewId) is { } review)
            {
                unresolved = review.Current.Bars.Any(b => b.Written is null || b.UserFlags.Length > 0 || !b.Accepted && b.ModelDoubts.Length > 0);
                if (!unresolved)
                {
                    var reviewed = MeiReader.Read(draft.WithReview(review, piece.Title).Mei);
                    // A corrected but not yet kept draft must not bless the older library notes.
                    unresolved = !score.Expected().Select(n => (n.Midi, n.Tick, n.Length, n.Staff, n.Bar))
                        .SequenceEqual(reviewed.Expected().Select(n => (n.Midi, n.Tick, n.Length, n.Staff, n.Bar)));
                }
            }
        }
        return new(pieceId, piece.Title, RoutineCompiler.SourceRevision(mei), mei, !fromMidi || confirmHands, unresolved);
    }

    public async Task<RoutineGenerationJob> CreateGenerationAsync(RoutineRequest request, RoutineSource? source, string model)
    {
        await recovered;
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Choose an explicit model before asking for a routine.", nameof(model));
        if (provider.Capabilities.StructuredOutput is false) throw new InvalidOperationException("This provider does not support structured routines.");
        if (source is { HandsConfirmed: false }) throw new InvalidOperationException("Confirm the inferred hand assignment before using this piece.");
        if (source is { HasUnresolvedErrors: true }) throw new InvalidOperationException("Review the uncertain source notes before building exercises from them.");
        var evidence = await EvidenceAsync();
        _ = RoutineCompiler.Input(request, evidence, source);
        var job = new RoutineGenerationJob(Guid.NewGuid().ToString("N"), 0, RoutineGenerationState.Queued, DateTimeOffset.UtcNow,
            request, evidence, source, provider.ProviderId, model.Trim());
        // Round-trip freezes caller-owned lists before persistence and background use.
        job = JsonSerializer.Deserialize<RoutineGenerationJob>(JsonSerializer.Serialize(job, PrimaStore.Json), PrimaStore.Json)!;
        await store.CreateRoutineJobAsync(job);
        await store.SetSettingAsync("Routine.Model", model.Trim());
        Publish(job);
        return job;
    }

    public async Task<RoutineGenerationJob?> GetJobAsync(string id) { await recovered; return await store.GetRoutineJobAsync(id); }
    /// <summary>The requests that aren't routines yet, newest first: every one, however many were accepted after it.</summary>
    public async Task<IReadOnlyList<RoutineGenerationJob>> RecentAsync() { await recovered; return await store.GetRoutineJobsNotAcceptedAsync(); }

    public async Task RunAsync(string id, CancellationToken hostStopped = default)
    {
        await recovered;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(hostStopped);
        if (!active.TryAdd(id, cancellation)) return;
        var entered = false;
        try
        {
            await lane.WaitAsync(cancellation.Token);
            entered = true;
            var job = await store.GetRoutineJobAsync(id) ?? throw new InvalidOperationException("The routine request was not found.");
            if (job.State != RoutineGenerationState.Queued) return;
            if (job.Provider != provider.ProviderId) throw new InvalidOperationException("The saved inference provider is unavailable.");
            var input = RoutineCompiler.Input(job.Request, job.Evidence, job.Source);
            job = await CommitAsync(job with { State = RoutineGenerationState.Running, Dispatched = true });
            if (job.State != RoutineGenerationState.Running) return;
            var execution = new InferenceSettings(job.Model, MaxOutputTokens: 6000, ValidationRetries: 0, AllowModelFallback: false);
            var answer = await provider.AskAsync(new PlanRoutine(execution), input, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            job = job with { Usage = answer.Usage, Response = answer.Response };
            if (!answer.Ok)
            {
                await CommitAsync(job with { State = RoutineGenerationState.Failed, Problem = answer.Summary });
                return;
            }
            job = await CommitAsync(job with { Draft = answer.Value });
            if (job.State != RoutineGenerationState.Running) return;
            var prepared = RoutineCompiler.Prepare(answer.Value, job.Request, job.Source);
            await CommitAsync(job with { State = RoutineGenerationState.Ready, Draft = answer.Value, Prepared = prepared, Problem = null });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await InterruptAsync(id, "Routine generation was interrupted. The saved request can be reviewed; sending it again requires a new explicit request.");
        }
        catch (Exception error)
        {
            var job = await store.GetRoutineJobAsync(id);
            if (job is not null && job.State is not (RoutineGenerationState.Cancelled or RoutineGenerationState.Accepted))
                await CommitAsync(job with { State = RoutineGenerationState.Failed, Problem = error.Message });
        }
        finally
        {
            active.TryRemove(id, out _);
            if (entered) lane.Release();
        }
    }

    public async Task CancelAsync(string id)
    {
        await recovered;
        var job = await store.GetRoutineJobAsync(id);
        if (job is not null && job.State is not (RoutineGenerationState.Accepted or RoutineGenerationState.Cancelled))
            await CommitAsync(job with { State = RoutineGenerationState.Cancelled });
        if (active.TryGetValue(id, out var running)) await running.CancelAsync();
    }

    /// <summary>
    /// Takes a request off Asked for for good: one that came back, didn't finish, or was cancelled. One being asked just now is
    /// left alone (false), so what Claude sends back isn't lost.
    /// </summary>
    public async Task<bool> DismissAsync(string id)
    {
        await recovered;
        var job = await store.GetRoutineJobAsync(id);
        if (job is null || !await store.DeleteRoutineJobAsync(id)) return false;

        Publish(job);
        return true;
    }

    public async Task InterruptAsync(string id, string reason)
    {
        await recovered;
        var job = await store.GetRoutineJobAsync(id);
        if (job is not null && job.State is RoutineGenerationState.Queued or RoutineGenerationState.Running)
            await CommitAsync(job with { State = RoutineGenerationState.Interrupted, Problem = reason });
        if (active.TryGetValue(id, out var running)) await running.CancelAsync();
    }

    public async Task<RoutineGenerationJob> SaveDraftAsync(string id, RoutineDraft draft)
    {
        var job = await GetJobAsync(id) ?? throw new InvalidOperationException("The routine request was not found.");
        if (job.State != RoutineGenerationState.Ready) throw new InvalidOperationException("Only a ready draft can be edited.");
        var prepared = RoutineCompiler.Prepare(draft, job.Request, job.Source);
        return await CommitAsync(job with { Draft = draft, Prepared = prepared });
    }

    public async Task<RoutineDefinition> AcceptAsync(string jobId)
    {
        var job = await GetJobAsync(jobId) ?? throw new InvalidOperationException("The routine request was not found.");
        var definition = await store.AcceptRoutineAsync(jobId);
        if (await store.GetRoutineJobAsync(jobId) is { } saved) Publish(saved);
        return definition;
    }

    public async Task<RoutineDefinition?> ActiveAsync() { await recovered; return await store.GetActiveRoutineAsync(); }

    // Requests left half asked when Prima last stopped; the exercises once kept as My routine, now a routine of yours by that
    // name; and the routines that come with Prima.
    private static async Task RecoverAsync(PrimaStore store)
    {
        await store.RecoverRoutineJobsAsync();
        await MoveMyRoutineAsync(store);
        await PutInStandardAsync(store);
    }

    // The exercises once kept as My routine (a list of them, with nothing else to it), now a routine of your own by that name.
    private static async Task MoveMyRoutineAsync(PrimaStore store)
    {
        if (await store.GetTrackAsync<List<string>>("exercise-routine") is not { Count: > 0 } kept) return;

        var items = kept.Select(Exercises.Find).OfType<Exercise>().Where(OwnRoutines.CanHave).Select(e => OwnRoutines.Activity(e)).ToList();
        if (items.Count > 0) await store.SaveOwnRoutineAsync(Own("My routine", items));
        await store.SaveTrackAsync<List<string>>("exercise-routine", []);
    }

    /// <summary>
    /// The routines that come with Prima, each put in once as one of yours, listed in their own order: one changed stays as it was
    /// changed, one taken away stays away, and one a later Prima adds comes in once too. Clearing your progress clears what was
    /// put in, so they come again. Run as Prima starts; the preview runs it for a fresh start.
    /// </summary>
    public static async Task PutInStandardAsync(PrimaStore store)
    {
        var done = await store.GetTrackAsync<List<string>>(StandardTrack) ?? [];
        var now = DateTimeOffset.UtcNow;
        var added = false;
        foreach (var (standard, at) in StandardRoutines.All.Select((r, i) => (r, i)).Where(x => !done.Contains(x.r.Key)))
        {
            var items = standard.Activities.Select(a => (Exercise: Exercises.Find(a.Id), a.Minutes)).Where(a => a.Exercise is not null && OwnRoutines.CanHave(a.Exercise))
                .Select(a => OwnRoutines.Activity(a.Exercise!, a.Minutes)).ToList();
            // Listed newest first: each a second older than the one before it, so they come in their own order.
            if (items.Count > 0) await store.SaveOwnRoutineAsync(Own(standard.Title, items) with { Summary = standard.Purpose, Accepted = now.AddSeconds(-at) });
            (done, added) = ([.. done, standard.Key], true);
        }
        if (added) await store.SaveTrackAsync(StandardTrack, done);
    }

    // A routine of your own as it's first made.
    private static RoutineDefinition Own(string title, IReadOnlyList<PreparedRoutineItem> items)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), now, title.Trim(), "", new RoutineRequest(Minutes(items), ""),
            new PracticeEvidenceSnapshot(now, []), null, RoutineDefinition.Yours, "", items);
    }

    private static int Minutes(IReadOnlyList<PreparedRoutineItem> items) => Math.Clamp(items.Sum(i => i.Recipe.Minutes), 1, 120);

    /// <summary>A routine of your own, made under a name, with what's to be in it to start with.</summary>
    public async Task<RoutineDefinition> MakeAsync(string title, IReadOnlyList<PreparedRoutineItem>? items = null)
    {
        await recovered;
        var routine = Own(title, items ?? []);
        await store.SaveOwnRoutineAsync(routine);
        return routine;
    }

    /// <summary>A routine of yours as it now is: renamed, or its activities added, taken out, timed or put in another order.</summary>
    public async Task<RoutineDefinition> SaveAsync(RoutineDefinition routine)
    {
        await recovered;
        routine = routine with { Request = routine.Request with { Minutes = Minutes(routine.Items) } };
        await store.SaveOwnRoutineAsync(routine);
        return routine;
    }

    /// <summary>A copy of a routine as one of yours, to change as you like; one of Claude's stays as it was accepted.</summary>
    public async Task<RoutineDefinition> CopyAsync(string revision)
    {
        var from = await GetDefinitionAsync(revision) ?? throw new InvalidOperationException("That routine isn't saved any more.");
        return await MakeAsync(from.Title, [.. from.Items.Select(i => i with { Recipe = i.Recipe with { Id = Guid.NewGuid().ToString("N")[..12] } })]);
    }

    /// <summary>A routine of yours taken away, with its star; the runs played in it stay, as runs of their exercises.</summary>
    public async Task DeleteAsync(string revision)
    {
        await recovered;
        await store.DeleteOwnRoutineAsync(revision);
        await StarAsync(revision, false);
    }

    /// <summary>The routines starred, by revision, in the order they were starred.</summary>
    public async Task<IReadOnlyList<string>> StarredAsync() => await store.GetTrackAsync<List<string>>(StarsTrack) ?? [];

    /// <summary>A routine starred, or its star taken off; false, and nothing changed, when three are starred already.</summary>
    public async Task<bool> StarAsync(string revision, bool on)
    {
        var stars = (await StarredAsync()).ToList();
        if (on == stars.Contains(revision)) return true;
        if (on && stars.Count >= MostStarred) return false;

        if (on) stars.Add(revision);
        else stars.Remove(revision);
        await store.SaveTrackAsync(StarsTrack, stars);
        return true;
    }

    /// <summary>A routine's day: its progress on the day, made the first time it's opened that day, whether or not Today follows it.</summary>
    public async Task<RoutineDay?> DayAsync(string revision, DateOnly? day = null) { await recovered; return await store.GetOrCreateRoutineDayAsync(day ?? DateOnly.FromDateTime(DateTime.Now), revision); }

    /// <summary>Each routine activity's time on each day it was opened.</summary>
    public Task<List<RoutineTime>> TimesAsync() => store.GetRoutineTimesAsync();

    /// <summary>Every run played in a routine.</summary>
    public Task<List<RoutineRun>> RunsAsync() => store.GetRoutineRunsAsync();

    /// <summary>Every routine accepted, newest first, the one followed among them.</summary>
    public async Task<IReadOnlyList<RoutineDefinition>> RoutinesAsync() { await recovered; return await store.GetRoutineDefinitionsAsync(); }

    /// <summary>The last day each routine had an activity practiced, by revision.</summary>
    public Task<Dictionary<string, DateOnly>> LastPracticedAsync() => store.GetRoutineLastPracticedAsync();

    /// <summary>Follows another accepted routine: Today has it from now on, in place of the one followed before (kept, to follow again).</summary>
    public async Task FollowAsync(string revision) { await recovered; await store.FollowRoutineAsync(revision); }
    public Task<RoutineDefinition?> GetDefinitionAsync(string revision) => store.GetRoutineDefinitionAsync(revision);
    public async Task<RoutineDay?> TodayAsync(DateOnly? day = null) { await recovered; return await store.GetOrCreateRoutineDayAsync(day ?? DateOnly.FromDateTime(DateTime.Now)); }
    public Task<RoutineDay?> GetDayAsync(string id) => store.GetRoutineDayAsync(id);
    public Task<RoutineExecution> BeginExecutionAsync(string dayId, string itemId) => store.BeginRoutineExecutionAsync(dayId, itemId);

    /// <summary>Begins an activity of the day, or takes it up again, and gives the address of the screen it's practiced on.</summary>
    public async Task<string> OpenAsync(string dayId, string itemId)
    {
        var execution = await BeginExecutionAsync(dayId, itemId);
        var item = execution.Item.Recipe;
        return item.Kind switch
        {
            "exercise" => $"exercises/{Uri.EscapeDataString(item.TargetId)}?execution={execution.Id}",
            "drill" => $"drills/{Uri.EscapeDataString(item.TargetId)}?execution={execution.Id}",
            _ => $"routine/play/{execution.Id}",
        };
    }
    public Task<RoutineExecution?> GetExecutionAsync(string id) => store.GetRoutineExecutionAsync(id);
    public Task<long> RecordAttemptAsync(string executionId, string submissionId, AttemptRecord attempt, double activeSeconds, int completedPasses = 0) =>
        store.RecordRoutineAttemptAsync(executionId, submissionId, attempt, activeSeconds, completedPasses);
    public Task CheckpointAsync(string executionId, double totalActiveSeconds, bool pause = false) => store.CheckpointRoutineExecutionAsync(executionId, totalActiveSeconds, pause);
    public Task CompleteAsync(string executionId, bool skipped = false) => store.CompleteRoutineExecutionAsync(executionId, skipped);
    public Task DeactivateAsync() => store.DeactivateRoutineAsync();
    public Task FeedbackAsync(string executionId, string feedback) => store.SaveRoutineFeedbackAsync(executionId, feedback);

    /// <summary>Local suggestions compare the same material, hand, key, mode and pace; accepting them is a separate action.</summary>
    public async Task<IReadOnlyList<RoutineAdjustment>> AdjustmentsAsync()
    {
        // A routine of yours is played as you leave each exercise: there's nothing of its own to adjust.
        var definition = await ActiveAsync();
        if (definition is null || definition.Own) return [];
        var attempts = await store.GetAttemptsAsync(DateOnly.FromDateTime(DateTime.Now).AddDays(-30));
        var executions = await store.GetRoutineExecutionsAsync(definition.Revision);
        var catalog = RoutineCompiler.Input(definition.Request, definition.Evidence, definition.Source).Catalog;
        var suggestions = new List<RoutineAdjustment>();
        foreach (var prepared in definition.Items)
        {
            var item = prepared.Recipe;
            var ids = await store.GetRoutineAttemptIdsAsync(definition.Revision, item.Id);
            var technique = item.Kind == "drill" && item.TargetId is "scales" or "arpeggios" or "independence";
            var rhythm = item.Kind == "drill" && item.TargetId == "rhythm";
            var quiz = item.Kind == "drill" && !technique && !rhythm;
            var timed = item.Mode == "time" && !quiz;
            var expectedTechnique = technique ? new TechniqueChoice(new Key(item.Fifths, item.Minor), item.TargetId == "arpeggios" ? ScaleKind.Arpeggio : item.Minor ? ScaleKind.HarmonicMinor : ScaleKind.Major, 1, item.Hands, item.Step).Item(item.TargetId).Id : null;
            var comparable = new List<(AttemptRecord Attempt, bool Good, bool Hard)>();
            foreach (var attempt in attempts.Where(a => ids.Contains(a.Id)).OrderByDescending(a => a.At))
            {
                if (timed && (attempt.Tempo != item.Tempo || attempt.Variant == "wait") || !timed && !quiz && attempt.Variant != "wait") continue;
                if ((quiz || rhythm) && attempt.Level != item.Step || technique && attempt.SetId != expectedTechnique) continue;
                if (item.Kind == "exercise" && attempt.SetId != item.TargetId || item.Kind is "generated" or "excerpt" && attempt.SetId != item.Id) continue;
                if (!quiz && !rhythm)
                {
                    if (attempt.Settings is null) continue;
                    using var chosen = JsonDocument.Parse(attempt.Settings);
                    var value = chosen.RootElement;
                    if (!value.TryGetProperty("hands", out var hands) || hands.GetString() != item.Hands.ToString()) continue;
                    if (item.Kind is "generated" or "excerpt")
                    {
                        if (!value.TryGetProperty("Fifths", out var fifths) || fifths.GetInt32() != item.Fifths ||
                            !value.TryGetProperty("Minor", out var minor) || minor.GetBoolean() != item.Minor ||
                            !value.TryGetProperty("sourceRevision", out var revision) || revision.GetString() != prepared.SourceRevision) continue;
                    }
                    else if (!value.TryGetProperty("key", out var key) || key.GetString() != new Key(item.Fifths, item.Minor).Name) continue;
                }
                using var document = JsonDocument.Parse(attempt.Measures);
                var measures = document.RootElement;
                if (measures.TryGetProperty("stopped", out var stopped) && stopped.ValueKind == JsonValueKind.True) continue;
                if (technique && timed)
                {
                    var valid = measures.TryGetProperty("valid", out var value) && value.ValueKind == JsonValueKind.True;
                    var spread = new[] { "right", "left" }.Select(hand => measures.TryGetProperty(hand, out var result) && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("IoiSdPercent", out var sd) ? sd.GetDouble() : (double?)null).OfType<double>().DefaultIfEmpty(double.PositiveInfinity).Max();
                    comparable.Add((attempt, valid && spread <= 8, !valid || spread > 16));
                }
                else
                {
                    var metric = quiz ? "share" : rhythm ? "accuracy" : timed ? "inTime" : "rightFirstTime";
                    if (!measures.TryGetProperty(metric, out var result) || !result.TryGetDouble(out var accuracy)) continue;
                    if (!quiz && !timed && !rhythm)
                    {
                        if (!measures.TryGetProperty("notes", out var count) || count.GetInt32() <= 0) continue;
                        accuracy /= count.GetInt32();
                    }
                    comparable.Add((attempt, accuracy >= 0.9, accuracy < 0.7));
                }
            }
            // Fading-click stages are different tests: take the latest stage with enough matching evidence.
            var recent = comparable.GroupBy(c => c.Attempt.Variant).Select(g => g.Take(3).ToArray())
                .Where(g => g.Length == 3 && g.Select(c => c.Attempt.Day).Distinct().Count() >= 2)
                .OrderByDescending(g => g[0].Attempt.At).FirstOrDefault() ?? [];
            var feedback = executions.FirstOrDefault(e => e.ItemId == item.Id && e.Feedback is not null)?.Feedback;
            var easier = feedback == "hard" || recent.Length == 3 && recent.All(c => c.Hard);
            var harder = feedback is not ("hard" or "right") && recent.Length == 3 && recent.All(c => c.Good);
            if (!easier && !harder) continue;
            var reason = feedback == "hard" ? "You reported this activity as too hard." : easier ? "Repeated comparable practice suggests an easier step." : "Three comparable runs over multiple days support a small next step.";
            if (quiz)
            {
                var top = catalog.Single(c => c.Kind == "drill" && c.Id == item.TargetId).MaxStep;
                var step = Math.Clamp(item.Step + (easier ? -1 : 1), 1, top);
                if (step != item.Step) suggestions.Add(new(item.Id, item.Tempo, item.Tempo, recent.Length, reason, SuggestedStep: step));
                continue;
            }
            if (!timed)
            {
                if (item.Kind == "generated" && item.TargetId is "jump" or "chord-changes")
                {
                    var preparation = Math.Clamp(item.PreparationBeats + (easier ? 1 : -1), 0, 4);
                    if (preparation != item.PreparationBeats) suggestions.Add(new(item.Id, item.Tempo, item.Tempo, recent.Length, reason, SuggestedPreparationBeats: preparation));
                    else if (harder) suggestions.Add(new(item.Id, item.Tempo, item.Tempo, recent.Length, reason, SuggestedMode: "time"));
                }
                else if (harder) suggestions.Add(new(item.Id, item.Tempo, item.Tempo, recent.Length, reason, SuggestedMode: "time"));
                continue;
            }
            var tempo = easier ? Math.Max(20, Math.Round(item.Tempo * 0.9)) : Math.Min(160, Math.Round(item.Tempo * 1.05));
            if (tempo != item.Tempo) suggestions.Add(new(item.Id, item.Tempo, tempo, recent.Length, reason));
        }
        return suggestions;
    }

    public async Task<RoutineGenerationJob> CreateAdjustmentDraftAsync()
    {
        var definition = await ActiveAsync() ?? throw new InvalidOperationException("There is no accepted routine.");
        var suggestions = await AdjustmentsAsync();
        if (suggestions.Count == 0) throw new InvalidOperationException("More comparable practice is needed before suggesting an adjustment.");
        var draft = new RoutineDraft(definition.Title, definition.Summary, definition.Items.Select(i => suggestions.FirstOrDefault(s => s.ItemId == i.Recipe.Id) is { } suggestion ? i.Recipe with
        {
            Tempo = suggestion.SuggestedTempo, PreparationBeats = suggestion.SuggestedPreparationBeats ?? i.Recipe.PreparationBeats,
            Step = suggestion.SuggestedStep ?? i.Recipe.Step, Mode = suggestion.SuggestedMode ?? i.Recipe.Mode,
        } : i.Recipe).ToArray());
        var prepared = RoutineCompiler.Prepare(draft, definition.Request, definition.Source);
        var job = new RoutineGenerationJob(Guid.NewGuid().ToString("N"), 0, RoutineGenerationState.Ready, DateTimeOffset.UtcNow,
            definition.Request, await EvidenceAsync(), definition.Source, "local", "practice-adjustment", draft, prepared);
        await store.CreateRoutineJobAsync(job);
        Publish(job);
        return job;
    }

    private async Task<RoutineGenerationJob> CommitAsync(RoutineGenerationJob job)
    {
        var next = job with { Revision = job.Revision + 1, Updated = DateTimeOffset.UtcNow };
        if (!await store.SaveRoutineJobAsync(next, job.Revision)) return await store.GetRoutineJobAsync(job.Id) ?? throw new InvalidOperationException("The routine request was removed.");
        Publish(next);
        return next;
    }

    private void Publish(RoutineGenerationJob job)
    {
        foreach (var changed in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action<RoutineGenerationJob>)changed)(job); }
            catch (Exception) { } // A disposed page cannot turn a saved generation into a failed job.
        }
    }
}
