using Prima.Core.Music;

namespace Prima.UI.Components;

/// <summary>Written-out music for showing the sheet: in Settings (the font and size) and on the sheet's check page.</summary>
public static class SheetSamples
{
    private static Spelled P(Step step, int octave, int alter = 0) => new(step, alter, octave);

    private static Notes N(string id, int start, int length, params Spelled[] pitches) => new(id, start, length, pitches.Select((p, i) => new Tone(pitches.Length == 1 ? id : $"{id}-{i}", p)).ToList());

    /// <summary>Two bars in G major on the grand staff.</summary>
    public static Score TwoBars() => new(new Key(1), new Meter(4, 4),
    [
        new Bar(1,
        [
            new Voice(1, [N("s1", 0, 480, P(Step.B, 4)), N("s2", 480, 240, P(Step.C, 5)), N("s3", 720, 240, P(Step.D, 5)), N("s4", 960, 960, P(Step.G, 4))]),
            new Voice(2, [N("s5", 0, 960, P(Step.G, 2), P(Step.D, 3)), N("s6", 960, 960, P(Step.B, 2), P(Step.D, 3))]),
        ]),
        new Bar(2,
        [
            new Voice(1, [N("s7", 0, 720, P(Step.A, 4)), N("s8", 720, 240, P(Step.F, 4, 1)), N("s9", 960, 960, P(Step.G, 4))]),
            new Voice(2, [N("s10", 0, 960, P(Step.D, 3), P(Step.C, 3)), N("s11", 960, 960, P(Step.G, 2))]),
        ]),
    ]);

    /// <summary>Eight bars in D major with the left hand in long notes, a triplet, a tie, accidentals, a dynamic and a slur.</summary>
    public static Score Passage()
    {
        var triplet = new Tuplet(3, 2);
        Bar Bar(int n, Event[] right, Event[] left) => new(n, [new Voice(1, right), new Voice(2, left)]);
        var bars = new List<Bar>
        {
            Bar(1, [N("r1", 0, 480, P(Step.F, 4, 1)), N("r2", 480, 480, P(Step.A, 4)), N("r3", 960, 240, P(Step.B, 4)), N("r4", 1200, 240, P(Step.A, 4)), N("r5", 1440, 480, P(Step.G, 4))], [N("l1", 0, 1920, P(Step.D, 3))]),
            Bar(2, [N("r6", 0, 720, P(Step.F, 4, 1)), N("r7", 720, 240, P(Step.E, 4)), N("r8", 960, 960, P(Step.D, 4))], [N("l2", 0, 960, P(Step.D, 3)), N("l3", 960, 960, P(Step.A, 2))]),
            Bar(3, [N("r9", 0, 480, P(Step.E, 4)), N("r10", 480, 160, P(Step.F, 4, 1)) with { Tuplet = triplet }, N("r11", 640, 160, P(Step.G, 4)) with { Tuplet = triplet }, N("r12", 800, 160, P(Step.A, 4)) with { Tuplet = triplet }, N("r13", 960, 480, P(Step.B, 4)), N("r14", 1440, 480, P(Step.A, 4))], [N("l4", 0, 1920, P(Step.A, 2), P(Step.E, 3))]),
            Bar(4, [new Notes("r15", 0, 1920, [new Tone("r15", P(Step.A, 4)) { TieToNext = true }])], [N("l5", 0, 960, P(Step.A, 2)), N("l6", 960, 960, P(Step.C, 3, 1))]),
            Bar(5, [new Notes("r16", 0, 480, [new Tone("r16", P(Step.A, 4)) { TiedFromPrevious = true }]), N("r17", 480, 480, P(Step.B, 4)), N("r18", 960, 480, P(Step.C, 5, 1)), N("r19", 1440, 480, P(Step.D, 5))], [N("l7", 0, 1920, P(Step.D, 3), P(Step.F, 3, 1))]),
            Bar(6, [N("r20", 0, 720, P(Step.E, 5)), N("r21", 720, 240, P(Step.D, 5)), N("r22", 960, 480, P(Step.C, 5, 1)), N("r23", 1440, 480, P(Step.B, 4))], [N("l8", 0, 960, P(Step.G, 2)), N("l9", 960, 960, P(Step.E, 2))]),
            Bar(7, [N("r24", 0, 480, P(Step.A, 4)), N("r25", 480, 480, P(Step.G, 4, 1)), N("r26", 960, 480, P(Step.A, 4)), N("r27", 1440, 480, P(Step.E, 4))], [N("l10", 0, 1920, P(Step.A, 2), P(Step.E, 3), P(Step.G, 3))]),
            Bar(8, [N("r28", 0, 1920, P(Step.D, 4), P(Step.F, 4, 1))], [N("l11", 0, 1920, P(Step.D, 3))]),
        };
        return new Score(new Key(2), new Meter(4, 4), bars) { Title = "Eight bars in D major", Marks = [new Mark(MarkKind.Dynamic, "r1", Text: "mp"), new Mark(MarkKind.Slur, "r1", "r5"), new Mark(MarkKind.Crescendo, "r17", "r19")] };
    }
}
