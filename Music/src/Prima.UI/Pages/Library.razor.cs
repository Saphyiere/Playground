using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Prima.Core.Library;
using Prima.Core.Practice;
using Prima.UI.Practice;
using Prima.UI.Storage;

namespace Prima.UI.Pages;

public partial class Library
{
    private static readonly (string Key, string Label)[] Formats = [("all", "All"), ("notation", "To play along"), ("pdf", "PDFs")];

    // What the Library's list goes by, and how the uploaded pieces are sorted.
    private static readonly (string Key, string Label)[] Lists = [("composers", "Composers"), ("kinds", "Kinds"), ("levels", "Levels")];
    private static readonly (string Key, string Label)[] Sorts = [("latest", "Latest"), ("az", "A–Z"), ("artist", "By artist")];

    private const string NotGiven = "Composer not given";

    // Past this many composers, a list's keys go by letter: Chorales and hymns had 136 keys, a few screens of them above its
    // first piece.
    private const int MostKeys = 12;

    // The most pieces a search lists: "sonata" finds hundreds, and another word narrows them.
    private const int Most = 150;

    // The work chosen, when none is to be open (upright, a work closed again).
    private const string Closed = "-";

    /// <summary>What's shown beside the list: composer, kind, level or period (with <see cref="Of"/>), near, mine, all or pdf; the uploaded pieces when none, or the pieces near your level, or the first composer.</summary>
    [SupplyParameterFromQuery(Name = "by")]
    public string? By { get; set; }

    /// <summary>Which composer, kind, level or period.</summary>
    [SupplyParameterFromQuery(Name = "of")]
    public string? Of { get; set; }

    /// <summary>What's searched for, as typed: kept in the address, so what was found comes back with it.</summary>
    [SupplyParameterFromQuery(Name = "q")]
    public string? Query { get; set; }

    /// <summary>The pieces a list is cut to: "notation" (to play along) or "pdf"; all of them when none.</summary>
    [SupplyParameterFromQuery(Name = "only")]
    public string? Only { get; set; }

    /// <summary>What the list down the left goes by: composers, kinds or levels; what's shown says, when none.</summary>
    [SupplyParameterFromQuery(Name = "list")]
    public string? List { get; set; }

    /// <summary>The kind of a composer's works shown: its tab; the first when none.</summary>
    [SupplyParameterFromQuery(Name = "kind")]
    public string? Kind { get; set; }

    /// <summary>The work chosen, by its key; the first when none.</summary>
    [SupplyParameterFromQuery(Name = "work")]
    public string? Chosen { get; set; }

    /// <summary>How the uploaded pieces are sorted: "az" or "artist"; the latest first when none.</summary>
    [SupplyParameterFromQuery(Name = "sort")]
    public string? Sort { get; set; }

    private List<PieceRecord> pieces = [];
    private Dictionary<string, PieceRecord> byId = [];
    // Each piece's period and kind, by its id: worked out once each time the pieces are read.
    private Dictionary<string, string> periodOf = [];
    private Dictionary<string, string> kindOf = [];
    // Every composer by surname, as the list names them, with how many pieces.
    private List<(string Name, string Listed, int Count)> composers = [];
    // A composer's pieces as works, read the first time they're shown, and what the pieces were then: read again when they change.
    private readonly Dictionary<string, IReadOnlyList<ListedWork>> worksOf = [];
    private readonly Dictionary<string, int> readFrom = [];
    private Dictionary<string, int> signatures = [];
    // The work each piece is of, once every composer's are read: a search finds a piece by its work's name too.
    private Dictionary<string, ListedWork>? workOf;
    // What's been done on each piece that has been played, by its id.
    private Dictionary<string, PieceHistory> played = [];
    private ReadingState? reading;
    private string format = "all";
    private string? search;
    private string? find;
    private string? note;
    private DateTime lastLoad;
    private CancellationTokenSource? typing;
    private bool holding;
    // Prima's own pieces kept out of the Library (all of them, after Remove library; or those removed by hand), and putting them
    // back while it's done.
    private (bool All, HashSet<string> Removed) ownOut = (false, []);
    private Task? puttingBack;
    // The collection's pieces removed by hand, which its zip imported again leaves out.
    private HashSet<string> collectionOut = [];
    // The pieces are read, and the columns drawn from them are to be scrolled to where they were left (Library.razor.js keeps where).
    private bool loaded;
    private bool placing;
    private IJSObjectReference? places;
    // The piece whose star was tapped a second time, and where: its collections are chosen there.
    private (PieceRecord Piece, double X, double Y)? starring;
    private bool naming;
    private string? newName;
    private string? nameNote;

    // What's shown beside the list. With nothing asked for, what's yours comes first.
    private string View => search is not null ? "found"
        : By is "composer" or "kind" or "level" or "near" or "mine" or "all" or "pdf" or "period" ? By
        : pieces.Any(p => p.Imported) ? "mine" : Near().Any() ? "near" : composers.Count > 0 ? "composer" : "all";

    private string? ShownOf => View == "composer" && (By != "composer" || Of is null) ? composers.Select(c => c.Name).FirstOrDefault() : Of;

    private string ListMode => List is "composers" or "kinds" or "levels" ? List : View switch { "kind" => "kinds", "level" => "levels", _ => "composers" };

    private string Sorted => Sort is "az" or "artist" ? Sort : "latest";

    private bool worksRead => composers.All(c => worksOf.ContainsKey(c.Name));

    private string? Heading => View switch
    {
        "found" => $"“{search}”",
        "composer" or "kind" or "period" => ShownOf,
        "level" => Of == "0" ? "Not graded" : $"Level {Of}",
        "near" => "Near your level",
        "pdf" => "PDFs",
        "mine" => "Uploaded pieces",
        "all" => pieces.Count > 0 ? "Everything" : null,
        _ => null,
    };

    protected override async Task OnInitializedAsync()
    {
        reading = await Store.GetTrackAsync<ReadingState>("reading");
        await Book.LoadAsync();
        await Load();
        played = (await Store.GetPieceRunsAsync()).GroupBy(r => r.Piece).Select(g => (g.Key, Done: PieceHistory.Of(g.Select(r => r.Run)))).Where(p => p.Done is not null).ToDictionary(p => p.Key, p => p.Done!);
        loaded = true;
        Prep.Changed += Changed;
        Openings.Changed += Heard;
        Book.Changed += Heard;
        _ = Prep.EnsureAsync();
    }

    // What's shown is its address: the list chosen, its tab, its work, its filter and the search are in it, so coming back to it
    // (the way back, or the rail's Library key, which goes to the address kept here) finds them as they were left, and then the
    // place each column was scrolled to.
    protected override async Task OnParametersSetAsync()
    {
        (format, search, note, starring) = (Only is "notation" or "pdf" ? Only : "all", string.IsNullOrWhiteSpace(Query) ? null : Query, null, null);
        // Only the Library's own address: a piece's, as the Library is left for it, isn't one. What's shown is a piece's way back.
        var here = Nav.ToBaseRelativePath(Nav.Uri).Split('#')[0];
        if (here.Split('?')[0] == "library")
        {
            Place.Last = here;
            Navigation.Name(Heading ?? "Library");
        }
        // The columns are put back where they were once they're drawn whole: after the works they show are read.
        await ReadWorks();
        placing = true;
    }

    // Once the columns are drawn from the pieces read, each is scrolled to where it was left: the top, for one not seen before.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!placing || !loaded) return;

        placing = false;
        try
        {
            places ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Prima.UI/Pages/Library.razor.js");
            await places.InvokeVoidAsync("show");
        }
        catch (Exception e) when (e is JSDisconnectedException or JSException)
        {
        }
    }

    // The address with one of its values changed, in this one's place: the way back doesn't step through each search, filter,
    // tab or work chosen.
    private void Keep(string name, string? value) => Navigation.Replace(Nav.GetUriWithQueryParameter(name, value));

    private void Choose(string key) => Keep("only", key == "all" ? null : key);

    // Another of a composer's kinds: its first work, until one is chosen.
    private void Turn(string kind) => Navigation.Replace(Nav.GetUriWithQueryParameters(new Dictionary<string, object?> { ["kind"] = kind, ["work"] = null }));

    // One of the list's own addresses. The list stays as it was switched to (kinds, say) while something else is shown.
    private string Address(string by, string? of) =>
        $"library?by={by}{(of is null ? "" : $"&of={Uri.EscapeDataString(of)}")}{(List is "composers" or "kinds" or "levels" ? $"&list={List}" : "")}";

    private async Task Load()
    {
        pieces = await Store.GetPiecesAsync();
        byId = pieces.ToDictionary(p => p.Id);
        periodOf = pieces.ToDictionary(p => p.Id, p => Shelves.Period(p.Composer));
        kindOf = pieces.ToDictionary(p => p.Id, p => Shelves.Kind(p.Title, p.Work));
        var named = pieces.Where(p => !string.IsNullOrWhiteSpace(p.Composer)).GroupBy(p => p.Composer).ToList();
        composers = [.. named.Select(g => (g.Key, Shelves.Listed(g.Key), g.Count())).OrderBy(c => Shelves.Surname(c.Key), StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Key)];
        // A composer's works are read again once their pieces have changed: one added or removed, or a title brought up to date.
        signatures = named.ToDictionary(g => g.Key, g => g.Aggregate(g.Count(), (hash, p) => hash ^ HashCode.Combine(p.Id, p.Title, p.Work, p.Format)));
        foreach (var name in worksOf.Keys.Where(name => signatures.GetValueOrDefault(name) != readFrom.GetValueOrDefault(name)).ToList()) worksOf.Remove(name);
        workOf = null;
        ownOut = await Store.GetOwnPiecesOutAsync();
        collectionOut = await Store.GetCollectionPiecesOutAsync();
    }

    // The works what's shown needs, read away from the screen's thread: the composer's, or every composer's for a kind's index
    // and for the search, which finds a piece by its work's name ("sonata 14" for a movement titled without its number).
    private async Task ReadWorks()
    {
        var view = View;
        string[] need = view == "composer" && ShownOf is { } one ? (worksOf.ContainsKey(one) ? [] : [one])
            : view is "kind" or "found" ? [.. composers.Select(c => c.Name).Where(name => !worksOf.ContainsKey(name))] : [];
        if (need.Length == 0) return;

        var (from, marks) = (pieces, signatures);
        var read = await Task.Run(() =>
        {
            var byComposer = from.ToLookup(p => p.Composer);
            return need.Select(name => (Name: name, Works: Works.Of(byComposer[name].Select(p => new ListedPiece(p.Id, p.Title, p.Work, p.Source, p.IsPdf))))).ToList();
        });
        // The pieces changed meanwhile: what was read is of the list before.
        if (!ReferenceEquals(from, pieces)) return;

        foreach (var (name, works) in read) (worksOf[name], readFrom[name]) = (works, marks.GetValueOrDefault(name));
        workOf = null;
        placing = true;
    }

    // The piece just taken out put back as it was, where it was in the list.
    private async Task Undo()
    {
        if (Place.Removed is not { } removed) return;

        Place.Removed = null;
        await Store.PutPieceBackAsync(removed);
        await Load();
        await ReadWorks();
        note = $"“{removed.Piece.Title}” is back in the Library.";
    }

    // The collection's pieces removed by hand let back in: they come back the next time their collection is imported.
    private async Task LetBackIn()
    {
        var count = collectionOut.Count;
        await Store.LetCollectionPiecesBackAsync();
        await Load();
        note = count == 1 ? "It comes back the next time its collection is imported." : "They come back the next time their collection is imported.";
    }

    // What the list's last lines say of Prima's own pieces kept out, when any are.
    private string? OwnOut() => ownOut switch
    {
        { All: true } => "Prima's own pieces are out of the Library: Remove library took them out, and Prima won't put them back by itself.",
        { Removed.Count: 1 } => "1 of Prima's own pieces is out of the Library: you removed it, and Prima won't put it back by itself.",
        { Removed.Count: > 1 and var count } => $"{count} of Prima's own pieces are out of the Library: you removed them, and Prima won't put them back by itself.",
        _ => null,
    };

    // Prima's own pieces back in the Library, every one: stored again as on the first open, the meter showing how far it's got.
    private async Task PutBack()
    {
        note = null;
        var putting = puttingBack = Prep.PutBackAsync();
        try
        {
            await putting;
        }
        finally
        {
            puttingBack = null;
            await Load();
            await ReadWorks();
        }
    }

    // While pieces are stored, the lists fill in (and the meter moves) a second at a time at most: with a thousand pieces listed,
    // drawing them again for each one stored kept the screen busy.
    private void Changed() => _ = InvokeAsync(async () =>
    {
        if (DateTime.Now - lastLoad < TimeSpan.FromSeconds(1) && Prep.Running) return;

        lastLoad = DateTime.Now;
        await Load();
        // While the collection comes in, a composer's works wait for it: reading them again a second at a time would keep the
        // tablet busy for nothing.
        if (!Prep.Running) await ReadWorks();
        StateHasChanged();
    });

    private void Heard() => _ = InvokeAsync(StateHasChanged);

    protected override bool ShouldRender() => !holding;

    // What's found follows the search once typing pauses for a quarter of a second, not at every key: it goes in the address,
    // which the screen then shows.
    private async Task Typed(ChangeEventArgs e)
    {
        holding = true;
        typing?.Cancel();
        var mine = typing = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, mine.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        holding = false;
        var typed = e.Value?.ToString();
        Keep("q", string.IsNullOrWhiteSpace(typed) ? null : typed);
    }

    // The list of composers cut to the ones whose names have what's typed (accents aside: "Dvorak" finds Dvořák).
    private void Finding(ChangeEventArgs e) => find = e.Value?.ToString();

    // The pieces of the list shown, in the format chosen.
    private IEnumerable<PieceRecord> Shown()
    {
        var view = View;
        var (from, to) = view == "near" ? NearLevels() : default;
        return pieces.Where(p => view switch
        {
            "period" => periodOf[p.Id] == Of,
            "level" => LevelOf(p).ToString() == Of,
            "near" => p.PlaysAlong && (p.TypicalLevel ?? p.Level) is { } l && l >= from && l <= to,
            "pdf" => p.IsPdf,
            _ => true,
        }).Where(p => format switch
        {
            "notation" => p.PlaysAlong,
            "pdf" => p.IsPdf,
            _ => true,
        });
    }

    // Every piece the search finds, in its title, its composer, the work its source names, and its work as the catalogue names
    // it once the works are read (the Moonlight's movements by "sonata 14"); under their composers by surname, a work's together.
    private IEnumerable<PieceRecord> Found()
    {
        workOf ??= worksOf.Values.SelectMany(works => works.SelectMany(w => w.Parts.Concat(w.Others).Select(p => (p.Id, Work: w)))).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Work);
        return pieces.Where(p => Shelves.Finds(workOf.GetValueOrDefault(p.Id) is { } w ? $"{p.Title} {p.Composer} {p.Work} {w.Title} {w.Catalogue}" : $"{p.Title} {p.Composer} {p.Work}", search!))
            .OrderBy(p => string.IsNullOrWhiteSpace(p.Composer)).ThenBy(p => Shelves.Surname(p.Composer), StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Composer, StringComparer.Ordinal)
            .ThenBy(p => Works.Natural(workOf.GetValueOrDefault(p.Id)?.Title ?? p.Title), StringComparer.Ordinal).ThenBy(p => Works.Natural(p.Title), StringComparer.Ordinal);
    }

    // The pieces uploaded: the latest first, or by title, or by artist (the ones without one last).
    private IEnumerable<PieceRecord> Uploaded()
    {
        var mine = pieces.Where(p => p.Imported);
        return Sorted switch
        {
            "az" => mine.OrderBy(p => Works.Natural(p.Title), StringComparer.Ordinal),
            "artist" => mine.OrderBy(p => string.IsNullOrWhiteSpace(p.Composer)).ThenBy(p => Works.Natural(p.Composer), StringComparer.Ordinal).ThenBy(p => Works.Natural(p.Title), StringComparer.Ordinal),
            _ => mine.OrderByDescending(p => p.Added),
        };
    }

    // How many pieces the Library holds, and how many of them were uploaded.
    private string Counted()
    {
        var mine = pieces.Count(p => p.Imported);
        return $"{(pieces.Count == 1 ? "1 piece" : $"{pieces.Count:N0} pieces")}{(mine > 0 ? $" · {mine} uploaded" : "")}";
    }

    // "1 work", "2,104 pieces".
    private static string Counted(int count, string one) => count == 1 ? $"1 {one}" : $"{count:N0} {one}s";

    // One of a composer's tabs: its name, how many pieces it holds, and the kinds under it.
    private sealed record Tab(string Name, int Held, IReadOnlyList<string> Kinds);

    // A composer's kinds as tabs, the fullest first (alike, in the kinds' own order). Past five, the smallest share one, Other:
    // Beethoven's two preludes and two hymn tunes needn't each have a tab beside 134 sonatas.
    private static List<Tab> Tabs(IReadOnlyList<ListedWork> works)
    {
        var kinds = Shelves.Kinds.Select(kind => (Kind: kind, Held: works.Where(w => w.Kind == kind).Sum(w => w.Parts.Count + w.Others.Count))).Where(k => k.Held > 0)
            .OrderByDescending(k => k.Held).ToList();
        if (kinds.Count <= 5) return [.. kinds.Select(k => new Tab(k.Kind, k.Held, [k.Kind]))];

        return [.. kinds.Take(4).Select(k => new Tab(k.Kind, k.Held, [k.Kind])), new Tab("Other", kinds.Skip(4).Sum(k => k.Held), [.. kinds.Skip(4).Select(k => k.Kind)])];
    }

    // The tab shown: the one asked for by its name or by a kind under it, or the first.
    private Tab? TabOf(IReadOnlyList<ListedWork> works)
    {
        var tabs = Tabs(works);
        return tabs.FirstOrDefault(t => t.Name == Kind) ?? tabs.FirstOrDefault(t => t.Kinds.Contains(Kind ?? "")) ?? tabs.FirstOrDefault();
    }

    // The work chosen among the ones shown: the one asked for, or the first.
    private ListedWork? ChosenOf(List<ListedWork> shown) => shown.FirstOrDefault(w => w.Key == Chosen) ?? shown.FirstOrDefault();

    // A movement's numeral apart from its name: "III. Presto agitato" is III and Presto agitato.
    private static (string No, string Name) Numbered(ListedWork work, WorkPart part) =>
        Regex.Match(part.Name, @"^(?<no>[IVX]+)\.\s*(?<name>.*)$") is { Success: true } m ? (m.Groups["no"].Value, m.Groups["name"].Value is { Length: > 0 } name ? name : work.Title) : ("", part.Name);

    // A work's line: its catalogue number, how many movements, the other versions of it, and (for the one chosen) its levels.
    private string WorkLine(ListedWork work, bool levels = false)
    {
        var pdfs = work.Others.Count(o => byId.GetValueOrDefault(o.Id) is { IsPdf: true });
        return string.Join(" · ", new[]
        {
            work.Catalogue,
            work.Parts.Count > 1 ? $"{work.Parts.Count} {(work.Kind == "Sonatas" ? "movements" : "pieces")}" : null,
            work.Others.Count == 0 ? null : pdfs == work.Others.Count ? $"{pdfs} as PDF too" : work.Others.Count == 1 ? "1 other version" : $"{work.Others.Count} other versions",
            levels && Levels(work) is { Length: > 0 } range ? $"level{(range.Contains('–') ? "s" : "")} {range}" : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
    }

    // The levels a work's own pieces are at: "11", or "10–12".
    private string Levels(ListedWork work)
    {
        var levels = work.Parts.Select(p => byId.GetValueOrDefault(p.Id)?.Level).OfType<int>().ToList();
        return levels.Count == 0 ? "" : levels.Min() == levels.Max() ? $"{levels.Min()}" : $"{levels.Min()}–{levels.Max()}";
    }

    // A piece's level as it's shown: about, for a PDF whose notes aren't read (its collection's); a dash when it has none.
    private static string LevelShown(PieceRecord p) => p.Level is { } l ? (p.IsPdf && !p.HasNotes ? $"~{l}" : $"{l}") : "–";

    // The line under a movement: what it is, how long, and where most of it reads when that's lower than its hardest excerpt.
    private static string About(PieceRecord p) => Line(p.IsPdf ? "PDF" : null, Bars(p), Most_(p));

    // Under another version of a work: whose it is.
    private static string AboutVersion(PieceRecord p) => Line(Whose(p), p.IsPdf ? "PDF" : null, Bars(p));

    // Under a piece that is a work by itself: its catalogue number first, then the set its source has it in.
    private static string AboutAlone(ListedWork work, PieceRecord p) =>
        Line(work.Catalogue, string.Equals(p.Work?.Replace(" ", ""), work.Catalogue?.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ? null : p.Work, p.IsPdf ? "PDF" : null, Bars(p), Most_(p));

    // Under a piece in a list of pieces: the work its source names.
    private static string AboutListed(PieceRecord p) => Line(p.Work, p.IsPdf ? "PDF" : null, Most_(p));

    // Under a piece found: its work as the catalogue names it, where that isn't its own title.
    private string AboutFound(PieceRecord p) =>
        Line(workOf?.GetValueOrDefault(p.Id) is { Parts.Count: > 1 } w ? $"{w.Title}{(w.Catalogue is null ? "" : $", {w.Catalogue}")}" : p.Work, p.Imported ? "Uploaded" : null, p.IsPdf ? "PDF" : null, Bars(p));

    private static string Line(params string?[] parts) => string.Join(" · ", parts.Where(s => !string.IsNullOrEmpty(s)));

    private static string? Bars(PieceRecord p) => p.Bars is { } bars ? (bars == 1 ? "1 bar" : $"{bars} bars") : null;

    private static string? Most_(PieceRecord p) => p.TypicalLevel < p.Level ? $"most at {p.TypicalLevel}" : null;

    // Whose a version is: an edition by its name, an arrangement as what it is.
    private static string? Whose(PieceRecord p) => p.Imported ? "Uploaded" : p.Source switch
    {
        "PDMX" => "A MuseScore user's score",
        "Mutopia" => "The Mutopia Project's edition",
        null or "" => null,
        var source => source,
    };

    private static string Capital(string text) => text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    // Where most of a piece reads, as its level's list has it; 0 when it isn't graded.
    private static int LevelOf(PieceRecord p) => p.TypicalLevel ?? p.Level ?? 0;

    // A composer's surname's first letter, without its accent: Dvořák under D.
    private static string Initial(string composer) => Shelves.Surname(composer) is { Length: > 0 } surname
        ? surname[..1].Normalize(System.Text.NormalizationForm.FormD)[..1].ToUpperInvariant() : "#";

    // The pieces to play along with near your level.
    private IEnumerable<PieceRecord> Near()
    {
        var (from, to) = NearLevels();
        return pieces.Where(p => p.PlaysAlong && (p.TypicalLevel ?? p.Level) is { } l && l >= from && l <= to);
    }

    private string NearNote()
    {
        var (from, to) = NearLevels();
        var level = (reading ?? ReadingState.Start).Level;
        var yours = reading is null ? "level 1, where sight-reading starts" : $"your sight-reading level, {level}";
        return from > level + 2 ? $"Nothing to play along with reads mostly at levels {level} to {level + 2}, up to two above {yours}, so these are the easiest, at level {from}."
            : $"To play along with, reading mostly at levels {from} to {to}: a piece to learn sits up to two above {yours}.";
    }

    // Near your level's levels, for the pieces to play along with (a PDF's level is only about where its collection sits): where
    // most of a piece reads, from your sight-reading level to two above it, since a piece to learn sits above what you read at
    // sight; with none there, the easiest level above them that has some.
    private (int From, int To) NearLevels()
    {
        var level = (reading ?? ReadingState.Start).Level;
        var easiest = pieces.Where(p => p.PlaysAlong).Select(p => p.TypicalLevel ?? p.Level ?? 0).Where(l => l >= level).DefaultIfEmpty(level).Min();
        return easiest > level + 2 ? (easiest, easiest) : (level, level + 2);
    }

    // This list's own address, for a link to a group in it.
    private string Here() => Nav.Uri.Split('#')[0];

    // The keys above a long list's groups, each with how many pieces it leads to and the group it goes to: one for each composer,
    // or with more composers than that, one for each letter their surnames start with, going to the first of them. The pieces
    // without a composer keep a key of their own, and aren't counted as a composer.
    private static IEnumerable<(string Label, int Count, int At)> Keys(List<IGrouping<string, PieceRecord>> groups) =>
        ComposersIn(groups) <= MostKeys
            ? groups.Select((g, i) => (g.Key, g.Count(), i))
            : groups.Select((g, i) => (Group: g, At: i)).GroupBy(x => x.Group.Key == NotGiven ? NotGiven : Initial(x.Group.Key))
                .Select(letter => (letter.Key, letter.Sum(x => x.Group.Count()), letter.First().At));

    // How many composers a list's groups are by: the group of pieces without one isn't a composer.
    private static int ComposersIn(List<IGrouping<string, PieceRecord>> groups) => groups.Count(g => g.Key != NotGiven);

    // A piece can be heard when it has notes, or a PDF the MIDI it came with.
    private static bool Hearable(PieceRecord p) => !p.IsPdf || p.HasNotes || p.Midi is not null;

    private async Task Hear(PieceRecord piece)
    {
        note = await Openings.ToggleAsync(piece);
    }

    // A star: the first tap keeps the piece in the repertoire; a tap on one kept opens where it's kept, there.
    private async Task Star(PieceRecord piece, MouseEventArgs e)
    {
        (naming, newName, nameNote) = (false, null, null);
        if (Book.Current.Has(piece.Id))
        {
            starring = (piece, e.ClientX, e.ClientY);
            return;
        }
        await Book.StarAsync(piece.Id);
        note = $"“{piece.Title}” is in your repertoire.";
    }

    private void CloseStar() => starring = null;

    // Out of the repertoire, and so out of every collection.
    private async Task Unstar()
    {
        if (starring is not { } kept) return;

        starring = null;
        await Book.DropAsync(kept.Piece.Id);
        note = $"“{kept.Piece.Title}” is out of your repertoire.";
    }

    // A new collection, with the piece in it.
    private async Task AddCollection()
    {
        if (starring is not { } kept) return;

        if (await Book.AddAsync(newName ?? "") is not { } id)
        {
            nameNote = string.IsNullOrWhiteSpace(newName) ? "Give it a name." : "A collection has that name already.";
            return;
        }
        await Book.PutAsync(id, kept.Piece.Id, true);
        (naming, newName, nameNote) = (false, null, null);
    }

    private async Task NameKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await AddCollection();
    }

    // One piece imported opens; several (or a zip of them) stay here, with a line saying what came in.
    private async Task Import()
    {
        note = null;
        var picked = await Files.PickManyAsync();
        if (picked.Count == 0)
        {
            note = "No file chosen.";
            return;
        }

        note = picked.Count == 1 ? $"Reading {picked[0].Name}…" : $"Reading {picked.Count} files…";
        StateHasChanged();
        var (id, said) = await Prep.ImportAsync(picked);
        if (id is not null)
        {
            Nav.NavigateTo($"library/{Uri.EscapeDataString(id)}");
            return;
        }
        note = said;
        await Load();
        await ReadWorks();
    }

    public async ValueTask DisposeAsync()
    {
        Prep.Changed -= Changed;
        Openings.Changed -= Heard;
        Book.Changed -= Heard;
        // Leaving the Library (opening a piece, say) stops an opening playing, and a piece taken out can't be put back any more.
        Openings.Stop();
        Place.Removed = null;
        typing?.Cancel();
        try
        {
            if (places is not null)
            {
                await places.InvokeVoidAsync("leave");
                await places.DisposeAsync();
            }
        }
        catch (Exception e) when (e is JSDisconnectedException or JSException)
        {
        }
    }
}
