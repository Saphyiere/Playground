using Prima.Core.Library;

namespace Prima.Core.Tests;

// The owner, 17 November: a sonata's movements were three rows among a hundred, told apart by the end of a long title, and the
// same sonata stood in three places. The catalogue lists a composer's works, each with its parts in order and the other
// versions of it under it. The titles, works and ids here are the Library's own, as its sources wrote them.
public class WorksTests
{
    private static ListedPiece Edition(string id, string title, string work, string source = "DCML") => new(id, title, work, source, false);

    private static ListedPiece Pdf(string id, string title, string work) => new(id, title, work, "Mutopia", true);

    private static ListedPiece Arranged(string id, string title, string work = "") => new(id, title, work, "PDMX", false);

    private static readonly ListedPiece[] Pathetique =
    [
        Edition("dcml-beethoven-piano-sonatas-08-3", "Sonata No. 8 'Pathetique', Op. 13: III. Rondo: Allegro", "Piano sonatas"),
        Edition("dcml-beethoven-piano-sonatas-08-1", "Sonata No. 8 'Pathetique', Op. 13: I. Grave: Allegro di molto e con brio", "Piano sonatas"),
        Edition("dcml-beethoven-piano-sonatas-08-2", "Sonata No. 8 'Pathetique', Op. 13: II. Adagio cantabile", "Piano sonatas"),
    ];

    [Fact]
    public void AnEditionsMovementsAreOneWorkInOrder()
    {
        var work = Assert.Single(Works.Of(Pathetique));

        Assert.Equal("Sonata No. 8 “Pathetique”", work.Title);
        Assert.Equal("Op. 13", work.Catalogue);
        Assert.Equal("Sonatas", work.Kind);
        Assert.Equal(["I. Grave: Allegro di molto e con brio", "II. Adagio cantabile", "III. Rondo: Allegro"], work.Parts.Select(p => p.Name));
        Assert.Equal(["dcml-beethoven-piano-sonatas-08-1", "dcml-beethoven-piano-sonatas-08-2", "dcml-beethoven-piano-sonatas-08-3"], work.Parts.Select(p => p.Id));
        Assert.Empty(work.Others);
    }

    [Fact]
    public void AnotherEditionsPdfsAndAnArrangementAreItsOtherVersions()
    {
        var work = Assert.Single(Works.Of(
        [
            Pdf("mutopia-pathetique-2", "Sonata No. 8 “Pathétique” (2nd Movement: Adagio cantabile)", "Op. 13"),
            Arranged("pdmx-twist", "Pathetique Sonata with a twist"),
            .. Pathetique,
            Pdf("mutopia-pathetique-1", "Sonata No. 8 “Pathétique” (1st Movement: Grave, Allegro molto e con brio)", "Op. 13"),
        ]));

        Assert.Equal(3, work.Parts.Count);
        // By their own titles: the notation first, then the PDFs.
        Assert.Equal(
            ["Pathetique Sonata with a twist", "Sonata No. 8 “Pathétique” (1st Movement: Grave, Allegro molto e con brio)", "Sonata No. 8 “Pathétique” (2nd Movement: Adagio cantabile)"],
            work.Others.Select(p => p.Name));
    }

    [Fact]
    public void TheMoonlightHasItsNumberAndEveryVersionOfIt()
    {
        // The edition's title leaves the number out; the piece's id has it.
        var work = Assert.Single(Works.Of(
        [
            Edition("dcml-beethoven-piano-sonatas-14-2", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: II. Allegretto", "Piano sonatas"),
            Edition("dcml-beethoven-piano-sonatas-14-1", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: I. Adagio sostenuto", "Piano sonatas"),
            Edition("dcml-beethoven-piano-sonatas-14-3", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: III. Presto agitato", "Piano sonatas"),
            Arranged("pdmx-a", "Sonata No. 14 Moonlight"),
            Arranged("pdmx-b", "Moonlight sonata"),
            Arranged("pdmx-c", "Moonlight Sonata 1st movement"),
            Arranged("pdmx-d", "Opus 27-2: Beethoven Moonlight - The first and second movements"),
            Arranged("pdmx-e", "Piano Sonata No. 14 3rd Movement"),
        ]));

        Assert.Equal("Sonata No. 14 “quasi una fantasia” “Moonlight”", work.Title);
        Assert.Equal(14, work.Number);
        Assert.Equal("Op. 27 No. 2", work.Catalogue);
        Assert.Equal(["I. Adagio sostenuto", "II. Allegretto", "III. Presto agitato"], work.Parts.Select(p => p.Name));
        Assert.Equal(5, work.Others.Count);
    }

    [Fact]
    public void TwoSonatasOfOneOpusAreTwoWorksEachWithItsOwnPdfs()
    {
        var works = Works.Of(
        [
            Edition("dcml-beethoven-piano-sonatas-05-1", "Sonata No. 5, Op. 10/1: I. Allegro molto e con brio", "Piano sonatas"),
            Edition("dcml-beethoven-piano-sonatas-05-2", "Sonata No. 5, Op. 10/1: II. Adagio molto", "Piano sonatas"),
            Edition("dcml-beethoven-piano-sonatas-06-1", "Sonata No. 6, Op. 10/2: I. Allegro", "Piano sonatas"),
            Pdf("mutopia-LVB-Sonate-10no1-1", "Sonata No. 5 (1st Movement: Allegro molto e con brio), No. 1", "Op. 10"),
            Pdf("mutopia-LVB-Sonate-10no1-3", "Sonata No. 5 (3rd Movement: Prestissimo), No. 1", "Op. 10"),
            Pdf("mutopia-LVB-Sonate-10no2-1", "Sonata No. 6 (1st Movement: Allegro), No. 2", "Op. 10"),
        ]);

        Assert.Equal(["Sonata No. 5", "Sonata No. 6"], works.Select(w => w.Title));
        Assert.Equal(["Op. 10 No. 1", "Op. 10 No. 2"], works.Select(w => w.Catalogue));
        Assert.Equal(["mutopia-LVB-Sonate-10no1-1", "mutopia-LVB-Sonate-10no1-3"], works[0].Others.Select(p => p.Id));
        Assert.Equal(["mutopia-LVB-Sonate-10no2-1"], works[1].Others.Select(p => p.Id));
    }

    [Fact]
    public void ANameCountsOnlyWithinItsKind()
    {
        // The “Pastoral” sonata isn't the Pastoral Symphony.
        var works = Works.Of(
        [
            Edition("dcml-beethoven-piano-sonatas-15-1", "Sonata No. 15 ('Pastoral'), Op. 28: I. Allegro", "Piano sonatas"),
            Edition("dcml-beethoven-piano-sonatas-15-2", "Sonata No. 15 ('Pastoral'), Op. 28: II. Andante", "Piano sonatas"),
            Arranged("pdmx-s", "Simfonia pastoral"),
            Arranged("pdmx-t", "Tema de la Simfonia Pastoral"),
        ]);

        Assert.Equal(3, works.Count);
        Assert.Empty(works.Single(w => w.Title == "Sonata No. 15 “Pastoral”").Others);
    }

    [Fact]
    public void AnEditionsWorkUnderTwoNamesIsOneWork()
    {
        var work = Assert.Single(Works.Of(
        [
            Edition("humdrum-mozart-piano-sonatas-sonata15-3", "Sonata No. 15 in C major, K. 545: III. Rondo: Allegretto", "Piano sonatas", "Humdrum"),
            Edition("humdrum-mozart-piano-sonatas-sonata15-1", "Piano Sonata No. 15 in C major, K. 545: I. Allegro", "Piano sonatas", "Humdrum"),
            Edition("humdrum-mozart-piano-sonatas-sonata15-2", "Piano Sonata No. 15 in C major, K. 545: II. Andante", "Piano sonatas", "Humdrum"),
            Pdf("mutopia-K545-1", "Sonata Facile - First movement", "K 545"),
            Pdf("mutopia-K545-2", "Sonata Facile - Second movement", "K 545"),
        ]));

        Assert.Equal("Piano Sonata No. 15 in C major", work.Title);
        Assert.Equal("K. 545", work.Catalogue);
        Assert.Equal(["I. Allegro", "II. Andante", "III. Rondo: Allegretto"], work.Parts.Select(p => p.Name));
        Assert.Equal(["Sonata Facile - First movement", "Sonata Facile - Second movement"], work.Others.Select(p => p.Name));
    }

    [Fact]
    public void ASuitesDancesAreInTheOrderTheyArePlayed()
    {
        var work = Assert.Single(Works.Of(
        [
            .. new[] { "Allemande", "Bourrée I", "Bourrée II", "Courante I", "Courante II", "Double I", "Double II", "Gigue", "Prelude", "Sarabande" }
                .Select(dance => Edition("dcml-bach-en-fr-suites-BWV806-" + dance, "English Suite No. 1, BWV 806: " + dance, "English Suites")),
            Pdf("mutopia-bach-english-suite-1-bourree-1", "English Suite I: Bourree I, BWV 806", "BWV 806"),
            Pdf("mutopia-bach-english-suite-1-bourree-2", "English Suite I: Bourree II, BWV 806", "BWV 806"),
        ]));

        Assert.Equal("English Suite No. 1", work.Title);
        Assert.Equal("BWV 806", work.Catalogue);
        Assert.Equal("Dances", work.Kind);
        Assert.Equal(["Prelude", "Allemande", "Courante I", "Courante II", "Double I", "Double II", "Sarabande", "Bourrée I", "Bourrée II", "Gigue"], work.Parts.Select(p => p.Name));
        Assert.Equal(2, work.Others.Count);
    }

    [Fact]
    public void ASetIsOneWorkAndAnotherEditionOfItsPiecesIsUnderIt()
    {
        var works = Works.Of(
        [
            Edition("asap-bach-fugue-bwv-856", "Fugue No. 11 in F major, BWV 856", "The Well-Tempered Clavier, Book I", "ASAP"),
            Edition("asap-bach-fugue-bwv-846", "Fugue No. 1 in C major, BWV 846", "The Well-Tempered Clavier, Book I", "ASAP"),
            Edition("asap-bach-prelude-bwv-846", "Prelude No. 1 in C major, BWV 846", "The Well-Tempered Clavier, Book I", "ASAP"),
            Edition("asap-bach-fugue-bwv-857", "Fugue No. 12 in F minor, BWV 857", "The Well-Tempered Clavier, Book I", "ASAP"),
            Pdf("mutopia-wtk1-fugue1", "Das Wohltemperierte Clavier I, Fuga I, BWV 846", "BWV 846"),
            Pdf("mutopia-wtk1-prelude1", "Das Wohltemperierte Clavier I, Praeludium I, BWV 846", "BWV 846"),
            Pdf("mutopia-fuga2", "Das Wohltemperierte Clavier I, Fuga II, BWV 847", "BWV 847"),
            Pdf("mutopia-BWV856-Fugue11", "Das Wohltemperierte Clavier I, Fuga XI, BWV 856", "BWV 856"),
            Arranged("pdmx-847", "Prelude No. 2 BWV 847 in C Minor"),
            // Which book's first prelude? Each has one, so it stays by itself.
            Arranged("pdmx-c", "Prelude No.1 in C Major"),
        ]);

        Assert.Equal(["Prelude No.1 in C Major", "The Well-Tempered Clavier, Book I"], works.Select(w => w.Title));
        var book = works[1];
        Assert.Equal("Preludes and fugues", book.Kind);
        Assert.Equal(["Prelude No. 1 in C major, BWV 846", "Fugue No. 1 in C major, BWV 846", "Fugue No. 11 in F major, BWV 856", "Fugue No. 12 in F minor, BWV 857"], book.Parts.Select(p => p.Name));
        Assert.Equal(["pdmx-847", "mutopia-wtk1-fugue1", "mutopia-fuga2", "mutopia-BWV856-Fugue11", "mutopia-wtk1-prelude1"], book.Others.Select(p => p.Id));
    }

    [Fact]
    public void AnOpusInThreeSourcesIsOneWork()
    {
        var work = Assert.Single(Works.Of(
        [
            Arranged("pdmx-28-02", "24 Préludes Opus 28, No. 2", "Preludes Op. 28"),
            Arranged("pdmx-28-10", "24 Préludes Opus 28, No. 10", "Preludes Op. 28"),
            Arranged("pdmx-28-01", "24 Préludes Opus 28, No. 1", "Preludes Op. 28"),
            Arranged("pdmx-e-minor", "Prelude in E minor Op.28 No.4", "Preludes Op. 28"),
            Pdf("mutopia-Chop-28-1", "Prelude: Op. 28, No. 1", "Op. 28"),
            Pdf("mutopia-Chop-28-15", "Prelude: Op. 28, No. 15", "Op. 28"),
            Pdf("mutopia-Chop-28-10", "Prélude 10", "Op. 28"),
        ]));

        Assert.Equal("24 Préludes", work.Title);
        Assert.Equal("Op. 28", work.Catalogue);
        Assert.Equal(["No. 1", "No. 2", "No. 10"], work.Parts.Select(p => p.Name));
        Assert.Equal(["Prelude in E minor Op.28 No.4", "Prélude 10", "Prelude: Op. 28, No. 1", "Prelude: Op. 28, No. 15"], work.Others.Select(p => p.Name));
    }

    [Fact]
    public void APieceOfAnOpusIsUnderTheSetThatHasIt()
    {
        var works = Works.Of(
        [
            Edition("asap-chopin-etudes-op-25-2", "Étude in F minor, Op. 25 No. 2", "Études", "ASAP"),
            Edition("asap-chopin-etudes-op-10-2", "Étude in A minor, Op. 10 No. 2", "Études", "ASAP"),
            Edition("asap-chopin-etudes-op-10-1", "Étude in C major, Op. 10 No. 1", "Études", "ASAP"),
            Pdf("mutopia-chp-10-01", "Etüde C-Dur, No. 1", "Op. 10"),
            Pdf("mutopia-PS-chopin-25-02", "Etüde f-moll, No. 2: A4 PS chopin 25 02 i", "Op. 25"),
            // The set doesn't have Op. 25 No. 3.
            Arranged("pdmx-horseman", "Étude in F major Op.25 No.3 The Horseman"),
        ]);

        Assert.Equal(["Étude in F major Op.25 No.3 The Horseman", "Études"], works.Select(w => w.Title));
        Assert.Equal(["Étude in C major, Op. 10 No. 1", "Étude in A minor, Op. 10 No. 2", "Étude in F minor, Op. 25 No. 2"], works[1].Parts.Select(p => p.Name));
        Assert.Equal(["mutopia-chp-10-01", "mutopia-PS-chopin-25-02"], works[1].Others.Select(p => p.Id));
    }

    [Fact]
    public void ANumberedPieceIsUnderTheSetOfItsKind()
    {
        var work = Assert.Single(Works.Of(
        [
            Edition("asap-chopin-ballades-1", "Ballade No. 1 in G minor, Op. 23", "Ballades", "ASAP"),
            Edition("asap-chopin-ballades-2", "Ballade No. 2 in F major, Op. 38", "Ballades", "ASAP"),
            Arranged("pdmx-ballade", "Ballade No. 1 in G Minor"),
            Pdf("mutopia-chopin-op23-ballade-1", "Première Ballade", "Op. 23"),
        ]));

        Assert.Equal("Ballades", work.Title);
        Assert.Equal(2, work.Parts.Count);
        Assert.Equal(["pdmx-ballade", "mutopia-chopin-op23-ballade-1"], work.Others.Select(p => p.Id));
    }

    [Fact]
    public void WhatStandsBeforeAColonIsAWorkOnlyWhereAnotherTitleHasItToo()
    {
        var works = Works.Of(
        [
            Arranged("pdmx-april", "April: Snowdrop"),
            Pdf("mutopia-goldberg-2", "Goldberg Variations - 2", "BWV 988"),
            Pdf("mutopia-goldberg-1", "Goldberg Variations - 1", "BWV 988"),
        ]);

        var april = works.Single(w => w.Title == "April: Snowdrop");
        Assert.Equal("April: Snowdrop", Assert.Single(april.Parts).Name);
        var goldberg = works.Single(w => w.Title == "Goldberg Variations");
        Assert.Equal("BWV 988", goldberg.Catalogue);
        Assert.Equal("Variations", goldberg.Kind);
        Assert.Equal(["1", "2"], goldberg.Parts.Select(p => p.Name));
    }

    [Fact]
    public void AChoralesFirstLineIsNotAWork()
    {
        var work = Assert.Single(Works.Of(
        [
            Edition("humdrum-chorale-153", "Ach Gott, vom Himmel sieh darein, BWV 153/1", "Chorales", "Humdrum"),
            Edition("humdrum-chorale-254", "Ach Gott, erhör mein Seufzen, BWV 254", "Chorales", "Humdrum"),
        ]));

        Assert.Equal("Chorales", work.Title);
        Assert.Equal("Chorales and hymns", work.Kind);
        Assert.Equal(2, work.Parts.Count);
    }

    [Fact]
    public void TwoSetsEachHaveANumberedPieceInTheSameKey()
    {
        // Schubert's impromptus in A-flat: the fourth of D. 899 and the second of D. 935.
        var works = Works.Of(
        [
            Edition("asap-schubert-impromptu-op-90-d-899-4", "Impromptu in A♭ major, D. 899 No. 4", "Impromptus", "ASAP"),
            Edition("asap-schubert-impromptu-op-90-d-899-1", "Impromptu in C minor, D. 899 No. 1", "Impromptus", "ASAP"),
            Pdf("mutopia-SchubertF-D899-4-Impromptu", "Impromptu in As-dur, No. 4", "D.899 (Op. 90)"),
            Pdf("mutopia-SchubertF-D935-2-Impromptu", "Impromptu in As-dur, No. 2", "D.935 (Op. 142)"),
        ]);

        Assert.Equal(["Impromptu in As-dur, No. 2", "Impromptus"], works.Select(w => w.Title));
        Assert.Equal("D.935 (Op. 142)", works[0].Catalogue);
        Assert.Equal(["mutopia-SchubertF-D899-4-Impromptu"], works[1].Others.Select(p => p.Id));
    }

    [Fact]
    public void ASetWithOnePieceHereIsThatPieceUnderItsOwnName()
    {
        var work = Assert.Single(Works.Of([Edition("allemande", "Allemande", "English Suites"), .. Pathetique]).Where(w => w.Kind == "Dances"));

        Assert.Equal("Allemande", work.Title);
        Assert.Equal("Allemande", Assert.Single(work.Parts).Name);
    }

    [Fact]
    public void WorksAreByKindThenByNameWithNumbersAsNumbers()
    {
        var works = Works.Of(
        [
            Arranged("pdmx-minuet", "Minuet in G"),
            Edition("dcml-beethoven-piano-sonatas-10-1", "Sonata No. 10, Op. 14/2: I. Allegro", "Piano sonatas"),
            Edition("dcml-beethoven-piano-sonatas-02-1", "Sonata No. 2, Op. 2/2: I. Allegro vivace", "Piano sonatas"),
            Arranged("pdmx-elise", "Für Elise"),
        ]);

        Assert.Equal(["Sonata No. 2", "Sonata No. 10", "Minuet in G", "Für Elise"], works.Select(w => w.Title));
        Assert.Equal(["Sonatas", "Sonatas", "Dances", "Character pieces"], works.Select(w => w.Kind));
        Assert.Equal(["Op. 2 No. 2", "Op. 14 No. 2", null, null], works.Select(w => w.Catalogue));
    }

    [Fact]
    public void APieceByItselfIsNamedByItsWorkAndItsPartOfIt()
    {
        Assert.Equal(("Sonata No. 14 “quasi una fantasia” “Moonlight”", "I. Adagio sostenuto"),
            Works.NameOf(Edition("dcml-beethoven-piano-sonatas-14-1", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: I. Adagio sostenuto", "Piano sonatas")));
        Assert.Equal(("Sonata No. 8 “Pathétique”", "II. Adagio cantabile"), Works.NameOf(Pdf("mutopia-pathetique-2", "Sonata No. 8 “Pathétique” (2nd Movement: Adagio cantabile)", "Op. 13")));
        // A piece that's a work by itself has no part; nor has one whose title only looks like a work's and a part's.
        Assert.Equal(("Hallelujah", null), Works.NameOf(new ListedPiece("import-1", "Hallelujah", null, null, false)));
        Assert.Equal(("April: Snowdrop", null), Works.NameOf(Arranged("pdmx-april", "April: Snowdrop")));
        Assert.Equal(("Allemande", null), Works.NameOf(Edition("allemande", "Allemande", "English Suites")));
    }

    [Fact]
    public void ANameSortsWithoutAccentsAndByItsNumbers()
    {
        Assert.Equal("etude no. 00002", Works.Natural("Étude No. 2"));
        Assert.True(string.CompareOrdinal(Works.Natural("Sonata No. 2"), Works.Natural("Sonata No. 10")) < 0);
    }
}
