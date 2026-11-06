using System.Buffers.Binary;
using Prima.Core.Library;
using Prima.Core.Music;

namespace Prima.Core.Tests;

public class MidiNotationTests
{
    // A Standard MIDI File (format 1) from tracks of events at ticks; the delta times are worked out here.
    private static byte[] File(int division, params (long Tick, byte[] Event)[][] tracks)
    {
        List<byte> bytes = [.. "MThd"u8, 0, 0, 0, 6, 0, 1, 0, (byte)tracks.Length, (byte)(division >> 8), (byte)division];
        foreach (var track in tracks)
        {
            var body = new List<byte>();
            var last = 0L;
            foreach (var (tick, message) in track.OrderBy(e => e.Tick))
            {
                var delta = tick - last;
                var groups = new List<byte> { (byte)(delta & 0x7F) };
                for (delta >>= 7; delta > 0; delta >>= 7) groups.Insert(0, (byte)(0x80 | (delta & 0x7F)));
                body.AddRange(groups);
                body.AddRange(message);
                last = tick;
            }
            body.AddRange([0, 0xFF, 0x2F, 0]);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, body.Count);
            bytes.AddRange([.. "MTrk"u8, .. length, .. body]);
        }
        return [.. bytes];
    }

    // Notes on a channel as note-on and note-off events; an off sorts before an on at the same tick, being listed first.
    private static (long Tick, byte[] Event)[] Notes(params (int Key, long On, long Off)[] notes) =>
        [.. notes.SelectMany(n => new[] { (n.On, new byte[] { 0x90, (byte)n.Key, 80 }), (n.Off, new byte[] { 0x80, (byte)n.Key, 0 }) }).OrderBy(e => e.Item1).ThenBy(e => e.Item2[0])];

    private static (long Tick, byte[] Event) Meter(int count, int unitPower) => (0, [0xFF, 0x58, 4, (byte)count, (byte)unitPower, 24, 8]);

    private static (long Tick, byte[] Event) KeySignature(int fifths, bool minor = false) => (0, [0xFF, 0x59, 2, (byte)(sbyte)fifths, (byte)(minor ? 1 : 0)]);

    private static (long Tick, byte[] Event) Name(string name) => (0, [0xFF, 0x03, (byte)name.Length, .. System.Text.Encoding.Latin1.GetBytes(name)]);

    // What the score sounds: each note's key, start and length, ties followed through.
    private static List<(int Midi, long Tick, int Length)> Sounding(Score score) => [.. score.Expected().Select(n => (n.Midi, n.Tick, n.Length)).Order()];

    [Fact]
    public void AScaleInOneTrackIsWrittenInQuartersOnTheTrebleStaff()
    {
        var keys = new[] { 60, 62, 64, 65, 67, 69, 71, 72 };
        var file = File(480, [Meter(4, 2), .. Notes([.. keys.Select((k, i) => (k, i * 480L, i * 480L + 480))])]);

        var score = MidiNotation.Read(file, "Scale");

        Assert.Equal(2, score.Bars.Count);
        Assert.Equal(new Key(0), score.Key);
        Assert.Equal(new Meter(4, 4), score.Meter);
        var treble = score.Bars[0].Voices.Single(v => v.Staff == 1).Events.OfType<Notes>().ToList();
        Assert.Equal([480, 480, 480, 480], treble.Select(n => n.Length));
        Assert.Equal([new Spelled(Step.C, 0, 4), new Spelled(Step.D, 0, 4), new Spelled(Step.E, 0, 4), new Spelled(Step.F, 0, 4)], treble.Select(n => n.Tones.Single().Pitch));
        Assert.All(score.Bars, b => Assert.IsType<Rest>(b.Voices.Single(v => v.Staff == 2).Events.Single()));
        Assert.Equal([.. keys.Select((k, i) => (k, i * 480L, 480))], Sounding(score));
    }

    [Fact]
    public void TwoTracksAreTheTwoHandsTheHigherOneOnTheTrebleStaff()
    {
        // The left hand's track comes first in the file.
        var file = File(480, [Meter(3, 2), .. Notes((48, 0, 1440), (43, 1440, 2880))], [.. Notes((64, 0, 480), (67, 480, 960), (72, 960, 1440), (71, 1440, 2880))]);

        var score = MidiNotation.Read(file, "Waltz");

        Assert.Equal(new Meter(3, 4), score.Meter);
        Assert.Equal([64, 67, 72, 71], score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == 1)).SelectMany(v => v.Events.OfType<Notes>()).Select(n => n.Tones[0].Pitch.Midi));
        Assert.Equal([48, 43], score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == 2)).SelectMany(v => v.Events.OfType<Notes>()).Select(n => n.Tones[0].Pitch.Midi));
    }

    [Fact]
    public void ANoteAcrossTheBarlineIsTiedIntoTheNextBar()
    {
        // A half note from the last beat of bar 1, then a quarter.
        var file = File(480, [Meter(4, 2), .. Notes((60, 0, 1440), (62, 1440, 2400), (64, 2400, 2880))]);

        var score = MidiNotation.Read(file, "Tie");

        var last = score.Bars[0].Voices.Single(v => v.Staff == 1).Events.OfType<Notes>().Last();
        var first = score.Bars[1].Voices.Single(v => v.Staff == 1).Events.OfType<Notes>().First();
        Assert.Equal((1440, 480), (last.Start, last.Length));
        Assert.True(last.Tones[0].TieToNext);
        Assert.Equal((0, 480), (first.Start, first.Length));
        Assert.True(first.Tones[0].TiedFromPrevious);
        Assert.Contains((62, 1440L, 960), Sounding(score));
    }

    [Fact]
    public void TheKeySignatureSpellsTheNotes()
    {
        var file = File(480, [Meter(4, 2), KeySignature(-3), .. Notes((63, 0, 480), (70, 480, 960), (68, 960, 1440), (75, 1440, 1920))]);

        var score = MidiNotation.Read(file, "E flat");

        Assert.Equal(new Key(-3), score.Key);
        Assert.Equal([new Spelled(Step.E, -1, 4), new Spelled(Step.B, -1, 4), new Spelled(Step.A, -1, 4), new Spelled(Step.E, -1, 5)],
            score.Bars[0].Voices.Single(v => v.Staff == 1).Events.OfType<Notes>().Select(n => n.Tones[0].Pitch));
    }

    [Fact]
    public void WithoutAKeySignatureTheKeyComesFromTheNotes()
    {
        // E flat major's scale and its tonic chord, with no signature in the file.
        var scale = new[] { 63, 65, 67, 68, 70, 72, 74, 75, 70, 67, 63 };
        var file = File(480, [Meter(4, 2), .. Notes([.. scale.Select((k, i) => (k, i * 480L, i * 480L + 480))])]);

        var score = MidiNotation.Read(file, "Guess");

        Assert.Equal(new Key(-3), score.Key);
        Assert.Equal(new Spelled(Step.E, -1, 4), score.Bars[0].Voices.Single(v => v.Staff == 1).Events.OfType<Notes>().First().Tones[0].Pitch);
    }

    [Fact]
    public void TripletsAreWrittenAsTripletsAndTheRestAsItIs()
    {
        // Triplet eighths on beat 1, then three quarters; a held whole note in the left hand.
        var file = File(480, [Meter(4, 2), .. Notes((72, 0, 160), (74, 160, 320), (76, 320, 480), (77, 480, 960), (79, 960, 1440), (77, 1440, 1920))], [.. Notes((48, 0, 1920))]);

        var score = MidiNotation.Read(file, "Triplets");

        var treble = score.Bars[0].Voices.Single(v => v.Staff == 1).Events.OfType<Notes>().ToList();
        Assert.Equal([160, 160, 160, 480, 480, 480], treble.Select(n => n.Length));
        Assert.Equal(new Tuplet(3, 2), treble[0].Tuplet);
        Assert.Same(treble[0].Tuplet, treble[2].Tuplet);
        Assert.Null(treble[3].Tuplet);
        var bass = score.Bars[0].Voices.Single(v => v.Staff == 2).Events.OfType<Notes>().Single();
        Assert.Equal((0, 1920), (bass.Start, bass.Length));
        Assert.Null(bass.Tuplet);
    }

    [Fact]
    public void ANoteHeldUnderMovingOnesIsASecondVoice()
    {
        // In the right hand: E4 held for a half note under four eighths, then a half note.
        var file = File(480, [Meter(4, 2), .. Notes((64, 0, 960), (67, 0, 240), (69, 240, 480), (71, 480, 720), (72, 720, 960), (74, 960, 1920))]);

        var score = MidiNotation.Read(file, "Voices");

        var voices = score.Bars[0].Voices.Where(v => v.Staff == 1).ToList();
        Assert.Equal(2, voices.Count);
        Assert.Equal([67, 69, 71, 72, 74], voices[0].Events.OfType<Notes>().Select(n => n.Tones[0].Pitch.Midi));
        Assert.Equal(64, voices[1].Events.OfType<Notes>().Single().Tones[0].Pitch.Midi);
        Assert.All(voices[1].Events.OfType<Rest>(), r => Assert.True(r.Hidden));
    }

    [Fact]
    public void APerformancesTimingIsSnappedToTheBeat()
    {
        // Quarters played a little early and late, each let go a little short; and at 96 ticks a quarter, not 480.
        var file = File(96, [Meter(4, 2), .. Notes((60, 0, 90), (62, 97, 188), (64, 191, 286), (65, 290, 380))]);

        var score = MidiNotation.Read(file, "Played");

        Assert.Equal([(60, 0L, 480), (62, 480L, 480), (64, 960L, 480), (65, 1440L, 480)], Sounding(score));
    }

    [Fact]
    public void AFileThatStartsLateIsMovedBackOntoTheBeat()
    {
        // LilyPond's MIDI after a grace note before the first beat: the grace at 0, then every note 54 ticks late.
        var file = File(480, [Meter(4, 2), .. Notes((73, 0, 54), (74, 54, 534), (76, 534, 1014), (78, 1014, 1494), (79, 1494, 1974))]);

        var score = MidiNotation.Read(file, "Late");

        Assert.Equal([(74, 0L, 480), (76, 480L, 480), (78, 960L, 480), (79, 1440L, 480)], Sounding(score).Where(n => n.Midi != 73));
    }

    [Fact]
    public void TheWrittenScoreReadsBackAsTheSameNotes()
    {
        // Both hands, a tie over the barline, triplets, a second voice, a chord and a key change's worth of accidentals.
        var file = File(480,
            [Meter(4, 2), KeySignature(1), Name("Little Study"), .. Notes((67, 0, 480), (66, 480, 960), (64, 960, 1120), (62, 1120, 1280), (60, 1280, 1440), (71, 1440, 2400), (72, 2400, 2880), (74, 2400, 3840), (78, 2880, 3840))],
            [.. Notes((43, 0, 960), (47, 0, 960), (50, 960, 1920), (55, 1920, 3840))]);

        var score = MidiNotation.Read(file, "file");
        var reread = MeiReader.Read(MeiWriter.Write(score));

        Assert.Equal("Little Study", score.Title);
        Assert.Equal(Sounding(score), Sounding(reread));
        Assert.Equal([(43, 0L, 960), (47, 0L, 960), (50, 960L, 960), (55, 1920L, 1920), (60, 1280L, 160), (62, 1120L, 160), (64, 960L, 160), (66, 480L, 480), (67, 0L, 480), (71, 1440L, 960), (72, 2400L, 480), (74, 2400L, 1440), (78, 2880L, 960)], Sounding(score));
    }

    [Fact]
    public void TheFilesNameIsUsedWhenItsFirstTrackDoesntNameThePiece()
    {
        // LilyPond's: a control track, then voices named "one" and "two"; and a track named for the instrument.
        var lilypond = File(480, [Meter(4, 2), Name("control track")], [Name("one"), .. Notes((72, 0, 1920))], [Name("two"), .. Notes((48, 0, 1920))]);
        var named = File(480, [Meter(4, 2), Name("Acoustic Grand Piano"), .. Notes((60, 0, 1920))]);

        Assert.Equal("Bourrée", MidiNotation.Read(lilypond, "Bourrée").Title);
        Assert.Equal("Bourrée", MidiNotation.Read(named, "Bourrée").Title);
    }

    [Fact]
    public void AFileWithoutNotesIsNotMusic()
    {
        Assert.Throws<FormatException>(() => MidiNotation.Read(File(480, [Meter(4, 2)]), "Empty"));
    }
}
