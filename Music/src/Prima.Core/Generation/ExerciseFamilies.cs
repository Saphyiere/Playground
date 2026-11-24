using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>One thing a variation can be set to: its key, as it's kept, and its name on the exercise's page.</summary>
public sealed record VariationOption(string Key, string Name);

/// <summary>
/// One way a family can be varied: a choice with a name and what it can be set to. The usual one is what the family plays until
/// another is chosen: the first, unless <see cref="Usual"/> names another.
/// </summary>
public sealed record Variation(string Key, string Name, IReadOnlyList<VariationOption> Options)
{
    public string? Usual { get; init; }

    /// <summary>Only for both hands, like how they move against each other: with one hand it isn't offered.</summary>
    public bool TogetherOnly { get; init; }

    public string UsualKey => Usual ?? Options[0].Key;
}

/// <summary>What's chosen of a family's variations: an option's key for each variation's key. Kept as "interval=sixth;quick=2".</summary>
public sealed record FamilyChoice(IReadOnlyDictionary<string, string> Chosen)
{
    public string this[string variation] => Chosen[variation];

    public FamilyChoice With(string variation, string option) => new(new Dictionary<string, string>(Chosen) { [variation] = option });

    public override string ToString() => string.Join(';', Chosen.Select(c => $"{c.Key}={c.Value}"));
}

/// <summary>
/// A family of exercises: a main exercise and the side ones a few choices make of it. Its variations, in the order its page
/// offers them, and its music for a key, a choice of hands and what's chosen of them.
/// </summary>
public sealed record ExerciseFamily(IReadOnlyList<Variation> Variations, Func<Key, HandUse, FamilyChoice, Score> Music)
{
    /// <summary>
    /// The keys for what's chosen, where they depend on it: the thumb passing under keeps to the keys where it lands on white
    /// ones, and where it lands depends on how many fingers it passes under. Null: the exercise's own keys, whatever is chosen.
    /// </summary>
    public Func<FamilyChoice, IReadOnlyList<Key>>? Keys { get; init; }

    /// <summary>The main exercise: every variation at its usual.</summary>
    public FamilyChoice Usual => new(Variations.ToDictionary(v => v.Key, v => v.UsualKey));

    /// <summary>The keys the family's exercise is played in with this chosen, easiest first.</summary>
    public IReadOnlyList<Key> KeysOf(Exercise exercise, FamilyChoice variant) => Keys?.Invoke(variant) ?? Exercises.KeysOf(exercise);

    /// <summary>A kept choice, as far as it still names this family's variations and their options; the usual for the rest.</summary>
    public FamilyChoice Read(string? kept)
    {
        var said = (kept ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        return new(Variations.ToDictionary(v => v.Key, v => said.TryGetValue(v.Key, out var option) && v.Options.Any(o => o.Key == option) ? option : v.UsualKey));
    }

    /// <summary>The variations offered for a choice of hands.</summary>
    public IEnumerable<Variation> For(HandUse hands) => Variations.Where(v => !v.TogetherOnly || hands == HandUse.Together);

    /// <summary>What's chosen, in words: its options' names for the variations offered with these hands, "Sixths · Two".</summary>
    public string Describe(FamilyChoice variant, HandUse hands) => string.Join(" · ", For(hands).Select(v => v.Options.First(o => o.Key == variant[v.Key]).Name));

    /// <summary>Every way it can be played: each combination of its variations' options.</summary>
    public IEnumerable<FamilyChoice> Every()
    {
        IEnumerable<FamilyChoice> all = [new FamilyChoice(new Dictionary<string, string>())];
        foreach (var variation in Variations) all = [.. all.SelectMany(v => variation.Options.Select(o => v.With(variation.Key, o.Key)))];
        return all;
    }
}

/// <summary>The lesson an exercise's idea comes from: who gave it, what it's called, and where to watch it, at the exercise's own place in it.</summary>
public sealed record ExerciseLesson(string By, string Title, string Url);

/// <summary>
/// The exercises that are families, their variations and where their ideas come from. The first set is from one lesson, a
/// concert pianist's ten daily exercises (docs/exercises/the_ten_daily_exercises.md): the ideas are his, as anyone's who teaches
/// them; the names, the words and the notes are Prima's own, written by <see cref="FamilyMusic"/>.
/// </summary>
public static class ExerciseFamilies
{
    private static readonly HandUse[] Either = [HandUse.Right, HandUse.Left];
    private static readonly HandUse[] Any = [HandUse.Right, HandUse.Left, HandUse.Together];
    private static readonly HandUse[] Both = [HandUse.Together];

    private static readonly Variation Fingers = new("fingers", "Fingers", [new("1", "1 and 2"), new("2", "2 and 3"), new("3", "3 and 4"), new("4", "4 and 5")]) { Usual = "2" };

    private static readonly (Exercise Exercise, ExerciseFamily Family, ExerciseLesson Lesson)[] Families =
    [
        Family("scale-parting", "scales", "Scale that parts in the middle", ExerciseLevel.Beginner,
            "A scale in both hands that splits halfway: up together, apart and back, on up, and the same coming home.",
            ["The hands climb together to the middle. There they part, the left going back down as the right goes on up, and come in again.", "On up together, back to the middle, apart and in once more, and home.", "Each hand keeps the scale's own fingering; a minor key's is the harmonic minor.", "Change one thing each time you play it: detached, soft, loud, growing and fading, or the next key."],
            "Both hands turning at the same instant, and the notes as even going apart as going together.",
            ExerciseKeys.All, Both, 72, 28,
            [
                new("size", "Size", [new("1", "One octave each way"), new("2", "Two octaves each way")]),
                new("touch", "Touch", [new("joined", "Joined"), new("detached", "Detached")]),
                new("loud", "Loudness", [new("plain", "As it comes"), new("soft", "Soft"), new("loud", "Loud"), new("swell", "Growing and fading")]),
            ],
            (key, hands, v) => FamilyMusic.PartingScale(key, hands, int.Parse(v["size"]), v["touch"] == "detached" ? Articulation.Staccato : Articulation.None,
                v["loud"] switch { "soft" => Shading.Soft, "loud" => Shading.Loud, "swell" => Shading.Swell, _ => Shading.Plain })),

        Family("arpeggio-groups", "broken", "Arpeggio counted in fours", ExerciseLevel.Beginner,
            "An arpeggio with a lean on every fourth note, so the stress keeps landing on a different finger.",
            ["A chord has three notes, so counted in threes the lean always falls on the same one.", "Count it in fours instead: a small lean on each “one”, the notes between them light.", "Over four octaves the lean goes round every note of the chord.", "Counted in threes is here too, to hear the difference."],
            "The marked notes a little stronger than their neighbours, and no bump where the thumb goes under. Prima says when a marked note came out no stronger than the rest.",
            ExerciseKeys.All, Any, 54, 111,
            [
                new("count", "Count", [new("4", "In fours"), new("3", "In threes")]),
                new("octaves", "Octaves", [new("2", "Two octaves"), new("4", "Four octaves")]),
            ],
            (key, hands, v) => FamilyMusic.GroupedArpeggio(key, hands, int.Parse(v["count"]), int.Parse(v["octaves"]))),

        // Its id is from when it was one exercise, under three fingers, so what's been played of it is still its own.
        Family("thumb-three", "thumb", "Thumb under", ExerciseLevel.Beginner,
            "Up a few fingers, the thumb passes under to the next note, and back: under two, three, four or five fingers.",
            ["The thumb starts moving as soon as it has played, and stays loose.", "Two ways to do it, on different days: let the arm carry the hand along, as in a scale; or keep the hand still, so the thumb does the travelling.", "Under two is the short pass. Under five is a long way for the thumb: slowly, and not if it pulls.", "Left hand: the same, going down from its thumb."],
            "No bump on the thumb's note, round and round as evenly as a scale.",
            ExerciseKeys.WhiteThumb, Either, 60, 173,
            [new("under", "Under", [new("2", "2 fingers"), new("3", "3 fingers"), new("4", "4 fingers"), new("5", "5 fingers")]) { Usual = "3" }],
            (key, hands, v) => FamilyMusic.ThumbUnder(key, hands, int.Parse(v["under"])),
            // Under three or four fingers the thumb lands on the scale's fourth or fifth note (the right hand's going up, the
            // left's coming down); under two or five, on its third or sixth.
            keysFor: v => v["under"] is "3" or "4" ? Exercises.WhiteAt(0, 3, 4) : Exercises.WhiteAt(0, 2, 5)),

        Family("thirds", "double", "Thirds, three up and back", ExerciseLevel.Beginner,
            "Two notes at once, a third apart and joined: three neighbouring thirds up and back, round and round.",
            ["Fingers 1 and 3, then 2 and 4, then 3 and 5, and 2 and 4 again; the left hand's are the same ones.", "Join each third to the next: one pair of fingers stays down until the next pair has played.", "In one place first. Then up the scale: a bar on each step, the hand moving up in the breath at the bar line.", "With both hands, mirror is the easier way: the same fingers play together."],
            "The two keys of each third going down at the same instant, and no gap from one third to the next.",
            ExerciseKeys.All, Any, 50, 219,
            [
                new("where", "Where", [new("place", "In one place"), new("climb", "Up the scale")]),
                new("motion", "Together", [new("same", "The same way"), new("mirror", "In mirror")]) { TogetherOnly = true },
            ],
            (key, hands, v) => FamilyMusic.Thirds(key, hands, mirror: v["motion"] == "mirror", climb: v["where"] == "climb")),

        Family("bounce", "double", "Bounced double notes", ExerciseLevel.Beginner,
            "Two notes at once, repeated from a loose wrist: a few quick ones that bounce into a longer one, a step higher each bar.",
            ["One small throw of the wrist makes the quick notes: the hand rebounds off the keys like a ball. The fingers only keep their shape.", "Land on the long note and stay there; move up in the rest.", "Start with one quick note before the long one, then two, three, four.", "Sixths are the usual. A smaller interval is easier on the hand; octaves only if an octave is an easy reach for you, and then quietly."],
            "The quick notes light and even, both keys sounding every time. A forearm that tightens means too fast or too loud: stop and shake it out.",
            ExerciseKeys.Majors, Any, 60, 316,
            [
                new("interval", "Interval", [new("third", "Thirds"), new("fourth", "Fourths"), new("fifth", "Fifths"), new("sixth", "Sixths"), new("octave", "Octaves")]) { Usual = "sixth" },
                new("quick", "Quick notes", [new("1", "One"), new("2", "Two"), new("3", "Three"), new("4", "Four")]) { Usual = "2" },
            ],
            (key, hands, v) => FamilyMusic.Bounce(key, hands, Enum.Parse<DoubleNote>(v["interval"], ignoreCase: true), int.Parse(v["quick"])),
            // Its last bounce is on the step it started from: round a loop that's the next time round's first, so it goes with the close.
            close: 2),

        Family("octave-scale", "double", "Scale in octaves", ExerciseLevel.Intermediate,
            "A major scale with each hand playing octaves, up and back, from a loose wrist.",
            ["The thumb and little finger keep the octave's shape; the wrist does the playing.", "Short notes, close to the keys, the hand sliding sideways from one to the next.", "Only if an octave is an easy reach for you. Quietly, a few times, and stop at the first ache: wide stretches are where hands get overworked."],
            "Both keys of every octave sounding together. Speed comes last.",
            ExerciseKeys.Majors, Any, 60, 365,
            [new("octaves", "Octaves", [new("1", "One octave"), new("2", "Two octaves")])],
            (key, hands, v) => FamilyMusic.OctaveScale(key, hands, int.Parse(v["octaves"])),
            // Its last octave is held only to the end of its bar: there's no bar that's only the close, and all of it goes round.
            close: 0),

        Family("trill-bursts", "trills", "Trill bursts up the scale", ExerciseLevel.Beginner,
            "Four quick notes between two fingers and a stop, then the same a step higher: up an octave and back.",
            ["Play the four notes as one gesture, then rest on the long note and let the hand go soft.", "Going up, the lower finger starts each burst; coming down, the upper one.", "Choose the pair of fingers. With both hands the left takes the opposite pair, 5 and 4 against the right's 1 and 2, and the two hands' notes sound exactly together.", "Faster only once the four notes are even."],
            "Four even notes, and a stop that's really still.",
            ExerciseKeys.All, Any, 54, 389,
            [Fingers],
            (key, hands, v) => FamilyMusic.TrillBursts(key, hands, int.Parse(v["fingers"]))),

        Family("hold-turn", "arm", "Hold one, turn to the next", ExerciseLevel.Beginner,
            "Hold a key down and play the one beside it three times by turning the forearm; then hold that one, and so on up the hand and back.",
            ["The held finger stays down with only the weight it needs.", "The playing finger doesn't lift and strike: the forearm turns, as on a doorknob, and carries it into the key.", "From the thumb up to the little finger, then back down.", "A finger further apart makes the turn wider."],
            "The held key staying down for its whole bar, which Prima checks, and three repeated notes that sound alike.",
            ExerciseKeys.All, Any, 60, 465,
            [new("reach", "Reach", [new("1", "The next finger"), new("2", "A finger further")])],
            (key, hands, v) => FamilyMusic.HoldAndTurn(key, hands, int.Parse(v["reach"]))),

        Family("hold-one", "five", "One finger holds, four play", ExerciseLevel.Beginner,
            "One finger keeps its key down while the other four play their notes up and back.",
            ["Five notes under five fingers; the hand doesn't move.", "Hold with only the weight the key needs. No pressing.", "Each finger takes its turn to hold, or choose the one to work on: the third and fourth are the hard ones.", "Short and light: a few bars, then shake the hand out. Stop if the forearm tightens."],
            "The held key staying down, which Prima checks, and the moving notes even all the same. It's the feel of one finger staying while the others move that this gives, which a great deal of music asks for.",
            ExerciseKeys.All, Either, 50, 533,
            [new("holding", "Holding", [new("0", "Each in turn"), new("1", "Thumb"), new("2", "2"), new("3", "3"), new("4", "4"), new("5", "5")])],
            (key, hands, v) => FamilyMusic.HoldOne(key, hands, int.Parse(v["holding"]))),

        Family("slur-walk", "touch", "Two-note slurs up the scale", ExerciseLevel.Beginner,
            "Pairs of joined notes with one pair of fingers, each pair a step higher: up an octave and back.",
            ["Down into the first note, the wrist dropping; up and off on the second, the wrist rising.", "The first note a little fuller, the second lighter and shorter.", "The same two fingers all the way: choose which. Coming down, the upper finger starts.", "With both hands, the left takes the opposite pair."],
            "The second note softer and shorter than the first, and every pair alike. Prima marks a second note held long, and a gap inside a pair.",
            ExerciseKeys.All, Any, 66, 586,
            [Fingers],
            (key, hands, v) => FamilyMusic.SlurWalk(key, hands, int.Parse(v["fingers"]))),
    ];

    /// <summary>The exercises that are families, in the catalogue's order.</summary>
    public static IReadOnlyList<Exercise> All { get; } = [.. Families.Select(f => f.Exercise)];

    /// <summary>An exercise's family, if it is one: its variations and its music for each.</summary>
    public static ExerciseFamily? Of(string exerciseId) => Families.FirstOrDefault(f => f.Exercise.Id == exerciseId).Family;

    /// <summary>The lesson an exercise's idea is from, if it's from one.</summary>
    public static ExerciseLesson? LessonOf(string exerciseId) => Families.FirstOrDefault(f => f.Exercise.Id == exerciseId).Lesson;

    /// <summary>
    /// What an exercise that has become part of a family is now: the family's id, and the choice that plays it as it was. Its
    /// own id still opens it (a routine's item, a pin), and what's played under it is still kept under it.
    /// </summary>
    public static (string Family, string Choice)? Formerly(string exerciseId) => exerciseId == "thumb-four" ? ("thumb-three", "under=4") : null;

    // A family from the pianist's lesson: its exercise, whose own music is the family's usual form, its variations, and the
    // lesson at the place the idea is shown, in seconds. Its close is how many of its last bars only end it, whatever is chosen
    // (Exercise.Close); keysFor gives what's chosen keys of its own.
    private static (Exercise, ExerciseFamily, ExerciseLesson) Family(string id, string section, string name, ExerciseLevel level, string does, string[] how, string notice, ExerciseKeys keys, HandUse[] hands, double tempo, int at, Variation[] variations, Func<Key, HandUse, FamilyChoice, Score> music,
        int close = 1, Func<FamilyChoice, IReadOnlyList<Key>>? keysFor = null)
    {
        var family = new ExerciseFamily(variations, music) { Keys = keysFor };
        var usual = family.Usual;
        var exercise = new Exercise(id, section, name, level, ExerciseKind.Played, does, how, notice) { Music = (key, with) => music(key, with, usual), Keys = keys, Hands = hands, Tempo = tempo, Close = close };
        return (exercise, family, new ExerciseLesson("Peterson Piano Academy", "I'm a concert pianist. I play these 10 exercises EVERY day.", $"https://www.youtube.com/watch?v=8QI_InPpq0s&t={at}s"));
    }
}
