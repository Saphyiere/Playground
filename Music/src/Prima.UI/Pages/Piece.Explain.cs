using System.Xml.Linq;
using Microsoft.AspNetCore.Components;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Glossary;

namespace Prima.UI.Pages;

// What's this on a piece: its key and its notes' names in What's this's panel, the names over its music as Settings has them
// (none, the hard ones, all), and what a tap on its music is: the nearest thing printed within a finger's reach, a note with the
// key it plays and why it's sharp or flat, a key signature with the key the music is in, or which of several things it was.
public partial class Piece
{
    [Inject] private WhatsThis WhatsThis { get; set; } = default!;

    // The keys the piece is in, a stretch for each signature, and its notes as a reader meets them: found once, when first asked.
    private IReadOnlyList<KeyStretch>? keyStretches;
    private IReadOnlyList<NamedNote>? namedNotes;
    private Dictionary<string, NamedNote>? namedById;
    // The names last given the sheet, and the choice they were made for: the same list while that holds, so it isn't drawn again.
    private (string Choice, IReadOnlyList<NoteLabel> Labels)? labelled;

    // What's this is on: the music marks what there is to tap, and a tap says what it is.
    private bool explain => WhatsThis.On;

    private IReadOnlyList<KeyStretch> KeyStretches => keyStretches ??= score is null ? [] : KeyFinder.Find(score, piece?.Title, piece?.Work);

    private IReadOnlyList<NamedNote> NamedNotes => namedNotes ??= score is null ? [] : NoteNames.Of(score);

    protected override void OnInitialized() => WhatsThis.Changed += Explained;

    private void Explained() => InvokeAsync(StateHasChanged);

    private void ForgetWhatsThis() => WhatsThis.Changed -= Explained;

    // What's this goes with whatever takes the music's place, or a run starting.
    private void Unexplain() => _ = WhatsThis.SetAsync(false);

    private Task ToggleExplain()
    {
        about = false;
        return WhatsThis.ToggleAsync();
    }

    // The notes' names over the music: none, or the hard ones or all, each as its label says it ("F♯", "C7"), the same list for
    // as long as the choice is the same.
    private IReadOnlyList<NoteLabel>? Labels()
    {
        var choice = Settings.NoteNames;
        if (choice is not ("hard" or "all") || score is null || Pdf) return null;
        if (labelled is { } made && made.Choice == choice) return made.Labels;

        var fifths = score.WithSignatures().Select(s => s.Key.Fifths).ToList();
        List<NoteLabel> labels = [.. NamedNotes.Where(n => choice == "all" || NoteNames.IsHard(n, fifths[n.Bar])).Select(n => new NoteLabel(n.Id, NoteNames.Label(n)))];
        labelled = (choice, labels);
        return labels;
    }

    private Task SetNoteNames(string choice)
    {
        Settings.NoteNames = choice;
        return Store.SaveAsync(Settings);
    }

    // The key where the music is: the stretch with the first bar on the screen, or the bar a run starts from.
    private KeyStretch? KeyHere()
    {
        if (score is null || KeyStretches.Count == 0) return null;
        var bar = seen ?? from;
        var index = Math.Max(0, score.Bars.ToList().FindIndex(b => b.Number == bar));
        return KeyStretches.LastOrDefault(k => k.First <= index) ?? KeyStretches[0];
    }

    // A key in words: its name ("E major"; "E major or C♯ minor" unsure), then its signature and how it was told.
    private static string KeyName(KeyStretch key) => key.Sure ? key.Key.Name : $"{key.Key.Name} or {key.Other.Name}";

    private static string KeyWords(KeyStretch key)
    {
        if (!key.AsWritten)
        {
            var own = key.Key.Fifths == 0 ? "none" : key.Key.Signature[..key.Key.Signature.IndexOf(',')];
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

    private static string Letter(Step step, int alter) => $"{step}{alter switch { 1 => "♯", -1 => "♭", _ => "" }}";

    // A tap on the music: what's nearest the finger, or, where several things are as near (a finger can't tell them apart), which.
    private void SymbolsTapped(IReadOnlyList<SheetSymbol> near)
    {
        if (near.Count == 0 || score is null)
        {
            WhatsThis.Show(null);
            return;
        }
        meiDocument ??= mei is null ? null : XDocument.Parse(mei);
        var nearest = near[0];
        var close = near.Where(s => s.Away - nearest.Away <= 4).ToList();
        // A chord's notes as near as each other (and what's printed on them, their sharps and dots): the chord, every note named.
        if (close.Count > 1 && close.FirstOrDefault(s => s.Kind == "note")?.Within is { } chordId && ChordOf(chordId) is { } chord
            && close.All(s => s.Kind == "note" ? s.Within == chordId : s.Kind is "accid" or "dots" or "artic" or "fing" && chord.Tones.Any(t => t.Id == s.Within))
            && ChordSaid(chord, nearest) is { } card)
        {
            WhatsThis.Show(card);
            return;
        }
        var cards = close.Select(CardOf).DistinctBy(c => c.Name).ToList();
        if (cards.Count == 1)
        {
            WhatsThis.Show(cards[0]);
            return;
        }
        WhatsThis.Show(new ExplainCard("Which one?", ["Several things are printed there:"], nearest.Left, nearest.Top, nearest.Right, nearest.Bottom)
        {
            Choices = [.. cards.Take(4).Select(card => (card.Name, (Func<Task>)(() =>
            {
                WhatsThis.Show(card);
                return Task.CompletedTask;
            })))],
        });
    }

    // What a symbol on the music is: a note by the key it plays and why it's sharp or flat; a key signature by its sharps or
    // flats and the key the music is in; anything else by its glossary entry, a note's accidental, dots or finger with its note.
    private ExplainCard CardOf(SheetSymbol symbol)
    {
        var (left, top, right, bottom) = (symbol.Left, symbol.Top, symbol.Right, symbol.Bottom);
        var entry = GlossaryLookup.Find(meiDocument, symbol);
        if (symbol.Kind == "keySig" && KeyAt(symbol) is { } key)
        {
            var notes = key.Signature.Accidentals.Select(a => a.Step.ToString()).ToList();
            List<string> lines = key.Fifths == 0
                ? ["No sharps or flats: the white keys, unless a note is marked."]
                : [$"{string.Join(" ", key.Signature.Accidentals.Select(a => Letter(a.Step, a.Alter)))}: every {string.Join(", ", notes.SkipLast(1))}{(notes.Count > 1 ? " and " : "")}{notes[^1]} is played {(key.Fifths > 0 ? "sharp" : "flat")}, in every octave, unless a note is marked otherwise."];
            lines.Add($"The music here is in {KeyName(key)}.{(key.Sure ? "" : " It doesn't say which.")}");
            var count = key.Fifths == 0 ? "No sharps or flats" : key.Signature.Signature[..key.Signature.Signature.IndexOf(',')];
            return new ExplainCard($"Key signature: {count}", lines, left, top, right, bottom) { EntryId = entry?.Id ?? "key-signature" };
        }
        var noteId = symbol.Kind == "note" ? symbol.Id : symbol.Kind is "accid" or "artic" or "dots" or "fing" ? symbol.Within : null;
        if (noteId is not null && NoteSaid(noteId) is { } said)
        {
            // A note: the key it plays first; the sign tapped on it after, by its glossary entry.
            if (symbol.Kind == "note") return new ExplainCard(said.Name, said.Lines, left, top, right, bottom) { EntryId = entry?.Id };
            return new ExplainCard(entry?.Name ?? said.Name, [.. entry is null ? [] : new[] { entry.Summary }, $"On {said.Name}: {said.Lines[0]}"], left, top, right, bottom) { EntryId = entry?.Id };
        }
        return entry is null
            ? new ExplainCard("Not in the glossary yet", ["The glossary can find it by what it looks like: search \"curved line\", \"dots\", \"hairpin\"."], left, top, right, bottom)
            : new ExplainCard(entry.Name, [entry.Summary, entry.Play], left, top, right, bottom) { EntryId = entry.Id };
    }

    // The chord by its id, where it is one.
    private Notes? ChordOf(string id) => score?.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events.OfType<Notes>()).FirstOrDefault(n => n.Id == id && n.Tones.Count > 1);

    // A chord tapped: its notes from the top, each by the key it plays, with why any is sharp or flat.
    private ExplainCard? ChordSaid(Notes chord, SheetSymbol tapped)
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

    // The stretch a key signature tapped on the music starts (its bar, by the bar it's printed in).
    private KeyStretch? KeyAt(SheetSymbol symbol)
    {
        if (score is null || KeyStretches.Count == 0) return null;
        var index = symbol.Within is { } bar ? MeasureIds().IndexOf(bar) : -1;
        return index < 0 ? KeyHere() : KeyStretches.LastOrDefault(k => k.First <= index) ?? KeyStretches[0];
    }

    // A note in words: the key it plays as its name ("F♯5"), where it's printed, and why it's sharp or flat or so far off.
    private (string Name, IReadOnlyList<string> Lines)? NoteSaid(string id)
    {
        namedById ??= NamedNotes.ToDictionary(n => n.Id);
        if (!namedById.TryGetValue(id, out var note) || score is null) return null;

        var place = GlossaryLookup.NoteLine(score, id) is { } line ? line[(line.IndexOf(", ", StringComparison.Ordinal) + 2)..] : null;
        var lines = new List<string>();
        if (place is not null) lines.Add(char.ToUpperInvariant(place[0]) + place[1..]);
        var sign = note.Plays.Alter switch { 2 => "double sharp", 1 => "sharp", -1 => "flat", -2 => "double flat", _ => "natural" };
        var key = KeyStretches.LastOrDefault(k => k.First <= note.Bar);
        if (note.Hard.HasFlag(Hard.Signature) && key is not null) lines.Add($"It's {sign} by the key signature: {key.Signature.Signature}.");
        else if (note.Hard.HasFlag(Hard.Carried)) lines.Add($"It's {sign} by the sign before it in the bar, which lasts to the bar line.");
        else if (note.Hard.HasFlag(Hard.Accidental)) lines.Add($"It's {sign} by its own sign.");
        return (note.Plays.ToString(), lines);
    }
}
