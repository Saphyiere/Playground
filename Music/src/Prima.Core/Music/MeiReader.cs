using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Prima.Core.Music;

/// <summary>
/// Reads MEI into a score: Prima's own (from <see cref="MeiWriter"/>) and the MEI Verovio makes from an imported MusicXML,
/// Humdrum or ABC file, keeping every id. It reads bars in written order with their repeat signs and endings (which
/// <see cref="Score.PlayOrder"/> follows), staves and layers, notes, chords and rests through beams, tuplets and grace groups,
/// durations by value or by ticks, ties (as attributes or tie elements), accidentals as written or implied (by the key, by an
/// earlier one in the bar, or marked as gestural), and key, metre and clef changes. A piece's slurs, dynamics, hairpins, pedal
/// marks and ornaments are read as its <see cref="Score.FileMarks"/>, for judging how it's played; Verovio engraves them from
/// the MEI itself.
/// </summary>
public static class MeiReader
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    public static Score Read(string mei)
    {
        var document = XDocument.Parse(mei);
        var score = document.Descendants(Mei + "score").First();
        var title = document.Descendants(Mei + "title").FirstOrDefault()?.Value.Trim();
        var firstDef = score.Element(Mei + "scoreDef");
        var key = ReadKey(firstDef) ?? new Key(0);
        var meter = ReadMeter(firstDef) ?? new Meter(4, 4);
        var ppq = Ppq(firstDef) ?? 0;
        var (startKey, startMeter) = (key, meter);
        var staffCount = firstDef?.Descendants(Mei + "staffDef").Count() ?? 2;

        // Tie elements sit in the bar where the tie starts, so they're gathered from the whole score first.
        var tied = score.Descendants(Mei + "tie").Select(t => (Start: Id((string?)t.Attribute("startid")), End: Id((string?)t.Attribute("endid")))).ToList();
        var bars = new List<Bar>();
        // Each bar's metre, and the marks the bars hold beside their staves (slurs, dynamics, pedal…), placed once all are read.
        var meters = new List<Meter>();
        var controls = new List<(int Bar, XElement Mark)>();
        Key? newKey = null;
        Meter? newMeter = null;
        var number = 0;
        var startsNext = false;
        foreach (var element in score.Descendants().Where(e => e.Name == Mei + "measure" || (e.Name == Mei + "scoreDef" && e != firstDef)))
        {
            if (element.Name == Mei + "scoreDef")
            {
                if (ReadKey(element) is { } k) newKey = key = k;
                if (ReadMeter(element) is { } m) newMeter = meter = m;
                ppq = Ppq(element) ?? ppq;
                continue;
            }

            var voices = new List<Voice>();
            var length = 0;
            foreach (var staff in element.Elements(Mei + "staff"))
            {
                var staffNumber = (int?)staff.Attribute("n") ?? 1;
                var accidentals = new Dictionary<(Step, int), int>();
                foreach (var layer in staff.Elements(Mei + "layer"))
                {
                    var reader = new LayerReader(key, ppq, accidentals, tied);
                    reader.ReadChildren(layer, 1, 1);
                    voices.Add(new Voice(staffNumber, reader.Events, (int?)layer.Attribute("n") ?? 1));
                    length = Math.Max(length, reader.Tick);
                }
            }

            // A bar rest (mRest, mSpace) lasts the bar: the metre's, or in a short bar the other layers' length.
            if (length == 0) length = meter.BarTicks;
            voices = [.. voices.Select(v => v with { Events = [.. v.Events.Select(e => e is Rest { Length: 0 } bar ? bar with { Length = length } : e)] })];
            var pickup = (string?)element.Attribute("metcon") == "false" || (bars.Count == 0 && length < meter.BarTicks);
            // Bars are numbered in order, a pickup 0, whatever the file says: files repeat numbers (two endings both 17) or skip
            // them. Renumber puts the same numbers in the MEI, so the printed ones agree.
            number = bars.Count == 0 ? (pickup ? 0 : 1) : number + 1;
            // Repeat signs: a start before the bar (or a both after the one before), an end after it (or before the next); an
            // ending's number says which time through plays it ("1", "2", "1, 2").
            var (left, right) = ((string?)element.Attribute("left"), (string?)element.Attribute("right"));
            if (left is "rptend" or "rptboth" && bars.Count > 0) bars[^1] = bars[^1] with { RepeatEnd = true };
            var ending = element.Ancestors(Mei + "ending").FirstOrDefault();
            var endingName = (string?)ending?.Attribute("n") ?? (string?)ending?.Attribute("label") ?? "";
            var endings = Regex.Matches(endingName, @"\d+").Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
            var repeatStart = left is "rptstart" or "rptboth" || startsNext;
            var repeatEnd = right is "rptend" or "rptboth";
            var bar = new Bar(number, voices) { Length = pickup ? length : null, NewKey = newKey, NewMeter = newMeter };
            controls.AddRange(element.Elements().Where(e => e.Name.LocalName is "slur" or "dynam" or "hairpin" or "pedal" or "trill" or "mordent" or "turn").Select(e => (bars.Count, e)));
            meters.Add(meter);
            bars.Add(bar with { RepeatStart = repeatStart, RepeatEnd = repeatEnd, Endings = endings });
            startsNext = right == "rptboth";
            (newKey, newMeter) = (null, null);
        }

        // The marks: on a note (startid), or by the beat (tstamp, the metre's beats from 1), tied to the note at or before it on its
        // staff with the ticks between; an end the same way (endid, or tstamp2: "2m+3" is two bars on, beat 3).
        var fileMarks = new List<Mark>();
        foreach (var (index, control) in controls)
        {
            var staves = ((string?)control.Attribute("staff") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => int.Parse(n, CultureInfo.InvariantCulture)).ToList();
            if (Place(index, (string?)control.Attribute("startid"), (string?)control.Attribute("tstamp")) is not { } start) continue;

            var ahead = Regex.Match((string?)control.Attribute("tstamp2") ?? "", @"^(\d+)m\+([\d.]+)$");
            var end = control.Attribute("endid") is { } endId ? (Id((string)endId), 0L)
                : ahead.Success ? Place(index + int.Parse(ahead.Groups[1].Value, CultureInfo.InvariantCulture), null, ahead.Groups[2].Value) : null;
            var text = Regex.Replace(control.Value, @"\s+", " ").Trim();
            var dynamic = Regex.Match(text, "^(ppp|pp|mp|mf|p|fff|ff|fp|fz|f|sfz|sf|rfz|rf)(?![a-z])");
            MarkKind? kind = control.Name.LocalName switch
            {
                "slur" when end is not null => MarkKind.Slur,
                "hairpin" when end is not null => (string?)control.Attribute("form") == "dim" ? MarkKind.Diminuendo : MarkKind.Crescendo,
                "dynam" when text.StartsWith("cresc", StringComparison.OrdinalIgnoreCase) => MarkKind.Crescendo,
                "dynam" when text.StartsWith("dim", StringComparison.OrdinalIgnoreCase) || text.StartsWith("decresc", StringComparison.OrdinalIgnoreCase) => MarkKind.Diminuendo,
                "dynam" when dynamic.Success => MarkKind.Dynamic,
                "pedal" => (string?)control.Attribute("dir") == "up" ? MarkKind.PedalUp : MarkKind.PedalDown,
                "trill" => MarkKind.Trill,
                "mordent" => MarkKind.Mordent,
                "turn" => MarkKind.Turn,
                _ => null,
            };
            if (kind is null) continue;

            // A bounce is the pedal lifted and pressed again at once: a change.
            if ((string?)control.Attribute("dir") == "bounce") fileMarks.Add(new Mark(MarkKind.PedalUp, start.Id) { StartOffset = start.Offset });
            var hasEnd = kind is MarkKind.Slur or MarkKind.Crescendo or MarkKind.Diminuendo && end is not null;
            fileMarks.Add(new Mark(kind.Value, start.Id, hasEnd ? end!.Value.Id : null, kind == MarkKind.Dynamic ? dynamic.Value : null) { StartOffset = start.Offset, EndOffset = hasEnd ? end!.Value.Offset : 0 });

            // The note at or before a beat of a bar (on the mark's staff when it has notes there), and the ticks from it to the beat.
            (string Id, long Offset)? Place(int at, string? noteId, string? beat)
            {
                if (noteId is not null) return (Id(noteId), 0);
                if (at >= bars.Count || !double.TryParse(beat, NumberStyles.Float, CultureInfo.InvariantCulture, out var beats)) return null;

                var tick = (long)Math.Round((beats - 1) * Ticks.Whole / meters[at].Unit);
                var all = bars[at].Voices.SelectMany(v => v.Events.OfType<Notes>().Where(n => !n.Grace).Select(n => (v.Staff, Notes: n))).ToList();
                var pool = all.Any(p => staves.Contains(p.Staff)) ? all.Where(p => staves.Contains(p.Staff)).Select(p => p.Notes).ToList() : all.Select(p => p.Notes).ToList();
                var anchor = pool.Where(n => n.Start <= tick).MaxBy(n => n.Start) ?? pool.MinBy(n => n.Start);
                return anchor is null ? null : (anchor.Tones.Count == 1 ? anchor.Tones[0].Id : anchor.Id, tick - anchor.Start);
            }
        }

        return new Score(startKey, startMeter, bars) { Title = string.IsNullOrEmpty(title) ? null : title, RhythmOnly = staffCount == 1, FileMarks = fileMarks };
    }

    /// <summary>The MEI with its bars numbered as <see cref="Read"/> numbers them, so the bar numbers printed are the ones Prima says.</summary>
    public static string Renumber(string mei, Score score)
    {
        var document = XDocument.Parse(mei);
        var measures = document.Descendants(Mei + "measure").ToList();
        for (var i = 0; i < measures.Count && i < score.Bars.Count; i++) measures[i].SetAttributeValue("n", score.Bars[i].Number.ToString(CultureInfo.InvariantCulture));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string Id(string? reference) => reference?.TrimStart('#') ?? "";

    private static int? Ppq(XElement? def) => (int?)def?.Attribute("ppq") ?? (int?)def?.Descendants(Mei + "staffDef").FirstOrDefault()?.Attribute("ppq");

    private static Key? ReadKey(XElement? def)
    {
        var sig = (string?)def?.Attribute("key.sig") ?? (string?)def?.Descendants(Mei + "keySig").FirstOrDefault()?.Attribute("sig") ?? (string?)def?.Descendants(Mei + "staffDef").FirstOrDefault()?.Attribute("key.sig");
        if (sig is null) return null;
        if (sig is "0" or "") return new Key(0);

        var count = int.Parse(sig[..^1], CultureInfo.InvariantCulture);
        return new Key(sig.EndsWith('f') ? -count : count);
    }

    private static Meter? ReadMeter(XElement? def)
    {
        var holder = def?.Attribute("meter.count") is not null ? def : def?.Descendants(Mei + "staffDef").FirstOrDefault(s => s.Attribute("meter.count") is not null);
        if (holder is not null) return new Meter((int)holder.Attribute("meter.count")!, (int)holder.Attribute("meter.unit")!);

        var sig = def?.Descendants(Mei + "meterSig").FirstOrDefault();
        return sig?.Attribute("count") is null ? null : new Meter((int)sig.Attribute("count")!, (int)sig.Attribute("unit")!);
    }

    // Walks a layer's children in order, keeping the tick, through beams, tuplets and grace groups.
    private sealed class LayerReader(Key key, int ppq, Dictionary<(Step, int), int> accidentals, List<(string Start, string End)> tied)
    {
        public List<Event> Events { get; } = [];

        public int Tick { get; private set; }

        public void ReadChildren(XElement parent, int actual, int normal, Tuplet? tuplet = null, bool grace = false)
        {
            foreach (var child in parent.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "beam" or "beamSpan" or "graceGrp" or "ftrem" or "btrem":
                        ReadChildren(child, actual, normal, tuplet, grace || child.Name.LocalName == "graceGrp");
                        break;
                    case "tuplet":
                        var (num, numbase) = ((int?)child.Attribute("num") ?? 3, (int?)child.Attribute("numbase") ?? 2);
                        ReadChildren(child, actual * num, normal * numbase, new Tuplet(num, numbase), grace);
                        break;
                    case "note" or "chord":
                        ReadNotes(child, actual, normal, tuplet, grace);
                        break;
                    case "rest" or "space":
                        var restLength = Length(child, actual, normal);
                        Events.Add(new Rest(IdOf(child), Tick, restLength) { Tuplet = tuplet, Hidden = child.Name.LocalName == "space" });
                        Tick += restLength;
                        break;
                    case "mRest" or "mSpace":
                        // Its length is the bar's, set once every layer is read.
                        Events.Add(new Rest(IdOf(child), Tick, 0) { Hidden = child.Name.LocalName == "mSpace" });
                        break;
                    case "clef":
                        Events.Add(new ClefChange(Tick, (string?)child.Attribute("shape") == "F" ? Clef.Bass : Clef.Treble));
                        break;
                }
            }
        }

        private void ReadNotes(XElement element, int actual, int normal, Tuplet? tuplet, bool grace)
        {
            var isGrace = grace || element.Attribute("grace") is not null;
            var length = isGrace ? 0 : Length(element, actual, normal);
            List<XElement> notes = element.Name.LocalName == "chord" ? [.. element.Elements(Mei + "note")] : [element];
            var tones = notes.Select(note => ReadTone(note, element)).ToList();
            // Its articulations, on it or its notes, as attributes or elements, each a list ("stacc acc"): a staccatissimo, spiccato or
            // portato is played short as a staccato is.
            var artics = element.DescendantsAndSelf().Where(e => e.Name.LocalName is "note" or "chord" or "artic").Select(e => (string?)e.Attribute("artic")).OfType<string>()
                .SelectMany(a => a.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet();
            Events.Add(new Notes(IdOf(element), Tick, length, tones)
            {
                Tuplet = tuplet,
                Grace = isGrace,
                Articulation = artics.Overlaps(["stacc", "stacciss", "spicc", "ten-stacc"]) ? Articulation.Staccato : artics.Contains("acc") ? Articulation.Accent : artics.Contains("ten") ? Articulation.Tenuto : Articulation.None,
            });
            Tick += length;
        }

        private Tone ReadTone(XElement note, XElement holder)
        {
            var step = Enum.Parse<Step>(((string)note.Attribute("pname")!).ToUpperInvariant());
            var octave = (int)note.Attribute("oct")!;
            var accid = note.Element(Mei + "accid");
            var written = (string?)note.Attribute("accid") ?? (string?)accid?.Attribute("accid");
            var gestural = (string?)note.Attribute("accid.ges") ?? (string?)accid?.Attribute("accid.ges");
            int alter;
            if (written is not null) alter = Alter(written);
            else if (gestural is not null) alter = Alter(gestural);
            else alter = accidentals.TryGetValue((step, octave), out var earlier) ? earlier : key.AlterOf(step);
            if (written is not null) accidentals[(step, octave)] = alter;

            var id = IdOf(note);
            var tie = (string?)note.Attribute("tie") ?? (string?)holder.Attribute("tie");
            return new Tone(id, new Spelled(step, alter, octave))
            {
                TieToNext = tie is "i" or "m" || tied.Any(t => t.Start == id),
                TiedFromPrevious = tie is "t" or "m" || tied.Any(t => t.End == id),
            };
        }

        private int Length(XElement element, int actual, int normal)
        {
            if (ppq > 0 && element.Attribute("dur.ppq") is { } ticks) return (int)ticks * Ticks.Quarter / ppq;

            var dur = (string?)element.Attribute("dur") ?? (string?)element.Elements(Mei + "note").FirstOrDefault()?.Attribute("dur") ?? "4";
            var baseTicks = dur switch { "long" => 4 * Ticks.Whole, "breve" => 2 * Ticks.Whole, _ => Ticks.Whole / int.Parse(dur, CultureInfo.InvariantCulture) };
            var dots = (int?)element.Attribute("dots") ?? 0;
            return baseTicks * ((1 << (dots + 1)) - 1) / (1 << dots) * normal / actual;
        }

        private static string IdOf(XElement element) => (string?)element.Attribute(Xml + "id") ?? "";

        private static int Alter(string accid) => accid switch { "s" => 1, "f" => -1, "x" or "ss" => 2, "ff" => -2, _ => 0 };
    }
}
