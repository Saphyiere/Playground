using System.Text.Json;
using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.UI.Storage;

namespace Prima.UI.Practice;

/// <summary>
/// A passage ready for the reading screen: its score (what's scored), and what the sheet engraves. Generated music is written
/// out as MEI; a stretch of a real piece is the piece's own engraving with only those bars shown (<see cref="Measures"/>, by
/// position) and the cursor on its notes (<see cref="Onsets"/>).
/// </summary>
public sealed record ReadyPassage(Passage Passage, string Mei, string? Measures, IReadOnlyList<(string Id, double Quarter)>? Onsets, ExcerptRecord? Excerpt, PieceRecord? Piece)
{
    public bool Real => Piece is not null;
}

/// <summary>
/// The day's passages come in order from its date, each in a different key from the one before, so Today's "up next" is the passage
/// Sight-read gives, until it's read. Every third is real music from the Library at or below your level, when an excerpt of the
/// right length has rested long enough (library.md's starting mix, ours to tune).
/// </summary>
public static class DayPassages
{
    public static Passage Next(LevelSpec spec, DateOnly day, int readToday, Key? previous) => PassageGenerator.Generate(spec, (ulong)day.DayNumber * 31 + (ulong)readToday, previous);

    public static async Task<ReadyPassage> NextAsync(PrimaStore store, ReadingState reading, DateOnly day, int readToday, Key? previous, double tempo)
    {
        if (readToday % 3 == 2 && reading.Level >= 4)
        {
            var excerpts = await store.GetExcerptsAsync(reading.Level, reading.Level >= 7 ? 8 : 4, day);
            if (excerpts.FirstOrDefault() is { } excerpt && await store.GetPieceAsync(excerpt.Piece) is { } piece && await store.GetMeiAsync(excerpt.Piece) is { } mei)
            {
                return Real(piece, mei, MeiReader.Read(mei), excerpt.FirstBar, excerpt.LastBar, excerpt.Level, excerpt);
            }
        }

        var passage = Next(reading.Spec, day, readToday, previous);
        return new ReadyPassage(passage, MeiWriter.Write(passage.Score, tempo), null, null, null, null);
    }

    /// <summary>Bars <paramref name="first"/> to <paramref name="last"/> of a piece (bar 0 is a pickup), engraved from the piece itself.</summary>
    public static ReadyPassage Real(PieceRecord piece, string mei, Score whole, int first, int last, int level, ExcerptRecord? excerpt = null, string? description = null)
    {
        var score = whole.Excerpt(first, last);
        var offset = whole.Bars[0].Number == 0 ? 1 : 0;
        var pickup = score.Bars[0].Number == 0 ? score.Bars[0].Length ?? 0 : 0;
        var onsets = score.Expected().GroupBy(n => n.Tick).Select(g => (g.First().Id, (g.Key + pickup) / (double)Ticks.Quarter)).ToList();
        var name = $"{piece.Title}{(piece.Composer is { Length: > 0 } c ? $", {c.Split(' ')[^1]}" : "")}";
        var passage = new Passage(score, Ladder.Level(level), 0, Style.Plain, description ?? $"Bars {Math.Max(1, first)} to {last} of {name}");
        return new ReadyPassage(passage, mei, $"{first + offset}-{last + offset}", onsets, excerpt, piece);
    }

    /// <summary>The key a passage attempt was read in, from its settings; null for one saved without it.</summary>
    public static Key? KeyOf(AttemptRecord? attempt)
    {
        if (attempt?.Settings is not { } json) return null;

        using var settings = JsonDocument.Parse(json);
        return settings.RootElement.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.Object ? new Key(key.GetProperty("fifths").GetInt32(), key.GetProperty("minor").GetBoolean()) : null;
    }
}
