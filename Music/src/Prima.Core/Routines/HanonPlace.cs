using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Routines;

/// <summary>A local bookmark in the book or in a single study's rotating keys.</summary>
public sealed record HanonPlace(int Number = 1, int Fifths = 0, bool Minor = false, bool Complete = false, bool Cycle = false)
{
    public Key Key => new(Fifths, Minor);

    public HanonPlace At(int number, Key chosen)
    {
        var keys = Hanon.Keys(number);
        var key = keys.Contains(chosen) ? chosen : keys[0];
        return this with { Number = number, Fifths = key.Fifths, Minor = key.Minor, Complete = false };
    }

    public HanonPlace Next(bool book)
    {
        var keys = Hanon.Keys(Number);
        var following = keys.ToList().IndexOf(Key) + 1;
        if (!book) return At(Number, keys[following % keys.Count]);
        if (Hanon.PrintedKeys(Number) && following < keys.Count) return At(Number, keys[following]);
        return Number == Hanon.All.Count ? this with { Complete = true } : At(Number + 1, Hanon.Keys(Number + 1)[0]);
    }
}
