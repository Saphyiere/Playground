using System.Globalization;
using System.Xml.Linq;
using Prima.Core.Music;

namespace Prima.Core.Transcription;

/// <summary>
/// Notation from systems Claude has read, in order, as Prima's own <see cref="Score"/>, and as MEI for Verovio.
/// <list type="bullet">
/// <item>Pitches are read as printed. Here the key signature, the bar's earlier accidentals (on the same staff's line or space,
/// whatever the voice, as printed music has it), ties and 8va lines turn them into the pitches that sound.</item>
/// <item>Ties are joined across barlines and systems, and a tie that lands nowhere is dropped.</item>
/// <item>A short first bar is a pickup, bar 0.</item>
/// <item>Notes under an 8va line keep the pitch they sound, so they're engraved an octave above the print, without the line.</item>
/// <item>A grace note keeps its printed value as its length, which <see cref="MeiWriter"/> writes; it isn't played, so it takes no
/// time from the bar.</item>
/// <item>Fingering goes on its notes. Slurs, dynamics, hairpins, cresc. and dim., and pedal marks become the score's
/// <see cref="Score.FileMarks"/>, tied to notes as <see cref="MeiReader"/> ties a file's own: a mark on a note to that note (or
/// its chord), and one at a rest or a grace note to the note at or before it on its staff, with the ticks between. A slur joins
/// notes of one voice and a hairpin runs on one staff, each ending the latest one open there; one that doesn't close, or closes
/// without a start, is dropped, as at the edge of a system read alone.</item>
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
        // Each mark as read: its bar (by its place in the score), start, staff and voice, its event's place in the voice, and the note.
        var marked = new List<(int Bar, int Start, int Staff, int Voice, int Order, string? Note, bool Grace, ReadMark Mark)>();
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
                    foreach (var (voice, order, start, e) in placed.OrderBy(p => p.Start).ThenBy(p => p.Voice).ThenBy(p => p.Order))
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
                            marked.AddRange(e.Marks.Select(mark => (bars.Count, start, staff.Staff, voice, order, (string?)null, false, mark)));
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
                            tones.Add(new Tone(e.Pitches.Count == 1 ? id : $"{id}-{j}", new Spelled(p.Step, alter, octave))
                            {
                                TieToNext = p.Tie && !e.Grace,
                                TiedFromPrevious = continues,
                                Finger = p.Finger is >= 1 and <= 5 ? p.Finger : null,
                            });
                        }

                        events.Add(new Notes(id, start, length, tones) { Tuplet = tuplet, Grace = e.Grace, Articulation = e.Articulation });
                        marked.AddRange(e.Marks.Select(mark => (bars.Count, start, staff.Staff, voice, order, (string?)id, e.Grace, mark)));
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

        // The marks in the order they come. At one moment, a grace note's come before its main note's, and what ends before what
        // starts, so a slur ending on a note where the next begins closes the first.
        var marks = new List<Mark>();
        var slurs = new List<(int Staff, int Voice, string Id, long Offset)>();
        var hairpins = new List<(int Staff, bool Crescendo, string Id, long Offset)>();
        static int Rank(ReadMark mark) => mark switch
        {
            ReadMark.SlurEnd or ReadMark.HairpinEnd or ReadMark.PedalUp => 0,
            ReadMark.SlurStart or ReadMark.CrescendoStart or ReadMark.DiminuendoStart or ReadMark.PedalChange or ReadMark.PedalDown => 2,
            _ => 1,
        };
        foreach (var m in marked.Distinct().OrderBy(m => (m.Bar, m.Start, m.Grace ? 0 : 1, Rank(m.Mark), m.Staff, m.Voice, m.Order)))
        {
            // A slur can start or end on a grace note; any other mark there, or at a rest, goes to a played note by its beat.
            var onNote = m.Note is not null && (!m.Grace || m.Mark is ReadMark.SlurStart or ReadMark.SlurEnd);
            (string Id, long Offset)? anchor = onNote ? (m.Note!, 0) : Place(bars[m.Bar], m.Staff, m.Start);
            if (anchor is not { } at) continue;
            switch (m.Mark)
            {
                case ReadMark.SlurStart:
                    slurs.Add((m.Staff, m.Voice, at.Id, at.Offset));
                    break;
                case ReadMark.SlurEnd:
                    var slur = slurs.FindLastIndex(s => s.Staff == m.Staff && s.Voice == m.Voice);
                    if (slur < 0) break;
                    var (_, _, slurStart, slurOffset) = slurs[slur];
                    slurs.RemoveAt(slur);
                    if (slurStart != at.Id) marks.Add(new Mark(MarkKind.Slur, slurStart, at.Id) { StartOffset = slurOffset, EndOffset = at.Offset });
                    break;
                case ReadMark.CrescendoStart or ReadMark.DiminuendoStart:
                    hairpins.Add((m.Staff, m.Mark == ReadMark.CrescendoStart, at.Id, at.Offset));
                    break;
                case ReadMark.HairpinEnd:
                    var hairpin = hairpins.FindLastIndex(h => h.Staff == m.Staff);
                    if (hairpin < 0) break;
                    var (_, crescendo, hairpinStart, hairpinOffset) = hairpins[hairpin];
                    hairpins.RemoveAt(hairpin);
                    var kind = crescendo ? MarkKind.Crescendo : MarkKind.Diminuendo;
                    if ((hairpinStart, hairpinOffset) != at) marks.Add(new Mark(kind, hairpinStart, at.Id) { StartOffset = hairpinOffset, EndOffset = at.Offset });
                    break;
                case ReadMark.Cresc or ReadMark.Dim:
                    marks.Add(new Mark(m.Mark == ReadMark.Cresc ? MarkKind.Crescendo : MarkKind.Diminuendo, at.Id) { StartOffset = at.Offset });
                    break;
                case ReadMark.PedalDown or ReadMark.PedalUp:
                    marks.Add(new Mark(m.Mark == ReadMark.PedalDown ? MarkKind.PedalDown : MarkKind.PedalUp, at.Id) { StartOffset = at.Offset });
                    break;
                case ReadMark.PedalChange:
                    marks.Add(new Mark(MarkKind.PedalUp, at.Id) { StartOffset = at.Offset });
                    marks.Add(new Mark(MarkKind.PedalDown, at.Id) { StartOffset = at.Offset });
                    break;
                default:
                    // A dynamic, by its name as printed: ppp to fff and fp set a level; sf, sfz, fz, rf and rfz stress a note.
                    marks.Add(new Mark(MarkKind.Dynamic, at.Id, null, m.Mark.ToString().ToLowerInvariant()) { StartOffset = at.Offset });
                    break;
            }
        }

        var first = systems.SelectMany(s => s.Bars).First();
        var score = new Score(new Key(Math.Clamp(first.Fifths, -7, 7)), new Meter(first.MeterCount, first.MeterUnit), bars);
        return (score with { Title = title, FileMarks = marks }, firstBars);

        // The played note at or before a tick of a bar, on the staff when it has notes there, and the ticks from it to the tick:
        // where MeiReader ties a mark a file places by the beat.
        static (string Id, long Offset)? Place(Bar bar, int staff, long tick)
        {
            var pool = bar.Voices.Where(v => v.Staff == staff).SelectMany(v => v.Events.OfType<Notes>()).Where(n => !n.Grace).ToList();
            if (pool.Count == 0) pool = [.. bar.Voices.SelectMany(v => v.Events.OfType<Notes>()).Where(n => !n.Grace)];
            var anchor = pool.Where(n => n.Start <= tick).MaxBy(n => n.Start) ?? pool.MinBy(n => n.Start);
            return anchor is null ? null : (anchor.Tones.Count == 1 ? anchor.Tones[0].Id : anchor.Id, tick - anchor.Start);
        }
    }

    /// <summary>
    /// The score as MEI for Verovio, with the file marks <see cref="MeiWriter"/> leaves to a piece's own file: each in its note's
    /// bar, on the note when it's tied to it, and by its beat when it's ticks after it, so <see cref="MeiReader"/> brings it back
    /// to the same note and ticks when the notation is kept. A pedal lifted and pressed again at once is written as a bounce.
    /// </summary>
    public static string Mei(Score score)
    {
        XNamespace ns = "http://www.music-encoding.org/ns/mei";
        var mei = XDocument.Parse(MeiWriter.Write(score));
        var measures = mei.Descendants(ns + "measure").ToDictionary(m => (int)m.Attribute("n")!);
        var bars = score.WithSignatures().Select((s, at) => (s.Bar.Number, At: at, s.Meter)).ToDictionary(b => b.Number, b => (b.At, b.Meter));
        string Beat(int bar, long tick) => (1 + tick * (double)bars[bar].Meter.Unit / Ticks.Whole).ToString("0.######", CultureInfo.InvariantCulture);
        // Each note's and chord's bar, staff and start, by the id a mark names it by.
        var notes = new Dictionary<string, (int Bar, int Staff, int Start)>();
        foreach (var bar in score.Bars)
        {
            foreach (var voice in bar.Voices)
            {
                foreach (var n in voice.Events.OfType<Notes>()) notes[n.Tones.Count == 1 ? n.Tones[0].Id : n.Id] = (bar.Number, voice.Staff, n.Start);
            }
        }

        var marks = score.FileMarks;
        for (var i = 0; i < marks.Count; i++)
        {
            var mark = marks[i];
            if (!notes.TryGetValue(mark.StartId, out var start) || mark.EndId is not null && !notes.ContainsKey(mark.EndId)) continue;
            var bounce = mark.Kind == MarkKind.PedalUp && i + 1 < marks.Count && marks[i + 1] == mark with { Kind = MarkKind.PedalDown };
            if (bounce) i++;
            var below = new XAttribute("place", "below");
            var (form, word) = mark.Kind == MarkKind.Crescendo ? ("cres", "cresc.") : ("dim", "dim.");
            var direction = bounce ? "bounce" : mark.Kind == MarkKind.PedalDown ? "down" : "up";
            XElement? element = mark.Kind switch
            {
                MarkKind.Slur when mark.EndId is not null => new(ns + "slur"),
                MarkKind.Crescendo or MarkKind.Diminuendo when mark.EndId is not null => new(ns + "hairpin", new XAttribute("form", form), below),
                MarkKind.Crescendo or MarkKind.Diminuendo => new(ns + "dynam", below, word),
                MarkKind.Dynamic => new(ns + "dynam", below, mark.Text),
                MarkKind.PedalDown or MarkKind.PedalUp => new(ns + "pedal", new XAttribute("dir", direction)),
                _ => null,
            };
            if (element is null) continue;

            element.Add(new XAttribute("staff", start.Staff));
            if (mark.StartOffset == 0) element.Add(new XAttribute("startid", "#" + mark.StartId));
            else element.Add(new XAttribute("tstamp", Beat(start.Bar, start.Start + mark.StartOffset)));
            if (mark.EndId is { } endId)
            {
                // An end by its beat counts the bars on from the start's: "1m+3" is the next bar's third beat.
                var end = notes[endId];
                if (mark.EndOffset == 0) element.Add(new XAttribute("endid", "#" + endId));
                else element.Add(new XAttribute("tstamp2", $"{bars[end.Bar].At - bars[start.Bar].At}m+{Beat(end.Bar, end.Start + mark.EndOffset)}"));
            }
            measures[start.Bar].Add(element);
        }
        return mei.ToString(SaveOptions.DisableFormatting);
    }
}
