using Prima.Core.Library;
using Prima.Core.Music;

namespace Prima.Core.Tests;

public class BrowsingTests
{
    [Theory]
    [InlineData("Johann Sebastian Bach", "Baroque")]
    [InlineData("Bach and others", "Baroque")]
    [InlineData("Carl Philipp Emanuel Bach", "Classical")]
    [InlineData("Ludwig van Beethoven", "Classical")]
    [InlineData("Frédéric Chopin", "Romantic")]
    [InlineData("Camille Saint-Saëns", "Romantic")]
    [InlineData("Claude Debussy", "1890–1930")]
    [InlineData("Scott Joplin", "Ragtime")]
    [InlineData("Carl Czerny", "Teaching pieces")]
    [InlineData("Ramana Kumar", "Other")]
    [InlineData("", "Other")]
    public void AComposerHasAPeriod(string composer, string period) => Assert.Equal(period, Shelves.Period(composer));

    [Theory]
    [InlineData("Piano Sonata No. 11 in A major, K. 331: III. Alla turca", "Piano sonatas", "Sonatas")]
    [InlineData("Allemande", "English Suites", "Dances")]
    [InlineData("Prelude No. 1 in C major, BWV 846", "The Well-Tempered Clavier, Book I", "Preludes and fugues")]
    [InlineData("Étude in C major, Op. 10 No. 1", "Études", "Études")]
    [InlineData("Etüde f-moll, No. 9", "Op. 10", "Études")]
    [InlineData("Aus meines Herzens Grunde, BWV 269", "Chorales", "Chorales and hymns")]
    [InlineData("Träumerei, No. 7", "Kinderszenen Op. 15", "Character pieces")]
    [InlineData("Maple Leaf Rag", "Rags, marches and waltzes", "Dances")]
    [InlineData("33 Variations on a Waltz by Diabelli, Op. 120", null, "Variations")]
    public void APieceHasAKind(string title, string? work, string kind) => Assert.Equal(kind, Shelves.Kind(title, work));

    [Theory]
    [InlineData("Ludwig van Beethoven", "Beethoven")]
    [InlineData("Johann Strauss II", "Strauss")]
    [InlineData("Bach and others", "Bach")]
    [InlineData("Traditional", "Traditional")]
    public void ComposersAreListedBySurname(string composer, string surname) => Assert.Equal(surname, Shelves.Surname(composer));

    [Theory]
    [InlineData("Ludwig van Beethoven", "Beethoven, Ludwig van")]
    [InlineData("Ralph Vaughan Williams", "Vaughan Williams, Ralph")]
    [InlineData("Johann Strauss II", "Johann Strauss II")]
    [InlineData("Bach, Johann Sebastian", "Bach, Johann Sebastian")]
    [InlineData("Bach and others", "Bach and others")]
    [InlineData("Traditional", "Traditional")]
    [InlineData("", "")]
    public void AnIndexNamesAComposerSurnameFirst(string composer, string listed) => Assert.Equal(listed, Shelves.Listed(composer));

    // The owner, 17 November: "beethoven sonata 14" and "moonlight first movement" found nothing, the search taking only the
    // whole phrase. Each word counts by itself, in any order; a number is a whole number or a movement's, and so is an ordinal.
    // The pieces' words as the Library searches them: title, composer, work.
    private const string MoonlightI = "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: I. Adagio sostenuto Ludwig van Beethoven Piano sonatas";
    private const string MoonlightII = "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: II. Allegretto Ludwig van Beethoven Piano sonatas";
    private const string MoonlightIII = "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: III. Presto agitato Ludwig van Beethoven Piano sonatas";
    private const string Arranged14 = "Sonata No. 14 Moonlight Ludwig van Beethoven ";
    private const string ArrangedFirst = "Moonlight Sonata 1st movement Ludwig van Beethoven ";
    private const string Op14 = "Sonata No. 9, Op. 14/1: I. Allegro Ludwig van Beethoven Piano sonatas";
    private const string Op140 = "Grand Duo in C major, D. 812 (Op. 140) Franz Schubert ";
    private const string Haydn = "Sonata No. 45 in A major, Hob. XVI:30: III. Tema con variazioni: Tempo di Menuetto Joseph Haydn Piano sonatas";

    [Theory]
    [InlineData("beethoven sonata 14", Arranged14, true)]
    [InlineData("sonata beethoven 14", Arranged14, true)]
    [InlineData("beethoven sonata 14", Op14, true)]
    [InlineData("beethoven sonata 14", MoonlightI, false)]
    [InlineData("14", Op140, false)]
    [InlineData("moonlight first movement", MoonlightI, true)]
    [InlineData("moonlight first movement", ArrangedFirst, true)]
    [InlineData("moonlight first movement", MoonlightII, false)]
    [InlineData("Moonlight 1st mvt.", MoonlightI, true)]
    [InlineData("moonlight 1", MoonlightI, true)]
    [InlineData("moonlight 1", Arranged14, false)]
    [InlineData("moonlight third", MoonlightIII, true)]
    [InlineData("moonlight 3", MoonlightIII, true)]
    [InlineData("moonlight 3", MoonlightII, false)]
    [InlineData("haydn 16", Haydn, false)]
    [InlineData("haydn 30 iii", Haydn, true)]
    [InlineData("movement", ArrangedFirst, true)]
    [InlineData("movement", MoonlightI, false)]
    [InlineData("op. 27/2", MoonlightII, true)]
    [InlineData("beeth", MoonlightI, true)]
    [InlineData("  ", MoonlightI, true)]
    public void ASearchTakesEachWordByItselfANumberWholeOrAMovementsAndAnOrdinalAMovements(string typed, string words, bool found) =>
        Assert.Equal(found, Shelves.Finds(words, typed));

    [Theory]
    [InlineData("Dvorak humoresque", "Humoresque, Op. 101 No. 7 Antonín Dvořák Humoresques")]
    [InlineData("SAINT-SAENS", "Le Cygne Camille Saint-Saëns ")]
    public void ASearchStillFoldsAccentsAndCase(string typed, string words) => Assert.True(Shelves.Finds(words, typed));

    [Fact]
    public void AnOpeningIsTheFirstHalfMinuteAtTheTempoItStartsAt()
    {
        // Twenty bars of crotchets at 120 a minute: half a second each, so the first half minute is 60 of them.
        var bars = Enumerable.Range(1, 20).Select(b => new Bar(b, [new Voice(1, [.. Enumerable.Range(0, 4).Select(q =>
            new Notes($"n{b}-{q}", q * Ticks.Quarter, Ticks.Quarter, [new Tone($"t{b}-{q}", new Spelled(Step.C, 0, 4))]))])])
        { Tempos = b == 1 ? [(0, 120.0)] : [] }).ToList();

        var opening = Opening.Of(new Score(new Key(0), new Meter(4, 4), bars));

        Assert.Equal(60, opening.Count);
        Assert.Equal(0, opening[0].Start, 3);
        Assert.Equal(0.5, opening[1].Start, 3);
        Assert.Equal(29.5, opening[^1].Start, 3);
        Assert.All(opening, n => Assert.Equal(60, n.Midi));
    }

    [Fact]
    public void AnOpeningOfAMidiFileStartsAtItsFirstNote()
    {
        var midi = new MidiFile([new FileNote(64, 2, 3, 90), new FileNote(60, 40, 41, 90)], 120, 4, 4, 41, 21);

        var opening = Opening.Of(midi);

        Assert.Equal([new FileNote(64, 0, 1, 90)], opening);
    }

    [Fact]
    public void AnOpeningAsMidiReadsBackAsTheSameNotes()
    {
        FileNote[] notes = [new(60, 0, 0.5, 72), new(64, 0, 0.5, 72), new(67, 0.5, 1.25, 80), new(60, 0.5, 2, 64)];

        var read = MidiFile.Read(Opening.ToMidi(notes));

        Assert.Equal(notes.Length, read.Notes.Count);
        foreach (var (wrote, back) in notes.OrderBy(n => n.Start).ThenBy(n => n.Midi).Zip(read.Notes.OrderBy(n => n.Start).ThenBy(n => n.Midi)))
        {
            Assert.Equal(wrote.Midi, back.Midi);
            Assert.Equal(wrote.Start, back.Start, 2);
            Assert.Equal(wrote.End, back.End, 2);
            Assert.Equal(wrote.Velocity, back.Velocity);
        }
    }

    // The owner's rules for the shelves and openings, decided 15 November (the review of main at a7ab94d, part 5).

    // 22. The known double surnames are read whole (Vaughan Williams, Mendelssohn Bartholdy at least), and a name written
    // "Surname, First" or ending "Jr." is read right.
    [Theory]
    [InlineData("Ralph Vaughan Williams", "Vaughan Williams")]
    [InlineData("Felix Mendelssohn Bartholdy", "Mendelssohn Bartholdy")]
    [InlineData("Bach, Johann Sebastian", "Bach")]
    [InlineData("Vaughan Williams, Ralph", "Vaughan Williams")]
    [InlineData("John Philip Sousa Jr.", "Sousa")]
    [InlineData("John Philip Sousa, Jr.", "Sousa")]
    [InlineData("Strauss II, Johann", "Strauss")]
    public void KnownDoubleSurnamesAreReadWholeAndANameWrittenSurnameFirstOrEndingJrIsReadRight(string composer, string surname) =>
        Assert.Equal(surname, Shelves.Surname(composer));

    // 23. Periods, by name: Hans Leo Hassler with the earliest shelf there is, the hymn composer William G. Fischer in Romantic,
    // Johann Christoph Friedrich Bach in Classical, Richard Strauss in the shelf after Romantic if there is one.
    [Fact]
    public void HasslerFischerJCFBachAndRichardStraussAreShelvedByTheirOwnNames()
    {
        Assert.Equal(Shelves.Periods[0], Shelves.Period("Hans Leo Hassler"));
        Assert.Equal("Romantic", Shelves.Period("William G. Fischer"));
        Assert.Equal("Romantic", Shelves.Period("William Gustavus Fischer"));
        Assert.Equal("Classical", Shelves.Period("Johann Christoph Friedrich Bach"));
        Assert.Equal(Shelves.Periods[Shelves.Periods.ToList().IndexOf("Romantic") + 1], Shelves.Period("Richard Strauss"));
        // Their namesakes keep theirs.
        Assert.Equal("Classical", Shelves.Period("Johann Wilhelm Hässler"));
        Assert.Equal("Baroque", Shelves.Period("Johann Caspar Ferdinand Fischer"));
        Assert.Equal("Romantic", Shelves.Period("Johann Strauss II"));
        Assert.Equal("Baroque", Shelves.Period("Johann Sebastian Bach"));
    }

    // 24. Fantasies, capriccios and toccatas come out of "Preludes and fugues" into the kind that suits each; "Cake-walk" is read
    // as a cakewalk, and "Doctor Gradus ad Parnassum" isn't an étude.
    [Theory]
    [InlineData("Fantasia in C minor, K. 475", "Piano pieces", "Character pieces")]
    [InlineData("Rondo a Capriccio", "Op. 129", "Character pieces")]
    [InlineData("Toccata in C major, Op. 7", "Piano pieces", "Character pieces")]
    [InlineData("Bagatelle, Op. 6 No. 7 (Allegretto molto capriccioso)", "14 Bagatelles, Op. 6", "Character pieces")]
    [InlineData("Polonaise-Fantasie (F. Op.61)", null, "Dances")]
    [InlineData("Fantasia and Fugue in C minor, Wq. 119/7", "Keyboard works", "Preludes and fugues")]
    [InlineData("Toccata & Fugue in D minor BWV 565", null, "Preludes and fugues")]
    [InlineData("Golliwogg's Cake-walk", "Children's Corner", "Dances")]
    [InlineData("Doctor Gradus ad Parnassum", "Children's Corner", "Character pieces")]
    [InlineData("Gradus ad Parnassum, No. 1", null, "Études")]
    public void FantasiesCapricciosAndToccatasGoWhereTheySuitACakeWalkIsADanceAndDoctorGradusIsntAnEtude(string title, string? work, string kind) =>
        Assert.Equal(kind, Shelves.Kind(title, work));

    // 25. An opening from notation starts at its first note, as one from a MIDI file does: the rests a piece starts with (a drum
    // intro, now unseen, among them) aren't played through.
    [Fact]
    public void AnOpeningFromNotationStartsAtItsFirstNoteNotThroughTheRestsAndTheDrumIntroBeforeIt()
    {
        // At 120: a bar where only the drums play (the piano's hands rest), then the piano comes in after a crotchet's rest.
        var mei = """
            <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score>
            <scoreDef meter.count="4" meter.unit="4" key.sig="0"><staffGrp>
            <staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp>
            <staffDef n="3" lines="5" clef.shape="perc"/>
            </staffGrp></scoreDef>
            <section>
            <measure n="1">
            <staff n="1"><layer n="1"><mRest/></layer></staff>
            <staff n="2"><layer n="1"><mRest/></layer></staff>
            <staff n="3"><layer n="1"><note loc="5" dur="4"/><note loc="5" dur="4"/><note loc="5" dur="4"/><note loc="5" dur="4"/></layer></staff>
            <tempo tstamp="1" mm="120" mm.unit="4">Allegro</tempo>
            </measure>
            <measure n="2">
            <staff n="1"><layer n="1"><rest dur="4"/><note xml:id="c" pname="c" oct="5" dur="4"/><note xml:id="d" pname="d" oct="5" dur="4"/><note xml:id="e" pname="e" oct="5" dur="4"/></layer></staff>
            <staff n="2"><layer n="1"><mRest/></layer></staff>
            <staff n="3"><layer n="1"><note loc="5" dur="1"/></layer></staff>
            </measure>
            </section></score></mdiv></body></music></mei>
            """;

        var opening = Opening.Of(MeiReader.Read(mei));

        Assert.Equal([(72, 0.0), (74, 0.5), (76, 1.0)], opening.Select(n => (n.Midi, Math.Round(n.Start, 3))));
    }

    // 26. An opening from a MIDI file leaves its drum channel out.
    [Fact]
    public void AnOpeningFromAMidiFileLeavesItsDrumChannelOut()
    {
        // A file of one track, 480 ticks a quarter at 120: a closed hi-hat on the drums' channel (the tenth) for the first beat, as
        // a count-in, then middle C on the first channel for the second, with a bass drum under it.
        byte[] file =
        [
            .. "MThd"u8, 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0,
            .. "MTrk"u8, 0, 0, 0, 30,
            0x00, 0x99, 42, 90, 0x83, 0x60, 0x89, 42, 0,
            0x00, 0x90, 60, 80, 0x00, 0x99, 36, 100, 0x83, 0x60, 0x80, 60, 0, 0x00, 0x89, 36, 0,
            0x00, 0xFF, 0x2F, 0x00,
        ];

        var read = MidiFile.Read(file);
        var opening = Opening.Of(read);

        Assert.Equal(2, read.Notes.Count(n => n.Channel == MidiFile.DrumChannel));
        Assert.Equal([new FileNote(60, 0, 0.5, 80)], opening);
    }
}
