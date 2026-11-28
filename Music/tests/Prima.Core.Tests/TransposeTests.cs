using System.Text.RegularExpressions;
using System.Xml.Linq;
using Prima.Core.Music;

namespace Prima.Core.Tests;

/// <summary>
/// A piece played in another key: the way there (<see cref="Transposition"/>) and its MEI moved in place (<see cref="MeiTranspose"/>),
/// read back as Prima reads a piece, so what's drawn and what's expected agree.
/// </summary>
public partial class TransposeTests
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>
    /// A piece for two staves in a key signature ("0", "2s", "3f"), a bar of quarter notes on the upper staff for each string given
    /// and a bar rest under it. A note is its letter, its accidental, its octave: "f#4" printed sharp (as Verovio writes one, an
    /// accid in the note), "fn4" a natural, "fx4" a double sharp, "f(#)4" sounding sharp with none printed (accid.ges, as the key
    /// gives it); "~" after a note ties it to the next, before one ties it from the one before; a "'" after it puts it under an 8va.
    /// A bar that starts "key=4s" changes the key there. Each note's id is "n" and its place in the piece, from 1.
    /// </summary>
    private static string Piece(string sig, params string[] bars)
    {
        var count = 0;
        var measures = bars.Select((bar, i) =>
        {
            var tokens = bar.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var change = tokens.Count > 0 && tokens[0].StartsWith("key=", StringComparison.Ordinal) ? $"""<scoreDef><keySig sig="{tokens[0][4..]}"/></scoreDef>""" : "";
            var notes = string.Concat(tokens.Where(t => !t.StartsWith("key=", StringComparison.Ordinal)).Select(t => Note(t, ++count)));
            return $"""{change}<measure n="{i + 1}"><staff n="1"><layer n="1">{notes}</layer></staff><staff n="2"><layer n="1"><mRest/></layer></staff></measure>""";
        });
        return $"""
            <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score>
            <scoreDef><staffGrp><staffDef n="1" lines="5"><clef shape="G" line="2"/><keySig sig="{sig}"/><meterSig count="4" unit="4"/></staffDef>
            <staffDef n="2" lines="5"><clef shape="F" line="4"/><keySig sig="{sig}"/><meterSig count="4" unit="4"/></staffDef></staffGrp></scoreDef>
            <section>{string.Concat(measures)}</section></score></mdiv></body></music></mei>
            """;
    }

    private static string Note(string token, int number)
    {
        var m = NoteToken().Match(token);
        Assert.True(m.Success, token);
        var accid = m.Groups["accid"].Value switch { "#" => "s", "b" => "f", "bb" => "ff", "n" => "n", "x" => "x", _ => null };
        var sounds = m.Groups["sounds"].Value switch { "#" => "s", "b" => "f", _ => null };
        var octave = int.Parse(m.Groups["oct"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var tie = m.Groups["from"].Success ? m.Groups["to"].Success ? "m" : "t" : m.Groups["to"].Success ? "i" : null;
        return $"""<note xml:id="n{number}" pname="{m.Groups["pname"].Value}" oct="{octave}" dur="4" """
            + (m.Groups["ottava"].Success ? $"""oct.ges="{octave + 1}" """ : "") + (sounds is null ? "" : $"""accid.ges="{sounds}" """)
            + (tie is null ? "" : $"""tie="{tie}" """) + (accid is null ? "/>" : $"""><accid accid="{accid}"/></note>""");
    }

    [GeneratedRegex(@"^(?<from>~)?(?<pname>[a-g])(?<accid>bb|#|b|n|x)?(\((?<sounds>#|b)\))?(?<oct>\d)(?<ottava>')?(?<to>~)?$")]
    private static partial Regex NoteToken();

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    // Each note's pitch as it sounds, by id.
    private static string Pitch(Score score, string id) =>
        score.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events.OfType<Notes>()).SelectMany(n => n.Tones).Single(t => t.Id == id).Pitch.ToString();

    private static Dictionary<string, XElement> Notes(string mei) =>
        XDocument.Parse(mei).Descendants(Mei + "note").ToDictionary(n => (string)n.Attribute(Xml + "id")!);

    private static List<string> Ids(string mei) => [.. XDocument.Parse(mei).Descendants().Select(e => (string?)e.Attribute(Xml + "id")).OfType<string>().Order()];

    // An attribute's value, "" where there's none.
    private static string At(XElement element, string name) => (string?)element.Attribute(name) ?? "";

    // The accidental printed on a note, on itself or in its accid, "" where none is.
    private static string Printed(XElement note) => (string?)note.Attribute("accid") ?? (note.Element(Mei + "accid") is { } accid ? At(accid, "accid") : "");

    [Fact]
    public void VeroviosMeiInDMajorPlaysInC()
    {
        // D major in 3/4: a triplet with F sharps the key gives, a chord, a tie into bar 2, and a C natural printed.
        var mei = Fixture("verovio-from-musicxml.mei");
        var written = MeiReader.Read(mei);

        var by = Transposition.To(written, new Key(0));
        var moved = MeiTranspose.Apply(mei, by);
        var read = MeiReader.Read(moved);

        Assert.Equal(new Transposition(-1, -2), by);
        Assert.Equal(new Key(0), read.Key);
        Assert.Equal(written.Expected().Select(n => (n.Id, n.Tick, n.Midi - 2, n.Length)), read.Expected().Select(n => (n.Id, n.Tick, n.Midi, n.Length)));
        // Its F sharps are Es, the key giving them; the C natural printed against the key is a B flat printed.
        var notes = Notes(moved);
        Assert.Equal(("e", "5", ""), (At(notes["szib6yz"], "pname"), At(notes["szib6yz"], "oct"), At(notes["szib6yz"], "accid.ges")));
        Assert.Equal(("b", "4", "f"), (At(notes["r15gd3wy"], "pname"), At(notes["r15gd3wy"], "oct"), Printed(notes["r15gd3wy"])));
        Assert.Equal("0", At(XDocument.Parse(moved).Descendants(Mei + "keySig").First(), "sig"));
        // Every id is where it was, so marks kept on the notes find them.
        Assert.Equal(Ids(mei), Ids(moved));
    }

    [Fact]
    public void AsWrittenIsTheMeiItself()
    {
        var mei = Piece("2s", "d4 f4 a4 d5");

        Assert.Same(mei, MeiTranspose.Apply(mei, Transposition.None));
        Assert.Equal(Transposition.None, Transposition.To(MeiReader.Read(mei), new Key(2)));
    }

    [Fact]
    public void EachNoteKeepsItsPlaceInItsKey()
    {
        // C major to E♭ major: the scale's notes are the new key's, and a raised fourth (F♯) is printed as the raised fourth (A natural).
        var mei = Piece("0", "c4 e4 f#4 g4", "b4 c5 d5 e5");
        var by = Transposition.To(MeiReader.Read(mei), new Key(-3));
        var read = MeiReader.Read(MeiTranspose.Apply(mei, by));

        Assert.Equal(new Transposition(2, 3), by);
        Assert.Equal(["E♭4", "G4", "A4", "B♭4", "D5", "E♭5", "F5", "G5"], Enumerable.Range(1, 8).Select(i => Pitch(read, $"n{i}")));
        var notes = Notes(MeiTranspose.Apply(mei, by));
        Assert.Equal("n", Printed(notes["n3"]));
        Assert.Equal("f", At(notes["n1"], "accid.ges"));
        Assert.Equal("", At(notes["n2"], "accid.ges"));
    }

    [Theory]
    // Raised notes: G♭ major spells a raised fifth D, F♯ major C double sharp.
    [InlineData("0", "c4 d4 e4 g#4", -6)]
    // A lowered third: F♯ major spells it A, G♭ major B double flat.
    [InlineData("0", "c4 d4 eb4 g4", 6)]
    // Neither: a piece in sharps, or none, goes to F♯; one in flats to G♭.
    [InlineData("0", "c4 d4 e4 g4", 6)]
    [InlineData("1f", "f4 g4 a4 c5", -6)]
    public void TheKeyAHalfOctaveAwayIsSpelledForThePiece(string sig, string notes, int fifths) =>
        Assert.Equal(new Key(fifths), Transposition.Keys(MeiReader.Read(Piece(sig, notes)))[6]);

    [Fact]
    public void TheOtherKeysWithTwoNamesTakeTheFewerSharpsOrFlats()
    {
        var keys = Transposition.Keys(MeiReader.Read(Piece("0", "c4 e4 g4 c5")));

        Assert.Equal(["C major", "D♭ major", "D major", "E♭ major", "E major", "F major", "F♯ major", "G major", "A♭ major", "A major", "B♭ major", "B major"], keys.Select(k => k.Name));
        // A piece's own key is offered as it's written, even with seven sharps.
        Assert.Equal(new Key(7), Transposition.Keys(MeiReader.Read(Piece("7s", "c4 e4 g4 c5")))[1]);
    }

    [Fact]
    public void APieceThatModulatesIsSpelledSoNoneOfItsKeysGoesPastSeven()
    {
        // C major, then G major: in F♯ major its G major would be C♯ major (7 sharps), in G♭ major it's D♭ major (5 flats).
        var score = MeiReader.Read(Piece("0", "c4 e4 g4 c5", "key=1s g4 b4 d5 f4"));

        Assert.Equal(new Key(-6), Transposition.Keys(score)[6]);
    }

    [Fact]
    public void EveryWayAKeySignatureIsWrittenMoves()
    {
        // MEI 4's key.sig on the score, MEI 5's keysig (as the Open Hymnal's hymns have it), and a key change with its tonic named.
        var mei = Piece("0", "f4 c4", "key=1s f4 c4")
            .Replace("<scoreDef><staffGrp>", """<scoreDef keysig="1s"><staffGrp>""").Replace("""<keySig sig="0"/>""", "")
            .Replace("""<scoreDef><keySig sig="1s"/></scoreDef>""", """<scoreDef key.sig="2s"/><scoreDef><keySig sig="1s" pname="e" mode="minor"/></scoreDef>""");

        var moved = XDocument.Parse(MeiTranspose.Apply(mei, new Transposition(1, 2)));
        var defs = moved.Descendants(Mei + "scoreDef").ToList();

        Assert.Equal(("3s", "4s"), (At(defs[0], "keysig"), At(defs[1], "key.sig")));
        Assert.Equal(("3s", "f", "minor"), (At(defs[2].Element(Mei + "keySig")!, "sig"), At(defs[2].Element(Mei + "keySig")!, "pname"), At(defs[2].Element(Mei + "keySig")!, "mode")));
        // The F the score's G major gave a sharp: a G sharp, which A major gives too.
        Assert.Equal(("g", "s"), (At(Notes(moved.ToString())["n1"], "pname"), At(Notes(moved.ToString())["n1"], "accid.ges")));
    }

    [Fact]
    public void AKeyThatWouldPassSevenSharpsIsWrittenInFlats()
    {
        // C major, then E major; moved up to E major its E major would be G♯ major (8 sharps): it's A♭ major, and its notes with it.
        var mei = Piece("0", "c4 e4 g4 b4", "key=4s e4 g4 b4 d5");
        var by = Transposition.To(MeiReader.Read(mei), new Key(4));
        var read = MeiReader.Read(MeiTranspose.Apply(mei, by));

        Assert.Equal(new Transposition(2, 4), by);
        Assert.Equal(new Key(4), read.Key);
        Assert.Equal(new Key(-4), read.Bars[1].NewKey);
        Assert.Equal(["E4", "G♯4", "B4", "D♯5", "A♭4", "C5", "E♭5", "G5"], Enumerable.Range(1, 8).Select(i => Pitch(read, $"n{i}")));
        Assert.Equal([64, 68, 71, 75, 68, 72, 75, 79], read.Expected().Select(n => n.Midi));
    }

    [Fact]
    public void AKeysStepsAndSignatureGoRoundTogether()
    {
        var up = new Transposition(2, 4);

        Assert.Equal(4, up.Fifths);
        Assert.Equal(new Key(4), up.Of(new Key(0)));
        Assert.Equal((new Key(-4), 3), (up.Of(new Key(4)), up.StepsIn(new Key(4))));
        // Up a semitone from D major is E♭ major, not D♯ (9 sharps): a letter up. From C♯ minor, D minor.
        var semitone = new Transposition(0, 1);
        Assert.Equal((new Key(-3), 1), (semitone.Of(new Key(2)), semitone.StepsIn(new Key(2))));
        Assert.Equal((new Key(-1, Minor: true), 1), (semitone.Of(new Key(4, Minor: true)), semitone.StepsIn(new Key(4, Minor: true))));
        Assert.Equal((new Key(1, Minor: true), -1), (new Transposition(-1, -1).Of(new Key(-4, Minor: true)), new Transposition(-1, -1).StepsIn(new Key(-4, Minor: true))));
        Assert.Equal(-5, new Transposition(1, 1).Fifths);
        Assert.Equal(6, new Transposition(-4, -6).Fifths);
    }

    [Fact]
    public void TheNearerWayUnlessANoteWouldLeaveThePiano()
    {
        var middle = MeiReader.Read(Piece("0", "c4 e4"));
        Assert.Equal(new Transposition(1, 2), Transposition.To(middle, new Key(2)));
        Assert.Equal(new Transposition(-1, -2), Transposition.To(middle, new Key(-2)));
        Assert.Equal(new Transposition(-3, -5), Transposition.To(middle, new Key(1)));
        // Down to B♭ is nearer, but the A0 would be off the piano; up to D is nearer, but the C8 would.
        Assert.Equal(new Transposition(6, 10), Transposition.To(MeiReader.Read(Piece("0", "a0 c4")), new Key(-2)));
        Assert.Equal(new Transposition(-6, -10), Transposition.To(MeiReader.Read(Piece("0", "c8 c4")), new Key(2)));
        // A tritone is as near either way: toward middle C.
        Assert.Equal(new Transposition(-4, -6), Transposition.To(middle, new Key(6)));
        Assert.Equal(new Transposition(3, 6), Transposition.To(MeiReader.Read(Piece("0", "c3 e3")), new Key(6)));
    }

    [Fact]
    public void AMinorPieceMovesBetweenMinorKeys()
    {
        // C minor with its leading note, a B natural: in D♯ minor that would be a C double sharp, so it's E♭ minor.
        var written = MeiReader.Read(Piece("3f", "c4 eb4 g4 bn4")) with { Key = new Key(-3, Minor: true) };
        var keys = Transposition.Keys(written);

        Assert.Equal(["C minor", "C♯ minor", "D minor", "E♭ minor", "E minor", "F minor", "F♯ minor", "G minor", "G♯ minor", "A minor", "B♭ minor", "B minor"], keys.Select(k => k.Name));
        var by = Transposition.To(written, new Key(0, Minor: true));
        Assert.Equal(new Transposition(-2, -3), by);
        var read = MeiReader.Read(MeiTranspose.Apply(Piece("3f", "c4 eb4 g4 bn4"), by));
        Assert.Equal(["A3", "C4", "E4", "G♯4"], Enumerable.Range(1, 4).Select(i => Pitch(read, $"n{i}")));
    }

    [Fact]
    public void ANoteUnderAn8vaMovesWithItsPrintedNote()
    {
        // B5 printed under the line sounds B6: up a major second it's C♯6 printed, sounding C♯7, the line where it was.
        var mei = Piece("0", "b5' c4");
        var moved = MeiTranspose.Apply(mei, new Transposition(1, 2));
        var note = Notes(moved)["n1"];
        var tone = MeiReader.Read(moved).Bars[0].Voices[0].Events.OfType<Notes>().First().Tones[0];

        Assert.Equal(("c", "6", "7", "s"), (At(note, "pname"), At(note, "oct"), At(note, "oct.ges"), At(note, "accid.ges")));
        Assert.Equal(("C♯7", 1), (tone.Pitch.ToString(), tone.Ottava));
    }

    [Fact]
    public void ATiedNoteKeepsItsAccidentalFromTheNoteBeforeAndACourtesyOneStays()
    {
        // C major: an F♯ tied over the bar line, then a reminder natural on the F after it. Down to B♭ major: an E natural tied
        // over (printed once, before the bar line), and a reminder flat on the E♭ after it.
        var mei = Piece("0", "c4 d4 e4 f#4~", "~f(#)4 fn4 g4 a4");
        var moved = MeiTranspose.Apply(mei, Transposition.To(MeiReader.Read(mei), new Key(-2)));
        var notes = Notes(moved);
        var read = MeiReader.Read(moved);

        Assert.Equal("n", Printed(notes["n4"]));
        Assert.Equal(("e", "", "n"), (At(notes["n5"], "pname"), Printed(notes["n5"]), At(notes["n5"], "accid.ges")));
        Assert.Equal("f", Printed(notes["n6"]));
        Assert.Equal(["B♭3", "C4", "D4", "E4", "E4", "E♭4", "F4", "G4"], Enumerable.Range(1, 8).Select(i => Pitch(read, $"n{i}")));
    }

    [Fact]
    public void ANoteThatWouldTakeADoubleSharpItDidntHaveIsSpelledAsTheKeySpellsIt()
    {
        // C major's G♯, the G♯ after it in the bar, then an A, down to B major: not F double sharps but G naturals, the first one
        // printed and the second taking it from the first; then the A is B major's own G♯, which after them needs its sharp printed.
        var mei = Piece("0", "g#4 g(#)4 a4 c4");
        var moved = MeiTranspose.Apply(mei, Transposition.To(MeiReader.Read(mei), new Key(5)));
        var notes = Notes(moved);

        Assert.Equal(["g", "g", "g", "b"], Enumerable.Range(1, 4).Select(i => At(notes[$"n{i}"], "pname")));
        Assert.Equal(["n", "", "s", ""], Enumerable.Range(1, 4).Select(i => Printed(notes[$"n{i}"])));
        Assert.Equal(["G4", "G4", "G♯4", "B3"], Enumerable.Range(1, 4).Select(i => Pitch(MeiReader.Read(moved), $"n{i}")));
        // A double sharp printed in the music stays one where it can: B major's F double sharp is C double sharp in F♯ major.
        var doubled = Piece("5s", "fx4 g4");
        Assert.Equal("C𝄪4", Pitch(MeiReader.Read(MeiTranspose.Apply(doubled, Transposition.To(MeiReader.Read(doubled), new Key(6)))), "n1"));
    }

    [Fact]
    public void ANoteThatWouldNeedATripleSharpIsSpelledAsTheKeySpellsIt()
    {
        // An F double sharp in C major, up to C♯ major: not F triple sharp but G♯, its accidental still printed.
        var mei = Piece("0", "fx4 c4");
        var moved = MeiTranspose.Apply(mei, Transposition.To(MeiReader.Read(mei), new Key(7)));

        Assert.Equal(("g", "s"), (At(Notes(moved)["n1"], "pname"), Printed(Notes(moved)["n1"])));
        Assert.Equal(68, MeiReader.Read(moved).Expected().First(n => n.Id == "n1").Midi);
    }

    [Fact]
    public void ChordSymbolsMoveWithTheMusic()
    {
        var mei = Piece("0", "c4 e4 g4 c5").Replace("</measure>", """
            <harm staff="1" tstamp="1">B♭7/D</harm><harm staff="1" tstamp="2">F#m</harm><harm staff="1" tstamp="3">A<rend>m7</rend>/G</harm><harm staff="1" tstamp="4">N.C.</harm>
            </measure>
            """);

        var moved = XDocument.Parse(MeiTranspose.Apply(mei, new Transposition(1, 2)));

        Assert.Equal(["C7/E", "G#m", "Bm7/A", "N.C."], moved.Descendants(Mei + "harm").Select(h => h.Value.Trim()));
        Assert.Equal(["E♭", "A♭7"], XDocument.Parse(MeiTranspose.Apply(Piece("0", "c4").Replace("</measure>", """<harm staff="1" tstamp="1">C</harm><harm staff="1" tstamp="2">F7</harm></measure>"""), new Transposition(2, 3)))
            .Descendants(Mei + "harm").Select(h => h.Value));
    }

    [Fact]
    public void AnOrnamentsAccidentalMovesAsItsNeighbourNoteWould()
    {
        // A trill on A4 to a B flat, in C major; down to G major: a trill on E4 to an F natural, which G major's sharp makes printed.
        var mei = Piece("0", "a4 c4").Replace("</measure>", """<trill startid="#n1" accidupper="f"/><mordent startid="#n2" accidlower="s"/></measure>""");

        var moved = XDocument.Parse(MeiTranspose.Apply(mei, Transposition.To(MeiReader.Read(mei), new Key(1))));

        Assert.Equal("n", At(moved.Descendants(Mei + "trill").Single(), "accidupper"));
        // And a mordent on C4 to the B sharp under it: on G3, to an F double sharp.
        Assert.Equal("x", At(moved.Descendants(Mei + "mordent").Single(), "accidlower"));
        // A trill on G♯4 to an A natural, down to B major, where its G♯ is written G natural: a trill to an A flat, a semitone still.
        var respelled = Piece("0", "g#4 c4").Replace("</measure>", """<trill startid="#n1" accidupper="n"/></measure>""");
        Assert.Equal("f", At(XDocument.Parse(MeiTranspose.Apply(respelled, Transposition.To(MeiReader.Read(respelled), new Key(5)))).Descendants(Mei + "trill").Single(), "accidupper"));
    }

    [Fact]
    public void ARestMovesBesideAnotherVoiceAndStaysMidStaffOnItsOwn()
    {
        var mei = Piece("0", "c4 e4 g4 c5").Replace("<layer n=\"1\"><note xml:id=\"n1\"", """<layer n="1"><rest xml:id="r1" dur="4" ploc="b" oloc="4"/><rest xml:id="r2" dur="4" loc="4"/><rest xml:id="r3" dur="2"/></layer><layer n="2"><note xml:id="n1" """)
            .Replace("<mRest/>", """<rest xml:id="r4" dur="1" ploc="d" oloc="3"/>""");

        var moved = XDocument.Parse(MeiTranspose.Apply(mei, new Transposition(1, 2)));
        var rests = moved.Descendants(Mei + "rest").ToDictionary(r => (string)r.Attribute(Xml + "id")!);

        Assert.Equal(("c", "5"), (At(rests["r1"], "ploc"), At(rests["r1"], "oloc")));
        Assert.Equal("5", At(rests["r2"], "loc"));
        Assert.Equal(("d", "3"), (At(rests["r4"], "ploc"), At(rests["r4"], "oloc")));
    }

    [Fact]
    public void FingeringCanBeLeftOut()
    {
        var mei = Piece("0", "c4 e4").Replace("</measure>", """<fing staff="1" startid="#n1">1</fing><fing staff="1" startid="#n2">3</fing></measure>""");

        var without = MeiTranspose.WithoutFingering(mei);

        Assert.Empty(XDocument.Parse(without).Descendants(Mei + "fing"));
        Assert.All(MeiReader.Read(without).Expected(), n => Assert.Null(n.Finger));
        Assert.Equal(1, MeiReader.Read(mei).Expected().First().Finger);
    }
}
