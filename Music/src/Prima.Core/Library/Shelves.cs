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
    // periods, C. P. E., J. C. and W. F. Bach (Classical) before their father (Baroque).
    private static readonly (string Period, Regex Who)[] Composers =
    [
        .. new (string, string)[]
        {
            ("Teaching pieces", @"czerny|burgm(ü|u|ue)ller|gurlitt|lemoine|duvernoy|k(ö|o|oe)hler|\bbeyer\b|bertini|streabbog|gobbaerts|\bhanon\b|spindler|lichner|concone|l(o|ö)e?schhorn|\bbehr\b|oesten|wohlfahrt|ellmenreich|\bt(ü|u)rk\b|le couppey|\bbiehl\b"),
            ("Ragtime", @"joplin|turpin|scott hayden|james scott|joseph lamb|charles hunter|\bsousa\b"),
            ("1890–1930", @"debussy|\bsatie\b|\bravel\b|scriabin|skryabin|rachmaninoff|rachmaninov|bart(ó|o)k|poulenc|prokofiev|jan(á|a)(č|c)ek|\breger\b|busoni|medtner|glazunov|stanchinsky|lyapunov|liapunov|granados|alb(é|e)niz|chaminade|\belgar\b"),
            ("Classical", @"c\.?\s?p\.?\s?e\.?\s?bach|carl philipp emanuel|j\.?\s?c\.?\s?bach|johann christian bach|friedemann|\bhaydn|mozart|clementi|ko(ž|z)eluc?h|dussek|dus(í|i)k|kuhlau|diabelli|beethoven|pleyel|vanhal|wanhal|\bbenda\b|\bneefe\b|h(ä|a|ae)(ss|ß)ler|cimarosa|antonio soler|kirnberger|johann andr(é|e)|james hook|eberhardt? m(ü|u)ller|\bhummel\b|\bcramer\b"),
            ("Romantic", @"schubert|mendelssohn|\bhensel\b|chopin|schumann|\bliszt\b|\bbrahms\b|john field|\bweber\b|\bgrieg\b|tchaikovsk|tschaikowsk|mussorgsk|moussorgsk|borodin|rimsky|balakirev|glinka|dvo(ř|r)(á|a)k|smetana|\balkan\b|\bheller\b|moscheles|thalberg|henselt|\braff\b|reinecke|godard|chabrier|c(é|e)sar franck|saint-sa(ë|e)ns|\bbizet\b|gounod|massenet|\bwagner\b|\bverdi\b|moszkowski|arensky|lyadov|liadov|gottschalk|macdowell|blumenthal|adolphe adam|minkus|\bstrauss\b|rubinstein|\bascher\b|faur(é|e)\b|peja(č|c)evi|\bpease\b|yaniewicz|\bcui\b"),
            ("Baroque", @"\bbach\b|h(a|ä|ae)ndel|scarlatti|couperin|rameau|purcell|telemann|pachelbel|frescobaldi|sweelinck|froberger|buxtehude|\bbyrd\b|gibbons|john bull|\bfischer\b|daquin|\broyer\b|kuhnau|petzold|zipoli|muffat|corelli|vivaldi|\bb(ö|o)hm\b"),
        }.Select(c => (c.Item1, new Regex(c.Item2, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))),
    ];

    // Each kind by words in a piece's title or work; earlier lines win, so a sonata's minuet is a sonata's and a suite's
    // allemande a dance.
    private static readonly (string Kind, Regex What)[] KindWords =
    [
        .. new (string, string)[]
        {
            ("Chorales and hymns", @"\bchorales?\b|\bchoral\b|\bhymn|\bpsalm"),
            ("Sonatas", @"sonat"),
            ("Variations", @"variat|chaconne|passacaglia"),
            ("Études", @"[ée]tude|\bstud(y|ies)\b|exercise|übung|uebung|school of|gradus"),
            ("Preludes and fugues", @"prelud|pr(é|e)lude|pr(ä|ae|a)eludium|praeambulum|\bfug(ue|a|hetta|ato)\b|invention|sinfonia|fantasi|ricercar|recercar|capriccio|canzon|\bcanon\b|contrapunctus|well-tempered|toccat"),
            ("Dances", @"waltz|valse|walzer|mazur|minuet|menuet|polonaise|polacca|gavott|bourr|sarabande|gigue|\bgiga\b|allemande|courante|corrente|polka|tarant|l(ä|a)ndler|(é|e)cossaise|galop|\bmarch|\bmarche\b|\brags?\b|ragtime|cakewalk|two-step|bolero|sicilian|passepied|rigaudon|hornpipe|\bjig\b|musette|tango|habanera|cz(á|a)rd(á|a)s|\bdance|\bdanse|\btanz|loure|forlane|anglaise|contredanse|quadrille|schottische|seguidilla|\bjota\b"),
        }.Select(k => (k.Item1, new Regex(k.Item2, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))),
    ];

    /// <summary>The composer's period, or Other.</summary>
    public static string Period(string? composer) =>
        string.IsNullOrWhiteSpace(composer) ? "Other" : Composers.FirstOrDefault(c => c.Who.IsMatch(composer)).Period ?? "Other";

    /// <summary>The piece's kind, from its title and its work.</summary>
    public static string Kind(string title, string? work) =>
        KindWords.FirstOrDefault(k => k.What.IsMatch(title) || work is not null && k.What.IsMatch(work)).Kind ?? "Character pieces";

    /// <summary>
    /// The name a composer is listed by: the surname ("Ludwig van Beethoven" is Beethoven, "Johann Strauss II" Strauss), and for
    /// "Bach and others" the first name given.
    /// </summary>
    public static string Surname(string? composer)
    {
        if (string.IsNullOrWhiteSpace(composer)) return "";

        var name = Regex.Replace(composer, @"\s+(and|&|und)\s+.*$|\s*\(.*\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Regex.IsMatch(w, @"^(I{1,3}|IV|Jr\.?|Sr\.?)$")).ToList();
        return words.Count == 0 ? name : words[^1];
    }
}
