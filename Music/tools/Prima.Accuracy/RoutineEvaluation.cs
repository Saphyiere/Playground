using System.Text.Json;
using Playground.AI.Assist;
using Prima.Core.Generation;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.Core.Routines;

namespace Prima.Accuracy;

/// <summary>Offline template/source checks, with a separately requested single paid planning call.</summary>
public static class RoutineEvaluation
{
    private sealed record ItemReport(string Id, string Kind, string Template, int Bars, int Notes, bool? SourcePitchMultisetPreserved, string? MeiFile, string? Problem);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: routine <source.mid|mei> <report.json> [--reference reference.json] [--model explicit-model] [--confirm-hands]");
            return 1;
        }
        string? model = null;
        string? referencePath = null;
        var confirmHands = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--confirm-hands") confirmHands = true;
            else if (args[i] is "--model" or "--reference" && i + 1 < args.Length)
            {
                if (args[i] == "--model") model = args[++i];
                else referencePath = args[++i];
            }
            else { Console.Error.WriteLine("Unrecognised or incomplete routine evaluation option."); return 1; }
        }
        try
        {
            var sourcePath = Path.GetFullPath(args[0]);
            var reportPath = Path.GetFullPath(args[1]);
            var folder = Path.Combine(Path.GetDirectoryName(reportPath)!, Path.GetFileNameWithoutExtension(reportPath) + "-music");
            Directory.CreateDirectory(folder);
            var midi = Path.GetExtension(sourcePath).ToLowerInvariant() is ".mid" or ".midi";
            var bytes = await File.ReadAllBytesAsync(sourcePath);
            var score = midi ? MidiNotation.Read(bytes, Path.GetFileNameWithoutExtension(sourcePath)) : MeiReader.Read(System.Text.Encoding.UTF8.GetString(bytes));
            var mei = MeiWriter.Write(score);
            await File.WriteAllTextAsync(Path.Combine(folder, "source.mei"), mei);
            var referenceCandidate = Path.Combine(Path.GetDirectoryName(sourcePath)!, "reference.json");
            if (referencePath is null && File.Exists(referenceCandidate)) referencePath = referenceCandidate;
            var comparisons = new List<AccuracyReport>();
            var handsMatched = false;
            if (referencePath is not null)
            {
                var reference = AccuracyEvaluator.LoadReference(await File.ReadAllTextAsync(referencePath));
                if (midi) comparisons.Add(AccuracyEvaluator.Compare(reference, AccuracyEvaluator.MidiNotes(MidiFile.Read(bytes)), "source-midi-ignore-staff", ignoreStaff: true));
                comparisons.Add(AccuracyEvaluator.Compare(reference, AccuracyEvaluator.Canonical(score), "canonical-ignore-staff", ignoreStaff: true));
                var staff = AccuracyEvaluator.Compare(reference, AccuracyEvaluator.Canonical(score), "canonical-with-staff");
                comparisons.Add(staff);
                handsMatched = staff.ActualNotes == staff.ExpectedNotes && staff.PitchAtOnsetMatches == staff.ExpectedNotes;
                comparisons.Add(AccuracyEvaluator.Compare(reference, AccuracyEvaluator.Canonical(MeiReader.Read(mei)), "mei-roundtrip-ignore-staff", ignoreStaff: true));
            }
            var handsConfirmed = !midi || handsMatched || confirmHands;
            var handStatus = handsMatched ? "Staff assignments match every reference pitch/onset." : midi
                ? confirmHands ? "MIDI hand inference explicitly confirmed by the caller; not independently verified." : "MIDI hand inference is unverified; offline template checks assume it only for the compiler fixture."
                : "Using written staff assignments; physical hand movement and fingering are not inferred.";
            var source = new RoutineSource("routine-evaluation", score.Title ?? Path.GetFileNameWithoutExtension(sourcePath), RoutineCompiler.SourceRevision(mei), mei, handsConfirmed);
            var request = new RoutineRequest(20, "Help me practise this piece, especially jumps, changing chord shapes, rhythm and hand coordination. Use precise source passages and include a return to the original music.", source.PieceId);
            var evidence = new PracticeEvidenceSnapshot(DateTimeOffset.UtcNow, []);
            var input = RoutineCompiler.Input(request, evidence, source);
            var frames = input.SourceEvents;
            var safe = frames.Where(f => f.Span <= 12 && f.Pitches.Count <= 5).ToArray();
            var pair = safe.Where(f => f.PreviousId is not null && safe.Any(p => p.Id == f.PreviousId)).OrderByDescending(f => f.MoveFromPrevious).FirstOrDefault();
            var chordPair = safe.Where(f => f.PreviousId is not null && safe.Any(p => p.Id == f.PreviousId && (p.Pitches.Count > 1 || f.Pitches.Count > 1))).OrderByDescending(f => f.MoveFromPrevious).FirstOrDefault();
            var chord = safe.Where(f => f.Pitches.Count > 1).OrderByDescending(f => f.Pitches.Count).FirstOrDefault();
            var recipes = new List<RoutineItem>();
            var skipped = new List<string>();
            foreach (var template in RoutineCompiler.Templates)
            {
                var selected = template == "jump" ? pair : template == "chord-changes" ? chordPair : template == "chord-shapes" ? chord : null;
                var recipe = new RoutineItem { Id = template, Kind = "generated", TargetId = template, Title = template, Purpose = "Offline source-preservation evaluation", Minutes = 2, Repetitions = 2 };
                if (template is "jump" or "chord-changes" or "chord-shapes")
                {
                    if (selected is null) { skipped.Add(template + ": no bounded suitable source transition/chord"); continue; }
                    recipe = recipe with { Hands = selected.Staff == 1 ? HandUse.Right : HandUse.Left, SourceEventIds = template == "chord-shapes" ? [selected.Id] : [selected.PreviousId!, selected.Id] };
                }
                else
                {
                    var bar = frames.GroupBy(f => f.Bar).Where(g => template != "coordination" || g.Select(f => f.Staff).Distinct().Count() == 2)
                        .OrderByDescending(g => g.Count()).FirstOrDefault();
                    if (bar is null) { skipped.Add(template + ": no suitable source bar"); continue; }
                    var staff = bar.Any(f => f.Staff == 2) ? 2 : 1;
                    recipe = recipe with { FirstBar = bar.Key, LastBar = bar.Key, Hands = template == "coordination" ? HandUse.Together : staff == 1 ? HandUse.Right : HandUse.Left, Mode = template == "rhythm" ? "time" : "wait" };
                }
                recipes.Add(recipe);
            }
            if (frames.Count > 0) recipes.Add(new RoutineItem { Id = "return", Kind = "excerpt", TargetId = "source", Title = "Original passage", Purpose = "Return to the source", FirstBar = frames[0].Bar, LastBar = frames[0].Bar, Hands = HandUse.Together, Repetitions = 1 });
            var offline = new List<ItemReport>();
            foreach (var recipe in recipes)
            {
                try
                {
                    // This is an explicitly labelled diagnostic assumption, never a user-accepted routine.
                    var prepared = RoutineCompiler.Prepare(new("Offline template check", "Mechanical source checks", [recipe]), request, source with { HandsConfirmed = true }).Single();
                    offline.Add(await InspectAsync(score, prepared, folder, "offline-"));
                }
                catch (Exception error) { offline.Add(new(recipe.Id, recipe.Kind, recipe.TargetId, 0, 0, null, null, error.Message)); }
            }
            Answer<RoutineDraft>? answer = null;
            var planned = new List<ItemReport>();
            string? modelProblem = null;
            if (model is not null)
            {
                if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("The model must be explicit.");
                if (!handsConfirmed) throw new InvalidOperationException("The MIDI hands are unverified. Supply a matching reference, verified MEI, or explicitly confirm them with --confirm-hands before the model run.");
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))) throw new InvalidOperationException("ANTHROPIC_API_KEY is not available; no model request was made.");
                var provider = new Claude(new EnvironmentKey(), NoAssistLog.Instance, TimeProvider.System, maxRetries: 0);
                var settings = new InferenceSettings(model, EffortLevel.Medium, 8000, ValidationRetries: 0, AllowModelFallback: false);
                answer = await provider.AskAsync(new PlanRoutine(settings), input);
                if (answer.Ok)
                {
                    try
                    {
                        foreach (var prepared in RoutineCompiler.Prepare(answer.Value, request, source)) planned.Add(await InspectAsync(score, prepared, folder, "model-"));
                    }
                    catch (Exception error) { modelProblem = error.Message; }
                }
                else modelProblem = answer.Kind.ToString();
            }
            var report = new
            {
                sourcePath, source.Revision, sourceBars = score.Bars.Count, sourceNotes = score.Expected().Count,
                handStatus, handsConfirmed, referencePath, comparisons, offline, skipped,
                model, modelCalls = model is null ? 0 : 1, answer, planned, modelProblem,
                limitations = new[] { "Pitch multiset checks verify the declared transformations, not teaching quality or transfer to playing.", "Rhythm studies intentionally replace source pitches with a single tapping pitch.", "Offline template success alone does not verify inferred MIDI hand assignments." },
            };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, AccuracyEvaluator.Json));
            Console.WriteLine($"Source: {score.Bars.Count} bars, {score.Expected().Count} notes. {handStatus}");
            foreach (var comparison in comparisons) Console.WriteLine($"{comparison.Stage}: {comparison.PitchAtOnsetMatches}/{comparison.ExpectedNotes} pitches at onset; {comparison.ExactMatches} exact durations.");
            foreach (var item in offline) Console.WriteLine($"{item.Template}: {item.Bars} bars, {item.Notes} notes, pitch preservation {item.SourcePitchMultisetPreserved?.ToString() ?? "not applicable"}{(item.Problem is null ? "" : "; " + item.Problem)}");
            if (answer is not null) Console.WriteLine($"Model: {answer.Kind}; {planned.Count} validated items; {answer.Usage.InputTokens} input / {answer.Usage.OutputTokens} output tokens; known cost ${answer.Usage.Cost:0.0000} (complete price: {answer.Usage.CostKnown}).");
            Console.WriteLine($"Report: {reportPath}");
            return offline.Any(i => i.Problem is not null || i.SourcePitchMultisetPreserved == false) || planned.Any(i => i.Problem is not null || i.SourcePitchMultisetPreserved == false) || modelProblem is not null ? 2 : 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }

    private static async Task<ItemReport> InspectAsync(Score source, PreparedRoutineItem prepared, string folder, string prefix)
    {
        var item = prepared.Recipe;
        if (prepared.Mei.Length == 0) return new(item.Id, item.Kind, item.TargetId, 0, 0, null, null, null);
        var generated = MeiReader.Read(prepared.Mei);
        var path = Path.Combine(folder, prefix + item.Id + ".mei");
        await File.WriteAllTextAsync(path, prepared.Mei);
        var actual = AccuracyEvaluator.Canonical(generated).Notes.Select(n => n.Midi).Order().ToArray();
        int[]? expected = null;
        if (item.TargetId is "jump" or "chord-changes" or "chord-shapes")
        {
            var frames = RoutineCompiler.SourceEvents(source).Where(e => item.SourceEventIds.Contains(e.Id));
            var pitches = frames.SelectMany(e => e.Pitches).ToArray();
            expected = Enumerable.Repeat(pitches, item.Repetitions * (item.TargetId == "chord-shapes" ? 2 : 1)).SelectMany(p => p).Order().ToArray();
        }
        else if (item.TargetId != "rhythm")
        {
            var pitches = AccuracyEvaluator.Canonical(source.Excerpt(item.FirstBar!.Value, item.LastBar!.Value)).Notes
                .Where(n => item.Hands == HandUse.Together || n.Staff == (item.Hands == HandUse.Right ? 1 : 2)).Select(n => n.Midi).ToArray();
            var repeats = item.Kind == "excerpt" ? 1 : item.Repetitions * (item.TargetId == "coordination" ? 2 : 1);
            expected = Enumerable.Repeat(pitches, repeats).SelectMany(p => p).Order().ToArray();
        }
        var preserved = expected is null ? (bool?)null : expected.SequenceEqual(actual);
        return new(item.Id, item.Kind, item.TargetId, generated.Bars.Count, generated.Expected().Count, preserved, path, null);
    }

    private sealed class EnvironmentKey : IAssistKeyStore
    {
        public Task<string?> GetAsync() => Task.FromResult(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        public Task SetAsync(string key) => throw new NotSupportedException();
        public Task RemoveAsync() => throw new NotSupportedException();
    }
}
