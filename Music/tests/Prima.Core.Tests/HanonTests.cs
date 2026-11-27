using System.Security.Cryptography;
using System.Text;
using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Routines;

namespace Prima.Core.Tests;

public class HanonTests
{
    [Fact]
    public void Every_written_attack_and_printed_repeat_matches_the_independent_playback_map()
    {
        var reference = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "hanon-reference.txt"));
        Assert.Equal(60, reference.Length);
        Assert.Equal(Enumerable.Range(1, 60), Hanon.All.Select(e => e.HanonNumber!.Value));
        foreach (var line in reference)
        {
            var fields = line.Split(' ');
            var n = int.Parse(fields[0]);
            var score = Hanon.Printed(n);
            var notes = score.Expected();
            Assert.Equal(int.Parse(fields[1]), score.Bars.Count);
            var order = Hanon.PlayOrder(score);
            Assert.Equal(int.Parse(fields[2]), order.Count);
            Assert.Equal(int.Parse(fields[3]), notes.Count);
            var events = notes.OrderBy(e => e.Tick).ThenBy(e => e.Staff).ThenBy(e => e.Midi).ThenBy(e => e.Length);
            var digest = Hash(string.Concat(events.Select(e => $"{e.Tick},{e.Staff},{e.Midi},{e.Length}\n")));
            Assert.True(fields[4] == digest, $"No. {n} differs from the reference pitches, durations, hands or onsets.");
            Assert.Equal(fields[5], Hash(string.Join(',', order)));

            var read = MeiReader.Read(MeiWriter.Write(score));
            Assert.Equal(notes.Select(e => (e.Midi, e.Staff, e.Tick, e.Length, e.Finger)), read.Expected().Select(e => (e.Midi, e.Staff, e.Tick, e.Length, e.Finger)));
        }
        static string Hash(string input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    [Fact]
    public void The_printed_scales_and_tremolo_keep_their_fingers_relative_minors_and_held_notes()
    {
        var c = Hanon.Music(39, new(0), HandUse.Right);
        Assert.Equal([48, 50, 52, 53, 55, 57, 59, 60], c.Expected().Take(8).Select(e => e.Midi));
        Assert.Equal([1, 2, 3, 1, 2, 3, 4, 1], c.Expected().Take(8).Select(e => e.Finger));
        Assert.Equal(17, c.PlayOrder().Count);
        var a = Hanon.Music(39, new(0,true), HandUse.Left);
        Assert.Equal(20, a.Bars.Count);
        Assert.Contains(a.Expected(), e => e.Bar == 1 && e.Midi % 12 == 5);
        Assert.Contains(a.Expected(), e => e.Bar == 11 && e.Midi % 12 == 6);
        Assert.Equal(24, Hanon.Keys(39).Count);
        var tremolo = Hanon.Printed(60);
        var first = tremolo.Expected().Where(e => e.Tick <= 60 && e.Staff == 1).ToList();
        Assert.Equal([64, 67, 60], first.Select(e => e.Midi));
        Assert.Equal([2, 4, 1], first.Select(e => e.Finger));
        Assert.Contains(tremolo.Expected(), e => e.Bar == 16 && e.Midi == 72 && e.Length == 540);
        Assert.Contains(tremolo.Marks, m => m.Kind == MarkKind.Dynamic && m.Text == "p");
        Assert.Contains(tremolo.Marks, m => m.Kind == MarkKind.Diminuendo);
        Assert.Equal(21, tremolo.Expected().Min(e => e.Midi));
    }

    [Fact]
    public void Offered_keys_fit_the_piano_and_single_hand_keeps_the_original_timing()
    {
        foreach (var study in Hanon.All)
        {
            foreach (var key in Exercises.KeysOf(study))
            {
                var together = study.Music!(key, HandUse.Together);
                Assert.All(together.Expected(), e => Assert.InRange(e.Midi, 21, 108));
                foreach (var (hand, staff) in new[] { (HandUse.Right,1), (HandUse.Left,2) })
                {
                    var alone = study.Music(key, hand);
                    Assert.Equal(together.Expected().Where(e => e.Staff == staff).Select(e => (e.Midi,e.Tick,e.Length,e.Finger)),
                        alone.Expected().Select(e => (e.Midi,e.Tick,e.Length,e.Finger)));
                }
            }
        }
    }

    [Fact]
    public void The_key_cycle_wraps_and_the_book_advances_through_printed_keys_before_the_next_number()
    {
        var place = new HanonPlace(6).At(6, new(1));
        var cycle = Enumerable.Range(0,12).Select(_ => { var here = place; place = place.Next(false); return here.Key; }).ToList();
        Assert.Equal(12, cycle.Distinct().Count());
        Assert.Equal(new Key(1), place.Key);
        Assert.Equal(6, place.Number);
        place = place.At(39, new(0));
        Assert.Equal(new Key(0,true), place.Next(true).Key);
        for (var i = 0; i < 24; i++) place = place.Next(true);
        Assert.Equal(40, place.Number);
        Assert.True(place.At(60,new(0)).Next(true).Complete);
    }
}
