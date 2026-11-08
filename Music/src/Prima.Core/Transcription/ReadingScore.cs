using Prima.Core.Music;

namespace Prima.Core.Transcription;

/// <summary>
/// Notation from systems Claude has read, in order, as Prima's own <see cref="Score"/>, ready for <see cref="MeiWriter"/>.
/// <list type="bullet">
/// <item>Pitches are read as printed. Here the key signature, the bar's earlier accidentals (on the same staff's line or space,
/// whatever the voice, as printed music has it), ties and 8va lines turn them into the pitches that sound.</item>
/// <item>Ties are joined across barlines and systems, and a tie that lands nowhere is dropped.</item>
/// <item>A short first bar is a pickup, bar 0.</item>
/// <item>Notes under an 8va line keep the pitch they sound, so they're engraved an octave above the print, without the line.</item>
/// <item>A grace note keeps its printed value as its length, which <see cref="MeiWriter"/> writes; it isn't played, so it takes no
/// time from the bar.</item>
/// </list>
/// </summary>
public static class ReadingScore
{
    /// <summary>The score, and the number of each system's first bar in it.</summary>
    public static (Score Score, IReadOnlyList<int> FirstBars) Build(IReadOnlyList<SystemReading> systems, string title = "")
    {
        if (!systems.SelectMany(s => s.Bars).Any()) throw new ArgumentException("There are no bars to build.", nameof(systems));

        var bars = new List<Bar>();
        var firstBars = new List<int>();
        var count = 0;
        var clefs = new Dictionary<int, Clef> { [1] = Clef.Treble, [2] = Clef.Bass };
        // Each voice's last note with tones tied on: where it is, and its tied pitches by letter and octave.
        var open = new Dictionary<(int Staff, int Voice), (List<Event> Events, int At, Dictionary<(Step, int), Spelled> Tied)>();
        (Key Key, Meter Meter)? before = null;

        // Drops the ties of a note that didn't land: all of them, or those whose pitches aren't in landed.
        void Drop((List<Event> Events, int At, Dictionary<(Step, int), Spelled> Tied) from, HashSet<(Step, int)> landed)
        {
            var notes = (Notes)from.Events[from.At];
            from.Events[from.At] = notes with { Tones = [.. notes.Tones.Select(t => t.TieToNext && !landed.Contains((t.Pitch.Step, t.Pitch.Octave)) ? t with { TieToNext = false } : t)] };
        }

        foreach (var system in systems)
        {
            for (var b = 0; b < system.Bars.Count; b++)
            {
                var read = system.Bars[b];
                var key = new Key(Math.Clamp(read.Fifths, -7, 7));
                var meter = new Meter(read.MeterCount, read.MeterUnit);
                // The bar lasts as long as its longest voice; a bar rest fills it.
                var longest = read.Staves.SelectMany(s => s.Voices).Select(v => v.Events.Where(e => !e.Grace && e.Kind is not (ReadKind.Clef or ReadKind.BarRest))
                    .Sum(e => ReadingChecks.Length(e) ?? 0)).DefaultIfEmpty(0).Max();
                var barLength = longest > 0 ? longest : meter.BarTicks;

                var voices = new List<Voice>();
                foreach (var staff in read.Staves.OrderBy(s => s.Staff))
                {
                    var built = staff.Voices.Select(v => v.Voice).Distinct().ToDictionary(v => v, _ => new List<Event>());
                    if (built.Count == 0) continue;
                    var atStart = staff.Clef switch { ReadClef.Treble => Clef.Treble, ReadClef.Bass => Clef.Bass, _ => clefs[staff.Staff] };
                    if (atStart != clefs[staff.Staff]) built[built.Keys.Min()].Add(new ClefChange(0, atStart));
                    clefs[staff.Staff] = atStart;

                    // Every event with its start, so the staff's events go in time order whatever their voice: an accidental holds
                    // for the rest of the bar on its line or space.
                    var placed = new List<(int Voice, int Order, int Start, ReadEvent Read)>();
                    foreach (var voice in staff.Voices)
                    {
                        var tick = 0;
                        for (var i = 0; i < voice.Events.Count; i++)
                        {
                            var e = voice.Events[i];
                            placed.Add((voice.Voice, i, tick, e));
                            if (!e.Grace && e.Kind != ReadKind.Clef) tick += e.Kind == ReadKind.BarRest ? barLength : ReadingChecks.Length(e) ?? 0;
                        }
                    }

                    var carried = new Dictionary<(Step, int), int>();
                    var tuplets = new Dictionary<int, Tuplet?>();
                    foreach (var (voice, _, start, e) in placed.OrderBy(p => p.Start).ThenBy(p => p.Voice).ThenBy(p => p.Order))
                    {
                        var events = built[voice];
                        if (e.Kind == ReadKind.Clef)
                        {
                            clefs[staff.Staff] = e.Clef == ReadClef.Bass ? Clef.Bass : Clef.Treble;
                            events.Add(new ClefChange(start, clefs[staff.Staff]));
                            continue;
                        }

                        var length = e.Kind == ReadKind.BarRest ? barLength : ReadingChecks.Length(e) ?? 0;
                        // A tuplet group runs from its start through the voice's next events in the same tuplet.
                        var tuplet = e.Tuplet <= 0 ? null : !e.TupletStart && tuplets.GetValueOrDefault(voice) is { } going && going.Actual == e.Tuplet
                            ? going : new Tuplet(e.Tuplet, ReadingChecks.Normal(e.Tuplet));
                        if (!e.Grace) tuplets[voice] = tuplet;
                        if (e.Kind != ReadKind.Note)
                        {
                            events.Add(new Rest($"r{++count}", start, length) { Hidden = e.Kind == ReadKind.Space, Tuplet = tuplet });
                            if (open.Remove((staff.Staff, voice), out var cut)) Drop(cut, []);
                            continue;
                        }

                        var id = $"n{++count}";
                        var tiedFrom = e.Grace ? default : open.GetValueOrDefault((staff.Staff, voice));
                        var tones = new List<Tone>();
                        for (var j = 0; j < e.Pitches.Count; j++)
                        {
                            var p = e.Pitches[j];
                            var octave = p.Octave + e.Ottava;
                            // A tied note sounds as the note it's tied from; otherwise its accidental, the bar's earlier one, or the key's.
                            var continues = tiedFrom.Tied is not null && tiedFrom.Tied.ContainsKey((p.Step, octave));
                            int alter;
                            if (continues) alter = tiedFrom.Tied![(p.Step, octave)].Alter;
                            else if (p.Accidental != Accidental.None) carried[(p.Step, p.Octave)] = alter = ReadingChecks.Alter(p.Accidental);
                            else alter = carried.TryGetValue((p.Step, p.Octave), out var earlier) ? earlier : key.AlterOf(p.Step);
                            tones.Add(new Tone(e.Pitches.Count == 1 ? id : $"{id}-{j}", new Spelled(p.Step, alter, octave)) { TieToNext = p.Tie && !e.Grace, TiedFromPrevious = continues });
                        }

                        events.Add(new Notes(id, start, length, tones) { Tuplet = tuplet, Grace = e.Grace, Articulation = e.Articulation });
                        if (e.Grace) continue;
                        if (open.Remove((staff.Staff, voice), out var from)) Drop(from, [.. tones.Where(t => t.TiedFromPrevious).Select(t => (t.Pitch.Step, t.Pitch.Octave))]);
                        var tiedOn = tones.Where(t => t.TieToNext).ToDictionary(t => (t.Pitch.Step, t.Pitch.Octave), t => t.Pitch);
                        if (tiedOn.Count > 0) open[(staff.Staff, voice)] = (events, events.Count - 1, tiedOn);
                    }

                    voices.AddRange(built.OrderBy(v => v.Key).Select(v => new Voice(staff.Staff, v.Value, v.Key)));
                }

                var number = bars.Count == 0 ? (barLength < meter.BarTicks ? 0 : 1) : bars[^1].Number + 1;
                if (b == 0) firstBars.Add(number);
                bars.Add(new Bar(number, voices)
                {
                    Length = barLength == meter.BarTicks ? null : barLength,
                    NewKey = before is { } k && k.Key != key ? key : null,
                    NewMeter = before is { } m && m.Meter != meter ? meter : null,
                    RepeatStart = read.RepeatStart,
                    RepeatEnd = read.End == BarEnd.RepeatEnd,
                    Endings = read.Ending > 0 ? [read.Ending] : [],
                });
                before = (key, meter);
            }
        }

        // Ties still open at the end land nowhere.
        foreach (var from in open.Values) Drop(from, []);
        var first = systems.SelectMany(s => s.Bars).First();
        return (new Score(new Key(Math.Clamp(first.Fifths, -7, 7)), new Meter(first.MeterCount, first.MeterUnit), bars) { Title = title }, firstBars);
    }
}
