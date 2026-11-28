using Microsoft.AspNetCore.Components;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Glossary;

namespace Prima.UI.Pages;

// What's this on a piece: its key where the music is and its notes' names in What's this's panel, the names over its music as
// Settings has them (none, the hard ones, all), and a tap on its music said by its own MusicExplainer, its key found once by its
// title too.
public partial class Piece
{
    [Inject] private WhatsThis WhatsThis { get; set; } = default!;

    private MusicExplainer? explainer;
    // The names last given the sheet, and the choice they were made for: the same list while that holds, so it isn't drawn again.
    private (string Choice, IReadOnlyList<NoteLabel> Labels)? labelled;

    // What's this is on: the music marks what there is to tap, and a tap says what it is.
    private bool explain => WhatsThis.On;

    // The piece's own, from its music as it's read already, found when first asked: in the key it's played in, which its title
    // names only as written.
    private MusicExplainer? Explainer => explainer ??= mei is null || score is null ? null : new MusicExplainer(mei, score, transposed.Moves ? [] : [piece?.Title, piece?.Work]);

    // The music moved to another key: what was read from it goes.
    private void ForgetMusic() => (explainer, labelled) = (null, null);

    private IReadOnlyList<KeyStretch> KeyStretches => Explainer?.Keys ?? [];

    protected override void OnInitialized() => WhatsThis.Changed += Explained;

    private void Explained() => InvokeAsync(StateHasChanged);

    private void ForgetWhatsThis() => WhatsThis.Changed -= Explained;

    // What's this goes with whatever takes the music's place, or a run starting.
    private void Unexplain() => _ = WhatsThis.SetAsync(false);

    // The notes' names over the music: none, or the hard ones or all, each as its label says it ("F♯", "C7"), the same list for
    // as long as the choice is the same.
    private IReadOnlyList<NoteLabel>? Labels()
    {
        var choice = Settings.NoteNames;
        if (choice is not ("hard" or "all") || score is null || Pdf || Explainer is not { } music) return null;
        if (labelled is { } made && made.Choice == choice) return made.Labels;

        var fifths = score.WithSignatures().Select(s => s.Key.Fifths).ToList();
        List<NoteLabel> labels = [.. music.Notes.Where(n => choice == "all" || NoteNames.IsHard(n, fifths[n.Bar])).Select(n => new NoteLabel(n.Id, NoteNames.Label(n)))];
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
        if (score is null || Explainer is not { } music) return null;
        var bar = seen ?? from;
        return music.KeyAt(Math.Max(0, score.Bars.ToList().FindIndex(b => b.Number == bar)));
    }

    private static string KeyName(KeyStretch key) => MusicExplainer.KeyName(key);

    private static string KeyWords(KeyStretch key) => MusicExplainer.KeyWords(key);

    // A tap on the music: the card for what's nearest the finger, or which of several.
    private void SymbolsTapped(IReadOnlyList<SheetSymbol> near) => WhatsThis.Show(Explainer?.Tapped(near, WhatsThis.Show, KeyHere()));
}
