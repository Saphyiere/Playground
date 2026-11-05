using System.Text.Json;
using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.UI.Storage;

namespace Prima.UI.Practice;

/// <summary>
/// The day's passages come in order from its date, each in a different key from the one before, so Today's "up next" is the passage
/// Sight-read gives, until it's read.
/// </summary>
public static class DayPassages
{
    public static Passage Next(LevelSpec spec, DateOnly day, int readToday, Key? previous) => PassageGenerator.Generate(spec, (ulong)day.DayNumber * 31 + (ulong)readToday, previous);

    /// <summary>The key a passage attempt was read in, from its settings; null for one saved without it.</summary>
    public static Key? KeyOf(AttemptRecord? attempt)
    {
        if (attempt?.Settings is not { } json) return null;

        using var settings = JsonDocument.Parse(json);
        return settings.RootElement.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.Object ? new Key(key.GetProperty("fifths").GetInt32(), key.GetProperty("minor").GetBoolean()) : null;
    }
}
