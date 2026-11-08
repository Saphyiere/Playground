using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Prima.Core.Music;

/// <summary>
/// Reads MEI into a score: Prima's own (from <see cref="MeiWriter"/>) and the MEI Verovio makes from an imported MusicXML,
/// Humdrum or ABC file, keeping every id. It reads bars in written order with their repeat signs and endings (which
/// <see cref="Score.PlayOrder"/> follows), staves and layers, notes, chords and rests through beams, tuplets and grace groups,
/// durations by value or by ticks, ties (as attributes or tie elements), accidentals as written or implied (by the key, by an
/// earlier one in the bar, or marked as gestural), and key, metre and clef changes. Printed marks other than ties aren't read as
/// the score's marks, since Verovio engraves them from the MEI itself; a slur's end and a note's ornament are kept on the note,
/// for judging a run.
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
        // So do slurs and ornaments, kept on their notes.
        var slurEnds = score.Descendants(Mei + "slur").Select(s => Id((string?)s.Attribute("endid"))).ToHashSet();
        var ornaments = score.Descendants().Where(e => e.Name.LocalName is "trill" or "mordent" or "turn").Select(o => Id((string?)o.Attribute("startid"))).ToHashSet();
        var bars = new List<Bar>();
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
                    var reader = new LayerReader(key, ppq, accidentals, tied, ornaments, slurEnds);
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
            bars.Add(bar with { RepeatStart = repeatStart, RepeatEnd = repeatEnd, Endings = endings });
            startsNext = right == "rptboth";
            (newKey, newMeter) = (null, null);
        }

        return new Score(startKey, startMeter, bars) { Title = string.IsNullOrEmpty(title) ? null : title, RhythmOnly = staffCount == 1 };
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
    private sealed class LayerReader(Key key, int ppq, Dictionary<(Step, int), int> accidentals, List<(string Start, string End)> tied, HashSet<string> ornaments, HashSet<string> slurEnds)
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
                Ornamented = element.DescendantsAndSelf().Any(e => e.Attribute("ornam") is not null) || notes.Append(element).Any(e => ornaments.Contains(IdOf(e))),
                EndsSlur = notes.Append(element).Any(e => slurEnds.Contains(IdOf(e))),
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
