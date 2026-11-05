using System.Globalization;
using System.Xml;

namespace Prima.Core.Music;

/// <summary>
/// Writes a score as MEI for Verovio to engrave: the grand staff (or one rhythm line), every note, chord, rest and bar with its id,
/// beams by the beat, tuplets, ties, accidentals as a printed score shows them (the key signature's and those earlier in the bar
/// aren't repeated), and the printed marks. No XML declaration: Verovio reads the text as it is.
/// </summary>
public static class MeiWriter
{
    private const string Ns = "http://www.music-encoding.org/ns/mei";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";

    public static string Write(Score score, double bpm = 80)
    {
        var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var xml = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            xml.WriteStartElement("mei", Ns);
            xml.WriteAttributeString("meiversion", "5.0");
            xml.WriteStartElement("meiHead", Ns);
            xml.WriteStartElement("fileDesc", Ns);
            xml.WriteStartElement("titleStmt", Ns);
            xml.WriteElementString("title", Ns, score.Title ?? "");
            xml.WriteEndElement();
            xml.WriteElementString("pubStmt", Ns, "");
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteStartElement("music", Ns);
            xml.WriteStartElement("body", Ns);
            xml.WriteStartElement("mdiv", Ns);
            xml.WriteStartElement("score", Ns);

            xml.WriteStartElement("scoreDef", Ns);
            WriteSignature(xml, score.Key, score.Meter);
            xml.WriteAttributeString("midi.bpm", bpm.ToString("0.###", CultureInfo.InvariantCulture));
            xml.WriteStartElement("staffGrp", Ns);
            if (score.RhythmOnly) WriteStaffDef(xml, 1, null);
            else
            {
                xml.WriteAttributeString("symbol", "brace");
                xml.WriteAttributeString("bar.thru", "true");
                WriteStaffDef(xml, 1, Clef.Treble);
                WriteStaffDef(xml, 2, Clef.Bass);
            }
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("section", Ns);
            var staffOf = score.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.SelectMany(e => Ids(e).Select(id => (id, v.Staff))))).ToDictionary(p => p.id, p => p.Staff);
            var barOf = score.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.SelectMany(Ids)).Select(id => (id, b.Number))).ToDictionary(p => p.id, p => p.Number);
            var signatures = score.WithSignatures().ToList();
            for (var i = 0; i < signatures.Count; i++)
            {
                var (bar, key, meter) = signatures[i];
                if (i > 0 && (bar.NewKey is not null || bar.NewMeter is not null))
                {
                    xml.WriteStartElement("scoreDef", Ns);
                    WriteSignature(xml, bar.NewKey, bar.NewMeter);
                    xml.WriteEndElement();
                }

                var length = bar.Length ?? meter.BarTicks;
                xml.WriteStartElement("measure", Ns);
                xml.WriteAttributeString("n", bar.Number.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("id", XmlNs, bar.Id);
                if (length < meter.BarTicks) xml.WriteAttributeString("metcon", "false");
                if (i == signatures.Count - 1) xml.WriteAttributeString("right", "end");
                foreach (var staff in bar.Voices.Select(v => v.Staff).Distinct().Order())
                {
                    xml.WriteStartElement("staff", Ns);
                    xml.WriteAttributeString("n", staff.ToString(CultureInfo.InvariantCulture));
                    // The bar's accidentals so far on this staff, by letter and octave; they carry across layers, as printed.
                    var accidentals = new Dictionary<(Step, int), int>();
                    foreach (var voice in bar.Voices.Where(v => v.Staff == staff).OrderBy(v => v.Layer))
                    {
                        xml.WriteStartElement("layer", Ns);
                        xml.WriteAttributeString("n", voice.Layer.ToString(CultureInfo.InvariantCulture));
                        WriteEvents(xml, voice.Events, key, meter, length, accidentals);
                        xml.WriteEndElement();
                    }
                    xml.WriteEndElement();
                }

                foreach (var tone in bar.Voices.SelectMany(v => v.Events.OfType<Notes>().SelectMany(n => n.Tones.Select(t => (Tone: t, v.Staff)))).Where(p => p.Tone.Finger is not null))
                {
                    xml.WriteStartElement("fing", Ns);
                    xml.WriteAttributeString("startid", "#" + tone.Tone.Id);
                    xml.WriteAttributeString("staff", tone.Staff.ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("place", tone.Staff == 1 ? "above" : "below");
                    xml.WriteString(tone.Tone.Finger!.Value.ToString(CultureInfo.InvariantCulture));
                    xml.WriteEndElement();
                }

                foreach (var mark in score.Marks.Where(m => barOf.GetValueOrDefault(m.StartId, -1) == bar.Number)) WriteMark(xml, mark, staffOf.GetValueOrDefault(mark.StartId, 1));
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
        return text.ToString();
    }

    private static IEnumerable<string> Ids(Event e) => e switch
    {
        Notes n => n.Tones.Select(t => t.Id).Append(n.Id).Distinct(),
        Rest r => [r.Id],
        _ => [],
    };

    private static void WriteSignature(XmlWriter xml, Key? key, Meter? meter)
    {
        if (key is { } k) xml.WriteAttributeString("key.sig", k.Fifths == 0 ? "0" : $"{Math.Abs(k.Fifths)}{(k.Fifths > 0 ? "s" : "f")}");
        if (meter is not { } m) return;

        xml.WriteAttributeString("meter.count", m.Count.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("meter.unit", m.Unit.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteStaffDef(XmlWriter xml, int n, Clef? clef)
    {
        xml.WriteStartElement("staffDef", Ns);
        xml.WriteAttributeString("n", n.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("lines", clef is null ? "1" : "5");
        xml.WriteAttributeString("clef.shape", clef switch { Clef.Treble => "G", Clef.Bass => "F", _ => "perc" });
        if (clef is not null) xml.WriteAttributeString("clef.line", clef == Clef.Treble ? "2" : "4");
        xml.WriteEndElement();
    }

    // Writes a layer's events, wrapping tuplets in <tuplet> and beamable runs within one beam group in <beam>.
    private static void WriteEvents(XmlWriter xml, IReadOnlyList<Event> events, Key key, Meter meter, int barLength, Dictionary<(Step, int), int> accidentals)
    {
        var i = 0;
        while (i < events.Count)
        {
            if (events[i].Tuplet is { } tuplet)
            {
                var end = i;
                while (end < events.Count && ReferenceEquals(events[end].Tuplet, tuplet)) end++;
                xml.WriteStartElement("tuplet", Ns);
                xml.WriteAttributeString("num", tuplet.Actual.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("numbase", tuplet.Normal.ToString(CultureInfo.InvariantCulture));
                WriteBeamed(xml, events.Skip(i).Take(end - i).ToList(), key, meter, barLength, accidentals, wholeGroup: true);
                xml.WriteEndElement();
                i = end;
                continue;
            }

            var run = i;
            while (run < events.Count && events[run].Tuplet is null) run++;
            WriteBeamed(xml, events.Skip(i).Take(run - i).ToList(), key, meter, barLength, accidentals, wholeGroup: false);
            i = run;
        }
    }

    private static void WriteBeamed(XmlWriter xml, List<Event> events, Key key, Meter meter, int barLength, Dictionary<(Step, int), int> accidentals, bool wholeGroup)
    {
        var groups = BeamGroups(meter);
        var i = 0;
        while (i < events.Count)
        {
            var end = i;
            if (Beamable(events[i]))
            {
                var group = wholeGroup ? 0 : GroupOf(events[i].Start, groups);
                while (end < events.Count && Beamable(events[end]) && (wholeGroup || GroupOf(events[end].Start, groups) == group)) end++;
            }

            if (end - i >= 2)
            {
                xml.WriteStartElement("beam", Ns);
                for (var k = i; k < end; k++) WriteEvent(xml, events[k], key, barLength, accidentals);
                xml.WriteEndElement();
                i = end;
            }
            else WriteEvent(xml, events[i++], key, barLength, accidentals);
        }
    }

    private static bool Beamable(Event e) => e is Notes { Grace: false } n && Written(n) <= Ticks.Eighth;

    // Where beams break, as tick boundaries in the bar: the beat, or for 3/8 the bar, 5/8 2+3, 7/8 2+2+3, 2/2 the half.
    private static int[] BeamGroups(Meter meter) => (meter.Count, meter.Unit) switch
    {
        (5, 8) => [0, 480, 1200],
        (7, 8) => [0, 480, 960, 1680],
        (3, 8) => [0, 720],
        (2, 2) => [0, 960, 1920],
        _ => Enumerable.Range(0, meter.Beats + 1).Select(b => b * meter.BeatTicks).ToArray(),
    };

    private static int GroupOf(int start, int[] boundaries) => boundaries.Count(b => b <= start);

    private static int Written(Event e) => e.Tuplet is { } t ? e.Length * t.Actual / t.Normal : e.Length;

    private static void WriteEvent(XmlWriter xml, Event e, Key key, int barLength, Dictionary<(Step, int), int> accidentals)
    {
        switch (e)
        {
            case ClefChange clef:
                xml.WriteStartElement("clef", Ns);
                xml.WriteAttributeString("shape", clef.Clef == Clef.Treble ? "G" : "F");
                xml.WriteAttributeString("line", clef.Clef == Clef.Treble ? "2" : "4");
                xml.WriteEndElement();
                return;
            case Rest rest when rest.Length == barLength && rest.Start == 0:
                xml.WriteStartElement("mRest", Ns);
                xml.WriteAttributeString("id", XmlNs, rest.Id);
                xml.WriteEndElement();
                return;
            case Rest rest:
                xml.WriteStartElement("rest", Ns);
                xml.WriteAttributeString("id", XmlNs, rest.Id);
                WriteDuration(xml, Written(rest));
                xml.WriteEndElement();
                return;
            case Notes { Tones.Count: 1 } single:
                WriteNote(xml, single.Tones[0], single, key, accidentals, inChord: false);
                return;
            case Notes chord:
                xml.WriteStartElement("chord", Ns);
                xml.WriteAttributeString("id", XmlNs, chord.Id);
                WriteDuration(xml, Written(chord));
                WriteNoteFlags(xml, chord);
                foreach (var tone in chord.Tones.OrderBy(t => t.Pitch.Midi)) WriteNote(xml, tone, chord, key, accidentals, inChord: true);
                xml.WriteEndElement();
                return;
        }
    }

    private static void WriteNote(XmlWriter xml, Tone tone, Notes notes, Key key, Dictionary<(Step, int), int> accidentals, bool inChord)
    {
        xml.WriteStartElement("note", Ns);
        xml.WriteAttributeString("id", XmlNs, tone.Id);
        xml.WriteAttributeString("pname", tone.Pitch.Step.ToString().ToLowerInvariant());
        xml.WriteAttributeString("oct", tone.Pitch.Octave.ToString(CultureInfo.InvariantCulture));
        if (!inChord)
        {
            WriteDuration(xml, Written(notes));
            WriteNoteFlags(xml, notes);
        }

        var place = (tone.Pitch.Step, tone.Pitch.Octave);
        var current = accidentals.TryGetValue(place, out var alter) ? alter : key.AlterOf(tone.Pitch.Step);
        accidentals[place] = tone.Pitch.Alter;
        if (tone.Pitch.Alter != current && !tone.TiedFromPrevious) xml.WriteAttributeString("accid", Accid(tone.Pitch.Alter));
        else if (tone.Pitch.Alter != 0) xml.WriteAttributeString("accid.ges", Accid(tone.Pitch.Alter));

        if (tone.TieToNext || tone.TiedFromPrevious) xml.WriteAttributeString("tie", tone.TieToNext && tone.TiedFromPrevious ? "m" : tone.TieToNext ? "i" : "t");
        xml.WriteEndElement();
    }

    private static void WriteNoteFlags(XmlWriter xml, Notes notes)
    {
        if (notes.Grace) xml.WriteAttributeString("grace", "unacc");
        if (notes.Articulation != Articulation.None) xml.WriteAttributeString("artic", notes.Articulation switch { Articulation.Staccato => "stacc", Articulation.Accent => "acc", _ => "ten" });
    }

    private static string Accid(int alter) => alter switch { 2 => "x", 1 => "s", -1 => "f", -2 => "ff", _ => "n" };

    private static void WriteDuration(XmlWriter xml, int written)
    {
        for (var dots = 0; dots <= 2; dots++)
        {
            // A dotted note lasts its base plus half, plus a quarter: base × (2 - 1/2^dots).
            var baseTicks = written * (1 << dots) / ((1 << (dots + 1)) - 1);
            if (baseTicks * ((1 << (dots + 1)) - 1) != written * (1 << dots) || Ticks.Whole % baseTicks != 0) continue;

            var dur = Ticks.Whole / baseTicks;
            if (dur is not (1 or 2 or 4 or 8 or 16 or 32 or 64)) continue;

            xml.WriteAttributeString("dur", dur.ToString(CultureInfo.InvariantCulture));
            if (dots > 0) xml.WriteAttributeString("dots", dots.ToString(CultureInfo.InvariantCulture));
            return;
        }
        throw new ArgumentException($"{written} ticks isn't a note value.");
    }

    private static void WriteMark(XmlWriter xml, Mark mark, int staff)
    {
        (string Name, (string, string)[] Attributes) element = mark.Kind switch
        {
            MarkKind.Dynamic => ("dynam", [("place", "below")]),
            MarkKind.Slur => ("slur", []),
            MarkKind.Crescendo => ("hairpin", [("form", "cres"), ("place", "below")]),
            MarkKind.Diminuendo => ("hairpin", [("form", "dim"), ("place", "below")]),
            MarkKind.PedalDown => ("pedal", [("dir", "down")]),
            MarkKind.PedalUp => ("pedal", [("dir", "up")]),
            MarkKind.OctaveUp => ("octave", [("dis", "8"), ("dis.place", "above")]),
            MarkKind.Fermata => ("fermata", []),
            MarkKind.Trill => ("trill", []),
            _ => ("mordent", []),
        };
        xml.WriteStartElement(element.Name, Ns);
        xml.WriteAttributeString("staff", staff.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("startid", "#" + mark.StartId);
        if (mark.EndId is not null) xml.WriteAttributeString("endid", "#" + mark.EndId);
        foreach (var (attribute, value) in element.Attributes) xml.WriteAttributeString(attribute, value);
        if (mark.Text is not null) xml.WriteString(mark.Text);
        xml.WriteEndElement();
    }
}
