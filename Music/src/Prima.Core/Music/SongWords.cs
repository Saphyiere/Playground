using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Prima.Core.Music;

/// <summary>
/// A song's words in its MEI, made ready for the sheet. Verovio prints lyrics (verses of syllables, one under each note) a line
/// for each verse number, and spaces the notes so the syllables on a line never run into each other. Some files put words where
/// they'd still collide: two voices on a staff each with lyrics numbered alike, which Verovio puts on one line; verses an
/// exporter names ("part1verse1", "part1verse2") rather than numbers, which Verovio reads as one verse; or a song's lines typed as
/// text over or under the music (directions), which Verovio places without spacing the notes for them, so they pile up on each
/// other and crush their bars into a corner of the line. <see cref="Prepare"/> gives each voice's lyrics and each verse a line of
/// its own, and moves words typed as text onto a line under the notes they start at. A whole verse typed as text (a direction as
/// long as a verse, printed after the music) isn't for playing and is left off; so are all the words when they're hidden. A
/// player's direction made of music's own words ("D.C. al Fine", "poco a poco cresc.") is never a song's: it stays as it is.
/// </summary>
public static class SongWords
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Longer than this, a direction is a whole verse typed as text, not words under the notes or a player's direction.</summary>
    public const int VerseLength = 60;

    private static readonly Regex Directions = new(@"<dir\b(?:[^>]*?/>|[^>]*>(?<text>[\s\S]*?)</dir>)", RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NotAWord = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);

    // Music's own words, as a player's directions are written (in Italian) and cut short, without accents or capitals: a direction
    // made of these and the words that join them ("D.C. al Fine", "poco a poco cresc.", "Allegro ma non troppo", "dim. e rit.") is
    // a player's, however many words it has, and never a line of a song.
    private static readonly HashSet<string> MusicWords =
    [
        // How fast, and faster or slower
        .. Of("""
            tempo tempi grave largo larghetto larghissimo lento lentissimo lentamente adagio adagietto adagissimo andante andantino
            moderato allegretto allegro allegrissimo vivace vivacissimo vivo vivamente presto prestissimo mosso moto movimento mov
            comodo giusto istesso stesso primo prima secondo seconda doppio veloce tosto principio cadenza recitando recitativo
            accelerando accel acceler acc stringendo string stretto incalzando affrettando affrettato animando ritardando ritard rit
            ritar ritenuto riten rallentando rallent rall allargando allarg slargando slentando calando calmando smorzando smorz
            morendo perdendosi perdendo mancando trattenuto rubato fermata lunga piacere libitum lib abbandonando abbandono
            """),
        // How loud, and how much
        .. Of("""
            piano pianissimo forte fortissimo mezzo mezza mezzoforte mezzopiano sforzando sforzato sforz sf sfz sffz sfp fz fp rf
            rfz rinforzando rinforz rinf crescendo cresc cres decrescendo decresc diminuendo dimin dim p pp ppp pppp mp mf f ff fff
            ffff possibile niente
            poco pochissimo poch pochettino pochetino molto molta assai troppo tanto piu meno sempre subito sub quasi ancora ben gran
            somma pieno
            """),
        // How it's played
        .. Of("""
            dolce dolcissimo dolcemente cantabile cant cantando canto espressivo espress espr espressione affettuoso amoroso
            appassionato passione agitato animato anim brillante brio brioso bravura calmo calmato calma calore deciso delicato
            delicatissimo dolente dolore doloroso eleganza energico eroico fantasia fuoco furioso furia giocoso giocosamente
            grandioso grazioso grazia gusto improvvisato intimo lamentoso languido languendo largamente leggiero leggero legg
            leggierissimo leggieramente leggiermente leggermente leggerezza lusingando maestoso marcato marcata marcatissimo marc
            marziale mesto misterioso nobile nobilmente pesante piacevole pietoso placido pomposo rigore risoluto scherzando
            scherzoso scorrevole semplice sentimento sereno serioso soave sonoro sostenuto sost spiritoso spirito anima strepitoso
            strepito tenebroso teneramente tenerezza tranquillo tranq tumultuoso affannato vertiginoso vigoroso ballabile slancio
            forza grande grand distinto distintamente
            legato legatissimo leg staccato staccatissimo stacc portato portamento martellato accentato accentuato pizzicato pizz
            arco tremolo trem glissando gliss arpeggio arpeggiato arpeggiando arp trillo tr vibrato tenuto ten tenuta tenute
            """),
        // The hands, the pedals, the voices
        .. Of("""
            ped pedale sordino sordini corda corde mano mani destra sinistra md ms rh lh voce voci tema melodia basso parte
            accompagnamento accomp arpa sopra sotto discrezione
            """),
        // Where to go, and what's played there ("Tempo di Valse", "Minuetto da capo")
        .. Of("""
            capo dc ds segno fine coda volta volti vs attacca segue bis ripresa replica ripetizione ossia loco ottava 8va 8vb 15ma
            alta bassa simile sim tacet tutti solo soli unisono unis divisi div pezzo
            valse valzer marcia minuetto menuetto menuett menuet trio scherzo scherzino gavotta gavotte bourree passepied sarabanda
            sarabande giga gigue polacca siciliana pastorale musette aria fuga turca breve preludio maggiore minore
            """),
    ];

    // The words that join music's own ("a", "e", "ma", "il", the "I" of "Tempo I"): a direction of these alone ("la la la") is no
    // player's.
    private static readonly HashSet<string> Joining =
    [
        .. Of("""
            a ad al all alla alle allo ai e ed ma non o con col coll colla coi senza sin sino fino il lo la le l i gli un una uno di
            del dell della dei da dal dall dalla in nel nella per come poi tutte tutta tutto due tre si
            """),
    ];

    private static string[] Of(string words) => words.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether the music has a song's words to show: lyrics, or its lines typed as text.</summary>
    public static bool Has(string mei) => HasLyrics(mei) || Scan(mei).Typed;

    /// <summary>
    /// The MEI for the sheet: with the song's words <paramref name="shown"/>, each voice's lyrics and each verse on lines of their
    /// own and words typed as text on a line under the notes they start at; or with no words at all. A whole verse typed as text is
    /// left off either way. Music with nothing of the kind comes back as it is.
    /// </summary>
    public static string Prepare(string mei, bool shown)
    {
        var scan = Scan(mei);
        var lyrics = HasLyrics(mei);
        if (!lyrics && !scan.Typed && scan.Verses == 0) return mei;

        XDocument document;
        try
        {
            document = XDocument.Parse(mei, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            // What isn't MEI goes to Verovio as it is, which draws nothing of it.
            return mei;
        }
        var directions = document.Descendants(Mei + "dir").Select(d => (Dir: d, Text: TextOf(d))).ToList();
        directions.Where(d => IsVerse(d.Text)).Select(d => d.Dir).Remove();
        var typed = scan.Typed ? directions.Where(d => IsLine(d.Text)).ToList() : [];

        if (!shown)
        {
            typed.Select(d => d.Dir).Remove();
            document.Descendants().Where(e => e.Name == Mei + "verse" || e.Name == Mei + "refrain" || e.Name == Mei + "syl" && e.Parent?.Name.LocalName is "note" or "chord").Remove();
            return document.ToString(SaveOptions.DisableFormatting);
        }

        OwnLines(document);
        if (typed.Count > 0) OntoTheNotes(document, [.. typed.Select(t => (t.Dir, t.Text))]);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool HasLyrics(string mei) => mei.Contains("<verse", StringComparison.Ordinal) || mei.Contains("<refrain", StringComparison.Ordinal) || mei.Contains("<syl", StringComparison.Ordinal);

    // The directions, read from the text without parsing it: how many are whole verses, and whether the music's lines of words are
    // typed as text. They are when there are many directions of a few words (four at least, and one every four bars), as a song's
    // lines run through it; a player's directions are a word or two (dolce, a tempo), or few, and one made of music's own words
    // (poco a poco cresc.) never counts.
    private static (int Verses, bool Typed) Scan(string mei)
    {
        if (!mei.Contains("<dir", StringComparison.Ordinal)) return (0, false);

        var texts = Directions.Matches(mei).Select(m => Space.Replace(System.Net.WebUtility.HtmlDecode(Tags.Replace(m.Groups["text"].Value, t => t.Value.StartsWith("<lb", StringComparison.Ordinal) ? " " : "")), " ").Trim()).ToList();
        var lines = texts.Count(IsLine);
        var bars = Regex.Count(mei, @"<measure\b");
        return (texts.Count(IsVerse), lines >= 4 && lines * 4 >= bars);
    }

    // A line of a song's words: three words or more, shorter than a verse, and not a player's direction. Dashes between a word's
    // parts ("ri - tar - dan - do") aren't words.
    private static bool IsLine(string text) => text.Length <= VerseLength && text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter)) >= 3 && !IsPlayers(text);

    // A whole verse typed as text: longer than a line, and not a player's direction (a "ritar - - - dan - - - do" drawn out over
    // the bars it slows).
    private static bool IsVerse(string text) => text.Length > VerseLength && !IsPlayers(text);

    // Whether a direction is a player's: made of music's own words and the words that join them, one of music's own at least.
    // Its words are read without accents or capitals, as the letters and digits between other marks ("D.C." two, "8va" one), and
    // a word spaced out in parts ("ri - tar - dan - do", "cre scen do") read whole.
    private static bool IsPlayers(string text)
    {
        var plain = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(char.ToLowerInvariant(c));
        var parts = NotAWord.Split(plain.ToString()).Where(p => p.Any(char.IsLetter)).ToList();
        if (parts.Count == 0) return false;

        // How far the first parts can be read: 0 not at all, 1 as joining words only, 2 with one of music's own among them. A word
        // is read from up to eight parts.
        var reach = new int[parts.Count + 1];
        reach[0] = 1;
        for (var from = 0; from < parts.Count; from++)
        {
            if (reach[from] == 0) continue;
            var word = "";
            for (var to = from + 1; to <= Math.Min(parts.Count, from + 8); to++)
            {
                word += parts[to - 1];
                if (MusicWords.Contains(word)) reach[to] = 2;
                else if (Joining.Contains(word)) reach[to] = Math.Max(reach[to], reach[from]);
            }
        }
        return reach[^1] == 2;
    }

    // A direction's words, a line break between two of its lines read as a space.
    private static string TextOf(XElement element) =>
        Space.Replace(string.Concat(element.DescendantNodes().Select(n => n is XText text ? text.Value : n is XElement { Name.LocalName: "lb" } ? " " : "")), " ").Trim();

    private static int N(XElement verse) => int.TryParse((string?)verse.Attribute("n"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;

    private static void SetN(XElement verse, int n) => verse.SetAttributeValue("n", n.ToString(CultureInfo.InvariantCulture));

    private static string StaffOf(XElement element) => (string?)element.Ancestors(Mei + "staff").FirstOrDefault()?.Attribute("n") ?? "1";

    private static int LayerOf(XElement element) => (int?)element.Ancestors(Mei + "layer").FirstOrDefault()?.Attribute("n") ?? 1;

    // Each verse a line of its own: verses numbered alike on one note (an exporter's names, all read as 1) take the next numbers
    // free on it, in order; then on each staff, the same words in two voices print once, and a second voice's other lyrics go on
    // the lines after the first voice's, and so on, so two voices' syllables never share a line. A voice sings the same words as
    // one before it where a verse's syllables in a bar are the same, in its own rhythm or not (a hymn's alto under its soprano);
    // other words, or the same a bar later, are its own.
    private static void OwnLines(XDocument document)
    {
        var verses = document.Descendants(Mei + "verse").ToList();
        foreach (var holder in verses.GroupBy(v => v.Parent))
        {
            var taken = new HashSet<int>();
            foreach (var verse in holder)
            {
                var n = N(verse);
                while (!taken.Add(n)) n++;
                SetN(verse, n);
            }
        }

        foreach (var staff in verses.GroupBy(StaffOf))
        {
            var sung = new HashSet<(XElement? Bar, int N, string Words)>();
            var below = 0;
            foreach (var layer in staff.GroupBy(LayerOf).OrderBy(l => l.Key))
            {
                var own = new List<XElement>();
                foreach (var bar in layer.GroupBy(v => (Bar: v.Ancestors(Mei + "measure").FirstOrDefault(), N: N(v))))
                {
                    if (sung.Add((bar.Key.Bar, bar.Key.N, string.Join(" ", bar.Select(v => Space.Replace(v.Value, " ").Trim()))))) own.AddRange(bar);
                    else bar.Remove();
                }
                if (own.Count == 0) continue;

                var lines = own.Max(N);
                if (below > 0) foreach (var verse in own) SetN(verse, N(verse) + below);
                below += lines;
            }
        }
    }

    // Lines of words typed as text, each moved onto a line of its own under its staff's notes, as one syllable on the note it starts
    // at: the note it names, or the first note at or after its beat in the staff's first voice (the last before it, at a bar's end).
    // Verovio then spaces the notes for it, as it does for lyrics. One with no note to go on stays as it was.
    private static void OntoTheNotes(XDocument document, List<(XElement Dir, string Text)> typed)
    {
        var byId = document.Descendants().Where(e => e.Name.LocalName is "note" or "chord").Select(e => (Id: (string?)e.Attribute(Xml + "id"), Element: e))
            .Where(e => e.Id is not null).GroupBy(e => e.Id!).ToDictionary(g => g.Key, g => g.First().Element);
        var lines = document.Descendants(Mei + "verse").GroupBy(StaffOf).ToDictionary(g => g.Key, g => g.Max(N));
        var unit = 4.0;
        var places = typed.Select(t => t.Dir).ToHashSet();
        var found = new List<(XElement Dir, string Text, XElement Note, string Staff)>();
        foreach (var element in document.Descendants().ToList())
        {
            if (element.Name.LocalName is "scoreDef" or "staffDef" or "meterSig" && (double?)(element.Attribute("meter.unit") ?? element.Attribute("unit")) is { } u and > 0) unit = u;
            if (!places.Contains(element)) continue;

            var staff = ((string?)element.Attribute("staff") ?? "1").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "1";
            var start = ((string?)element.Attribute("startid"))?.TrimStart('#');
            var note = start is not null && byId.TryGetValue(start, out var named) ? named : null;
            if (note is null && element.Ancestors(Mei + "measure").FirstOrDefault() is { } measure)
            {
                var layer = measure.Elements(Mei + "staff").FirstOrDefault(s => ((string?)s.Attribute("n") ?? "1") == staff)?.Elements(Mei + "layer").FirstOrDefault();
                var beat = double.TryParse((string?)element.Attribute("tstamp"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 1;
                var at = (beat - 1) * 4 / unit;
                var notes = layer is null ? [] : Onsets(layer).Where(o => o.Element.Name.LocalName is "note" or "chord").ToList();
                note = notes.FirstOrDefault(o => o.Quarters >= at - 1e-6).Element ?? notes.LastOrDefault().Element;
            }
            if (note?.Parent?.Name == Mei + "chord") note = note.Parent;
            if (note is not null) found.Add((element, typed.First(t => t.Dir == element).Text, note, staff));
        }

        foreach (var (dir, text, note, staff) in found)
        {
            var n = lines.GetValueOrDefault(staff) + 1;
            note.Add(new XElement(Mei + "verse", new XAttribute("n", n.ToString(CultureInfo.InvariantCulture)), new XElement(Mei + "syl", text)));
            dir.Remove();
        }
    }

    // Where each note, chord and rest of a layer starts, in quarter notes from the bar's start, through beams, tuplets and grace
    // groups; grace notes take no time.
    private static List<(XElement Element, double Quarters)> Onsets(XElement layer)
    {
        var onsets = new List<(XElement, double)>();
        var at = 0.0;
        void Walk(XElement parent, double ratio, bool grace)
        {
            foreach (var child in parent.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "beam" or "graceGrp" or "fTrem" or "bTrem" or "ftrem" or "btrem":
                        Walk(child, ratio, grace || child.Name.LocalName == "graceGrp");
                        break;
                    case "tuplet":
                        var (num, numbase) = ((double?)child.Attribute("num") ?? 3, (double?)child.Attribute("numbase") ?? 2);
                        Walk(child, ratio * numbase / num, grace);
                        break;
                    case "note" or "chord" or "rest" or "space":
                        onsets.Add((child, at));
                        if (!grace && child.Attribute("grace") is null) at += Quarters(child) * ratio;
                        break;
                }
            }
        }
        Walk(layer, 1, false);
        return onsets;
    }

    private static double Quarters(XElement element)
    {
        var dur = (string?)element.Attribute("dur") ?? (string?)element.Elements(Mei + "note").FirstOrDefault()?.Attribute("dur") ?? "4";
        var value = dur switch { "long" => 16.0, "breve" => 8.0, _ => double.TryParse(dur, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? 4 / d : 1 };
        var dots = (int?)element.Attribute("dots") ?? 0;
        return value * (2 - Math.Pow(0.5, dots));
    }
}
