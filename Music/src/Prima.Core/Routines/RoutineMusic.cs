using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Routines;

/// <summary>Constructs practice from the exact retained notes. Every transformation has a finite, checked recipe.</summary>
public static class RoutineMusic
{
    public static Score Build(Score source, RoutineItem item)
    {
        if (item.Repetitions is < 1 or > 8 || item.PreparationBeats is < 0 or > 4) throw new ArgumentException("Unsupported exercise dimensions.", nameof(item));
        Score result;
        if (item.TargetId is "jump" or "chord-changes" or "chord-shapes")
        {
            var frames = RoutineCompiler.SourceEvents(source).ToDictionary(e => e.Id);
            if (item.SourceEventIds.Any(id => !frames.ContainsKey(id))) throw new ArgumentException("A referenced source event no longer exists.", nameof(item));
            var selected = item.SourceEventIds.Select(id => frames[id]).ToArray();
            if (selected.Length != (item.TargetId == "chord-shapes" ? 1 : 2) || item.Hands == HandUse.Together) throw new ArgumentException("Select the required events for one hand.", nameof(item));
            var staff = item.Hands == HandUse.Right ? 1 : 2;
            if (selected.Any(e => e.Staff != staff || e.Span > 12 || e.Pitches.Count > 5)) throw new ArgumentException("The selected events need a different hand or individual chord review.", nameof(item));
            if (selected.Length == 2 && selected[1].PreviousId != selected[0].Id) throw new ArgumentException("The selected transition is not consecutive.", nameof(item));
            var tones = source.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>().SelectMany(n => n.Tones))).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
            var sequence = new List<IReadOnlyList<Tone>>();
            for (var repeat = 0; repeat < item.Repetitions; repeat++)
            {
                foreach (var frame in selected)
                {
                    var chord = frame.NoteIds.Select(id => tones[id]).ToArray();
                    // These are deliberately equal-length landing/shape studies; the return-to-source retains original rhythm.
                    if (item.TargetId == "chord-shapes") foreach (var tone in chord) sequence.Add([tone]);
                    sequence.Add(chord);
                    for (var pause = 0; pause < item.PreparationBeats; pause++) sequence.Add([]);
                }
            }
            while (sequence.Count % 4 != 0) sequence.Add([]);
            var bars = new List<Bar>();
            for (var b = 0; b < sequence.Count / 4; b++)
            {
                var events = new List<Event>();
                for (var beat = 0; beat < 4; beat++)
                {
                    var id = $"r{b + 1}e{beat + 1}";
                    var chord = sequence[b * 4 + beat];
                    if (chord.Count == 0) events.Add(new Rest(id, beat * Ticks.Quarter, Ticks.Quarter));
                    else events.Add(new Notes(id, beat * Ticks.Quarter, Ticks.Quarter, chord.Select((t, i) => t with { Id = $"{id}t{i}", TieToNext = false, TiedFromPrevious = false, Ottava = 0 }).ToArray()));
                }
                bars.Add(new Bar(b + 1, [new Voice(staff, events), new Voice(staff == 1 ? 2 : 1, [new Rest($"r{b + 1}space", 0, Ticks.Whole) { Hidden = true }])]));
            }
            var key = source.WithSignatures().First(s => s.Bar.Number == selected[0].Bar).Key;
            result = new Score(key, new Meter(4, 4), bars) { Title = item.Title };
        }
        else
        {
            if (item.FirstBar is not { } first || item.LastBar is not { } last || last < first || last - first > 7 || !source.Bars.Any(b => b.Number == first) || !source.Bars.Any(b => b.Number == last)) throw new ArgumentException("Choose one to eight existing source bars.", nameof(item));
            var cut = source.Excerpt(first, last);
            if (item.Kind != "excerpt" && item.TargetId is not ("accompaniment" or "rhythm" or "coordination")) throw new ArgumentException("Unsupported exercise template.", nameof(item));
            var rhythm = item.TargetId == "rhythm";
            if (item.TargetId is "accompaniment" or "rhythm" && item.Hands == HandUse.Together) throw new ArgumentException("Choose one hand's pattern.", nameof(item));
            if (item.TargetId == "coordination" && (!cut.Expected().Any(n => n.Staff == 1) || !cut.Expected().Any(n => n.Staff == 2))) throw new ArgumentException("Coordination needs notes in both hands.", nameof(item));
            var stages = item.TargetId == "coordination" ? new[] { HandUse.Left, HandUse.Right, HandUse.Together } : new[] { item.Hands };
            var bars = new List<Bar>();
            var marks = new List<Mark>();
            var block = 0;
            for (var repeat = 0; repeat < (item.Kind == "excerpt" ? 1 : item.Repetitions); repeat++)
            {
                foreach (var hand in stages)
                {
                    block++;
                    var ids = new Dictionary<string, string>();
                    string Rename(string id) => ids.TryGetValue(id, out var known) ? known : ids[id] = $"r{block}_{id}";
                    var firstInBlock = true;
                    foreach (var (bar, key, meter) in cut.WithSignatures())
                    {
                        var length = bar.Length ?? meter.BarTicks;
                        var voices = new List<Voice>();
                        foreach (var voice in bar.Voices.Where(v => hand == HandUse.Together || v.Staff == (hand == HandUse.Right ? 1 : 2)))
                        {
                            var events = new List<Event>();
                            foreach (var e in voice.Events)
                            {
                                if (e is Notes n)
                                {
                                    IReadOnlyList<Tone> copied = rhythm
                                        ? [new Tone(Rename(n.Tones[0].Id), new Spelled(Step.C, 0, 4)) { TieToNext = n.Tones.All(t => t.TieToNext), TiedFromPrevious = n.Tones.All(t => t.TiedFromPrevious) }]
                                        : n.Tones.Select(t => t with { Id = Rename(t.Id) }).ToArray();
                                    events.Add(n with { Id = Rename(n.Id), Tones = copied, Articulation = rhythm ? Articulation.None : n.Articulation });
                                }
                                else if (e is Rest rest) events.Add(rest with { Id = Rename(rest.Id) });
                                else if (!rhythm) events.Add(e);
                            }
                            voices.Add(Complete(new Voice(rhythm ? 1 : voice.Staff, events, voice.Layer), length, $"r{block}b{bar.Number}s{voice.Staff}l{voice.Layer}"));
                        }
                        foreach (var staff in rhythm ? new[] { 1 } : new[] { 1, 2 })
                        {
                            if (voices.All(v => v.Staff != staff)) voices.Add(new Voice(staff, [new Rest($"r{block}b{bar.Number}s{staff}space", 0, length) { Hidden = true }]));
                        }
                        bars.Add(new Bar(bars.Count + 1, voices) { Length = bar.Length, NewKey = firstInBlock ? cut.Key : bar.NewKey, NewMeter = firstInBlock ? cut.Meter : bar.NewMeter });
                        firstInBlock = false;
                    }
                    if (!rhythm) marks.AddRange(cut.AllMarks.Where(m => ids.ContainsKey(m.StartId) && (m.EndId is null || ids.ContainsKey(m.EndId)))
                        .Select(m => m with { StartId = ids[m.StartId], EndId = m.EndId is null ? null : ids[m.EndId] }));
                }
            }
            result = new Score(cut.Key, cut.Meter, bars) { Title = item.Title, RhythmOnly = rhythm, Marks = marks };
        }
        var expected = result.Expected();
        if (expected.Count == 0 || expected.Any(n => n.Midi is < 21 or > 108)) throw new ArgumentException("The exercise must contain playable piano notes.", nameof(item));
        var reread = MeiReader.Read(MeiWriter.Write(result, item.Tempo));
        if (!expected.Select(n => (n.Midi, n.Tick, n.Length, n.Staff)).SequenceEqual(reread.Expected().Select(n => (n.Midi, n.Tick, n.Length, n.Staff)))) throw new ArgumentException("The source rhythm cannot be represented faithfully in this exercise.", nameof(item));
        return result;
    }

    private static Voice Complete(Voice voice, int length, string prefix)
    {
        var events = new List<Event>();
        var at = 0;
        foreach (var e in voice.Events.OrderBy(e => e.Start).ThenBy(e => e.Length))
        {
            if (e.Length == 0) { events.Add(e); continue; }
            if (e.Start < at || e.Start + e.Length > length) throw new ArgumentException("A source voice has overlapping notes or overfills its bar.");
            if (e.Start > at) events.AddRange(Padding(at, e.Start - at, prefix));
            events.Add(e);
            at = e.Start + e.Length;
        }
        if (at < length) events.AddRange(Padding(at, length - at, prefix));
        return voice with { Events = events };
    }

    private static IEnumerable<Event> Padding(int start, int length, string prefix)
    {
        while (length > 0)
        {
            var piece = new[] { 1920, 1440, 960, 720, 480, 360, 240, 180, 120, 90, 60, 30 }.FirstOrDefault(n => n <= length);
            Tuplet? tuplet = null;
            if (!Durations.IsWritable(length) && length % 2 == 0 && Durations.IsWritable(length * 3 / 2)) (piece, tuplet) = (length, new Tuplet(3, 2));
            if (piece <= 0) throw new ArgumentException("The source contains a rest too short to represent.");
            yield return new Rest($"{prefix}pad{start}", start, piece) { Hidden = true, Tuplet = tuplet };
            start += piece;
            length -= piece;
        }
    }
}
