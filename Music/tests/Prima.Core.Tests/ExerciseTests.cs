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
        // The two lists they're shown in: one to play has something to play, its own music or a drill's, and one to read has neither.
        Assert.All(Exercises.All, e => Assert.Equal(e.ToPlay, e.Music is not null || e.Route is not null));
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

                // Every voice fills its bars, one thing after another; a staff has a second line only for a key held under moving ones.
                foreach (var (bar, _, meter) in score.WithSignatures())
                {
                    Assert.Equal([1, 2], bar.Voices.Select(v => v.Staff).Distinct());
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
                    Assert.True(n.Tones.Count(t => t.Finger is not null) == n.Tones.Where(t => t.Finger is not null).Select(t => t.Finger).Distinct().Count(), $"{where}: {n.Id} uses a finger twice.");
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
    [InlineData("THUMB under", new[] { "arpeggio-groups", "thumb-three" })]
    [InlineData("finger strength", new[] { "five", "long-short", "hanon", "independence", "trill-2-3", "trill-1-2", "trill-1-3", "arpeggio-groups", "thirds", "trill-bursts" })]
    [InlineData("xylophone", new string[0])]
    public void Search_finds_exercises_by_every_word(string search, string[] found)
    {
        Assert.Equal(found, Exercises.All.Where(e => Exercises.Matches(e, search)).Select(e => e.Id));
    }

    [Fact]
    public void Keys_come_easiest_first_and_only_those_an_exercise_offers()
    {
        // Every exercise that's played can be taken into other keys, from C.
        Assert.All(Exercises.All.Where(e => e.Kind == ExerciseKind.Played), e => Assert.True(Exercises.KeysOf(e).Count >= 10 && Exercises.KeysOf(e)[0] == C, e.Id));
        Assert.Equal(Technique.Order, Exercises.KeysOf(Exercises.Find("five")!));
        Assert.Equal(Technique.Order, Exercises.KeysOf(Exercises.Find("triad-walk")!));
        // One that's major by its nature has the twelve major keys.
        Assert.Equal(12, Exercises.KeysOf(Exercises.Find("four-chords")!).Count);
        Assert.DoesNotContain(Exercises.KeysOf(Exercises.Find("four-chords")!), k => k.Minor);
        // The thumb passes under only where it lands on white keys: under three or four fingers, the scale's fourth or fifth note.
        Assert.Equal(["C major", "A minor", "G major", "D major", "E minor", "D minor", "A major", "G minor", "E major", "C minor"], Exercises.KeysOf(Exercises.Find("thumb-three")!).Select(k => k.Name));
        Assert.Equal(Exercises.KeysOf(Exercises.Find("thumb-three")!), Exercises.KeysOf(Exercises.Find("thumb-four")!));
        // Under two or five it lands on the third or sixth, so those have keys of their own.
        var thumb = ExerciseFamilies.Of("thumb-three")!;
        Assert.Equal(Exercises.KeysOf(Exercises.Find("thumb-three")!), thumb.KeysOf(Exercises.Find("thumb-three")!, thumb.Read("under=4")));
        Assert.Equal(["C major", "A minor", "G major", "F major", "E minor", "B minor"], thumb.KeysOf(Exercises.Find("thumb-three")!, thumb.Read("under=2")).Select(k => k.Name));
        Assert.Equal(thumb.KeysOf(Exercises.Find("thumb-three")!, thumb.Read("under=2")), thumb.KeysOf(Exercises.Find("thumb-three")!, thumb.Read("under=5")));
        // A family with no keys of its own for what's chosen has its exercise's.
        Assert.Equal(Technique.Order, ExerciseFamilies.Of("thirds")!.KeysOf(Exercises.Find("thirds")!, ExerciseFamilies.Of("thirds")!.Usual));
    }

    [Fact]
    public void Exercises_once_in_C_alone_go_into_other_keys()
    {
        // The chromatic scale starts from each of the twelve notes, named by its note, and is written without a signature.
        var chromatic = Exercises.Find("chromatic")!;
        Assert.Equal(12, Exercises.KeysOf(chromatic).Count);
        Assert.Equal("From C", chromatic.KeyName(C));
        Assert.Equal("From E♭", chromatic.KeyName(new Key(-3)));
        var fromEFlat = ExerciseMusic.Chromatic(new Key(-3), HandUse.Together);
        Assert.Equal(["E♭4", "E4", "F4", "G♭4", "G4", "A♭4"], Sounded(fromEFlat, 1).Take(6));
        Assert.Equal("E♭3", Sounded(fromEFlat, 2)[0]);
        Assert.Equal(0, fromEFlat.Key.Fifths);
        // Black keys take the third finger wherever it starts; the left hand's second finger is on E and B.
        Assert.Equal([3, 1, 2, 3, 1, 3], Fingers(fromEFlat, 1).Take(6));
        Assert.Equal([3, 2, 1, 3, 1, 3], Fingers(fromEFlat, 2).Take(6));

        // Hanon's figure on another key's notes, the fingers unchanged, starting where it stays near the staff.
        Assert.Equal(["G3", "B3", "C4", "D4", "E4", "D4", "C4", "B3", "A3", "C4"], Sounded(ExerciseMusic.Hanon(new Key(1), HandUse.Right), 1).Take(10));
        Assert.Equal(Fingers(ExerciseMusic.Hanon(C, HandUse.Together), 2), Fingers(ExerciseMusic.Hanon(new Key(-2), HandUse.Together), 2));
        Assert.Equal(["E4", "E2"], [Sounded(ExerciseMusic.Hanon(new Key(4), HandUse.Together), 1)[0], Sounded(ExerciseMusic.Hanon(new Key(4), HandUse.Together), 2)[0]]);

        // A minor key's contrary-motion scale is the harmonic one, both hands with that scale's fingers.
        var minor = ExerciseMusic.ContraryScale(new Key(0, true), HandUse.Together);
        Assert.Equal(["A4", "B4", "C5", "D5", "E5", "F5", "G♯5", "A5", "G♯5"], Sounded(minor, 1).Take(9));
        Assert.Equal(["A3", "G♯3", "F3", "E3", "D3", "C3", "B2", "A2"], Sounded(minor, 2).Take(8));
        Assert.Equal([1, 2, 3, 1, 2, 3, 4, 5], Fingers(minor, 1).Take(8));

        // One shape up a minor scale keeps to the scale as its signature has it.
        Assert.Equal(["A4 C5 E5", "B4 D5 F5", "C5 E5 G5"], Sounded(ExerciseMusic.TriadWalk(new Key(0, true), HandUse.Right), 1).Take(3));
        Assert.Equal("G5 B5 D6", Sounded(ExerciseMusic.TriadWalk(new Key(0, true), HandUse.Right), 1)[6]);
        // The thumb passes under in G as in C.
        Assert.Equal(["G4", "A4", "B4", "C5", "B4", "A4"], Sounded(ExerciseMusic.ThumbUnder(new Key(1), HandUse.Right, 3), 1).Take(6));
    }

    // The Key signatures drill shows a signature alone. With no sharps or flats that's a bare staff, which looked like music that
    // hadn't drawn: its prompt says the bare staff is the signature.
    [Fact]
    public void A_bare_key_signature_says_that_it_is_one()
    {
        Assert.Equal("Play the tonic of this key: major (no sharps or flats)", Drills.KeySignature(C).Prompt);
        Assert.Equal("Play the tonic of this key: minor (no sharps or flats)", Drills.KeySignature(new Key(0, true)).Prompt);
        Assert.Equal("Play the tonic of this key: major", Drills.KeySignature(new Key(2)).Prompt);
        Assert.Equal([[62]], Drills.KeySignature(new Key(2)).Answer);
    }

    [Theory]
    [MemberData(nameof(Played))]
    public void Round_a_loop_an_exercise_goes_straight_on_without_its_close(string id)
    {
        var exercise = Exercises.Find(id)!;
        foreach (var hands in exercise.Hands) GoesRound(exercise, exercise.Music!(C, hands));
    }

    /// <summary>An exercise's music, its own or one of its variations', goes round a loop: all but its close.</summary>
    internal static void GoesRound(Exercise exercise, Score score)
    {
        var round = Exercises.Round(exercise, score);

        // Whole bars from the first: all but the close, and something to play in them.
        Assert.Equal(Enumerable.Range(0, score.Bars.Count - exercise.Close), round);
        Assert.NotEmpty(score.Performed(round));
        // The close is what's left: a bar or two that only end it.
        Assert.InRange(exercise.Close, 0, 2);

        // Printed with repeat signs round them, they read back as a repeat: twice round, then the close.
        var printed = MeiReader.Read(MeiWriter.Write(score.WithRepeat(round[0], round[^1])));
        Assert.Equal([.. round, .. round, .. Enumerable.Range(round.Count, exercise.Close)], printed.PlayOrder());
        // Three times round, each note has its own name and its own time, a pass after the last.
        var thrice = score.Performed([.. round, .. round, .. round]);
        Assert.Equal(thrice.Count, thrice.Select(n => n.Id).Distinct().Count());
        var pass = score.BarTimes(round).Sum(b => b.Length);
        var once = score.Performed(round);
        Assert.Equal(once.Select(n => (n.Midi, n.Tick + 2 * pass)), thrice.Skip(2 * once.Count).Select(n => (n.Midi, n.Tick)));
    }

    [Theory]
    [MemberData(nameof(Played))]
    public void A_run_through_the_keys_is_one_piece_of_music(string id)
    {
        var exercise = Exercises.Find(id)!;
        foreach (var hands in exercise.Hands) GoesThroughItsKeys(exercise, hands, null, null, $"{id} through the keys, {hands}");
    }

    /// <summary>
    /// An exercise through its keys is one piece of music: its own music in its own keys, or, given them, one of its variations'
    /// music in that variation's keys.
    /// </summary>
    internal static void GoesThroughItsKeys(Exercise exercise, HandUse hands, Func<Key, HandUse, Score>? music, IReadOnlyList<Key>? among, string where)
    {
        var keys = among ?? Exercises.KeysOf(exercise);
        // From the third key on, round to the second.
        var from = keys[2];
        List<Key> order = [.. keys.Skip(2), .. keys.Take(2)];
        var through = Exercises.Through(exercise, from, hands, music, among);
        var each = order.Select(k => (music ?? exercise.Music!)(k, hands)).ToList();
        var body = each[0].Bars.Count - exercise.Close;

        // Every key's bars without its close, one after another, and the last key's close to end on.
        Assert.Equal(keys.Count * body + exercise.Close, through.Bars.Count);
        Assert.Equal(Enumerable.Range(1, through.Bars.Count), through.Bars.Select(b => b.Number));
        Assert.Equal(each[0].Key.Fifths, through.Key.Fifths);
        // Each key's signature where it starts (a minor key after its relative major changes nothing printed), and its name.
        var signatures = through.WithSignatures().Select(s => s.Key.Fifths).ToList();
        for (var k = 0; k < order.Count; k++) Assert.True(signatures[k * body] == each[k].Key.Fifths, $"{where}: key {k} isn't printed in its signature.");
        Assert.Equal(order.Select(exercise.KeyName), through.Marks.Where(m => m.Kind == MarkKind.Text).Select(m => m.Text));

        // The notes are each key's own, in order, in time.
        var expected = through.Expected();
        var ticks = each[0].WithSignatures().Take(body).Sum(s => s.Bar.Length ?? s.Meter.BarTicks);
        for (var k = 0; k < order.Count; k++)
        {
            var own = each[k].Expected().Where(n => n.Bar <= body || k == order.Count - 1).Select(n => (n.Midi, Tick: n.Tick + k * ticks, n.Staff)).ToList();
            var there = expected.Where(n => n.Tick >= k * ticks && (k == order.Count - 1 || n.Tick < (k + 1) * ticks)).Select(n => (n.Midi, n.Tick, n.Staff)).ToList();
            Assert.True(own.SequenceEqual(there), $"{where}: key {k}'s notes aren't its own.");
        }

        // Each note is in the clef it's in when its key is played alone: a key starts from the staves' own clefs, whatever the
        // one before it left them in.
        foreach (var staff in new[] { 1, 2 })
        {
            var alone = order.SelectMany((_, k) => InClef(each[k], staff).Where(n => n.Bar <= body || k == order.Count - 1).Select(n => n.Clef)).ToList();
            Assert.True(alone.SequenceEqual(InClef(through, staff).Select(n => n.Clef)), $"{where}: a key starts in the clef the one before it ended in.");
        }

        // Ids are the sheet's handles: no two things share one, and every mark is on something there.
        var notes = through.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>())).ToList();
        var ids = notes.Select(n => n.Id).Concat(notes.Where(n => n.Tones.Count > 1).SelectMany(n => n.Tones.Select(t => t.Id))).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(through.Marks, m => Assert.True(ids.Contains(m.StartId) && (m.EndId is null || ids.Contains(m.EndId)), $"{where}: a mark points at nothing."));

        // Written out and read back, it asks for the same keys at the same times; and round a loop it's all but the close.
        var read = MeiReader.Read(MeiWriter.Write(through));
        Assert.Equal(expected.Select(n => (n.Midi, n.Tick, n.Length, n.Staff)), read.Expected().Select(n => (n.Midi, n.Tick, n.Length, n.Staff)));
        Assert.Equal(keys.Count * body, Exercises.Round(exercise, through).Count);
    }

    // A staff's notes and chords, each with its bar and the clef the staff is in when it's played: a clef changed to holds
    // until the next change.
    private static List<(int Bar, Clef Clef)> InClef(Score score, int staff)
    {
        List<(int, Clef)> placed = [];
        var clef = staff == 1 ? Clef.Treble : Clef.Bass;
        foreach (var bar in score.Bars)
        {
            foreach (var e in bar.Voices.Where(v => v.Staff == staff).OrderBy(v => v.Layer).SelectMany(v => v.Events))
            {
                if (e is ClefChange change) clef = change.Clef;
                if (e is Notes) placed.Add((bar.Number, clef));
            }
        }
        return placed;
    }

    [Fact]
    public void An_exercise_that_became_part_of_a_family_is_still_found_by_its_id()
    {
        // The two thumb exercises are one family: it has the first one's id, and the second's still opens it, as it was.
        Assert.Equal("thumb-three", Exercises.Find("thumb-four")!.Id);
        Assert.Equal(("thumb-three", "under=4"), ExerciseFamilies.Formerly("thumb-four"));
        Assert.Null(ExerciseFamilies.Formerly("thumb-three"));
        Assert.Null(Exercises.Find("thumb-six"));
        Assert.DoesNotContain(Exercises.All, e => e.Id == "thumb-four");
        // Every family is in the catalogue, in a section, after the exercises that were there.
        Assert.Equal(ExerciseFamilies.All.Select(e => e.Id), Exercises.All.Skip(Exercises.All.Count - ExerciseFamilies.All.Count).Select(e => e.Id));
        Assert.Equal(["thirds", "bounce", "octave-scale"], Exercises.All.Where(e => e.Section == "double").Select(e => e.Id));
    }

    // Its card in the catalogue shows its shape, how its notes move: every one to play has one, from its own music in C, in time
    // order and on the piano; a drill's is what a set of it is like. One to read has none.
    [Fact]
    public void Every_exercise_and_drill_to_play_has_a_shape_for_its_card()
    {
        foreach (var exercise in Exercises.All)
        {
            var shape = ExerciseShapes.Of(exercise);
            if (!exercise.ToPlay)
            {
                Assert.Empty(shape);
                continue;
            }
            // A held note is a shape too: wrist circles on one is a long bar or two.
            Assert.True(shape.Count >= 1, $"{exercise.Id} has no notes in its shape.");
            Assert.All(shape, n => Assert.True(n.Midi is >= 21 and <= 108 && n.Length > 0 && n.Start >= 0 && n.Hand is 1 or 2, exercise.Id));
            Assert.Same(shape, ExerciseShapes.Of(exercise));
        }
        // One for both hands has both in its shape; the left hand alone, the left's.
        Assert.Equal([1, 2], ExerciseShapes.Of(Exercises.Find("passed")!).Select(n => n.Hand).Distinct().Order());
        Assert.Equal([2], ExerciseShapes.Of(Exercises.Find("alberti")!).Select(n => n.Hand).Distinct());
        // A scale's is a ramp up: its first notes climb.
        var scale = ExerciseShapes.Of(Exercises.Find("scales")!).Where(n => n.Hand == 1).OrderBy(n => n.Start).Take(8).Select(n => n.Midi).ToList();
        Assert.Equal(scale.Order(), scale);
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
