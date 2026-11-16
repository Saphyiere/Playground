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
}
