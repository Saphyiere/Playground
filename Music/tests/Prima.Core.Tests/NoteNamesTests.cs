using Prima.Core.Music;

namespace Prima.Core.Tests;

/// <summary>
/// A piece's notes as a reader meets them, for their names over the music: the key each plays, the clef it's in, its ledger
/// lines, and what makes it hard (far off the staff, an octave line, the signature's sharps and flats, one printed earlier in
/// the bar or held over by a tie).
/// </summary>
public class NoteNamesTests
{
    private static string Mei(string scoreDef, string staffDefs, string measures) => $"""
        <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score><scoreDef {scoreDef} meter.count="4" meter.unit="4"><staffGrp>{staffDefs}</staffGrp></scoreDef>
        <section>{measures}</section></score></mdiv></body></music></mei>
        """;

    private const string Piano = """<staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/>""";

    private static Dictionary<string, NamedNote> Named(string mei) => NoteNames.Of(MeiReader.Read(mei)).ToDictionary(n => n.Id);

    [Theory]
    [InlineData(Step.C, 4, Clef.Treble, 1)]
    [InlineData(Step.C, 4, Clef.Bass, 1)]
    [InlineData(Step.D, 4, Clef.Treble, 0)]
    [InlineData(Step.G, 5, Clef.Treble, 0)]
    [InlineData(Step.A, 5, Clef.Treble, 1)]
    [InlineData(Step.C, 6, Clef.Treble, 2)]
    [InlineData(Step.E, 2, Clef.Bass, 1)]
    [InlineData(Step.C, 2, Clef.Bass, 2)]
    [InlineData(Step.D, 2, Clef.Bass, 1)]
    [InlineData(Step.C, 1, Clef.Bass, 5)]
    public void LedgerLinesAreCountedFromTheStaffItsPrintedOn(Step step, int octave, Clef clef, int ledgers) =>
        Assert.Equal(ledgers, NoteNames.Ledgers(new Spelled(step, 0, octave), clef));

    [Fact]
    public void ASharpOrFlatSaysWhereItComesFrom()
    {
        // D major: F is the signature's sharp; a C natural printed, then another C on its line (carried), and one an octave up
        // (the signature's again). Then a C sharp in the next bar, written, tied over to the bar after, where it's printed bare.
        var named = Named(Mei("""key.sig="2s" """, Piano, """
            <measure n="1"><staff n="1"><layer n="1"><note xml:id="f" pname="f" oct="4" dur="4"/><note xml:id="c1" pname="c" oct="5" dur="4" accid="n"/>
            <note xml:id="c2" pname="c" oct="5" dur="4"/><note xml:id="c3" pname="c" oct="6" dur="4"/></layer></staff>
            <staff n="2"><layer n="1"><mRest/></layer></staff></measure>
            <measure n="2"><staff n="1"><layer n="1"><note xml:id="g" pname="g" oct="4" dur="2"/><note xml:id="tie1" pname="c" oct="5" dur="2" accid="s" tie="i"/></layer></staff>
            <staff n="2"><layer n="1"><mRest/></layer></staff></measure>
            <measure n="3"><staff n="1"><layer n="1"><note xml:id="tie2" pname="c" oct="5" dur="2" accid.ges="s" tie="t"/><note xml:id="tie3" pname="c" oct="5" dur="2" accid.ges="s"/></layer></staff>
            <staff n="2"><layer n="1"><mRest/></layer></staff></measure>
            """));

        Assert.Equal((Hard.Signature, "F♯"), (named["f"].Hard, named["f"].Letter));
        Assert.Equal((Hard.Accidental, "C"), (named["c1"].Hard, named["c1"].Letter));
        Assert.Equal((Hard.Carried, "C"), (named["c2"].Hard, named["c2"].Letter));
        Assert.Equal((Hard.Signature | Hard.LedgerLines, "C♯", 2), (named["c3"].Hard, named["c3"].Letter, named["c3"].Ledgers));
        Assert.Equal(Hard.None, named["g"].Hard);
        // The tie's end isn't struck, so it isn't named; the next C sharp, in a bar that prints none, is the signature's.
        Assert.False(named.ContainsKey("tie2"));
        Assert.Equal(Hard.Signature, named["tie3"].Hard);
    }

    [Fact]
    public void AnAccidentalHeldOverByATieWithNothingPrinted()
    {
        // In C major, a G sharp tied into the next bar, nothing printed there: the file keeps the sharp, and the tie's end isn't
        // struck again, so only the first is named.
        var mei = Mei("""key.sig="0" """, Piano, """
            <measure n="1"><staff n="1"><layer n="1"><note xml:id="a" pname="g" oct="4" dur="1" accid="s" tie="i"/></layer></staff><staff n="2"><layer n="1"><mRest/></layer></staff></measure>
            <measure n="2"><staff n="1"><layer n="1"><note xml:id="b" pname="g" oct="4" dur="2" accid.ges="s" tie="t"/><note xml:id="c" pname="b" oct="4" dur="2"/></layer></staff><staff n="2"><layer n="1"><mRest/></layer></staff></measure>
            """);
        var named = Named(mei);

        Assert.Equal(Spelling.Tied, MeiReader.Read(mei).Bars[1].Voices[0].Events.OfType<Notes>().First().Tones[0].Spelling);
        Assert.Equal(Hard.Accidental, named["a"].Hard);
        Assert.False(named.ContainsKey("b"));
    }

    [Fact]
    public void UnderAnOctaveLineItsNamedByTheKeyItPlays()
    {
        // Printed C6 under an 8va: it plays C7, on two ledger lines as printed.
        var named = Named(Mei("""key.sig="0" """, Piano, """
            <measure n="1"><staff n="1"><layer n="1"><note xml:id="a" pname="c" oct="6" oct.ges="7" dur="1"/></layer></staff><staff n="2"><layer n="1"><mRest/></layer></staff>
            <octave staff="1" startid="#a" endid="#a" dis="8" dis.place="above"/></measure>
            """));

        Assert.Equal((new Spelled(Step.C, 0, 7), 2, Hard.OctaveLine | Hard.LedgerLines), (named["a"].Plays, named["a"].Ledgers, named["a"].Hard));
    }

    [Fact]
    public void TheHardOnesAndTheirNames()
    {
        static NamedNote Note(Spelled plays, int ledgers, Hard hard) => new("n", 0, 1, plays, Clef.Treble, ledgers, hard);
        var middleC = Note(new Spelled(Step.C, 0, 4), 1, Hard.LedgerLines);
        var high = Note(new Spelled(Step.C, 0, 7), 2, Hard.OctaveLine | Hard.LedgerLines);
        var fSharp = Note(new Spelled(Step.F, 1, 5), 0, Hard.Signature);
        var written = Note(new Spelled(Step.B, -1, 4), 0, Hard.Accidental);
        var carried = Note(new Spelled(Step.B, -1, 4), 0, Hard.Carried);
        var doubled = Note(new Spelled(Step.F, 2, 5), 0, Hard.Accidental);

        // Middle C's one ledger line isn't hard; two, or an 8va, is, and says its octave.
        Assert.Equal((false, "C"), (NoteNames.IsHard(middleC, 0), NoteNames.Label(middleC)));
        Assert.Equal((true, "C7"), (NoteNames.IsHard(high, 0), NoteNames.Label(high)));
        // The signature's sharp is hard with three or more; a sharp printed beside the note isn't, one carried through the bar is.
        Assert.Equal((false, true, "F♯"), (NoteNames.IsHard(fSharp, 2), NoteNames.IsHard(fSharp, 4), NoteNames.Label(fSharp)));
        Assert.Equal((false, true, true), (NoteNames.IsHard(written, 0), NoteNames.IsHard(carried, 0), NoteNames.IsHard(doubled, 0)));
        Assert.Equal("F𝄪", NoteNames.Label(doubled));
    }

    [Fact]
    public void AStaffsOwnClefCountsFromTheStartAndAfterAChange()
    {
        // The left hand starts in the treble clef: its C5 is in the staff. A bass clef in its first layer mid-bar, and the notes
        // after it in its second layer are read in the bass clef too, into the next bar.
        var named = Named(Mei("""key.sig="0" """, """<staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="G" clef.line="2"/>""", """
            <measure n="1"><staff n="1"><layer n="1"><mRest/></layer></staff>
            <staff n="2"><layer n="1"><note xml:id="a" pname="c" oct="5" dur="2"/><clef shape="F" line="4"/><note xml:id="b" pname="c" oct="3" dur="2"/></layer>
            <layer n="2"><note xml:id="c" pname="e" oct="4" dur="2"/><note xml:id="d" pname="c" oct="4" dur="2"/></layer></staff></measure>
            <measure n="2"><staff n="1"><layer n="1"><mRest/></layer></staff><staff n="2"><layer n="1"><note xml:id="e" pname="c" oct="2" dur="1"/></layer></staff></measure>
            """));

        Assert.Equal((Clef.Treble, 0), (named["a"].Clef, named["a"].Ledgers));
        Assert.Equal((Clef.Treble, 0), (named["c"].Clef, named["c"].Ledgers));
        Assert.Equal((Clef.Bass, 1), (named["d"].Clef, named["d"].Ledgers));
        Assert.Equal((Clef.Bass, 2), (named["e"].Clef, named["e"].Ledgers));
    }
}
