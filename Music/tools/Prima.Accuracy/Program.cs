using System.Text.Json;
using Prima.Accuracy;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.Core.Transcription;

if (args.Length > 0 && args[0] == "routine") return await RoutineEvaluation.RunAsync(args[1..]);

if (args.Length is 2 or 3 && args[0] == "reference")
{
    var sourceReference = AccuracyEvaluator.LoadReference(await File.ReadAllTextAsync(args[1]));
    var expected = AccuracyEvaluator.Expected(sourceReference);
    var referenceOutput = JsonSerializer.Serialize(new { sourceReference.Id, sourceReference.SourceSha256, sourceReference.ReviewStatus, expected }, AccuracyEvaluator.Json);
    if (args.Length == 3) await File.WriteAllTextAsync(args[2], referenceOutput);
    else Console.WriteLine(referenceOutput);
    return 0;
}

if (args.Length is < 3 or > 4 || args[0] is not ("reading" or "mei" or "midi"))
{
    Console.Error.WriteLine("Usage: Prima.Accuracy <reading|mei|midi> <reference.json> <candidate-file> [report.json]");
    Console.Error.WriteLine("       Prima.Accuracy reference <reference.json> [events.json]");
    Console.Error.WriteLine("       Prima.Accuracy routine <source.mid|mei> <report.json> [--reference reference.json] [--model explicit-model] [--confirm-hands]");
    return 1;
}

var reference = AccuracyEvaluator.LoadReference(await File.ReadAllTextAsync(args[1]));
var reports = new List<AccuracyReport>();
var candidatePath = args[2];
if (args[0] == "reading")
{
    var candidateJson = await File.ReadAllTextAsync(candidatePath);
    var readings = AccuracyEvaluator.Readings(candidateJson);
    foreach (var stage in new[] { "raw-reading", "canonical", "mei-roundtrip", "playback" })
    {
        try
        {
            var score = stage == "raw-reading" ? null : ReadingScore.Build(readings.Where(r => r is not null).Select(r => r!).ToArray()).Score;
            var notes = stage switch
            {
                "raw-reading" => AccuracyEvaluator.Raw(reference, readings),
                "canonical" => AccuracyEvaluator.Canonical(score!),
                "mei-roundtrip" => AccuracyEvaluator.Canonical(MeiReader.Read(ReadingScore.Mei(score!))),
                _ => AccuracyEvaluator.Playback(score!),
            };
            reports.Add(AccuracyEvaluator.Compare(reference, notes, stage));
        }
        catch (Exception e)
        {
            reports.Add(AccuracyEvaluator.Compare(reference, new CandidateNotes([], []), stage) with { Error = e.Message });
        }
    }
}
else
{
    var notes = args[0] == "midi" ? AccuracyEvaluator.MidiNotes(MidiFile.Read(await File.ReadAllBytesAsync(candidatePath)))
        : AccuracyEvaluator.Canonical(MeiReader.Read(await File.ReadAllTextAsync(candidatePath)));
    reports.Add(AccuracyEvaluator.Compare(reference, notes, args[0], ignoreStaff: args[0] == "midi"));
}
var output = JsonSerializer.Serialize(new { reference = reference.Id, reference.ReviewStatus, reference.Limitations, candidatePath, reports }, AccuracyEvaluator.Json);
if (args.Length == 4) await File.WriteAllTextAsync(args[3], output);
else Console.WriteLine(output);
return reports.Any(r => r.Error is not null) ? 1 : 0;
