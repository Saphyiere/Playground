using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Scoring;

namespace Prima.Core.Tests;

public class ExerciseTests
{
    public static TheoryData<string> Played => new(Exercises.All.Where(e => e.Kind == ExerciseKind.Played).Select(e => e.Id));

    private static readonly Key C = new(0);

    // What a hand plays, in order: each note or chord as its keys' names.
    private static List<string> Sounded(Score score, int staff) =>
        [.. score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == staff).SelectMany(v => v.Events.OfType<Notes>())).Select(n => string.Join(" ", n.Tones.Select(t => t.Pitch.ToString())))];

    private static List<int?> Fingers(Score score, int staff) =>
        [.. score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == staff).SelectMany(v => v.Events.OfType<Notes>())).SelectMany(n => n.Tones.Select(t => t.Finger))];

    [Fact]
    public void The_catalogue_is_whole()
    {
        Assert.Equal(Exercises.All.Count, Exercises.All.Select(e => e.Id).Distinct().Count());
        Assert.All(Exercises.All, e => Assert.Contains(Exercises.Sections, s => s.Key == e.Section));
        Assert.All(Exercises.Sections, s => Assert.Contains(Exercises.All, e => e.Section == s.Key));
        Assert.All(Exercises.All, e => Assert.False(string.IsNullOrWhiteSpace(e.Name) || string.IsNullOrWhiteSpace(e.Does)));
        Assert.All(Exercises.All.Where(e => e.Kind == ExerciseKind.Played), e => Assert.True(e.Music is not null && e.Hands.Count > 0 && e.How.Count > 0 && e.Notice.Length > 0 && e.Tempo is >= 40 and <= 120));
        Assert.All(Exercises.All.Where(e => e.Kind == ExerciseKind.Drill), e => Assert.StartsWith("drills/", e.Route));
        Assert.All(Exercises.All.Where(e => e.Kind is ExerciseKind.Away or ExerciseKind.Way), e => Assert.True(e.Music is null && e.How.Count > 0 && e.Notice.Length > 0));
    }

    [Theory]
    [MemberData(nameof(Played))]
    public void A_played_exercise_writes_music_that_can_be_played_in_every_key_and_hand_it_offers(string id)
    {
        var exercise = Exercises.Find(id)!;
        foreach (var key in Exercises.KeysOf(exercise))
        {
            foreach (var hands in exercise.Hands)
            {
                var score = exercise.Music!(key, hands);
                var where = $"{id} in {key.Name}, {hands}";

                // Every voice fills its bars, one thing after another.
                foreach (var (bar, _, meter) in score.WithSignatures())
                {
                    Assert.Equal([1, 2], bar.Voices.Select(v => v.Staff));
                    foreach (var voice in bar.Voices)
                    {
                        var at = 0;
                        foreach (var e in voice.Events)
                        {
                            Assert.True(e.Start == at, $"{where}: bar {bar.Number} has a gap or an overlap at {at}.");
                            at += e.Length;
                        }
                        Assert.True(at == meter.BarTicks, $"{where}: bar {bar.Number} is {at} ticks long.");
                    }
                }

                // Each hand's notes are on its staff, on the piano, in reach of one hand, and fingered by a real finger.
                var notes = score.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>().Select(n => (v.Staff, Notes: n)))).ToList();
                Assert.NotEmpty(notes);
                Assert.Equal(hands != HandUse.Left, notes.Any(n => n.Staff == 1));
                Assert.Equal(hands != HandUse.Right, notes.Any(n => n.Staff == 2));
                foreach (var (_, n) in notes)
                {
                    var keys = n.Tones.Select(t => t.Pitch.Midi).ToList();
                    Assert.True(keys.All(k => k is >= 28 and <= 96), $"{where}: {n.Id} is off the middle of the piano.");
                    Assert.True(keys.SequenceEqual(keys.Order()) && keys.Distinct().Count() == keys.Count && keys[^1] - keys[0] <= 12, $"{where}: {n.Id} isn't a chord one hand can take.");
                    Assert.All(n.Tones, t => Assert.True(t.Finger is null or (>= 1 and <= 5)));
                    Assert.True(n.Tones.Count == 1 || n.Tones.Select(t => t.Finger).Distinct().Count() == n.Tones.Count, $"{where}: {n.Id} uses a finger twice.");
                }

                // Ids are the sheet's handles: no two things share one, and every mark is on something there.
                var ids = notes.Select(n => n.Notes.Id).Concat(notes.Where(n => n.Notes.Tones.Count > 1).SelectMany(n => n.Notes.Tones.Select(t => t.Id))).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
                Assert.All(score.Marks, m => Assert.True(ids.Contains(m.StartId) && (m.EndId is null || ids.Contains(m.EndId)), $"{where}: a mark points at nothing."));

                // Written out and read back, it asks for the same keys at the same times.
                var read = MeiReader.Read(MeiWriter.Write(score));
                Assert.Equal(score.Expected().Select(n => (n.Midi, n.Tick, n.Length, n.Staff)), read.Expected().Select(n => (n.Midi, n.Tick, n.Length, n.Staff)));
            }
        }
    }

    [Fact]
    public void The_cadence_keeps_its_chords_close_and_raises_the_leading_note_in_minor()
    {
        Assert.Equal(["C4 E4 G4", "C4 F4 A4", "C4 E4 G4", "B3 D4 G4", "C4 E4 G4"], Sounded(ExerciseMusic.Cadence(C, HandUse.Together), 1));
        Assert.Equal(["C3", "F2", "C3", "G2", "C3"], Sounded(ExerciseMusic.Cadence(C, HandUse.Together), 2));
        Assert.Equal(["A4 C5 E5", "A4 D5 F5", "A4 C5 E5", "G♯4 B4 E5", "A4 C5 E5"], Sounded(ExerciseMusic.Cadence(new Key(0, true), HandUse.Right), 1));
        // Alone, the left hand plays the chords itself, with its own fingers.
        Assert.Equal(["C3 E3 G3", "C3 F3 A3", "C3 E3 G3", "B2 D3 G3", "C3 E3 G3"], Sounded(ExerciseMusic.Cadence(C, HandUse.Left), 2));
        Assert.Equal([5, 3, 1, 5, 2, 1, 5, 3, 1, 5, 3, 1, 5, 3, 1], Fingers(ExerciseMusic.Cadence(C, HandUse.Left), 2));
        // Far apart, each chord sits on its own root.
        Assert.Equal(["C4 E4 G4", "F4 A4 C5", "C4 E4 G4", "G4 B4 D5", "C4 E4 G4"], Sounded(ExerciseMusic.CadenceFar(C, HandUse.Right), 1));
    }

    [Fact]
    public void Chords_take_their_shapes_and_fingers()
    {
        Assert.Equal(["C4 E4 G4", "C4 E♭4 G4", "C4 E♭4 G♭4", "C4 E4 G♯4", "C4 E4 G4"], Sounded(ExerciseMusic.ChordKinds(C, HandUse.Right), 1));
        Assert.Equal(["C4 E4 G4", "E4 G4 C5", "G4 C5 E5", "C5 E5 G5", "G4 C5 E5", "E4 G4 C5", "C4 E4 G4"], Sounded(ExerciseMusic.Inversions(C, HandUse.Right), 1));
        Assert.Equal([1, 3, 5, 1, 2, 5, 1, 3, 5, 1, 3, 5, 1, 3, 5, 1, 2, 5, 1, 3, 5], Fingers(ExerciseMusic.Inversions(C, HandUse.Right), 1));
        Assert.Equal([5, 3, 1, 5, 3, 1, 5, 2, 1, 5, 3, 1, 5, 2, 1, 5, 3, 1, 5, 3, 1], Fingers(ExerciseMusic.Inversions(C, HandUse.Left), 2));
        Assert.Equal(15, Sounded(ExerciseMusic.TriadWalk(new Key(1), HandUse.Right), 1).Count);
        Assert.Equal("F♯5 A5 C6", Sounded(ExerciseMusic.TriadWalk(new Key(1), HandUse.Right), 1)[6]);
        Assert.Equal(["C4 E4 G4", "B3 D4 G4", "C4 E4 A4", "C4 F4 A4"], Sounded(ExerciseMusic.FourChords(C, HandUse.Right), 1).Take(4));
    }

    [Fact]
    public void Lines_have_their_standard_fingers()
    {
        // The chromatic scale: the third finger on black keys, the second on the upper of two white neighbours.
        Assert.Equal([1, 3, 1, 3, 1, 2, 3, 1, 3, 1, 3, 1, 2], Fingers(ExerciseMusic.Chromatic(C, HandUse.Right), 1).Take(13));
        Assert.Equal([1, 3, 1, 3, 2, 1, 3, 1, 3, 1, 3, 2, 1], Fingers(ExerciseMusic.Chromatic(C, HandUse.Left), 2).Take(13));
        // Contrary motion: each hand its scale's own fingering, the left's backwards as it goes down.
        Assert.Equal([1, 2, 3, 1, 2, 3, 4, 5, 4, 3, 2, 1, 3, 2, 1], Fingers(ExerciseMusic.ContraryScale(C, HandUse.Together), 1));
        Assert.Equal([1, 2, 3, 1, 2, 3, 4, 5, 4, 3, 2, 1, 3, 2, 1], Fingers(ExerciseMusic.ContraryScale(C, HandUse.Together), 2));
        Assert.Equal(["C3", "B2", "A2", "G2", "F2", "E2", "D2", "C2"], Sounded(ExerciseMusic.ContraryScale(C, HandUse.Together), 2).Take(8));
        Assert.Equal([4, 3, 2, 1, 4, 3, 2, 1], Fingers(ExerciseMusic.ContraryScale(new Key(-1), HandUse.Together), 1).Skip(7).Take(8));
        // Hanon's first figure, and the thumb passing under.
        Assert.Equal(["C4", "E4", "F4", "G4", "A4", "G4", "F4", "E4", "D4", "F4"], Sounded(ExerciseMusic.Hanon(C, HandUse.Right), 1).Take(10));
        Assert.Equal(["G5", "E5", "D5", "C5", "B4", "C5", "D5", "E5"], Sounded(ExerciseMusic.Hanon(C, HandUse.Right), 1).Skip(56).Take(8));
        Assert.Equal([1, 2, 3, 1, 3, 2], Fingers(ExerciseMusic.ThumbUnder(C, HandUse.Right, 3), 1).Take(6));
        Assert.Equal(["C3", "B2", "A2", "G2", "F2", "G2", "A2", "B2"], Sounded(ExerciseMusic.ThumbUnder(C, HandUse.Left, 4), 2).Take(8));
        // A trill's two fingers sit on their own notes in each hand.
        Assert.Equal(["D4", "E4"], Sounded(ExerciseMusic.Rock(C, HandUse.Right, (2, 3), quick: true), 1).Take(2));
        Assert.Equal(["E3", "F3"], Sounded(ExerciseMusic.Rock(C, HandUse.Left, (2, 3), quick: true), 2).Take(2));
        Assert.Equal([3, 2], Fingers(ExerciseMusic.Rock(C, HandUse.Left, (2, 3), quick: true), 2).Take(2));
    }

    [Fact]
    public void A_jump_gets_less_time_to_travel_each_line()
    {
        var jump = ExerciseMusic.ChordJump(C, HandUse.Right).Expected().Where(n => n.Midi is 60 or 72).ToList();

        // Three beats of rest between the chords, then one, then none.
        Assert.Equal([0L, 1920, 3840, 5760, 7680, 8640, 9600, 10560, 11520, 12000, 12480, 12960, 13440], jump.Select(n => n.Tick));
        Assert.Equal([60, 72, 60, 72, 60, 72, 60, 72, 60, 72, 60, 72, 60], jump.Select(n => n.Midi));
        // The left hand jumps down, and the ladder's left hand starts near middle C where the key lets it.
        Assert.Equal(["C3 E3 G3", "C2 E2 G2"], Sounded(ExerciseMusic.ChordJump(C, HandUse.Left), 2).Take(2));
        Assert.Equal(["C4", "A3", "C4", "F3", "C4", "C3", "C4", "A2", "C4"], Sounded(ExerciseMusic.JumpLadder(C, HandUse.Left), 2));
        Assert.Equal(["C2", "C3 E3 G3", "C3 E3 G3"], Sounded(ExerciseMusic.Waltz(C, HandUse.Left), 2).Take(3));
        Assert.Equal(["G2", "B2 D3 G3"], Sounded(ExerciseMusic.Waltz(C, HandUse.Left), 2).Skip(6).Take(2));
    }

    [Fact]
    public void Touch_loudness_and_pedal_are_marked_for_the_judges()
    {
        var slurs = ExerciseMusic.TwoNoteSlurs(C, HandUse.Left);
        Assert.Equal(8, slurs.Marks.Count(m => m.Kind == MarkKind.Slur && m.StartId.StartsWith('l')));
        Assert.Equal(8, slurs.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>())).Count(n => n.Articulation == Articulation.Staccato));

        Assert.Equal(["p", "mf", "f", "mf", "p"], ExerciseMusic.SoftToLoud(C, HandUse.Right).Marks.Select(m => m.Text));
        Assert.Equal([MarkKind.Dynamic, MarkKind.Crescendo, MarkKind.Diminuendo, MarkKind.Crescendo, MarkKind.Diminuendo], ExerciseMusic.Swell(C, HandUse.Right).Marks.Select(m => m.Kind));

        // The pedal goes down on the first chord, changes on each one after, and comes up at the end.
        var pedal = ExerciseMusic.PedalChanges(C, HandUse.Right).Marks.Select(m => (m.Kind, m.StartId)).ToList();
        Assert.Equal([(MarkKind.PedalDown, "r0"), (MarkKind.PedalUp, "r1"), (MarkKind.PedalDown, "r1"), (MarkKind.PedalUp, "r2"), (MarkKind.PedalDown, "r2"), (MarkKind.PedalUp, "r3"), (MarkKind.PedalDown, "r3"), (MarkKind.PedalUp, "r3")], pedal);

        // Three against two: the right hand's notes a third of a beat apart, the left's a half.
        var cross = ExerciseMusic.TwoAgainstThree(C, HandUse.Together).Expected();
        Assert.Equal([0L, 160, 320, 480], cross.Where(n => n.Staff == 1).Take(4).Select(n => n.Tick));
        Assert.Equal([0L, 240, 480], cross.Where(n => n.Staff == 2).Take(3).Select(n => n.Tick));
    }

    [Theory]
    [InlineData("jump", new[] { "octave-jump", "jump-ladder", "chord-jump", "cadence-far", "waltz" })]
    [InlineData("pedal chord", new[] { "pedal-changes" })]
    [InlineData("THUMB under", new[] { "thumb-three", "thumb-four" })]
    [InlineData("xylophone", new string[0])]
    public void Search_finds_exercises_by_every_word(string search, string[] found)
    {
        Assert.Equal(found, Exercises.All.Where(e => Exercises.Matches(e, search)).Select(e => e.Id));
    }

    [Fact]
    public void Keys_come_easiest_first_and_only_those_an_exercise_offers()
    {
        Assert.Equal([C], Exercises.KeysOf(Exercises.Find("chromatic")!));
        Assert.Equal(12, Exercises.KeysOf(Exercises.Find("triad-walk")!).Count);
        Assert.DoesNotContain(Exercises.KeysOf(Exercises.Find("triad-walk")!), k => k.Minor);
        Assert.Equal(Technique.Order, Exercises.KeysOf(Exercises.Find("five")!));
    }

    [Fact]
    public void The_pedal_exercise_is_right_when_the_pedal_changes_as_its_words_say()
    {
        const double BeatNs = 1e9;
        static long At(double beats) => (long)(beats * BeatNs);
        var score = ExerciseMusic.PedalChanges(C, HandUse.Right);
        var expected = score.Expected();
        var played = expected.Select(e => new PlayedNote(e.Midi, At(e.Tick / 480.0), At((e.Tick + e.Length) / 480.0 - 0.1), 70)).ToList();
        PassageResult Pedalled(params (long, long)[] damper) => PassageScorer.Score(expected, At, Ticks.Quarter, BeatNs, 4 * BeatNs, played, 0, damper: damper, markings: Markings.Of(score, expected));

        // Up as each new chord sounds and down again while it's held: a chord a bar, so the changes are at beats 4, 8 and 12.
        var changed = Pedalled((At(0.2), At(4.1)), (At(4.3), At(8.1)), (At(8.3), At(12.1)), (At(12.3), At(16)));
        Assert.Equal([Pedalling.Right, Pedalling.Right, Pedalling.Right, Pedalling.Right], changed.Pedal.Select(p => p.Outcome));
        Assert.Equal([1, 2, 3, 4], changed.Pedal.Select(p => p.FromBar));

        // Held down through every change, the first three chords blur into the next; never pressed, all four are missed.
        Assert.Equal([Pedalling.NotLifted, Pedalling.NotLifted, Pedalling.NotLifted, Pedalling.Right], Pedalled((At(0.2), At(16))).Pedal.Select(p => p.Outcome));
        Assert.Equal([Pedalling.NotPressed, Pedalling.NotPressed, Pedalling.NotPressed, Pedalling.NotPressed], Pedalled().Pedal.Select(p => p.Outcome));
    }
}
