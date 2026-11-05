namespace Prima.Core.Generation;

/// <summary>
/// Prima's own random numbers (SplitMix64), so a passage can be rebuilt from the seed kept with its attempt in any .NET version:
/// System.Random's sequence for a seed isn't promised to stay the same.
/// </summary>
public sealed class Rng(ulong seed)
{
    private ulong state = seed;

    public ulong Next()
    {
        state += 0x9E3779B97F4A7C15;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    /// <summary>0 to <paramref name="count"/> − 1.</summary>
    public int Int(int count) => (int)(Next() % (ulong)count);

    public double Double() => (Next() >> 11) * (1.0 / (1UL << 53));

    public bool Chance(double p) => Double() < p;

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(items.Count)];

    /// <summary>An item chosen with chances in proportion to its weight; the first if all weights are zero.</summary>
    public T Weighted<T>(IReadOnlyList<(T Item, double Weight)> items)
    {
        var total = items.Sum(i => Math.Max(0, i.Weight));
        if (total <= 0) return items[0].Item;

        var at = Double() * total;
        foreach (var (item, weight) in items)
        {
            at -= Math.Max(0, weight);
            if (at < 0) return item;
        }
        return items[^1].Item;
    }
}
