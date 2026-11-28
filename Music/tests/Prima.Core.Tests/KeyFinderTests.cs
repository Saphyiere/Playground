using System.Text;
using Prima.Core.Music;

namespace Prima.Core.Tests;

/// <summary>
/// Which key a piece is in, of the two its signature can be (and now and then another): the file's minor, the title, then what's
/// heard (the notes, the bass's last note and its first). And the key signature as the file writes it, MEI 5's keysig too.
/// </summary>
public class KeyFinderTests
{
    // A piano piece in 4/4, a bar a string: the right hand's notes, "|", the left hand's. A note is its letter, its written sharp,
    // flat or natural (#, b, n), its octave, and its length after a colon (a quarter if none): "d#5:2". A bar starting "[3f]" has a
    // new key signature before it; one ending "fine" is marked Fine. The signature is the score's attributes (key.sig="2s"), or a
    // keySig element in it.
    private static string Piece(string signature, params string[] bars)
    {
        var (attributes, element) = signature.TrimStart().StartsWith('<') ? ("", signature) : (signature, "");
        var mei = new StringBuilder($"""<mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score><scoreDef {attributes} meter.count="4" meter.unit="4">{element}<staffGrp>""");
        mei.Append("""<staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef><section>""");
        var id = 0;
        for (var b = 0; b < bars.Length; b++)
        {
            var bar = bars[b].Trim();
            if (bar.StartsWith('['))
            {
                mei.Append($"""<scoreDef key.sig="{bar[1..bar.IndexOf(']')]}"/>""");
                bar = bar[(bar.IndexOf(']') + 1)..].Trim();
            }
            var fine = bar.EndsWith("fine", StringComparison.Ordinal);
            if (fine) bar = bar[..^4].Trim();
            var hands = bar.Split('|');
            mei.Append($"""<measure n="{b + 1}">""");
            for (var staff = 0; staff < 2; staff++)
            {
                mei.Append($"""<staff n="{staff + 1}"><layer n="1">""");
                foreach (var note in hands[staff].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var (pitch, dur) = note.Contains(':') ? (note[..note.IndexOf(':')], note[(note.IndexOf(':') + 1)..]) : (note, "4");
                    var accid = pitch.Length == 3 ? pitch[1] switch { '#' => """ accid="s" """, 'b' => """ accid="f" """, _ => """ accid="n" """ } : "";
                    mei.Append($"""<note xml:id="n{++id}" pname="{pitch[0]}" oct="{pitch[^1]}" dur="{dur}"{accid}/>""");
                }
                mei.Append("</layer></staff>");
            }
            if (fine) mei.Append("""<dir staff="1" tstamp="4">Fine</dir>""");
            mei.Append("</measure>");
        }
        return mei.Append("</section></score></mdiv></body></music></mei>").ToString();
    }

    // The same tune each time, in a key: up its triad, to the fifth's chord, and home.
    private static readonly string[] EMinor = ["e5 g5 b5 e6 | e3:1", "d#5 f#5 b5:2 | b2:1", "e5:1 | e3:1"];
    private static readonly string[] GMajor = ["g5 b5 d6 g6 | g3:1", "f#5 a5 d6:2 | d3:1", "g5:1 | g2:1"];
    private static readonly string[] CMajor = ["c5 e5 g5 c6 | c3:1", "b4 d5 g5:2 | g2:1", "c5:1 | c3:1"];
    private static readonly string[] GMinorInTwoFlats = ["g4 b4 d5 g5 | g2:1", "f#4 a4 d5:2 | d3:1", "g4:1 | g2:1"];
    // C minor with two flats, as Bach wrote it: its A flat and B natural written in as they come.
    private static readonly string[] CMinorInTwoFlats = ["c5 e5 g5 c6 | c3:1", "f5 ab5 c6 ab5 | f2:1", "bn4 d5 f5 g5 | g2:1", "c5:1 | c2:1"];

    private static KeyStretch Opening(string signature, string[] bars, params string[] titles) => KeyFinder.Find(MeiReader.Read(Piece(signature, bars)), titles)[0];

    [Theory]
    [InlineData("Sonatina in F major op. 168 no. 1", "F major")]
    [InlineData("Étude in B♭ minor, Op. 8 No. 11", "B♭ minor")]
    [InlineData("Sonata in B-flat major, Wq. 50/5", "B♭ major")]
    [InlineData("Waltz in C Sharp Minor", "C♯ minor")]
    [InlineData("Piano Sonata 2 in Bb Minor", "B♭ minor")]
    [InlineData("Song SerenadeD957No4-dminor", "D minor")]
    [InlineData("Grandes études de Paganini No.3 in G-sharp minor La Campanella", "G♯ minor")]
    [InlineData("Menuet g-Moll", "G minor")]
    [InlineData("Sonate Es-Dur", "E♭ major")]
    [InlineData("Messe in h-Moll", "B minor")]
    [InlineData("Konzert B-Dur", "B♭ major")]
    [InlineData("Fantasie fis-moll", "F♯ minor")]
    [InlineData("Nocturne en Do dièse mineur", "C♯ minor")]
    [InlineData("Sonatina en Sol major", "G major")]
    [InlineData("Canon in D", "D major")]
    [InlineData("Moscheles's Impromptu in E-flat", "E♭ major")]
    [InlineData("Impromptu in Eb", "E♭ major")]
    [InlineData("Ich steh an deiner Krippen hier", null)]
    [InlineData("Variations in a Classical style", null)]
    public void ATitleNamesItsKey(string title, string? key) => Assert.Equal(key, KeyFinder.Named(title)?.Name);

    [Fact]
    public void TheSignaturesInWords()
    {
        Assert.Equal("2 sharps, F♯ C♯", new Key(2).Signature);
        Assert.Equal("1 flat, B♭", new Key(-1, true).Signature);
        Assert.Equal("4 flats, B♭ E♭ A♭ D♭", new Key(-4).Signature);
        Assert.Equal("no sharps or flats", new Key(0).Signature);
    }

    [Fact]
    public void TheFilesMinorIsTheKeyAndItsMajorGivesWayToWhatsHeard()
    {
        // Verovio keeps MusicXML's mode on the key signature. A file's minor is the key; its "major" is often only the default
        // its program writes, so a G minor tune said to be major is G minor, by how it ends.
        var minor = MeiReader.Read(Piece("""<keySig sig="1s" mode="minor"/>""", GMajor));
        var told = KeyFinder.Find(minor)[0];
        Assert.Equal([0], minor.StatedModes);
        Assert.Equal(new Key(1, true), minor.Key);
        Assert.Equal((new Key(1, true), KeyTold.File), (told.Key, told.Told));

        var said = Opening("""key.sig="2f" key.mode="major" """, GMinorInTwoFlats);
        Assert.Equal((new Key(-2, true), KeyTold.Ending), (said.Key, said.Told));
    }

    [Fact]
    public void TheTitleSaysWhichOfTheTwoUnlessItsALaterMovements()
    {
        // The title's B minor though the tune is D major's: the title is the composer's word.
        var prelude = Opening("""key.sig="2s" """, ["d5 f5 a5 d6 | d3:1", "c5 e5 a5:2 | a2:1", "d5:1 | d3:1"], "Prelude in B minor");
        Assert.Equal((new Key(2, true), KeyTold.Title), (prelude.Key, prelude.Told));

        // A slow movement's title names its sonata's key; the movement is in the relative major, as it's heard.
        var later = Opening("""key.sig="0" """, CMajor, "Sonata in A minor, Wq. 50/3: II. Largo");
        Assert.Equal((new Key(0), KeyTold.Ending), (later.Key, later.Told));
    }

    [Fact]
    public void WhatsHeardTellsMinorFromMajor()
    {
        var minor = Opening("""key.sig="1s" """, EMinor);
        var major = Opening("""key.sig="1s" """, GMajor);

        Assert.Equal((new Key(1, true), KeyTold.Ending, true), (minor.Key, minor.Told, minor.AsWritten));
        Assert.Equal((new Key(1), KeyTold.Ending), (major.Key, major.Told));
    }

    [Fact]
    public void NothingClearIsUnsureAndSaysBoth()
    {
        // Only C sharp, in no sharps or flats: neither C major nor A minor more than the other.
        var found = Opening("""key.sig="0" """, ["c#5:1 | c#3:1", "c#5:1 | c#3:1"]);

        Assert.Equal((KeyTold.Unsure, "C major", "A minor"), (found.Told, found.Key.Name, found.Other.Name));
    }

    [Fact]
    public void EachSignatureIsAStretchAndTheMusicInOneIsHeardTogether()
    {
        // E minor, a middle in C major's no sharps, then E minor again: the first stretch ends on the fifth's chord, but the music
        // in one sharp ends on E.
        var score = MeiReader.Read(Piece("""key.sig="1s" """, EMinor[0], "b4 d#5 f#5 b5 | b2:1", "[0] c5 e5 g5 c6 | c3:1", "b4 d5 g5:2 | g2:1", "c5:1 | c3:1", "[1s] e5 g5 b5 e6 | e3:1", "d#5 f#5 b5:2 | b2:1", "e5:1 | e3:1"));
        var found = KeyFinder.Find(score);

        Assert.Equal([(0, 1, "E minor"), (2, 4, "C major"), (5, 7, "E minor")], found.Select(s => (s.First, s.Last, s.Key.Name)));
    }

    [Fact]
    public void ADaCapoEndsAtFine()
    {
        // E minor to Fine, then a G major middle that ends on its fifth for the D.C.: the music ends at Fine, on E.
        var score = MeiReader.Read(Piece("""key.sig="1s" """, EMinor[0], "b4 d#5 e5:2 | b2:2 e3:2 fine", "g5 b5 d6 g6 | g3:1", "f#5 a5 d6:2 | d3:1"));
        var found = KeyFinder.Find(score);

        Assert.True(score.Bars[1].Fine);
        Assert.Equal((new Key(1, true), KeyTold.Ending), (found[0].Key, found[0].Told));
    }

    [Fact]
    public void ATitleThatLostItsSharpOrNamesTheKeynoteAlone()
    {
        // "in C minor" over four sharps is C♯ minor; "in D" over one flat is D minor.
        Assert.Equal(new Key(4, true), Opening("""key.sig="4s" """, ["c5 e5 g5 c6 | c3:1"], "Prélude No. 25 in C Minor Opus 45").Key);
        var toccata = Opening("""key.sig="1f" """, ["d5 f5 a5 d6 | d3:1", "d5:1 | d2:1"], "Toccata in D");
        Assert.Equal((new Key(-1, true), true), (toccata.Key, toccata.AsWritten));
    }

    [Fact]
    public void AKeyItsSignatureDoesntWrite()
    {
        // Mozart's Fantasia in C minor is written without a signature: the title says the mode, and it's C minor.
        var fantasia = Opening("""key.sig="0" """, ["c5 eb5 g5 c6 | c3:1", "bn4 d5 g5:2 | g2:1", "c5:1 | c3:1"], "Fantasia in C minor, K. 475");
        Assert.Equal((new Key(-3, true), false, KeyTold.Title), (fantasia.Key, fantasia.AsWritten, fantasia.Told));

        // Bach's French Suite in C minor, with two flats: it ends on C, whose minor the notes fit best.
        var suite = Opening("""key.sig="2f" """, CMinorInTwoFlats, "French Suite No. 2, BWV 813: Courante");
        Assert.Equal((new Key(-3, true), false, -2, KeyTold.Ending), (suite.Key, suite.AsWritten, suite.Fifths, suite.Told));
    }

    [Fact]
    public void MeisOwnKeysigIsTheSignature()
    {
        // The Open Hymnal writes MEI 5's keysig: two sharps, so its F is F sharp.
        var score = MeiReader.Read(Piece("""keysig="2s" key.mode="major" """, ["d5 f5 a5 d6 | d3:1"]));

        Assert.Equal(2, score.Key.Fifths);
        Assert.Equal(new Spelled(Step.F, 1, 5), score.Bars[0].Voices[0].Events.OfType<Notes>().ElementAt(1).Tones[0].Pitch);
        Assert.Equal([0], score.StatedModes);
    }
}
