using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Scoring;

namespace Prima.Core.Tests;

public class ExerciseFamilyTests
{
    public static TheoryData<string> Families => new(ExerciseFamilies.All.Select(e => e.Id));

    private static readonly Key C = new(0);

    // What a hand plays, in order: each note or chord as its keys' names. A hand's second line comes after its first, bar by bar.
    private static List<string> Sounded(Score score, int staff, int? layer = null) =>
        [.. score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == staff && (layer is null || v.Layer == layer)).SelectMany(v => v.Events.OfType<Notes>())).Select(n => string.Join(" ", n.Tones.Select(t => t.Pitch.ToString())))];

    private static List<int?> Fingers(Score score, int staff, int? layer = null) =>
        [.. score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == staff && (layer is null || v.Layer == layer)).SelectMany(v => v.Events.OfType<Notes>())).SelectMany(n => n.Tones.Select(t => t.Finger))];

    private static List<Notes> Struck(Score score, int staff) => [.. score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == staff).SelectMany(v => v.Events.OfType<Notes>()))];

    [Fact]
    public void Each_family_is_an_exercise_with_choices_and_a_lesson()
    {
        Assert.Equal(ExerciseFamilies.All.Count, ExerciseFamilies.All.Select(e => e.Id).Distinct().Count());
        Assert.All(ExerciseFamilies.All, e =>
        {
            var family = ExerciseFamilies.Of(e.Id)!;
            Assert.True(e.Kind == ExerciseKind.Played && e.Music is not null && e.Hands.Count > 0 && e.How.Count > 0 && e.Notice.Length > 0 && e.Tempo is >= 40 and <= 120);
            Assert.NotEmpty(family.Variations);
            Assert.Equal(family.Variations.Count, family.Variations.Select(v => v.Key).Distinct().Count());
            Assert.All(family.Variations, v =>
            {
                Assert.True(v.Options.Count >= 2 && v.Options.Select(o => o.Key).Distinct().Count() == v.Options.Count, $"{e.Id}: {v.Key} needs at least two different options.");
                Assert.Contains(v.Options, o => o.Key == v.UsualKey);
                Assert.All(v.Options, o => Assert.DoesNotContain(o.Key, c => c is ';' or '='));
                // A choice about how two hands move against each other needs an exercise that offers both.
                Assert.True(!v.TogetherOnly || e.Hands.Contains(HandUse.Together));
            });
            Assert.StartsWith("https://www.youtube.com/watch?v=", ExerciseFamilies.LessonOf(e.Id)!.Url);
        });
        Assert.Null(ExerciseFamilies.Of("five"));
        Assert.Null(ExerciseFamilies.LessonOf("five"));
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_family_writes_music_that_can_be_played_in_every_variation_key_and_hand(string id)
    {
        var exercise = ExerciseFamilies.All.First(e => e.Id == id);
        var family = ExerciseFamilies.Of(id)!;
        foreach (var variant in family.Every())
        {
            foreach (var key in Exercises.KeysOf(exercise))
            {
                foreach (var hands in exercise.Hands)
                {
                    var score = family.Music(key, hands, variant);
                    var where = $"{id} ({variant}) in {key.Name}, {hands}";

                    // Every line fills its bars, one thing after another; a staff has one line or two.
                    foreach (var (bar, _, meter) in score.WithSignatures())
                    {
                        Assert.Equal([1, 2], bar.Voices.Select(v => v.Staff).Distinct());
                        Assert.All(bar.Voices.GroupBy(v => v.Staff), staff => Assert.Equal(Enumerable.Range(1, staff.Count()), staff.Select(v => v.Layer)));
                        Assert.True(bar.Voices.Count <= 4, $"{where}: bar {bar.Number} has more than two lines on a staff.");
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
                    Assert.Equal(hands != HandUse.Left, notes.Any(n => n.Staff == 1));
                    Assert.Equal(hands != HandUse.Right, notes.Any(n => n.Staff == 2));
                    foreach (var (_, n) in notes)
                    {
                        var keys = n.Tones.Select(t => t.Pitch.Midi).ToList();
                        Assert.True(keys.All(k => k is >= 21 and <= 108), $"{where}: {n.Id} is off the piano.");
                        Assert.True(keys.SequenceEqual(keys.Order()) && keys.Distinct().Count() == keys.Count && keys[^1] - keys[0] <= 12, $"{where}: {n.Id} isn't a chord one hand can take.");
                        Assert.All(n.Tones, t => Assert.True(t.Finger is null or (>= 1 and <= 5)));
                        Assert.True(n.Tones.Count(t => t.Finger is not null) == n.Tones.Where(t => t.Finger is not null).Select(t => t.Finger).Distinct().Count(), $"{where}: {n.Id} uses a finger twice.");
                        // Printed within three ledger lines of its staff, whatever clef and octave line it's under.
                        Assert.True(n.Tones.Select(t => t.Ottava).Distinct().Count() == 1, $"{where}: {n.Id} is half under an octave line.");
                    }

                    // The two hands never want the same key at once.
                    var expected = score.Expected();
                    Assert.All(expected.GroupBy(e => (e.Tick, e.Midi)), g => Assert.True(g.Count() == 1, $"{where}: two notes ask for key {g.Key.Midi} at {g.Key.Tick}."));

                    // Ids are the sheet's handles: no two things share one, and every mark is on something there.
                    var ids = notes.Select(n => n.Notes.Id).Concat(notes.Where(n => n.Notes.Tones.Count > 1).SelectMany(n => n.Notes.Tones.Select(t => t.Id))).ToList();
                    Assert.Equal(ids.Count, ids.Distinct().Count());
                    Assert.All(score.Marks, m => Assert.True(ids.Contains(m.StartId) && (m.EndId is null || ids.Contains(m.EndId)), $"{where}: a mark points at nothing."));

                    // Written out and read back, it asks for the same keys at the same times.
                    var read = MeiReader.Read(MeiWriter.Write(score));
                    Assert.Equal(expected.Select(n => (n.Midi, n.Tick, n.Length, n.Staff)), read.Expected().Select(n => (n.Midi, n.Tick, n.Length, n.Staff)));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_familys_own_music_is_its_usual_form_and_stays_on_its_staves(string id)
    {
        var exercise = ExerciseFamilies.All.First(e => e.Id == id);
        var family = ExerciseFamilies.Of(id)!;
        foreach (var key in Exercises.KeysOf(exercise))
        {
            foreach (var hands in exercise.Hands)
            {
                Assert.Equal(family.Music(key, hands, family.Usual).Expected(), exercise.Music!(key, hands).Expected());
            }
        }

        // No note is printed more than three ledger lines from its staff: a line that travels changes clef or goes under an octave
        // line first.
        foreach (var variant in family.Every())
        {
            foreach (var key in Exercises.KeysOf(exercise))
            {
                foreach (var hands in exercise.Hands)
                {
                    foreach (var bar in family.Music(key, hands, variant).Bars)
                    {
                        foreach (var staff in bar.Voices.GroupBy(v => v.Staff))
                        {
                            // The clef in force is the last one changed to on the staff; these change only in a hand's single line.
                            foreach (var voice in staff)
                            {
                                var clef = Clefs(family.Music(key, hands, variant), staff.Key, bar.Number);
                                foreach (var e in voice.Events)
                                {
                                    if (e is ClefChange change) clef = change.Clef;
                                    if (e is not Notes n) continue;

                                    foreach (var tone in n.Tones)
                                    {
                                        var place = tone.Written.Diatonic;
                                        // The treble staff's lines run from E4 to F5, the bass staff's from G2 to A3: three ledger lines is six places past them.
                                        var (bottom, top) = clef == Clef.Treble ? (4 * 7 + 2, 5 * 7 + 3) : (2 * 7 + 4, 3 * 7 + 5);
                                        Assert.True(place >= bottom - 7 && place <= top + 7, $"{id} ({variant}) in {key.Name}, {hands}: {tone.Pitch} is printed far off its staff in bar {bar.Number}.");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // The clef a staff is in as a bar starts.
        static Clef Clefs(Score score, int staff, int barNumber)
        {
            var clef = staff == 1 ? Clef.Treble : Clef.Bass;
            foreach (var change in score.Bars.Where(b => b.Number < barNumber).SelectMany(b => b.Voices.Where(v => v.Staff == staff).SelectMany(v => v.Events.OfType<ClefChange>()))) clef = change.Clef;
            return clef;
        }
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void Played_as_written_a_family_is_clean_and_its_touch_is_right(string id)
    {
        const double BeatNs = 1e9;
        static long At(double beats) => (long)(beats * BeatNs);
        var exercise = ExerciseFamilies.All.First(e => e.Id == id);
        var family = ExerciseFamilies.Of(id)!;
        foreach (var variant in family.Every())
        {
            foreach (var hands in exercise.Hands)
            {
                var score = family.Music(C, hands, variant);
                var expected = score.Expected();
                // Each key down for as long as it's written (a detached one a quarter of a beat), up in time to be struck again, an accent harder.
                var played = expected.Select(e =>
                {
                    var again = expected.Where(n => n.Midi == e.Midi && n.Tick > e.Tick).Select(n => n.Tick).DefaultIfEmpty(long.MaxValue).Min();
                    var up = Math.Min(e.Tick + (e.Articulation == Articulation.Staccato ? Math.Min(e.Length, 120) : e.Length), again - 30);
                    return new PlayedNote(e.Midi, At(e.Tick / 480.0), At(up / 480.0), e.Articulation == Articulation.Accent ? 90 : 64);
                }).ToList();
                var result = PassageScorer.Score(expected, At, Ticks.Quarter, BeatNs, score.Meter.BarTicks / 480.0 * BeatNs, played, 0, markings: Markings.Of(score, expected));

                var where = $"{id} ({variant}), {hands}";
                Assert.True(result.Clean, $"{where}: {result.Wrong} wrong, {result.Missed} missed, {result.Extra} extra.");
                Assert.True(result.LetGoEarly == 0, $"{where}: {result.LetGoEarly} notes let go early: {string.Join(", ", result.Notes.Where(n => n.LetGoEarly).Select(n => n.Id))}.");
                Assert.True(result.Touches == 0, $"{where}: touch judged wrong on {string.Join(", ", result.Notes.Where(n => n.Touch is not null).Select(n => $"{n.Id} {n.Touch}"))}.");
            }
        }
    }

    [Fact]
    public void A_kept_choice_is_read_as_far_as_it_still_means_something()
    {
        var bounce = ExerciseFamilies.Of("bounce")!;
        Assert.Equal("interval=sixth;quick=2", bounce.Usual.ToString());
        Assert.Equal("interval=octave;quick=4", bounce.Read("interval=octave;quick=4").ToString());
        // An option that's gone, a variation that's gone, and nonsense fall back to the usual.
        Assert.Equal("interval=third;quick=2", bounce.Read("quick=9;interval=third;colour=blue;;=").ToString());
        Assert.Equal(bounce.Usual.ToString(), bounce.Read(null).ToString());
        Assert.Equal("interval=sixth;quick=3", bounce.Usual.With("quick", "3").ToString());
        Assert.Equal(20, bounce.Every().Select(v => v.ToString()).Distinct().Count());

        Assert.Equal("Sixths · Two", bounce.Describe(bounce.Usual, HandUse.Right));
        // How the hands move against each other is a choice only when there are two of them.
        var thirds = ExerciseFamilies.Of("thirds")!;
        Assert.Equal(["where"], thirds.For(HandUse.Left).Select(v => v.Key));
        Assert.Equal(["where", "motion"], thirds.For(HandUse.Together).Select(v => v.Key));
        Assert.Equal("In one place", thirds.Describe(thirds.Usual, HandUse.Right));
        Assert.Equal("In one place · The same way", thirds.Describe(thirds.Usual, HandUse.Together));
    }

    [Fact]
    public void The_scale_parts_in_the_middle_and_each_hand_keeps_its_fingering()
    {
        var scale = FamilyMusic.PartingScale(C, HandUse.Together);
        var (right, left) = (Sounded(scale, 1), Sounded(scale, 2));

        // Up an octave together, apart an octave and in, on up, back, apart and in, and home: the turns are seven notes apart.
        Assert.Equal(["C4", "C5", "C6", "C5", "C6", "C5", "C6", "C5", "C4"], Enumerable.Range(0, 9).Select(leg => right[7 * leg]));
        Assert.Equal(["C2", "C3", "C2", "C3", "C4", "C3", "C2", "C3", "C2"], Enumerable.Range(0, 9).Select(leg => left[7 * leg]));
        Assert.Equal(57, right.Count);
        Assert.Equal(8, scale.Bars.Count);
        // The right hand's top takes the little finger and the left's bottom too; a tonic passed through takes the thumb.
        Assert.Equal([1, 2, 3, 1, 2, 3, 4, 1, 2, 3, 1, 2, 3, 4, 5, 4, 3, 2, 1, 3, 2, 1], Fingers(scale, 1).Take(22));
        Assert.Equal([5, 4, 3, 2, 1, 3, 2, 1, 2, 3, 1, 2, 3, 4, 5, 4, 3, 2, 1, 3, 2, 1], Fingers(scale, 2).Take(22));
        Assert.Empty(scale.Marks);

        // Two octaves each way is the four-octave form: the hands an octave apart, the left hand's top in the treble clef and the
        // right's under an octave line.
        var full = FamilyMusic.PartingScale(C, HandUse.Together, octaves: 2);
        Assert.Equal(["C3", "C5", "C7", "C5", "C7", "C5", "C7", "C5", "C3"], Enumerable.Range(0, 9).Select(leg => Sounded(full, 1)[14 * leg]));
        Assert.Equal(["C2", "C4", "C2", "C4", "C6", "C4", "C2", "C4", "C2"], Enumerable.Range(0, 9).Select(leg => Sounded(full, 2)[14 * leg]));
        Assert.Equal(15, full.Bars.Count);
        Assert.Contains(full.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == 2).SelectMany(v => v.Events.OfType<ClefChange>())), c => c.Clef == Clef.Treble);
        Assert.Equal(1, Struck(full, 1)[28].Tones[0].Ottava);
        Assert.Equal(0, Struck(full, 1)[14].Tones[0].Ottava);

        // A minor key's is the harmonic minor, in each hand's own fingering.
        Assert.Equal(["A4", "B4", "C5", "D5", "E5", "F5", "G♯5", "A5"], Sounded(FamilyMusic.PartingScale(new Key(0, true), HandUse.Together), 1).Take(8));
        Assert.Equal([2, 3, 1, 2, 3, 4, 1, 2], Fingers(FamilyMusic.PartingScale(new Key(-5), HandUse.Together), 1).Take(8));

        // Detached, every note but the last is short; soft and loud are marked at the start; growing and fading follows the hands out and back.
        var detached = FamilyMusic.PartingScale(C, HandUse.Together, touch: Articulation.Staccato);
        Assert.Equal(56, Struck(detached, 1).Count(n => n.Articulation == Articulation.Staccato));
        Assert.Equal(Articulation.None, Struck(detached, 2)[^1].Articulation);
        Assert.Equal(["f"], FamilyMusic.PartingScale(C, HandUse.Together, loudness: Shading.Loud).Marks.Select(m => m.Text));
        var swell = FamilyMusic.PartingScale(C, HandUse.Together, loudness: Shading.Swell).Marks;
        Assert.Equal([MarkKind.Dynamic, MarkKind.Crescendo, MarkKind.Diminuendo, MarkKind.Crescendo, MarkKind.Diminuendo, MarkKind.Crescendo, MarkKind.Diminuendo], swell.Select(m => m.Kind));
        Assert.Equal([("r0", "r14"), ("r14", "r21"), ("r21", "r28"), ("r28", "r35"), ("r35", "r42"), ("r42", "r56")], swell.Skip(1).Select(m => (m.StartId, m.EndId!)));
    }

    [Fact]
    public void Counted_in_fours_an_arpeggios_lean_goes_round_the_chord()
    {
        static List<string> Leaned(Score score) => [.. Struck(score, 1).Where(n => n.Articulation == Articulation.Accent).Select(n => n.Tones[0].Pitch.ToString())];

        // In fours, in three: over four octaves the lean falls on each note of the chord in turn, going up and coming down.
        var fours = FamilyMusic.GroupedArpeggio(C, HandUse.Right, group: 4, octaves: 4);
        Assert.Equal(new Meter(3, 4), fours.Meter);
        Assert.Equal(["C3", "E4", "G5", "C7", "G5", "E4"], Leaned(fours).Take(6));
        Assert.Equal(["C4", "E5", "E5"], Leaned(FamilyMusic.GroupedArpeggio(C, HandUse.Right)).Take(3));
        Assert.All(fours.Expected().Where(n => n.Articulation == Articulation.Accent), n => Assert.Equal(0, n.Tick % Ticks.Quarter));
        Assert.Equal(Ticks.Sixteenth, fours.Expected()[0].Length);

        // In threes, in four: always the tonic, which is the point of counting it the other way.
        var threes = FamilyMusic.GroupedArpeggio(C, HandUse.Right, group: 3);
        Assert.Equal(new Meter(4, 4), threes.Meter);
        Assert.Equal(["C4", "C5", "C6", "C5", "C4", "C5", "C6", "C5"], Leaned(threes));
        Assert.Equal([0L, 160, 320, 480], threes.Expected().Take(4).Select(n => n.Tick));

        // The scale book's fingers, up and back twice, and the tonic held; a minor key's chord is minor.
        Assert.Equal([1, 2, 3, 1, 2, 3, 5, 3, 2, 1, 3, 2, 1, 2], Fingers(FamilyMusic.GroupedArpeggio(C, HandUse.Right), 1).Take(14));
        Assert.Equal(25, Sounded(FamilyMusic.GroupedArpeggio(C, HandUse.Left), 2).Count);
        Assert.Equal(["A4", "C5", "E5"], Sounded(FamilyMusic.GroupedArpeggio(new Key(0, true), HandUse.Right), 1).Take(3));

        // A marked note no stronger than its neighbours is what Prima points out.
        var expected = fours.Expected();
        var flat = expected.Select((e, i) => new PlayedNote(e.Midi, (long)(e.Tick / 480.0 * 1e9), (long)((e.Tick + e.Length) / 480.0 * 1e9), 64 + i % 4)).ToList();
        var result = PassageScorer.Score(expected, beats => (long)(beats * 1e9), Ticks.Quarter, 1e9, 3e9, flat, 0, markings: Markings.Of(fours, expected));
        Assert.Contains(result.Notes, n => n.Touch == Touch.AccentNotStronger);
    }

    [Fact]
    public void The_thumb_passes_under_two_to_five_fingers()
    {
        // Under three and four it's the music the two exercises had before they were one family.
        foreach (var fingers in new[] { 3, 4 })
        {
            foreach (var hands in new[] { HandUse.Right, HandUse.Left })
            {
                Assert.Equal(MeiWriter.Write(ExerciseMusic.ThumbUnder(C, hands, fingers)), MeiWriter.Write(FamilyMusic.ThumbUnder(C, hands, fingers)));
            }
        }

        var two = FamilyMusic.ThumbUnder(C, HandUse.Right, 2);
        Assert.Equal(new Meter(2, 4), two.Meter);
        Assert.Equal(["C4", "D4", "E4", "D4", "C4"], Sounded(two, 1).Take(5));
        Assert.Equal([1, 2, 1, 2, 1], Fingers(two, 1).Take(5));
        Assert.Equal(5, two.Bars.Count);

        var five = FamilyMusic.ThumbUnder(C, HandUse.Left, 5);
        Assert.Equal(new Meter(5, 4), five.Meter);
        Assert.Equal(["C3", "B2", "A2", "G2", "F2", "E2", "F2", "G2", "A2", "B2", "C3"], Sounded(five, 2).Take(11));
        Assert.Equal([1, 2, 3, 4, 5, 1, 5, 4, 3, 2, 1], Fingers(five, 2).Take(11));
        Assert.Equal("thumb-three", ExerciseFamilies.All.Single(e => e.Section == "thumb").Id);
        Assert.Equal("under=3", ExerciseFamilies.Of("thumb-three")!.Usual.ToString());
    }

    [Fact]
    public void Thirds_go_three_up_and_back_alike_or_in_mirror()
    {
        var alike = FamilyMusic.Thirds(C, HandUse.Together);
        Assert.Equal(["C4 E4", "D4 F4", "E4 G4", "D4 F4", "C4 E4"], Sounded(alike, 1).Take(5));
        Assert.Equal(["C3 E3", "D3 F3", "E3 G3", "D3 F3", "C3 E3"], Sounded(alike, 2).Take(5));
        Assert.Equal([1, 3, 2, 4, 3, 5, 2, 4], Fingers(alike, 1).Take(8));
        Assert.Equal([5, 3, 4, 2, 3, 1, 4, 2], Fingers(alike, 2).Take(8));
        Assert.Equal(3, alike.Bars.Count);
        Assert.Equal("C4 E4", Sounded(alike, 1)[^1]);

        // In mirror the left hand goes the other way, so the same fingers play together.
        var mirror = FamilyMusic.Thirds(C, HandUse.Together, mirror: true);
        Assert.Equal(["E3 G3", "D3 F3", "C3 E3", "D3 F3", "E3 G3"], Sounded(mirror, 2).Take(5));
        Assert.Equal([3, 1, 4, 2, 5, 3, 4, 2], Fingers(mirror, 2).Take(8));
        Assert.Equal(Sounded(alike, 1), Sounded(mirror, 1));
        // Alone, the left hand plays its thirds from the bottom up whichever is chosen.
        Assert.Equal(Sounded(FamilyMusic.Thirds(C, HandUse.Left), 2), Sounded(FamilyMusic.Thirds(C, HandUse.Left, mirror: false), 2));

        // Up the scale: a bar on each step, the third an octave up held, a slur over each bar.
        var climb = FamilyMusic.Thirds(C, HandUse.Right, climb: true);
        Assert.Equal(8, climb.Bars.Count);
        Assert.Equal(["C4 E4", "D4 F4", "E4 G4", "F4 A4", "G4 B4", "A4 C5", "B4 D5", "C5 E5"], Enumerable.Range(0, 8).Select(bar => Sounded(climb, 1)[8 * bar]));
        Assert.Equal([("r0", "r7"), ("r8", "r15")], climb.Marks.Take(2).Select(m => (m.StartId, m.EndId!)));
        Assert.Equal(7, climb.Marks.Count(m => m.Kind == MarkKind.Slur));
        Assert.Equal(14, FamilyMusic.Thirds(C, HandUse.Together, climb: true).Marks.Count);
    }

    [Theory]
    [InlineData(DoubleNote.Third, "C4 E4", "G4 B4", 2, 4)]
    [InlineData(DoubleNote.Fourth, "G3 C4", "D4 G4", 1, 4)]
    [InlineData(DoubleNote.Fifth, "C4 G4", "G4 D5", 1, 5)]
    [InlineData(DoubleNote.Sixth, "E4 C5", "B4 G5", 1, 5)]
    [InlineData(DoubleNote.Octave, "C4 C5", "G4 G5", 1, 5)]
    public void A_double_note_bounces_up_five_steps_and_back(DoubleNote interval, string first, string furthest, int low, int high)
    {
        var bounce = FamilyMusic.Bounce(C, HandUse.Right, interval);
        var struck = Sounded(bounce, 1);
        // Two quick ones and the long one to a bar, five bars up and four back, and the first held.
        Assert.Equal(new Meter(3, 4), bounce.Meter);
        Assert.Equal(10, bounce.Bars.Count);
        Assert.Equal(first, struck[0]);
        Assert.Equal(furthest, struck[12]);
        Assert.Equal(first, struck[^1]);
        Assert.Equal(28, struck.Count);
        Assert.Equal([low, high], Fingers(bounce, 1).Take(2));
        Assert.Equal([high, low], Fingers(FamilyMusic.Bounce(C, HandUse.Left, interval), 2).Take(2));

        // Every one of them is the same interval, in every major key: none falls on the scale's odd one out.
        foreach (var key in Technique.Order.Where(k => !k.Minor))
        {
            var sizes = Struck(FamilyMusic.Bounce(key, HandUse.Right, interval), 1).Select(n => n.Tones[1].Pitch.Midi - n.Tones[0].Pitch.Midi).Distinct().ToList();
            Assert.True(interval switch { DoubleNote.Third => sizes.All(s => s is 3 or 4), DoubleNote.Fourth => sizes.SequenceEqual([5]), DoubleNote.Fifth => sizes.SequenceEqual([7]), DoubleNote.Sixth => sizes.All(s => s is 8 or 9), _ => sizes.SequenceEqual([12]) }, $"{interval} in {key.Name}: {string.Join(", ", sizes)} semitones.");
        }
    }

    [Fact]
    public void A_bounce_has_one_to_four_quick_notes_before_the_long_one()
    {
        static List<(long Tick, int Length)> FirstBar(int quick) => [.. FamilyMusic.Bounce(C, HandUse.Right, quick: quick).Expected().Where(n => n.Bar == 1 && n.Midi == 64).Select(n => (n.Tick, n.Length))];

        Assert.Equal([(240L, 240), (480, 480)], FirstBar(1));
        Assert.Equal([(0L, 240), (240, 240), (480, 480)], FirstBar(2));
        Assert.Equal([(0L, 160), (160, 160), (320, 160), (480, 480)], FirstBar(3));
        Assert.Equal([(0L, 120), (120, 120), (240, 120), (360, 120), (480, 480)], FirstBar(4));
        // The left hand is an octave under the right, and both end where they began.
        Assert.Equal(["E3 C4", "E3 C4", "E3 C4", "F3 D4"], Sounded(FamilyMusic.Bounce(C, HandUse.Together), 2).Take(4));
    }

    [Fact]
    public void A_scale_in_octaves_goes_up_and_back_detached()
    {
        var one = FamilyMusic.OctaveScale(C, HandUse.Together);
        Assert.Equal(["C4 C5", "D4 D5", "E4 E5"], Sounded(one, 1).Take(3));
        Assert.Equal(["C2 C3", "D2 D3", "E2 E3"], Sounded(one, 2).Take(3));
        Assert.Equal("C5 C6", Sounded(one, 1)[7]);
        Assert.Equal(15, Sounded(one, 1).Count);
        Assert.Equal(2, one.Bars.Count);
        Assert.Equal(14, Struck(one, 1).Count(n => n.Articulation == Articulation.Staccato));
        Assert.Equal([1, 5], Fingers(one, 1).Take(2));
        Assert.Equal([5, 1], Fingers(one, 2).Take(2));
        Assert.All(Fingers(one, 1).Skip(2), f => Assert.Null(f));

        // Two octaves: the top of the right hand's goes under an octave line.
        var two = FamilyMusic.OctaveScale(C, HandUse.Right, octaves: 2);
        Assert.Equal(29, Sounded(two, 1).Count);
        Assert.Equal("C6 C7", Sounded(two, 1)[14]);
        Assert.Equal([1, 1], Struck(two, 1)[14].Tones.Select(t => t.Ottava));
        Assert.Equal(Ticks.Half, Struck(two, 1)[^1].Length);
    }

    [Fact]
    public void Trill_bursts_stop_on_a_long_note_and_turn_round_at_the_top()
    {
        var trill = FamilyMusic.TrillBursts(C, HandUse.Right);
        var notes = Sounded(trill, 1);
        Assert.Equal(["C4", "D4", "C4", "D4", "C4", "D4", "E4", "D4", "E4", "D4"], notes.Take(10));
        // The eighth burst is on the tonic an octave up; then the upper note starts, and it comes down a step at a time.
        Assert.Equal(["C5", "D5", "C5", "D5", "C5", "D5", "C5", "D5", "C5", "D5", "C5", "B4", "C5", "B4", "C5"], notes.Skip(35).Take(15));
        Assert.Equal(["D4", "C4", "D4", "C4", "D4", "C4"], notes.Skip(75));
        Assert.Equal(9, trill.Bars.Count);
        Assert.Equal([120, 120, 120, 120, 480], trill.Expected().Take(5).Select(n => n.Length));
        Assert.Equal([2, 3, 2, 3, 2], Fingers(trill, 1).Take(5));
        Assert.Equal([3, 2, 3, 2, 3], Fingers(trill, 1).Skip(40).Take(5));

        // Each hand alone uses the pair itself; together the left takes the opposite pair on the same notes.
        Assert.Equal([1, 2, 1, 2, 1], Fingers(FamilyMusic.TrillBursts(C, HandUse.Right, pair: 1), 1).Take(5));
        Assert.Equal([2, 1, 2, 1, 2], Fingers(FamilyMusic.TrillBursts(C, HandUse.Left, pair: 1), 2).Take(5));
        var together = FamilyMusic.TrillBursts(C, HandUse.Together, pair: 1);
        Assert.Equal([5, 4, 5, 4, 5], Fingers(together, 2).Take(5));
        Assert.Equal(["C3", "D3", "C3", "D3", "C3"], Sounded(together, 2).Take(5));
        Assert.Equal([2, 1, 2, 1, 2], Fingers(FamilyMusic.TrillBursts(C, HandUse.Together, pair: 4), 2).Take(5));
    }

    [Fact]
    public void Slurs_walk_up_the_scale_with_one_pair_of_fingers()
    {
        var slurs = FamilyMusic.SlurWalk(C, HandUse.Right);
        Assert.Equal(["C4", "D4", "D4", "E4", "E4", "F4"], Sounded(slurs, 1).Take(6));
        // Coming down, each pair starts on its upper note.
        Assert.Equal(["B4", "C5", "C5", "B4", "B4", "A4"], Sounded(slurs, 1).Skip(12).Take(6));
        Assert.Equal(["D4", "C4", "C4"], Sounded(slurs, 1).Skip(26));
        Assert.Equal([2, 3, 2, 3], Fingers(slurs, 1).Take(4));
        Assert.Equal([3, 2, 3, 2], Fingers(slurs, 1).Skip(14).Take(4));
        Assert.Equal(8, slurs.Bars.Count);
        // Each pair is joined and its second note let go.
        Assert.Equal(14, slurs.Marks.Count(m => m.Kind == MarkKind.Slur));
        Assert.Equal(("r2", "r3"), (slurs.Marks[1].StartId, slurs.Marks[1].EndId));
        Assert.Equal(14, Struck(slurs, 1).Count(n => n.Articulation == Articulation.Staccato));
        Assert.Equal(Enumerable.Range(0, 14).Select(i => 2 * i + 1), Struck(slurs, 1).Select((n, i) => (n, i)).Where(x => x.n.Articulation == Articulation.Staccato).Select(x => x.i));
        Assert.Equal([3, 2, 3, 2], Fingers(FamilyMusic.SlurWalk(C, HandUse.Left), 2).Take(4));
        Assert.Equal([4, 3, 4, 3], Fingers(FamilyMusic.SlurWalk(C, HandUse.Together), 2).Take(4));
        Assert.Equal(28, FamilyMusic.SlurWalk(C, HandUse.Together).Marks.Count);
    }

    [Fact]
    public void A_key_is_held_while_its_neighbour_is_played_three_times()
    {
        var turn = FamilyMusic.HoldAndTurn(C, HandUse.Right);
        var expected = turn.Expected();
        // Bar 1: C held for the bar, D on its second, third and fourth beats.
        Assert.Equal([(60, 0L, 1920), (62, 480, 480), (62, 960, 480), (62, 1440, 480)], expected.Where(n => n.Bar == 1).Select(n => (n.Midi, n.Tick, n.Length)));
        // Up the hand each key is held in turn under the one played, then back down over it; and the tonic to end.
        Assert.Equal(["C4", "D4", "E4", "F4"], Sounded(turn, 1, layer: 2).Take(4));
        Assert.Equal(["G4", "F4", "E4", "D4", "C4"], Sounded(turn, 1, layer: 1).Skip(12));
        Assert.Equal(9, turn.Bars.Count);
        Assert.Equal([2, null, null, 3, null, null], Fingers(turn, 1, layer: 1).Take(6));
        Assert.Equal([1, 2, 3, 4], Fingers(turn, 1, layer: 2).Take(4));
        // The held key is the lower line while the hand goes up, the upper while it comes down.
        Assert.Equal(Ticks.Whole, Struck(turn, 1).First(n => n.Id == "rh0").Length);
        Assert.Equal(["rh5", "rh6", "rh7"], turn.Bars[4].Voices.Single(v => v.Staff == 1 && v.Layer == 2).Events.OfType<Notes>().Select(n => n.Id));

        // A finger further: the thumb's key under the third finger's, and so on; the left hand's fingers are its own.
        var wide = FamilyMusic.HoldAndTurn(C, HandUse.Left, reach: 2);
        Assert.Equal([(48, 0L, 1920), (52, 480, 480), (52, 960, 480), (52, 1440, 480)], wide.Expected().Where(n => n.Bar == 1).Select(n => (n.Midi, n.Tick, n.Length)));
        Assert.Equal(7, wide.Bars.Count);
        Assert.Equal([5, 4, 3], Fingers(wide, 2, layer: 2).Take(3));
    }

    [Fact]
    public void One_finger_holds_while_the_other_four_play()
    {
        var each = FamilyMusic.HoldOne(C, HandUse.Right);
        Assert.Equal(6, each.Bars.Count);
        // The thumb holds C while the others go up and back; then the second finger holds D, and so on up the hand.
        Assert.Equal([(60, 0L, 1920), (62, 0, 240), (64, 240, 240), (65, 480, 240), (67, 720, 240), (65, 960, 240), (64, 1200, 240), (62, 1440, 240)], each.Expected().Where(n => n.Bar == 1).Select(n => (n.Midi, n.Tick, n.Length)));
        Assert.Equal([60, 62, 64, 65, 67, 65, 64, 60], each.Expected().Where(n => n.Bar == 2).Select(n => n.Midi));
        Assert.Equal([(62, 1920L, 1920)], each.Expected().Where(n => n.Bar == 2 && n.Length == Ticks.Whole).Select(n => (n.Midi, n.Tick, n.Length)));
        Assert.Equal([60, 62, 64, 65, 67, 60], each.Expected().Where(n => n.Length == Ticks.Whole).Select(n => n.Midi));

        // One finger chosen: its own key in each hand, twice, and the tonic.
        var fourth = FamilyMusic.HoldOne(C, HandUse.Right, holding: 4);
        Assert.Equal(3, fourth.Bars.Count);
        Assert.Equal([65, 65, 60], fourth.Expected().Where(n => n.Length == Ticks.Whole).Select(n => n.Midi));
        var left = FamilyMusic.HoldOne(C, HandUse.Left, holding: 4);
        Assert.Equal([50, 50, 48], left.Expected().Where(n => n.Length == Ticks.Whole).Select(n => n.Midi));
        Assert.Equal(4, Fingers(left, 2, layer: 2)[0]);
        Assert.Equal([48, 52, 53, 55, 53, 52, 48], left.Expected().Where(n => n.Bar == 1 && n.Length == Ticks.Eighth).Select(n => n.Midi));
    }

    [Fact]
    public void Every_exercise_to_play_says_what_it_trains()
    {
        var ids = Exercises.All.Concat(ExerciseFamilies.All).Select(e => e.Id).ToHashSet();
        Assert.All(Exercises.All.Concat(ExerciseFamilies.All).Where(e => e.ToPlay), e => Assert.True(ExerciseFocus.Of(e.Id).Count is >= 1 and <= 3, $"{e.Id} trains {ExerciseFocus.Of(e.Id).Count} things."));
        Assert.All(Exercises.All.Where(e => !e.ToPlay), e => Assert.Empty(ExerciseFocus.Of(e.Id)));
        Assert.All(ExerciseFocus.Known, id => Assert.Contains(id, ids));
        Assert.All(ExerciseFocus.All, f => Assert.Contains(ids, id => ExerciseFocus.Trains(id, f.Key)));
        Assert.Equal(ExerciseFocus.All.Count, ExerciseFocus.All.Select(f => f.Key).Distinct().Count());
        Assert.Equal(["Finger strength", "The thumb"], ExerciseFocus.Of("arpeggio-groups").Select(f => f.Name));
        Assert.True(ExerciseFocus.Trains("bounce", "wrist") && !ExerciseFocus.Trains("bounce", "pedal"));
    }
}
