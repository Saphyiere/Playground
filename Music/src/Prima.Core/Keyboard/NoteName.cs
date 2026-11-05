namespace Prima.Core.Keyboard;

/// <summary>Names for keys by MIDI number, the way the screens print them: C4 is middle C, A0 and C8 the piano's ends.</summary>
public static class NoteName
{
    public const int LowestKey = 21;
    public const int HighestKey = 108;
    public const int MiddleC = 60;

    private static readonly string[] Sharps = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    /// <summary>The key's name with sharps for the black keys, like C♯4. Spelling in a key is the music model's job.</summary>
    public static string Of(int midi) => $"{Sharps[midi % 12]}{midi / 12 - 1}";

    /// <summary>Whether a key is on an 88-key piano, A0 to C8.</summary>
    public static bool OnPiano(int midi) => midi is >= LowestKey and <= HighestKey;
}
