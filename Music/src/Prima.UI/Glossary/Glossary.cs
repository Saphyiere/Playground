using System.Text;

namespace Prima.UI.Glossary;

/// <summary>The glossary's groups, in the order a reader meets them: the staff first, the rarer marks last.</summary>
public enum GlossaryGroup
{
    /// <summary>The staff: lines and spaces, clefs, the grand staff, ledger lines, middle C, bars and bar lines.</summary>
    Staff,

    /// <summary>Notes and rests: how long each lasts, dots, rests.</summary>
    Notes,

    /// <summary>Rhythm and time: beats, time signatures, ties, beams, triplets, a pickup.</summary>
    Rhythm,

    /// <summary>Sharps, flats and keys: accidentals, naturals, key signatures.</summary>
    Pitch,

    /// <summary>Loud and soft: p, f and the rest, getting louder and softer.</summary>
    Dynamics,

    /// <summary>Touch: how a note is played (slurs and legato, staccato, accents, tenuto, a fermata).</summary>
    Touch,

    /// <summary>Speed and mood: the tempo words, slowing down and back to tempo, expression words.</summary>
    Tempo,

    /// <summary>The roadmap: repeat signs, first and second endings, D.C., D.S., coda, fine.</summary>
    Roadmap,

    /// <summary>Ornaments: trills, mordents, turns, grace notes, spread chords.</summary>
    Ornaments,

    /// <summary>Hands, fingers and pedals: fingering, the pedal marks, playing an octave higher or lower, which hand.</summary>
    Hands,
}

/// <summary>
/// One thing on a sheet of music. <see cref="Rank"/> is how common it is in its group (1 the most, so the list reads common to
/// rare). <see cref="Summary"/> is what it is, in a line; <see cref="Play"/> how it's played and how it sounds. <see cref="Words"/>
/// are other ways to say it, for the search ("curved line", "line over the notes"). <see cref="Example"/> is a bar or two showing
/// it, as MEI the sheet engraves (<see cref="GlossaryMusic"/>). <see cref="Marks"/> are the MEI it is, so a tap on the sheet finds
/// it: an element's name ("slur", "fermata", "keySig"), or a name and a detail after a colon: a note's or rest's length
/// ("note:4", "rest:8"), an articulation ("artic:stacc"), an accidental ("accid:s"), a dynamic's text ("dynam:mf"), a hairpin's
/// form ("hairpin:cres"), a clef's shape ("clef:F"), a pedal's direction ("pedal:down"), an octave line ("octave:8above"), a
/// repeat bar line ("barLine:rpt"), or a word a direction or tempo contains ("dir:rit", "tempo:allegro").
/// </summary>
public sealed record GlossaryEntry(string Id, string Name, GlossaryGroup Group, int Rank, string Summary, string Play, string[] Words, string? Example, string[] Marks);

/// <summary>Examples' music: a bar or a few of MEI around what they show, for the sheet to engrave.</summary>
public static class GlossaryMusic
{
    /// <summary>
    /// Bars on one staff or two. <paramref name="clef"/> is "G" (treble), "F" (bass) or "GF" (the grand staff); the metre is "3/4",
    /// or null for none; the key signature as MEI writes it ("0", "1s", "2f"). Each bar is its notes, as MEI layer content (on the
    /// grand staff, the treble's and the bass's split by "||"), then optionally " | " and the bar's control events (a slur, a
    /// dynam, a fermata…), whose startid and endid point at notes given xml:ids in the bar.
    /// </summary>
    public static string Example(string clef, string? meter, string key, params string[] bars)
    {
        var measures = new StringBuilder();
        for (var i = 0; i < bars.Length; i++)
        {
            var (notes, controls) = bars[i].Split(" | ", 2) is [var n, var c] ? (n, c) : (bars[i], "");
            var staves = notes.Split("||");
            measures.Append($"<measure n=\"{i + 1}\">");
            for (var s = 0; s < staves.Length; s++) measures.Append($"<staff n=\"{s + 1}\"><layer n=\"1\">{staves[s].Trim()}</layer></staff>");
            measures.Append(controls).Append("</measure>");
        }
        return Section(clef, meter, key, measures.ToString());
    }

    /// <summary>
    /// Music written out in full, for what <see cref="Example"/> can't say (repeat signs on bars, first and second endings): the
    /// <c>&lt;section&gt;</c>'s content, measures and endings, on the staves <paramref name="clef"/> gives.
    /// </summary>
    public static string Section(string clef, string? meter, string key, string content)
    {
        var metre = meter?.Split('/') is [var count, var unit] ? $" meter.count=\"{count}\" meter.unit=\"{unit}\"" : "";
        var staves = clef == "GF"
            ? "<staffGrp symbol=\"brace\" bar.thru=\"true\"><staffDef n=\"1\" lines=\"5\" clef.shape=\"G\" clef.line=\"2\"/><staffDef n=\"2\" lines=\"5\" clef.shape=\"F\" clef.line=\"4\"/></staffGrp>"
            : $"<staffGrp><staffDef n=\"1\" lines=\"5\" clef.shape=\"{clef}\" clef.line=\"{(clef == "F" ? 4 : 2)}\"/></staffGrp>";
        return $"<mei xmlns=\"http://www.music-encoding.org/ns/mei\" meiversion=\"5.0\"><meiHead><fileDesc><titleStmt><title/></titleStmt><pubStmt/></fileDesc></meiHead><music><body><mdiv><score><scoreDef key.sig=\"{key}\"{metre}>{staves}</scoreDef><section>{content}</section></score></mdiv></body></music></mei>";
    }
}
