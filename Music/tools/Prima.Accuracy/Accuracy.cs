using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Playground.AI.Assist;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.Core.Transcription;

namespace Prima.Accuracy;

public sealed record ReferenceBar(int Number, int System, int Index, string UpperClef, string Upper, string Lower);
public sealed record AccuracyReference(string Id, string SourceSha256, string ReviewStatus, string[] Limitations, ReferenceBar[] Bars);
public sealed record MeasuredNote(int Bar, int Staff, long Start, long Length, int Midi);
public sealed record CandidateNotes(IReadOnlyList<int> Bars, IReadOnlyList<MeasuredNote> Notes);
public sealed record BarAccuracy(int Bar, int Expected, int Actual, int Exact, int PitchAtOnset, int Rhythm);
public sealed record AccuracyReport(string Stage, string ReferenceStatus, int ExpectedNotes, int ActualNotes, int ExactMatches,
    int PitchAtOnsetMatches, int RhythmMatches, double Precision, double Recall, IReadOnlyList<int> MissingBars,
    IReadOnlyList<int> ExtraBars, IReadOnlyList<int> DuplicateBars, IReadOnlyList<MeasuredNote> MissingNotes,
    IReadOnlyList<MeasuredNote> ExtraNotes, IReadOnlyList<BarAccuracy> Bars, string? Error = null);

/// <summary>Offline source-reference comparisons. No model calls and no generated result is used as a reference.</summary>
public static class AccuracyEvaluator
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static AccuracyReference LoadReference(string json) => JsonSerializer.Deserialize<AccuracyReference>(json, Json)
        ?? throw new FormatException("The reference is empty.");

    public static CandidateNotes Expected(AccuracyReference reference)
    {
        if (reference.Bars.Select(b => b.Number).Distinct().Count() != reference.Bars.Length) throw new FormatException("Duplicate reference bars.");
        var notes = new List<MeasuredNote>();
        foreach (var bar in reference.Bars)
        {
            foreach (var (staff, sequence) in new[] { (1, bar.Upper), (2, bar.Lower) })
            {
                long at = 0;
                foreach (var token in sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = token.Split(':');
                    if (parts.Length != 2) throw new FormatException($"Invalid reference event: {token}");
                    var ticks = decimal.Parse(parts[1], CultureInfo.InvariantCulture) * 480;
                    if (ticks <= 0 || ticks != decimal.Truncate(ticks)) throw new FormatException($"Invalid reference duration: {token}");
                    var length = (long)ticks;
                    if (parts[0] != "r") foreach (var pitch in parts[0].Split('+')) notes.Add(new(bar.Number, staff, at, length, Midi(pitch)));
                    at += length;
                }
                if (at != 1920) throw new FormatException($"Reference bar {bar.Number}, staff {staff} does not fill four quarter beats.");
            }
        }
        return new(reference.Bars.Select(b => b.Number).ToArray(), notes);
    }

    public static int Midi(string pitch)
    {
        var match = Regex.Match(pitch, @"^([A-G])([#b]?)(-?\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new FormatException($"Invalid pitch: {pitch}");
        var natural = match.Groups[1].Value switch { "C" => 0, "D" => 2, "E" => 4, "F" => 5, "G" => 7, "A" => 9, _ => 11 };
        var alter = match.Groups[2].Value switch { "#" => 1, "b" => -1, _ => 0 };
        return 12 * (int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) + 1) + natural + alter;
    }

    public static SystemReading?[] Readings(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array) return root.Deserialize<SystemReading?[]>(AssistSchema.Json) ?? [];
        if (root.TryGetProperty("readings", out var readings)) return readings.Deserialize<SystemReading?[]>(AssistSchema.Json) ?? [];
        throw new FormatException("Supply a saved reading draft or a JSON array of system readings.");
    }

    public static CandidateNotes Raw(AccuracyReference reference, IReadOnlyList<SystemReading?> readings)
    {
        var notes = new List<MeasuredNote>();
        var bars = new List<int>();
        for (var system = 0; system < readings.Count; system++)
        {
            if (readings[system] is not { } reading) continue;
            foreach (var bar in reading.Bars)
            {
                var number = reference.Bars.FirstOrDefault(b => b.System == system + 1 && b.Index == bar.Index)?.Number
                    ?? 10000 + system * 1000 + bar.Index;
                bars.Add(number);
                foreach (var staff in bar.Staves)
                {
                    var placed = new List<(long At, long Length, ReadEvent Event)>();
                    foreach (var voice in staff.Voices)
                    {
                        long at = 0;
                        foreach (var item in voice.Events)
                        {
                            if (item.Kind == ReadKind.Clef) continue;
                            if (!Enum.IsDefined(item.Value) || item.Dots is < 0 or > 2) throw new FormatException("Invalid note value or dots.");
                            if (bar.MeterCount != 4 || bar.MeterUnit != 4) throw new FormatException("This baseline evaluator requires four-quarter bars.");
                            if (item.Grace) throw new FormatException("This reference evaluator does not score grace notes.");
                            if (item.Tuplet != 0) throw new FormatException("This reference evaluator does not score tuplets.");
                            var length = item.Kind == ReadKind.BarRest ? bar.MeterCount * 1920L / bar.MeterUnit
                                : 1920L / (1L << (int)item.Value) * ((1L << (item.Dots + 1)) - 1) / (1L << item.Dots);
                            placed.Add((at, length, item));
                            at += length;
                        }
                    }
                    var accidentals = new Dictionary<(Step, int), int>();
                    foreach (var (at, length, item) in placed.OrderBy(p => p.At))
                    {
                        if (item.Kind != ReadKind.Note && item.Pitches.Count > 0) throw new FormatException("A non-note event contains pitches.");
                        foreach (var pitch in item.Pitches)
                        {
                            if (pitch.Tie) throw new FormatException("This reference evaluator does not score tied candidates as raw note attacks.");
                            var name = pitch.Step.ToString();
                            var keyAlter = bar.Fifths > 0 && "FCGDAEB".IndexOf(name, StringComparison.Ordinal) < bar.Fifths ? 1
                                : bar.Fifths < 0 && "BEADGCF".IndexOf(name, StringComparison.Ordinal) < -bar.Fifths ? -1 : 0;
                            var alter = pitch.Accidental switch
                            {
                                Accidental.Sharp => 1, Accidental.Flat => -1, Accidental.DoubleSharp => 2, Accidental.DoubleFlat => -2,
                                Accidental.Natural => 0, _ => accidentals.GetValueOrDefault((pitch.Step, pitch.Octave), keyAlter),
                            };
                            if (pitch.Accidental != Accidental.None) accidentals[(pitch.Step, pitch.Octave)] = alter;
                            notes.Add(new(number, staff.Staff, at, length, Midi(name + (pitch.Octave + item.Ottava).ToString(CultureInfo.InvariantCulture)) + alter));
                        }
                    }
                }
            }
        }
        return new(bars, notes);
    }

    public static CandidateNotes Canonical(Score score) => new(score.Bars.Select(b => b.Number).ToArray(),
        score.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>().SelectMany(n =>
            n.Tones.Select(t => new MeasuredNote(b.Number, v.Staff, n.Start, n.Length, t.Pitch.Midi))))).ToArray());

    public static CandidateNotes Playback(Score score) => new(score.Bars.Select(b => b.Number).ToArray(),
        score.Expected().Select(n => new MeasuredNote(n.Bar, n.Staff, n.Tick - (n.Bar - 1L) * 1920, n.Length, n.Midi)).ToArray());

    public static CandidateNotes MidiNotes(MidiFile midi)
    {
        var notes = midi.Ticked.Select(n =>
        {
            var at = n.On * 480m / midi.Division;
            var length = (n.Off - n.On) * 480m / midi.Division;
            if (at != decimal.Truncate(at) || length != decimal.Truncate(length)) throw new FormatException("MIDI timing is not exact at 480 ticks per quarter.");
            return new MeasuredNote((int)(at / 1920) + 1, 0, (long)at % 1920, (long)length, n.Midi);
        }).ToArray();
        return new(notes.Select(n => n.Bar).Distinct().Order().ToArray(), notes);
    }

    public static AccuracyReport Compare(AccuracyReference reference, CandidateNotes actual, string stage, bool ignoreStaff = false)
    {
        var expected = Expected(reference);
        if (expected.Notes.Count == 0) throw new FormatException("The reference contains no notes to compare.");
        var wanted = expected.Notes.Select(n => ignoreStaff ? n with { Staff = 0 } : n).ToArray();
        var given = actual.Notes.Select(n => ignoreStaff ? n with { Staff = 0 } : n).ToArray();
        var wantedCounts = wanted.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        var givenCounts = given.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        var missing = wantedCounts.SelectMany(p => Enumerable.Repeat(p.Key, Math.Max(0, p.Value - givenCounts.GetValueOrDefault(p.Key)))).ToArray();
        var extra = givenCounts.SelectMany(p => Enumerable.Repeat(p.Key, Math.Max(0, p.Value - wantedCounts.GetValueOrDefault(p.Key)))).ToArray();
        var perBar = expected.Bars.Concat(actual.Bars).Distinct().Order().Select(bar =>
        {
            var e = wanted.Where(n => n.Bar == bar).ToArray();
            var a = given.Where(n => n.Bar == bar).ToArray();
            return new BarAccuracy(bar, e.Length, a.Length, Matches(e, a),
                Matches(e.Select(n => (n.Staff, n.Start, n.Midi)), a.Select(n => (n.Staff, n.Start, n.Midi))),
                Matches(e.Select(n => (n.Staff, n.Start, n.Length)), a.Select(n => (n.Staff, n.Start, n.Length))));
        }).ToArray();
        var exact = perBar.Sum(b => b.Exact);
        return new(stage, reference.ReviewStatus, wanted.Length, given.Length, exact, perBar.Sum(b => b.PitchAtOnset), perBar.Sum(b => b.Rhythm),
            given.Length == 0 ? 0 : exact / (double)given.Length, exact / (double)wanted.Length,
            expected.Bars.Except(actual.Bars).Order().ToArray(), actual.Bars.Except(expected.Bars).Order().ToArray(),
            actual.Bars.GroupBy(b => b).Where(g => g.Count() > 1).Select(g => g.Key).Order().ToArray(), missing, extra, perBar);
    }

    private static int Matches<T>(IEnumerable<T> expected, IEnumerable<T> actual) where T : notnull
    {
        var available = actual.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        return expected.GroupBy(n => n).Sum(g => Math.Min(g.Count(), available.GetValueOrDefault(g.Key)));
    }
}
