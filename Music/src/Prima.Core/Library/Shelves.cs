using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prima.Core.Library;

/// <summary>
/// How the Library is browsed once it holds thousands of pieces: by composer, listed by surname; by period, from the composer;
/// by kind, from the piece's title and work; and by level. A composer it doesn't know (an import's, a living one's, a traditional
/// tune) is in Other; a piece that's none of the named kinds is a character piece, as most piano music without a form in its
/// name is.
/// </summary>
public static class Shelves
{
    /// <summary>The periods, in the order they're shown.</summary>
    public static readonly IReadOnlyList<string> Periods = ["Baroque", "Classical", "Romantic", "1890–1930", "Ragtime", "Teaching pieces", "Other"];

    /// <summary>The kinds, in the order they're shown.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["Sonatas", "Études", "Preludes and fugues", "Dances", "Character pieces", "Chorales and hymns", "Variations"];

    // Each period's composers, as the Library and imports name them. Earlier lines win: the teaching composers before their
    // periods, C. P. E., J. C., J. C. F. and W. F. Bach (Classical) before their father (Baroque), and first of all the few whose
    // name another composer's pattern would take: Hans Leo Hassler, older than any shelf, with the earliest there is (not with
    // J. W. Hässler), the hymn composer William G. Fischer with the Romantics (not with J. C. F. Fischer), and Richard Strauss
    // after them (not with Johann).
    private static readonly (string Period, Regex Who)[] Composers =
    [
        .. new (string, string)[]
        {
            ("Baroque", @"\bhans leo h(ä|a|ae)(ss|ß)ler\b|\bh(ä|a|ae)(ss|ß)ler,\s*hans leo\b"),
            ("Romantic", @"\bwilliam (g\.?|gustavus) fischer\b|\bfischer,\s*william\b"),
            ("1890–1930", @"\brichard strauss\b|\bstrauss,\s*richard\b"),
            ("Teaching pieces", @"czerny|burgm(ü|u|ue)ller|gurlitt|lemoine|duvernoy|k(ö|o|oe)hler|\bbeyer\b|bertini|streabbog|gobbaerts|\bhanon\b|spindler|lichner|concone|l(o|ö)e?schhorn|\bbehr\b|oesten|wohlfahrt|ellmenreich|\bt(ü|u)rk\b|le couppey|\bbiehl\b"),
            ("Ragtime", @"joplin|turpin|scott hayden|james scott|joseph lamb|charles hunter|\bsousa\b"),
            ("1890–1930", @"debussy|\bsatie\b|\bravel\b|scriabin|skryabin|rachmaninoff|rachmaninov|bart(ó|o)k|poulenc|prokofiev|jan(á|a)(č|c)ek|\breger\b|busoni|medtner|glazunov|stanchinsky|lyapunov|liapunov|granados|alb(é|e)niz|chaminade|\belgar\b"),
            ("Classical", @"c\.?\s?p\.?\s?e\.?\s?bach|carl philipp emanuel|j\.?\s?c\.?\s?bach|johann christian bach|j\.?\s?c\.?\s?f\.?\s?bach|johann christoph friedrich bach|friedemann|\bhaydn|mozart|clementi|ko(ž|z)eluc?h|dussek|dus(í|i)k|kuhlau|diabelli|beethoven|pleyel|vanhal|wanhal|\bbenda\b|\bneefe\b|h(ä|a|ae)(ss|ß)ler|cimarosa|antonio soler|kirnberger|johann andr(é|e)|james hook|eberhardt? m(ü|u)ller|\bhummel\b|\bcramer\b"),
            ("Romantic", @"schubert|mendelssohn|\bhensel\b|chopin|schumann|\bliszt\b|\bbrahms\b|john field|\bweber\b|\bgrieg\b|tchaikovsk|tschaikowsk|mussorgsk|moussorgsk|borodin|rimsky|balakirev|glinka|dvo(ř|r)(á|a)k|smetana|\balkan\b|\bheller\b|moscheles|thalberg|henselt|\braff\b|reinecke|godard|chabrier|c(é|e)sar franck|saint-sa(ë|e)ns|\bbizet\b|gounod|massenet|\bwagner\b|\bverdi\b|moszkowski|arensky|lyadov|liadov|gottschalk|macdowell|blumenthal|adolphe adam|minkus|\bstrauss\b|rubinstein|\bascher\b|faur(é|e)\b|peja(č|c)evi|\bpease\b|yaniewicz|\bcui\b"),
            ("Baroque", @"\bbach\b|h(a|ä|ae)ndel|scarlatti|couperin|rameau|purcell|telemann|pachelbel|frescobaldi|sweelinck|froberger|buxtehude|\bbyrd\b|gibbons|john bull|\bfischer\b|daquin|\broyer\b|kuhnau|petzold|zipoli|muffat|corelli|vivaldi|\bb(ö|o)hm\b"),
        }.Select(c => (c.Item1, new Regex(c.Item2, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))),
    ];

    // Each kind by words in a piece's title or work; earlier lines win, so a sonata's minuet is a sonata's and a suite's
    // allemande a dance. A fantasy, a capriccio or a toccata is none of the forms by its name: it goes by what else its title
    // says (a fantasy and fugue with the fugues, a polonaise-fantasy with the dances), and otherwise with the character pieces.
    // Debussy's Doctor Gradus ad Parnassum makes fun of Clementi's studies; it isn't one.
    private static readonly (string Kind, Regex What)[] KindWords =
    [
        .. new (string, string)[]
        {
            ("Chorales and hymns", @"\bchorales?\b|\bchoral\b|\bhymn|\bpsalm"),
            ("Sonatas", @"sonat"),
            ("Variations", @"variat|chaconne|passacaglia"),
            ("Études", @"[ée]tude|\bstud(y|ies)\b|exercise|übung|uebung|school of|(?<!doctor )gradus"),
            ("Preludes and fugues", @"prelud|pr(é|e)lude|pr(ä|ae|a)eludium|praeambulum|\bfug(ue|a|hetta|ato)\b|invention|sinfonia|ricercar|recercar|canzon|\bcanon\b|contrapunctus|well-tempered"),
            ("Dances", @"waltz|valse|walzer|mazur|minuet|menuet|polonaise|polacca|gavott|bourr|sarabande|gigue|\bgiga\b|allemande|courante|corrente|polka|tarant|l(ä|a)ndler|(é|e)cossaise|galop|\bmarch|\bmarche\b|\brags?\b|ragtime|cake-?\s?walk|two-step|bolero|sicilian|passepied|rigaudon|hornpipe|\bjig\b|musette|tango|habanera|cz(á|a)rd(á|a)s|\bdance|\bdanse|\btanz|loure|forlane|anglaise|contredanse|quadrille|schottische|seguidilla|\bjota\b"),
        }.Select(k => (k.Item1, new Regex(k.Item2, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))),
    ];

    /// <summary>The composer's period, or Other.</summary>
    public static string Period(string? composer) =>
        string.IsNullOrWhiteSpace(composer) ? "Other" : Composers.FirstOrDefault(c => c.Who.IsMatch(composer)).Period ?? "Other";

    /// <summary>The piece's kind, from its title and its work.</summary>
    public static string Kind(string title, string? work) =>
        KindWords.FirstOrDefault(k => k.What.IsMatch(title) || work is not null && k.What.IsMatch(work)).Kind ?? "Character pieces";

    // Surnames of two words that are one name, read whole: Ralph Vaughan Williams is listed under V, not W.
    private static readonly string[] TwoWordSurnames = ["Vaughan Williams", "Mendelssohn Bartholdy", "Coleridge Taylor", "Rimsky Korsakov", "Saint Saëns", "Saint Saens"];

    /// <summary>
    /// The name a composer is listed by: the surname ("Ludwig van Beethoven" is Beethoven, "Johann Strauss II" and "John Philip
    /// Sousa, Jr." Strauss and Sousa), the known surnames of two words whole ("Ralph Vaughan Williams" is Vaughan Williams), a name
    /// written surname first by what's before its comma ("Bach, Johann Sebastian"), and for "Bach and others" the first name given.
    /// </summary>
    public static string Surname(string? composer)
    {
        if (string.IsNullOrWhiteSpace(composer)) return "";

        var name = Bare(Regex.Replace(composer, @"\s+(and|&|und)\s+.*$|\s*\(.*\)\s*$", "", RegexOptions.IgnoreCase));
        if (name.IndexOf(',') is > 0 and var comma) return Bare(name[..comma]);
        if (TwoWordSurnames.FirstOrDefault(s => name.Equals(s, StringComparison.OrdinalIgnoreCase) || name.EndsWith(" " + s, StringComparison.OrdinalIgnoreCase)) is { } whole)
            return name[^whole.Length..];

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? name : words[^1];

        // A generation after a name (Jr., Sr., II), with a comma before it or not, isn't part of it.
        static string Bare(string name) => Regex.Replace(name.Trim(), @"(,?\s+(I{1,3}|IV|Jr\.?|Sr\.?))+$", "").Trim();
    }

    /// <summary>
    /// Whether a search finds what's typed in a piece's words: each word typed by itself, in any order, and every one of them
    /// ("beethoven sonata 14"), big or small letters and accents aside, so "Dvorak" finds Dvořák. A number typed is a whole
    /// number there ("14" finds No. 14 and Op. 14, not Op. 140), or a movement's: a movement is asked for by its number or as an
    /// ordinal ("1", "first", "1st"), and found by its numeral ("I. Adagio") or the way an arrangement's title says it ("1st
    /// movement", "Movement 1"). "Movement" itself needn't be written, so "moonlight first movement" finds the Moonlight's I.
    /// </summary>
    public static bool Finds(string words, string typed)
    {
        var asked = typed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Folded).Where(w => w.Any(char.IsLetterOrDigit)).ToList();
        // "Movement" is how the number beside it is meant: left out when there's anything else to find.
        if (asked.Any(w => !MovementWords.Contains(w))) asked.RemoveAll(MovementWords.Contains);
        if (asked.Count == 0) return true;

        var text = Folded(words);
        HashSet<int>? numbers = null;
        HashSet<int>? movements = null;
        foreach (var word in asked)
        {
            if (word.All(char.IsAsciiDigit) && int.TryParse(word, CultureInfo.InvariantCulture, out var number))
            {
                numbers ??= [.. WholeNumber.Matches(text).Select(m => int.TryParse(m.Value, CultureInfo.InvariantCulture, out var n) ? n : -1)];
                movements ??= Movements(text);
                if (!numbers.Contains(number) && !movements.Contains(number)) return false;
            }
            else if (Ordinal(word) is { } nth)
            {
                movements ??= Movements(text);
                if (!movements.Contains(nth) && !text.Contains(word, StringComparison.Ordinal)) return false;
            }
            else if (!text.Contains(word, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    // Words that say a number beside them is a movement's.
    private static readonly HashSet<string> MovementWords = ["movement", "movements", "mvt", "mvt.", "mvts", "mvmt", "mov", "mov."];

    private static readonly string[] OrdinalWords = ["first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth"];

    private static readonly Regex WholeNumber = new(@"\d+", RegexOptions.CultureInvariant);

    private static readonly Regex DigitOrdinal = new(@"^(\d+)(st|nd|rd|th)$", RegexOptions.CultureInvariant);

    // A movement in a title, in folded words: its numeral and a full stop ("op. 27/2: iii. presto", not "hob. xvi:30" nor "i.e."),
    // an ordinal ("1st movement", "the first and second movements"), or the number after "movement" ("movement 1", "mvt. 2").
    private static readonly Regex MovementInTitle = new(
        @"(?<![\p{L}\p{N}])(?<numeral>[ivx]+)\.(?!\p{L})|(?<![\p{L}\p{N}])(?<nth>\d+(st|nd|rd|th)|first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth)(?![\p{L}\p{N}])|\b(movement|mvt|mvmt|mov)\.?\s*(no\.?\s*)?(?<after>\d+)",
        RegexOptions.CultureInvariant);

    // The movements a title names, by number.
    private static HashSet<int> Movements(string text)
    {
        var found = new HashSet<int>();
        foreach (Match m in MovementInTitle.Matches(text))
        {
            if (m.Groups["numeral"].Success && Roman(m.Groups["numeral"].Value) is { } numeral) found.Add(numeral);
            else if (m.Groups["nth"].Success && Ordinal(m.Groups["nth"].Value) is { } nth) found.Add(nth);
            else if (m.Groups["after"].Success && int.TryParse(m.Groups["after"].Value, CultureInfo.InvariantCulture, out var after)) found.Add(after);
        }
        return found;
    }

    // "first" or "1st" as 1; null for any other word.
    private static int? Ordinal(string word) =>
        Array.IndexOf(OrdinalWords, word) is >= 0 and var at ? at + 1
        : DigitOrdinal.Match(word) is { Success: true } m && int.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var n) ? n : null;

    // The Roman numerals below 40, written the usual way ("xiv" is 14): "iiii" or "vx" isn't one.
    private static readonly Dictionary<string, int> Romans = Enumerable.Range(1, 39)
        .ToDictionary(n => new string('x', n / 10) + new[] { "", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix" }[n % 10], n => n);

    private static int? Roman(string numeral) => Romans.TryGetValue(numeral, out var n) ? n : null;

    // Small letters without their accents, as a search reads them.
    private static string Folded(string text) => Plain(text).ToLowerInvariant();

    // The letters without their accents, as the Library's letter keys take them: ř is r, é e.
    private static string Plain(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
}
