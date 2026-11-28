using System.Xml.Linq;
using Prima.Core.Music;
using Prima.UI.Components;

namespace Prima.UI.Glossary;

/// <summary>
/// What's this on a sheet of music, any sheet's: what a tap on it says. The nearest thing printed within a finger's reach, or,
/// where several are as near, which of them (a chord's notes as near as each other are the chord); a note by the key it plays,
/// where it's printed and why it's sharp or flat; a key signature by its sharps or flats and the key the music is in
/// (<see cref="KeyFinder"/>); anything else by its glossary entry. Made from the music's MEI, read when first asked.
/// </summary>
public sealed class MusicExplainer(string mei, Score? read = null, params string?[] titles)
{
    private XDocument? document;
    private Score? score = read;
    private bool tried = read is not null;
    private IReadOnlyList<KeyStretch>? keys;
    private IReadOnlyList<NamedNote>? notes;
    private Dictionary<string, NamedNote>? byId;
    private List<string>? measureIds;

    /// <summary>The music as a score; none where it can't be read as one (a rhythm alone, say).</summary>
    public Score? Score
    {
        get
        {
            if (tried) return score;
            tried = true;
            try
            {
                score = MeiReader.Read(mei);
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or System.Xml.XmlException or ArgumentException)
            {
                score = null;
            }
            return score;
        }
    }

    /// <summary>The keys it's in, a stretch for each signature.</summary>
    public IReadOnlyList<KeyStretch> Keys => keys ??= Score is { } s ? KeyFinder.Find(s, titles) : [];

    /// <summary>Its notes as a reader meets them, each struck one's name and what makes it hard.</summary>
    public IReadOnlyList<NamedNote> Notes => notes ??= Score is { } s ? NoteNames.Of(s) : [];

    /// <summary>The key at a bar (its index in the score), or none.</summary>
    public KeyStretch? KeyAt(int index) => Keys.Count == 0 ? null : Keys.LastOrDefault(k => k.First <= index) ?? Keys[0];

    /// <summary>A key in words: its name ("E major"; unsure, "E major or C♯ minor").</summary>
    public static string KeyName(KeyStretch key) => key.Sure ? key.Key.Name : $"{key.Key.Name} or {key.Other.Name}";

    /// <summary>The line under a key's name: its signature and how it was told ("4 sharps, F♯ C♯ G♯ D♯. It ends on E.").</summary>
    public static string KeyWords(KeyStretch key)
    {
        if (!key.AsWritten)
        {
            var own = key.Key.Fifths == 0 ? "none" : Count(key.Key);
            return $"Written with {key.Signature.Signature} ({key.Key.Name}'s own signature has {own}).";
        }
        var sig = char.ToUpperInvariant(key.Signature.Signature[0]) + key.Signature.Signature[1..];
        var how = key.Told switch
        {
            KeyTold.File => " Its file says so.",
            KeyTold.Title => " As its title says.",
            KeyTold.Ending => $" It ends on {Letter(key.Key.TonicStep, key.Key.AlterOf(key.Key.TonicStep))}.",
            KeyTold.Notes => " Its notes lean to it.",
            _ => " Its music doesn't say which.",
        };
        return $"{sig}.{how}";
    }

    // "4 sharps", "1 flat": a signature's count without its notes.
    private static string Count(Key key) => key.Signature[..key.Signature.IndexOf(',')];

    private static string Letter(Step step, int alter) => $"{step}{alter switch { 1 => "♯", -1 => "♭", _ => "" }}";

    /// <summary>
    /// The card for a tap on the music, given the few nearest things to it (the nearest first): what's nearest the finger, or,
    /// where several things are as near (a finger can't tell them apart), which, each choice shown by <paramref name="show"/>.
    /// <paramref name="here"/> is the key where the music is, for a key signature tapped that isn't found by its bar.
    /// </summary>
    public ExplainCard? Tapped(IReadOnlyList<SheetSymbol> near, Action<ExplainCard> show, KeyStretch? here = null)
    {
        if (near.Count == 0) return null;

        document ??= XDocument.Parse(mei);
        var nearest = near[0];
        var close = near.Where(s => s.Away - nearest.Away <= 4).ToList();
        // A chord's notes as near as each other (and what's printed on them, their sharps and dots): the chord, every note named.
        if (close.Count > 1 && close.FirstOrDefault(s => s.Kind == "note")?.Within is { } chordId && ChordOf(chordId) is { } chord
            && close.All(s => s.Kind == "note" ? s.Within == chordId : s.Kind is "accid" or "dots" or "artic" or "fing" && chord.Tones.Any(t => t.Id == s.Within)))
        {
            return ChordSaid(chord, nearest);
        }
        var cards = close.Select(s => CardOf(s, here)).DistinctBy(c => c.Name).ToList();
        if (cards.Count == 1) return cards[0];

        return new ExplainCard("Which one?", ["Several things are printed there:"], nearest.Left, nearest.Top, nearest.Right, nearest.Bottom)
        {
            Choices = [.. cards.Take(4).Select(card => (card.Name, (Func<Task>)(() =>
            {
                show(card);
                return Task.CompletedTask;
            })))],
        };
    }

    // What a symbol on the music is: a note by the key it plays and why it's sharp or flat; a key signature by its sharps or
    // flats and the key the music is in; anything else by its glossary entry, a note's accidental, dots or finger with its note.
    private ExplainCard CardOf(SheetSymbol symbol, KeyStretch? here)
    {
        var (left, top, right, bottom) = (symbol.Left, symbol.Top, symbol.Right, symbol.Bottom);
        var entry = GlossaryLookup.Find(document, symbol);
        if (symbol.Kind == "keySig" && (BarOf(symbol.Within) is { } bar ? KeyAt(bar) : here ?? KeyAt(0)) is { } key)
        {
            var letters = key.Signature.Accidentals.Select(a => a.Step.ToString()).ToList();
            List<string> lines = key.Fifths == 0
                ? ["No sharps or flats: the white keys, unless a note is marked."]
                : [$"{string.Join(" ", key.Signature.Accidentals.Select(a => Letter(a.Step, a.Alter)))}: every {string.Join(", ", letters.SkipLast(1))}{(letters.Count > 1 ? " and " : "")}{letters[^1]} is played {(key.Fifths > 0 ? "sharp" : "flat")}, in every octave, unless a note is marked otherwise."];
            lines.Add($"The music here is in {KeyName(key)}.{(key.Sure ? "" : " It doesn't say which.")}");
            return new ExplainCard($"Key signature: {(key.Fifths == 0 ? "No sharps or flats" : Count(key.Signature))}", lines, left, top, right, bottom) { EntryId = entry?.Id ?? "key-signature" };
        }
        var noteId = symbol.Kind == "note" ? symbol.Id : symbol.Kind is "accid" or "artic" or "dots" or "fing" ? symbol.Within : null;
        if (noteId is not null && NoteSaid(noteId) is { } said)
        {
            // A note: the key it plays first; the sign tapped on it after, by its glossary entry.
            if (symbol.Kind == "note") return new ExplainCard(said.Name, said.Lines, left, top, right, bottom) { EntryId = entry?.Id };
            return new ExplainCard(entry?.Name ?? said.Name, [.. entry is null ? [] : new[] { entry.Summary }, $"On {said.Name}: {said.Lines.FirstOrDefault() ?? "the note it's on."}"], left, top, right, bottom) { EntryId = entry?.Id };
        }
        return entry is null
            ? new ExplainCard("Not in the glossary yet", ["The glossary can find it by what it looks like: search \"curved line\", \"dots\", \"hairpin\"."], left, top, right, bottom)
            : new ExplainCard(entry.Name, [entry.Summary, entry.Play], left, top, right, bottom) { EntryId = entry.Id };
    }

    // A bar by its MEI id: its index in the score.
    private int? BarOf(string? id)
    {
        if (id is null) return null;
        measureIds ??= [.. (document ??= XDocument.Parse(mei)).Descendants().Where(e => e.Name.LocalName == "measure").Select(m => (string?)m.Attribute(XNamespace.Xml + "id") ?? "")];
        var index = measureIds.IndexOf(id);
        return index < 0 ? null : index;
    }

    // The chord by its id, where it is one.
    private Notes? ChordOf(string id) => Score?.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events.OfType<Notes>()).FirstOrDefault(n => n.Id == id && n.Tones.Count > 1);

    // A chord tapped: its notes from the top, each by the key it plays, with why any is sharp or flat.
    private ExplainCard ChordSaid(Notes chord, SheetSymbol tapped)
    {
        var said = chord.Tones.OrderByDescending(t => t.Pitch.Midi).Select(t => NoteSaid(t.Id)).OfType<(string Name, IReadOnlyList<string> Lines)>().ToList();
        // The notes sharp or flat for the same reason, together: "D♯5 and F♯5: sharp by the key signature".
        var why = said.Where(s => s.Lines.Count > 1 && s.Lines[^1].StartsWith("It's ", StringComparison.Ordinal)).GroupBy(s => s.Lines[^1])
            .Select(g => $"{string.Join(" and ", g.Select(s => s.Name))}: {g.Key[5..].TrimEnd('.')}.").ToList();
        return new ExplainCard($"Chord: {string.Join(" · ", said.Select(s => s.Name))}", why.Count > 0 ? why : ["Its notes from the top, as they're played."], tapped.Left, tapped.Top, tapped.Right, tapped.Bottom)
        {
            EntryId = GlossaryEntries.All.FirstOrDefault(e => e.Marks.Contains("chord"))?.Id,
        };
    }

    // A note in words: the key it plays as its name ("F♯5"), where it's printed, and why it's sharp or flat.
    private (string Name, IReadOnlyList<string> Lines)? NoteSaid(string id)
    {
        byId ??= Notes.ToDictionary(n => n.Id);
        if (!byId.TryGetValue(id, out var note) || Score is not { } s) return null;

        var place = GlossaryLookup.NoteLine(s, id) is { } line ? line[(line.IndexOf(", ", StringComparison.Ordinal) + 2)..] : null;
        var lines = new List<string>();
        if (place is not null) lines.Add(char.ToUpperInvariant(place[0]) + place[1..]);
        var sign = note.Plays.Alter switch { 2 => "double sharp", 1 => "sharp", -1 => "flat", -2 => "double flat", _ => "natural" };
        if (note.Hard.HasFlag(Hard.Signature) && KeyAt(note.Bar) is { } key) lines.Add($"It's {sign} by the key signature: {key.Signature.Signature}.");
        else if (note.Hard.HasFlag(Hard.Carried)) lines.Add($"It's {sign} by the sign before it in the bar, which lasts to the bar line.");
        else if (note.Hard.HasFlag(Hard.Accidental)) lines.Add($"It's {sign} by its own sign.");
        return (note.Plays.ToString(), lines);
    }
}
