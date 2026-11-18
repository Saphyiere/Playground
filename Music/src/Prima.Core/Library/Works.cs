using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prima.Core.Library;

/// <summary>A piece as the catalogue reads it: its title, the work its source names, where it's from and whether it's a PDF.</summary>
public sealed record ListedPiece(string Id, string Title, string? Work, string? Source, bool IsPdf);

/// <summary>One of a work's parts: a movement ("I. Adagio sostenuto"), a suite's dance, a set's piece, or the piece itself.</summary>
public sealed record WorkPart(string Id, string Name);

/// <summary>
/// A work as the Library's catalogue lists it under its composer: its name without its catalogue number ("Sonata No. 8
/// “Pathetique”"), the number ("Op. 13"), its kind, its parts in order, and the other versions of it the Library holds
/// (another edition's PDFs, people's arrangements), each by its own title.
/// </summary>
public sealed record ListedWork(string Key, string Title, string? Catalogue, string Kind, IReadOnlyList<WorkPart> Parts, IReadOnlyList<WorkPart> Others)
{
    /// <summary>Its number among its kind, where its name gives one: 14 for "Sonata No. 14". A set has none.</summary>
    public int? Number { get; init; }
}

/// <summary>
/// A composer's pieces as works, read from their titles and the work each source names. The Library keeps a piece a movement,
/// and its sources title them differently, so a sonata's three movements were three rows among a hundred, told apart only by the
/// end of a long title, and the same sonata stood in three places: an edition's movements, another's PDFs, people's arrangements.
/// <list type="bullet">
/// <item>A title that names a work and a part of it is that work's part: "Sonata No. 1, Op. 2/1: I. Allegro", "Sonata No. 8
/// “Pathétique” (1st Movement: Grave)", and, where two or more titles share what's before it, "English Suite No. 1, BWV 806:
/// Allemande", "Goldberg Variations - 1", "24 Préludes Opus 28, No. 1", "Die Kunst der Fuge, Contrapunctus I, BWV 1080".</item>
/// <item>Otherwise a piece belongs to the set its source names ("Mazurkas", "Kinderszenen", "The Well-Tempered Clavier, Book
/// I"), unless that's only a catalogue number ("Op. 13"), and by itself where it names none.</item>
/// <item>An edition that titles every work and movement the same way (<see cref="Editions"/>) gives a work its parts; a PDF
/// edition of the same work, or an arrangement of it, is one of its other versions: told by the same catalogue number (the
/// work's, or one of a set's pieces'), the same number of the same kind ("Sonata No. 14"), or the name the work goes by
/// ("Moonlight").</item>
/// </list>
/// </summary>
public static partial class Works
{
    /// <summary>The sources that are editions of a composer's works, each titled the same way throughout.</summary>
    public static readonly IReadOnlySet<string> Editions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DCML", "Humdrum", "ASAP" };

    /// <summary>One composer's pieces as works, in the catalogue's order: by kind as <see cref="Shelves.Kinds"/> has them, then by name, numbers as numbers.</summary>
    public static IReadOnlyList<ListedWork> Of(IEnumerable<ListedPiece> pieces)
    {
        var read = pieces.Select(Read).ToList();
        // What's before a colon, a dash or a comma is a work's name only where another title has it too: "April: Snowdrop" is a piece.
        var shared = read.SelectMany(r => r.Loose.Select(l => l.Key).Distinct()).GroupBy(h => h).ToDictionary(g => g.Key, g => g.Count());

        var groups = new Dictionary<string, Group>();
        foreach (var r in read)
        {
            var piece = r.Piece;
            // Of the ways a title could be split, the one the most titles share; the first of them when it's a tie.
            var loose = r.Sure is null ? r.Loose.Where(l => shared[l.Key] > 1).OrderByDescending(l => shared[l.Key]).Cast<Named?>().FirstOrDefault() : null;
            var (key, title, name, number) = r.Sure is { } sure ? ("w:" + sure.Key, sure.Head, sure.Tail, sure.Number)
                : loose is { } part ? ("w:" + part.Key, part.Head, part.Tail, (int?)null)
                : SetName(piece.Work) is { } set ? ("s:" + Folded(set), set, piece.Title, null)
                : ("p:" + piece.Id, piece.Title, piece.Title, null);
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = new Group(key, title, key[0] == 's');
            group.Parts.Add(new Part(piece, name, number, PieceMark(piece)));
            // The catalogue number a part's source gives the work ("Op. 13", and "No. 2" of it after the movement).
            if (!group.Set && BareCatalogue().IsMatch(piece.Work?.Trim() ?? "")) group.Givens.Add(piece.Work!.Trim() + (r.Sure?.Sub is { } sub ? $" No. {sub}" : ""));
        }

        // A set the Library has one piece of is that piece, under its own name: "Allemande", not "English Suites".
        var works = groups.Values.Select(g => g is { Set: true, Parts: [var only] } ? Alone(only) : g).ToList();
        foreach (var work in works) work.Settle();
        Join(works);
        Gather(works);
        return [.. works.Where(w => w.Into is null)
            .OrderBy(w => Shelves.Kinds.ToList().IndexOf(w.Kind)).ThenBy(w => Natural(w.Shown), StringComparer.Ordinal).ThenBy(w => w.Key, StringComparer.Ordinal)
            .Select(w => new ListedWork(w.Key, w.Shown, w.Catalogue, w.Kind,
                [.. w.Parts.OrderBy(p => p, w.Order).Select(p => new WorkPart(p.Piece.Id, w.Named(p)))],
                [.. w.Others.OrderBy(p => p.Piece.IsPdf).ThenBy(p => Natural(p.Piece.Title), StringComparer.Ordinal).ThenBy(p => p.Piece.Id, StringComparer.Ordinal).Select(p => new WorkPart(p.Piece.Id, p.Piece.Title))])
            { Number = w.Set ? null : w.Number })];
    }

    /// <summary>
    /// A piece by itself as the catalogue names it: its work's name without the catalogue number, and its own name in that work
    /// where its title gives one ("Sonata No. 14 “quasi una fantasia” “Moonlight”" and "I. Adagio sostenuto"); no part for a
    /// piece that's a work by itself.
    /// </summary>
    public static (string Work, string? Part) NameOf(ListedPiece piece)
    {
        var work = Of([piece])[0];
        return (work.Title, work.Parts[0].Name == piece.Title.Trim() ? null : work.Parts[0].Name);
    }

    private static Group Alone(Part only)
    {
        var alone = new Group("p:" + only.Piece.Id, only.Piece.Title, false);
        alone.Parts.Add(only with { Name = only.Piece.Title });
        return alone;
    }

    // ——— Catalogue numbers ———

    // The catalogues told by their letters wherever they stand in a title, and the ones only an initial, told after a comma.
    private const string Systems = @"(?:Op\.?|Opus|BWV|KV|K\.|Hob\.?|WoO|HWV|Wq\.?|BuxWV|SwWV|TWV|RV)";
    private const string Initials = @"(?:D\.|S\.|L\.|F\.|B\.)";
    private const string Numbered = @"(?:Anh\.?\s*|posth\.?\s*)?(?:[IVXL]+:\s*\d+[a-z]?|\d+[a-z]?)(?:/\d+[a-z]?)?(?:,?\s*(?:No|Nr)\.?\s*\d+)?";

    // "Sonata No. 8, Op. 13", "24 Préludes Opus 28": a catalogue number at a name's end, apart from the name.
    [GeneratedRegex(@"(?:,\s*|\s+)(?<cat>" + Systems + @"\s*" + Numbered + @")$|,\s*(?<cat>" + Initials + @"\s*" + Numbered + @")$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TrailingCatalogue();

    // A work named by its catalogue number alone, as the Mutopia Project's pieces are: no name for a set.
    [GeneratedRegex(@"^(?:(?:Op\.?|Opus|BWV|KV|K\d?\.?|Hob\.?|D\.?|S\.?|WoO|HWV|Wq\.?|L\.?|F\.?|BuxWV|SwWV|TWV|RV|Z\.?|B\.?)\s*(?:Anh\.?\s*|posth\.?\s*)?|Anh\.?\s*)(?:\d[\w/.: ()-]*|[IVXL]+:\s*\d[\w/.: ()-]*|deest|posth\.?)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BareCatalogue();

    // An opus by itself: "Op. 10", whose pieces are numbered in their titles.
    [GeneratedRegex(@"^(?:Op\.?|Opus)\s*(?:posth\.?\s*)?\d+[a-z]?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BareOpus();

    // A catalogue number anywhere in a title, for telling two titles are of the same work.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<sys>Op\.?|Opus|BWV|KV|K\.?|Hob\.?|WoO|HWV|Wq\.?|BuxWV|SwWV)\s*(?<anh>Anh\.?\s*)?(?:posth\.?\s*)?(?<n>[IVXL]+:\s*\d+|\d+[a-z]?)\.?(?:\s*(?:/|-|,?\s*No\.?|,?\s*Nr\.?)\s*(?<sub>\d+))?(?![\p{L}\p{N}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AnyCatalogue();

    // ", No. 2" at a title's end, or before a colon: which of its opus a piece is, when its source names the opus apart.
    [GeneratedRegex(@", No\. ?(?<n>\d+)(?:$|: )", RegexOptions.CultureInvariant)]
    private static partial Regex OfItsOpus();

    private static string? SetName(string? work) => string.IsNullOrWhiteSpace(work) || BareCatalogue().IsMatch(work.Trim()) ? null : work.Trim();

    // A text that begins with a catalogue number, the ones only an initial too: "D.899 (Op. 90)", "S. 139 No. 4".
    [GeneratedRegex(@"^(?<sys>Op\.?|Opus|BWV|KV|K\.?|Hob\.?|D\.?|S\.?|WoO|HWV|Wq\.?|L\.?|F\.?|BuxWV|SwWV|TWV|RV|Z\.?|B\.?)\s*(?<anh>Anh\.?\s*)?(?:posth\.?\s*)?(?<n>[IVXL]+:\s*\d+|\d+[a-z]?)\.?(?:\s*(?:/|-|,?\s*No\.?|,?\s*Nr\.?)\s*(?<sub>\d+))?(?![\p{L}\p{N}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LeadingCatalogue();

    // A catalogue number as two titles write it alike: "Op. 27/2", "Opus 27-2" and "Op. 27 No. 2" are op27/2; "K. 331" and "KV 331" k331.
    private static string? Mark(string? text) => text is not null && AnyCatalogue().Match(text) is { Success: true } m ? Mark(m) : null;

    private static string Mark(Match m)
    {
        var system = Folded(m.Groups["sys"].Value).TrimEnd('.') switch { "opus" => "op", "kv" => "k", var other => other };
        return system + (m.Groups["anh"].Success ? "anh" : "") + Regex.Replace(Folded(m.Groups["n"].Value), @"\s", "") + (m.Groups["sub"].Success ? "/" + m.Groups["sub"].Value : "");
    }

    // The same for a text that is a catalogue number and nothing else: "D. 899 No. 3" is d899/3, "D.899 (Op. 90)" d899.
    private static string Whole(string catalogue) =>
        LeadingCatalogue().Match(catalogue.Trim()) is { Success: true } m ? Mark(m) : Mark(catalogue) ?? Regex.Replace(Folded(catalogue), @"[\s.]", "");

    // The catalogue number of a piece by itself: in its title, or its source's with the number its title ends with.
    private static string? PieceMark(ListedPiece piece)
    {
        if (Mark(piece.Title) is { } within) return within;
        if (TrailingCatalogue().Match(piece.Title) is { Success: true } end) return Whole(end.Groups["cat"].Value);

        var work = piece.Work?.Trim() ?? "";
        if (!BareCatalogue().IsMatch(work)) return null;

        var mark = Whole(work);
        return !mark.Contains('/') && OfItsOpus().Match(piece.Title) is { Success: true } of ? $"{mark}/{of.Groups["n"].Value}" : mark;
    }

    // "Op. 27/2" and "Opus 28" as they're said: Op. 27 No. 2, Op. 28.
    private static string Tidy(string catalogue) =>
        Regex.Replace(Regex.Replace(catalogue.Trim(), @"^(?:Opus|Op\.?)\s*", "Op. ", RegexOptions.IgnoreCase), @"^(Op\. \d+[a-z]?)/(\d+)$", "$1 No. $2");

    // ——— Reading a title ———

    // A work's name and a part's, as a title has them. Within: the opus a numbered piece is of, where its source names one,
    // since two sets each have a "No. 2".
    private readonly record struct Named(string Head, string Tail, int? Number = null, int? Sub = null, string Within = "")
    {
        public string Key => Within.Length > 0 ? $"{Folded(Head)} | {Folded(Within)}" : Folded(Head);
    }

    private sealed record Reading(ListedPiece Piece, Named? Sure, IReadOnlyList<Named> Loose);

    // "Sonata No. 12, Op. 26: III. Maestoso andante: Marcia funebre": the work, the movement's numeral, its name.
    [GeneratedRegex(@"^(?<head>.+?): (?<numeral>[IVX]+)\. (?<name>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Movement();

    // "Sonata No. 17 (1st Movement: Largo - Allegro), No. 2": the work, the movement, its name, and which of its opus it is.
    [GeneratedRegex(@"^(?<head>.+?) \((?<n>\d+)(?:st|nd|rd|th) Movement(?:: (?<name>[^)]*))?\)(?:, No\. (?<sub>\d+))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MovementInBrackets();

    // "Keyboard partita in A minor, BWV 827, movement 1", "Chromatic Fantasy and Fugue, BWV 903 (Fugue)": the part after the number.
    [GeneratedRegex(@"^(?<head>.+?,\s*" + Systems + @"\s*" + Numbered + @")(?:, (?<tail>[^,]+)| \((?<tail>[^)]+)\))$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PartAfterCatalogue();

    // "24 Préludes Opus 28, No. 1": a numbered piece of something.
    [GeneratedRegex(@"^(?<head>.+), (?<tail>No\. ?\d+.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedOf();

    // "Sonata No. 'quasi una fantasia' ('Moonlight')": the number left out, as the DCML's corpus titles Beethoven's fourteenth.
    [GeneratedRegex(@"\bNo\. (?=['‘“(""])", RegexOptions.CultureInvariant)]
    private static partial Regex NumberLeftOut();

    // A piece's id from an edition ends with its work's number and its movement's: "…-piano-sonatas-14-1".
    [GeneratedRegex(@"-0*(?<work>\d+)-\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex NumbersInId();

    private static Reading Read(ListedPiece piece)
    {
        var title = piece.Title.Trim();
        // A work's number left out of its title is in the piece's id.
        if (NumberLeftOut().IsMatch(title) && NumbersInId().Match(piece.Id) is { Success: true } id) title = NumberLeftOut().Replace(title, $"No. {id.Groups["work"].Value} ", 1);
        if (Movement().Match(title) is { Success: true } m && Roman(m.Groups["numeral"].Value) is { } numeral)
            return new Reading(piece, new Named(m.Groups["head"].Value.Trim(), $"{m.Groups["numeral"].Value}. {m.Groups["name"].Value.Trim()}", numeral), []);
        if (MovementInBrackets().Match(title) is { Success: true } b && int.TryParse(b.Groups["n"].Value, CultureInfo.InvariantCulture, out var n) && n is > 0 and < 40)
        {
            var name = b.Groups["name"].Value.Trim();
            int? sub = int.TryParse(b.Groups["sub"].Value, CultureInfo.InvariantCulture, out var s) ? s : null;
            return new Reading(piece, new Named(b.Groups["head"].Value.Trim(), name.Length > 0 ? $"{Numeral(n)}. {name}" : $"{Numeral(n)}.", n, sub), []);
        }

        // The ways it could be a work and a part of it, each counting only if another title has the same work.
        var loose = new List<Named>();
        var given = piece.Work?.Trim() ?? "";
        void Could(string head, string tail, string within = "")
        {
            if (head.Trim().Length > 0 && tail.Trim().Length > 0) loose.Add(new Named(head.Trim(), Capital(tail.Trim()), Within: within));
        }

        // The first colon or dash with a space after it.
        var at = new[] { title.IndexOf(": ", StringComparison.Ordinal), title.IndexOf(" - ", StringComparison.Ordinal) }.Where(i => i > 0).DefaultIfEmpty(-1).Min();
        if (at > 0) Could(title[..at], title[(at + (title[at] == ':' ? 2 : 3))..]);
        if (PartAfterCatalogue().Match(title) is { Success: true } after) Could(after.Groups["head"].Value, after.Groups["tail"].Value);
        if (NumberedOf().Match(title) is { Success: true } of) Could(of.Groups["head"].Value, of.Groups["tail"].Value, BareCatalogue().IsMatch(given) ? given : "");

        // The first comma, where the source names the work by its catalogue number alone and more than a word stands before it:
        // "Die Kunst der Fuge, Contrapunctus I, BWV 1080". Not a chorale's first line, "Ach Gott, vom Himmel sieh darein", nor
        // a numbered piece, which is within its opus.
        var plain = TrailingCatalogue().Match(title) is { Success: true } end ? title[..end.Index] : title;
        if (BareCatalogue().IsMatch(given) && !NumberedOf().IsMatch(title) && plain.IndexOf(", ", StringComparison.Ordinal) is > 0 and var comma && plain[..comma].Trim().Contains(' '))
            Could(plain[..comma], plain[(comma + 2)..]);
        return new Reading(piece, null, loose);
    }

    private static string Capital(string text) => char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    // ——— A work being put together ———

    private sealed record Part(ListedPiece Piece, string Name, int? Number, string? Mark);

    private sealed class Group(string key, string title, bool set)
    {
        public string Key => key;
        public string Title => title;
        public bool Set => set;
        public List<Part> Parts { get; } = [];
        public List<Part> Others { get; } = [];
        public HashSet<string> Givens { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Group? Into { get; set; }

        public string Shown { get; private set; } = "";
        // Its name as two sources write it alike: small letters, no accents or punctuation.
        public string Called { get; private set; } = "";
        public string? Catalogue { get; set; }
        public string Kind { get; private set; } = "Character pieces";
        public IComparer<Part> Order { get; private set; } = Comparer<Part>.Default;
        private string strip = "";

        // How well a work's parts are known: an edition's first, then a work in parts, a piece in notation, a PDF. Among those
        // alike, notation before PDFs, and the more parts the better.
        public int Tier => Parts.Any(p => p.Piece.Source is { } s && Editions.Contains(s)) ? 0 : Parts.Count > 1 ? 1 : Parts[0].Piece.IsPdf ? 3 : 2;
        public (int Tier, int Pdf, int Fewer) Standing => (Tier, Parts.All(p => p.Piece.IsPdf) ? 1 : 0, -Parts.Count);

        public int? Number { get; private set; }
        public string? Genre { get; private set; }
        public string? Marked { get; private set; }
        public HashSet<string> Marks { get; } = [];
        public HashSet<(string Genre, int Number)> Numbers { get; } = [];
        public List<string> Nicknames { get; private set; } = [];

        public void Settle()
        {
            // Its name and its catalogue number apart: "Sonata No. 8 'Pathetique', Op. 13" is Sonata No. 8 “Pathetique”, Op. 13.
            var name = title;
            string? marked = null;
            Catalogue = null;
            if (TrailingCatalogue().Match(name) is { Success: true } cat)
            {
                Catalogue = Tidy(cat.Groups["cat"].Value);
                name = Regex.Replace(name[..cat.Index], @"[\s,;(]+(?:Op\.?)?$", "", RegexOptions.IgnoreCase);
            }
            else if (!set && Givens.Count == 1)
            {
                Catalogue = Tidy(Givens.First());
                // "Etüde C-Dur, No. 1" of Op. 10 is Op. 10 No. 1; "Impromptu in As-dur, No. 4" of D.899 (Op. 90) is the fourth of D. 899.
                if (!Whole(Catalogue).Contains('/') && OfItsOpus().Match(name) is { Success: true } of && of.Index + of.Length == name.Length)
                {
                    marked = $"{Whole(Catalogue)}/{of.Groups["n"].Value}";
                    if (BareOpus().IsMatch(Catalogue)) (Catalogue, name) = ($"{Catalogue} No. {of.Groups["n"].Value}", name[..of.Index]);
                }
            }
            Nicknames = [.. Nickname().Matches(name).Select(m => Folded(m.Groups["nick"].Value)).Where(n => n.Length >= 5)];
            Shown = Regex.Replace(Nickname().Replace(name, m => $" “{m.Groups["nick"].Value}”"), @"\s+", " ").Trim();
            if (Shown.Length == 0) Shown = title;
            // A file's name for a title: "Minuet_in_C_Minor".
            Shown = Shown.Replace('_', ' ');
            Called = Regex.Replace(Folded(Shown), @"[^\p{L}\p{N}]+", " ").Trim();

            Kind = Parts.GroupBy(p => Shelves.Kind(p.Piece.Title, p.Piece.Work)).OrderByDescending(g => g.Count()).ThenBy(g => Shelves.Kinds.ToList().IndexOf(g.Key)).First().Key;

            // What tells it's the same work as another: its catalogue number and its pieces', its number among its kind.
            Marked = marked ?? (Catalogue is not null ? Whole(Catalogue) : Mark(title));
            Marks.Clear();
            if (Marked is not null) Marks.Add(Marked);
            Marks.UnionWith(Parts.Select(p => p.Mark).OfType<string>());

            var words = Plain(name);
            Number = NumberAmongItsKind(words);
            Genre = Genres.FirstOrDefault(g => words.Contains(g, StringComparison.Ordinal));
            Numbers.Clear();
            if (!set && Number is { } number && Genre is { } genre) Numbers.Add((genre, number));
            // A set of one kind has its pieces numbered among that kind: "Ballade No. 1 in G minor, Op. 23" of the Ballades.
            // Not the Well-Tempered Clavier's "Prelude No. 1": each book has one.
            if (set && Genre is { } kind)
                foreach (var part in Parts)
                {
                    var its = Plain(part.Piece.Title);
                    if (its.Contains(kind, StringComparison.Ordinal) && NumberAmongItsKind(its) is { } n) Numbers.Add((kind, n));
                }

            // A set's pieces titled alike drop what they share: "24 Préludes Opus 28, No. 1" is No. 1 of them.
            strip = "";
            if (Parts.Count > 2)
            {
                var common = Parts.Select(p => p.Name).Aggregate(Common);
                var cut = Math.Max(common.LastIndexOf(", ", StringComparison.Ordinal) + 2, Math.Max(common.LastIndexOf(" - ", StringComparison.Ordinal) + 3, common.LastIndexOf(": ", StringComparison.Ordinal) + 2));
                if (cut > 4 && Parts.All(p => p.Name.Length > cut && StartsNumbered().IsMatch(p.Name[cut..]))) strip = common[..cut];
            }

            var folded = Folded(title);
            var suite = folded.Contains("suite", StringComparison.Ordinal) || folded.Contains("partita", StringComparison.Ordinal)
                || Parts.Count(p => DanceRank(Folded(p.Name)) is not null) > 1 && Parts.All(p => p.Number is null);
            Order = Comparer<Part>.Create((a, b) =>
            {
                var by = (a.Number ?? int.MaxValue).CompareTo(b.Number ?? int.MaxValue);
                if (by == 0 && suite) by = (DanceRank(Folded(a.Name)) ?? 4).CompareTo(DanceRank(Folded(b.Name)) ?? 4);
                if (by == 0) by = Published(a.Name).CompareTo(Published(b.Name));
                if (by == 0) by = Before(a.Name).CompareTo(Before(b.Name));
                if (by == 0) by = string.CompareOrdinal(Natural(a.Name), Natural(b.Name));
                return by != 0 ? by : string.CompareOrdinal(a.Piece.Id, b.Piece.Id);
            });
        }

        public string Named(Part part) => strip.Length > 0 && part.Name.StartsWith(strip, StringComparison.Ordinal) ? part.Name[strip.Length..] : part.Name;

        private static string Common(string a, string b)
        {
            var n = 0;
            while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
            return a[..n];
        }
    }

    // The name a work goes by, in quotes after its number: 'Pathetique', (‘Hammerklavier’), “Pathétique”. Not an apostrophe in a word.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])\s*\(?['‘“""](?<nick>[^'‘’“”""()]{3,40})['’”""]\)?(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex Nickname();

    // "No. 14", "n. 25", "Nr 3": a work's number among its kind.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:no|nr|n)\.?\s*(?<n>\d+)(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex WorkNumber();

    [GeneratedRegex(@"^(No\.? ?)?\d", RegexOptions.CultureInvariant)]
    private static partial Regex StartsNumbered();

    // The kinds whose works are numbered, by the letters every spelling of them shares ("sonate", "sonata").
    private static readonly string[] Genres =
        ["sonat", "suite", "partita", "ballad", "scherzo", "rhapsod", "nocturne", "walt", "vals", "mazur", "polon", "tude", "prelud", "impromptu", "invention", "sinfonia", "variation", "fug", "bagatelle", "concert"];

    // Words that tell one numbered set from another of the same kind: an English Suite No. 2 isn't a French one.
    private static readonly string[] Qualifiers = ["english", "french", "italian", "hungarian", "transcendental", "little"];

    // A title's words without its catalogue numbers: the "No. 2" of "Op. 10 No. 2" isn't the étude's number among études.
    private static string Plain(string title) => Folded(AnyCatalogue().Replace(TrailingCatalogue().Replace(title, ""), " "));

    private static int? NumberAmongItsKind(string words) =>
        WorkNumber().Match(words) is { Success: true } no && int.TryParse(no.Groups["n"].Value, CultureInfo.InvariantCulture, out var n) ? n : null;

    // ——— One work from several ———

    // An edition's work under two names is one work: Mozart's K. 545 is "Piano Sonata No. 15" for two movements and "Sonata
    // No. 15" for the third.
    private static void Join(List<Group> works)
    {
        foreach (var same in works.Where(w => !w.Set && w.Tier == 0 && w.Marked is not null).GroupBy(w => w.Marked).Where(g => g.Count() > 1).ToList())
        {
            var whole = same.OrderByDescending(w => w.Parts.Count).ThenBy(w => w.Key, StringComparer.Ordinal).First();
            foreach (var rest in same.Where(w => w != whole && w.Parts.All(p => p.Number is { } n && whole.Parts.All(q => q.Number != n))).ToList())
            {
                whole.Parts.AddRange(rest.Parts);
                rest.Into = whole;
            }
            whole.Settle();
        }
    }

    // A work gathers the other versions of it: the same catalogue number first, then the same number of the same kind, then the
    // name it goes by, when only one work goes by it.
    private static void Gather(List<Group> works)
    {
        Pass(works, ByCatalogue);
        Pass(works, ByNumber);
        Pass(works, ByName);
        // A name counts once the editions of a work are one: the DCML's 'Pathetique' and the Mutopia Project's “Pathétique”.
        var names = works.Where(w => w.Into is null && !w.Set).SelectMany(w => w.Nicknames.Select(n => (Name: n, Work: w))).GroupBy(x => x.Name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().Work);
        // By name only within its kind: the “Pastoral” sonata isn't the Pastoral Symphony.
        Pass(works, (work, lesser, words) => !work.Set && !lesser.Set && work.Genre is { } genre && words.Contains(genre, StringComparison.Ordinal)
            && work.Nicknames.Any(n => names.TryGetValue(n, out var only) && only == work && words.Contains(n, StringComparison.Ordinal)));
    }

    // Each lesser work (an arrangement, a PDF edition) goes into the work it's a version of, if any: the best known of them.
    private static void Pass(List<Group> works, Func<Group, Group, string, bool> same)
    {
        foreach (var lesser in works.Where(w => w.Into is null && w.Tier > 0).OrderByDescending(w => w.Standing).ThenBy(w => w.Key, StringComparer.Ordinal).ToList())
        {
            var words = Folded(lesser.Title + " " + string.Join(" ", lesser.Parts.Select(p => p.Piece.Title)));
            var into = works.Where(w => w != lesser && w.Into is null && w.Standing.CompareTo(lesser.Standing) < 0 && same(w, lesser, words))
                .OrderBy(w => w.Standing).ThenBy(w => w.Key, StringComparer.Ordinal).FirstOrDefault();
            if (into is null) continue;

            lesser.Into = into;
            // Two sources' one piece: the catalogue number either gives is its number.
            if (!into.Set && into.Parts.Count == 1 && lesser.Parts.Count == 1) into.Catalogue ??= lesser.Catalogue;
            into.Others.AddRange(lesser.Parts);
            into.Others.AddRange(lesser.Others);
            lesser.Others.Clear();
        }
    }

    // The same catalogue number: the work's own, or one of its pieces' ("BWV 847" among the Well-Tempered Clavier's). A work in
    // parts, each with its own number, is a version of the one that has two of them or more: another edition's "Das
    // Wohltemperierte Clavier I". A set named without a number ("Mazurkas") is nobody's version.
    private static bool ByCatalogue(Group work, Group lesser, string lesserWords)
    {
        // An opus by itself is a version of the work whose pieces are numbered in it ("Kinderszenen Op. 15", a mazurka of Op. 33),
        // and a numbered piece of an opus a version of the work in parts that is the whole opus: a prelude of the 24, Op. 28.
        if (lesser.Marked is { } mark)
            return work.Marks.Contains(mark) || work.Marks.Any(m => m.StartsWith(mark + "/", StringComparison.Ordinal))
                || work.Parts.Count > 1 && work.Marked is { } whole && mark.StartsWith(whole + "/", StringComparison.Ordinal);
        if (lesser.Set || lesser.Parts.Count < 2) return false;

        var marks = lesser.Parts.Select(p => p.Mark).OfType<string>().Distinct().ToList();
        var both = marks.Count(work.Marks.Contains);
        return both > 1 || both == 1 && marks.Count < 3;
    }

    // The same number of the same kind, "Sonata No. 14", "Ballade No. 1", where their catalogue numbers don't say otherwise.
    private static bool ByNumber(Group work, Group lesser, string lesserWords) =>
        !lesser.Set && lesser.Number is { } number
        && Genres.Any(g => lesserWords.Contains(g, StringComparison.Ordinal) && work.Numbers.Contains((g, number)))
        && Qualifiers.All(q => Folded(work.Title).Contains(q, StringComparison.Ordinal) == lesserWords.Contains(q, StringComparison.Ordinal))
        && Agree(work.Marked, lesser.Marked);

    // The same name, where their catalogue numbers don't say otherwise: two sources' "Children's Corner", "The Seasons".
    private static bool ByName(Group work, Group lesser, string lesserWords) =>
        lesser.Called.Length > 4 && work.Called == lesser.Called
        && (Agree(work.Marked, lesser.Marked) || work.Marked!.TrimEnd(Letters) == lesser.Marked!.TrimEnd(Letters));

    private static readonly char[] Letters = [.. "abcdefghijklmnopqrstuvwxyz"];

    // Two catalogue numbers that could be one work's: the same, or one the whole opus of the other ("Op. 2" and "Op. 2 No. 1").
    // Op. 10 No. 1 and Op. 10 No. 2 are two works.
    private static bool Agree(string? a, string? b) =>
        a is null || b is null || a == b || a.StartsWith(b + "/", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal);

    // ——— Order ———

    // A suite's dances in the order they're played: the prelude, allemande, courante, sarabande, the lighter dances, the gigue.
    private static int? DanceRank(string name) =>
        Regex.IsMatch(name, @"^(pr(e|ae)lud|prelude|preambul|ouverture|overture|sinfonia|fantasia|toccata|introdu)") ? 0
        : name.StartsWith("allemande", StringComparison.Ordinal) ? 1
        : Regex.IsMatch(name, @"^(courante|corrente|double)") ? 2
        : name.StartsWith("sarabande", StringComparison.Ordinal) ? 3
        : Regex.IsMatch(name, @"^(gigue|giga)") ? 5
        : Regex.IsMatch(name, @"^(gavott|bourr|menuet|minuet|passepied|air|anglaise|loure|polonaise|burlesca|scherzo|capriccio|rondeau|aria)") ? 4
        : null;

    // Where a piece stands in what it was published in: "Op. 68 No. 4" after Op. 12's, "No. 3" third; else all alike.
    private static (int Opus, int Number) Published(string name)
    {
        if (OfAnOpus().Match(name) is { Success: true } m) return (int.Parse(m.Groups["opus"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture));

        return NumberAmongItsKind(Folded(name)) is { } n ? (0, n) : (int.MaxValue, 0);
    }

    [GeneratedRegex(@"(?:Op\.|S\.|D\.)\s*(?:posth\.\s*)?(?<opus>\d+)[a-z]?,? No\. ?(?<n>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex OfAnOpus();

    // A prelude comes before the fugue of its number.
    private static int Before(string name) => Folded(name).StartsWith("prelud", StringComparison.Ordinal) || Folded(name).StartsWith("praelud", StringComparison.Ordinal) ? 0 : 1;

    /// <summary>A name as it's sorted: small letters without accents, its numbers as numbers ("No. 2" before "No. 11").</summary>
    public static string Natural(string text) => Regex.Replace(Folded(text), @"\d+", m => m.Value.PadLeft(5, '0'));

    private static string Folded(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();

    private static readonly string[] Units = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];

    private static string Numeral(int n) => new string('X', n / 10) + Units[n % 10];

    private static int? Roman(string numeral) => Enumerable.Range(1, 39).Cast<int?>().FirstOrDefault(n => Numeral(n!.Value) == numeral);
}
