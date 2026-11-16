namespace Prima.Core.Timing;

/// <summary>
/// Maelzel's metronome marks: the steps a tempo moves by (… 60, 63, 66, 69, 72, 76 …), each about the same share of the tempo,
/// slow or fast. They're carried on at their steps to the ends of the tempos there are, 20 and 240 (… 30, 28 … 20 under them, …
/// 208, 216 … 240 over them), so a step up from any tempo is faster and a step down slower.
/// </summary>
public static class TempoMarks
{
    public static readonly double[] All = [20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52, 54, 56, 58, 60, 63, 66, 69, 72, 76, 80, 84, 88, 92, 96, 100, 104, 108, 112, 116, 120, 126, 132, 138, 144, 152, 160, 168, 176, 184, 192, 200, 208, 216, 224, 232, 240];

    /// <summary>The mark above a tempo, or the fastest.</summary>
    public static double Up(double tempo) => All.FirstOrDefault(m => m > Math.Round(tempo), All[^1]);

    /// <summary>The mark below a tempo, or the slowest.</summary>
    public static double Down(double tempo) => All.LastOrDefault(m => m < Math.Round(tempo), All[0]);

    /// <summary>The mark nearest a tempo.</summary>
    public static double Near(double tempo) => All.MinBy(m => Math.Abs(m - tempo));
}
