using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Tests;

public class GeneratorTests
{
    public static TheoryData<int> Levels() => [.. Enumerable.Range(1, Ladder.Top)];

    private static IEnumerable<(Passage Passage, LevelSpec Spec)> Many(int level)
    {
        for (var step = 1; step <= 4; step++)
        {
            var spec = Ladder.Effective(level, step);
            for (ulong seed = 1; seed <= 25; seed++) yield return (PassageGenerator.Generate(spec, seed * 7919 + (ulong)step), spec);
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void EveryBarIsFullInEveryVoice(int level)
    {
        foreach (var (passage, _) in Many(level))
        {
            foreach (var (bar, _, meter) in passage.Score.WithSignatures())
            {
                var length = bar.Length ?? meter.BarTicks;
                foreach (var voice in bar.Voices)
                {
                    var at = 0;
                    foreach (var e in voice.Events.Where(e => e is not ClefChange))
                    {
                        Assert.True(e.Start == at, $"{passage.Description}, seed {passage.Seed}: bar {bar.Number} staff {voice.Staff} has a gap or overlap at {e.Start}");
                        at += e.Length;
                    }
                    Assert.True(at == length, $"{passage.Description}, seed {passage.Seed}: bar {bar.Number} staff {voice.Staff} lasts {at}, not {length}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void PassagesStayInsideTheirLevel(int level)
    {
        foreach (var (passage, spec) in Many(level))
        {
            var score = passage.Score;
            Assert.Contains(score.Key, spec.Keys);
            Assert.Contains(score.Meter, spec.Meters);
            Assert.InRange(score.Bars.Count(b => b.Number > 0), spec.Bars.Min, Math.Max(spec.Bars.Max, 6));
            var expected = score.Expected();
            Assert.NotEmpty(expected);
            Assert.All(expected, n => Assert.InRange(n.Midi, 21, 108));
            foreach (var bar in score.Bars)
            {
                foreach (var voice in bar.Voices.Where(v => !v.Events.OfType<ClefChange>().Any()))
                {
                    foreach (var tone in voice.Events.OfType<Notes>().SelectMany(n => n.Tones))
                    {
                        Assert.True(PassageGenerator.LedgerLines(tone.Pitch, voice.Staff) <= spec.LedgerLines + 1, $"{passage.Description}, seed {passage.Seed}: {tone.Pitch} on staff {voice.Staff} needs {PassageGenerator.LedgerLines(tone.Pitch, voice.Staff)} ledger lines");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void PassagesEndOnTheTonicAndWriteAsMei(int level)
    {
        foreach (var (passage, _) in Many(level))
        {
            var melody = passage.Score.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>().Select(n => (b.Number, v.Staff, n)))).Where(x => x.Number == passage.Score.Bars[^1].Number).ToList();
            var last = melody.Where(x => x.Staff == 1).Select(x => x.n).LastOrDefault() ?? melody.Last().n;
            Assert.Contains(last.Tones, t => ((t.Pitch.Midi % 12) + 12) % 12 == passage.Score.Key.TonicPitchClass);

            var mei = MeiWriter.Write(passage.Score);
            Assert.Equal(passage.Score.Expected(), MeiReader.Read(mei).Expected());
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void TheSameSeedWritesTheSamePassage(int level)
    {
        var spec = Ladder.Level(level);

        Assert.Equal(MeiWriter.Write(PassageGenerator.Generate(spec, 42).Score), MeiWriter.Write(PassageGenerator.Generate(spec, 42).Score));
        Assert.NotEqual(MeiWriter.Write(PassageGenerator.Generate(spec, 42).Score), MeiWriter.Write(PassageGenerator.Generate(spec, 43).Score));
    }

    [Fact]
    public void LevelOneIsOneHandInStepsOnLongNotes()
    {
        foreach (var (passage, _) in Many(1))
        {
            var notes = passage.Score.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>().Select(n => (v.Staff, n)))).ToList();
            Assert.Single(notes.Select(x => x.Staff).Distinct());
            Assert.All(notes, x => Assert.Contains(x.n.Length, new[] { 480, 960, 1920 }));
            var pitches = notes.Select(x => x.n.Tones[0].Pitch.Diatonic).ToList();
            Assert.All(pitches.Zip(pitches.Skip(1)), p => Assert.InRange(Math.Abs(p.First - p.Second), 0, 4));
        }
    }

    [Fact]
    public void MelodiesKeepToTheLevelsLeaps()
    {
        foreach (var level in new[] { 2, 3, 5 })
        {
            foreach (var (passage, spec) in Many(level))
            {
                var line = passage.Score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == 1).SelectMany(v => v.Events.OfType<Notes>())).Select(n => n.Tones[0].Pitch.Diatonic).ToList();
                if (spec.Hands != Hands.Together) continue;

                Assert.All(line.Zip(line.Skip(1)), p => Assert.InRange(Math.Abs(p.First - p.Second), 0, spec.MaxLeap));
            }
        }
    }

    [Fact]
    public void AStepBackUndoesTheLastKindOfDifficulty()
    {
        var full = Ladder.Effective(6, 4);
        var rhythmOnly = Ladder.Effective(6, 1);
        var below = Ladder.Effective(6, 0);

        Assert.Equal(Ladder.Level(6), full);
        Assert.Equal(Ladder.Level(6).Cells, rhythmOnly.Cells);
        Assert.Equal(Ladder.Level(5).Hands, rhythmOnly.Hands);
        Assert.Equal(Ladder.Level(5).Keys, rhythmOnly.Keys);
        Assert.Equal(Ladder.Level(5), below);
        Assert.Equal(Ladder.Level(6).Texture, Ladder.Effective(6, 2).Texture);
        Assert.Equal(Ladder.Level(5).Keys, Ladder.Effective(6, 2).Keys);
    }

    [Fact]
    public void FastFiguresBringTheTopTempoDown()
    {
        // Level 6 adds sixteenths: four a beat at five notes a second is 75 beats a minute.
        Assert.Equal(75, Ladder.Level(6).TopTempo);
        Assert.Equal(80, Ladder.Level(1).TopTempo);
        Assert.Equal(112, Ladder.Level(9).TopTempo);
    }
}

public class LedgerDrillTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ItemsReachTheStepsLedgerLinesAndNoFurtherFromInsideTheStaff(int step)
    {
        var rng = new Rng((ulong)(41 + step));
        var items = Enumerable.Range(0, 300).Select(_ => Prima.Core.Generation.Drills.Ledger(step, [Clef.Treble, Clef.Bass], rng)).ToList();
        int Lines(DrillItem item, int midi) => PassageGenerator.LedgerLines(new Key(0).Spell(midi), item.Score.Bars[0].Voices[0].Events.OfType<Notes>().Any() ? 1 : 2);
        var reach = items.Select(i => i.Answer.Max(a => Lines(i, a[0]))).ToList();

        Assert.All(reach, r => Assert.InRange(r, 1, step));
        Assert.Contains(step, reach);
        // Runs start inside the staff (or come back to it), and step by seconds, or by thirds only at step 3.
        var runs = items.Where(i => i.Answer.Count > 1).ToList();
        Assert.True(runs.Count > 100);
        Assert.All(runs, i => Assert.Equal(0, Math.Min(Lines(i, i.Answer[0][0]), Lines(i, i.Answer[^1][0]))));
        var steps = runs.SelectMany(i => i.Answer.Zip(i.Answer.Skip(1), (a, b) => Math.Abs(new Key(0).Spell(b[0]).Diatonic - new Key(0).Spell(a[0]).Diatonic))).Distinct().Order().ToList();
        Assert.Equal(step == 3 ? [1, 2] : [1], steps);
        // Different notes every time, in both clefs: not the same C over and over.
        Assert.True(items.SelectMany(i => i.Answer.Select(a => a[0])).Distinct().Count() >= 4 * step);
    }
}
