using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>How an exercise is done.</summary>
public enum ExerciseKind
{
    /// <summary>With nothing for Prima to follow (away from the keys, or the pedal alone): read it, do it, and say it's done.</summary>
    Away,

    /// <summary>Played at the piano: its music is shown and followed.</summary>
    Played,

    /// <summary>A way of practising anything: to read.</summary>
    Way,

    /// <summary>One of the drills Prima already has: its row opens it.</summary>
    Drill,
}

/// <summary>Who an exercise is for: the first weeks at the piano, a beginner past them, or an intermediate player.</summary>
public enum ExerciseLevel
{
    Start,
    Beginner,
    Intermediate,
}

/// <summary>The keys an exercise can be played in: C only, every major key, or every major and minor key.</summary>
public enum ExerciseKeys
{
    One,
    Majors,
    All,
}

/// <summary>A section of the exercises: its key, its name, and a line on what's in it.</summary>
public sealed record ExerciseSection(string Key, string Name, string About);

/// <summary>
/// An exercise: where it belongs, what you do in a line, how in a few steps, and what to notice. One that's played has its
/// music, written for a key and a choice of hands, and says which keys and hands it offers and the tempo it starts at.
/// </summary>
public sealed record Exercise(string Id, string Section, string Name, ExerciseLevel Level, ExerciseKind Kind, string Does, IReadOnlyList<string> How, string Notice)
{
    /// <summary>The drill it opens ("drills/scales"), for one Prima already has.</summary>
    public string? Route { get; init; }

    public Func<Key, HandUse, Score>? Music { get; init; }

    public ExerciseKeys Keys { get; init; }

    public IReadOnlyList<HandUse> Hands { get; init; } = [];

    public double Tempo { get; init; } = 60;

    /// <summary>Whether it's one to play at the piano, with its own music or a drill's, and not one to read and do.</summary>
    public bool ToPlay => Kind is ExerciseKind.Played or ExerciseKind.Drill;
}

/// <summary>
/// The exercises (docs/exercises.md says where they come from): the owner's list of technique work for a beginner and the
/// research on what's worth building, kept to what suits the first years and to patterns Prima can write itself. The words are
/// Prima's own; the patterns are the common stock of piano teaching.
/// </summary>
public static class Exercises
{
    private static readonly HandUse[] Either = [HandUse.Right, HandUse.Left];
    private static readonly HandUse[] Any = [HandUse.Right, HandUse.Left, HandUse.Together];
    private static readonly HandUse[] Both = [HandUse.Together];
    private static readonly HandUse[] LeftOnly = [HandUse.Left];

    public static IReadOnlyList<ExerciseSection> Sections { get; } =
    [
        new("setup", "Sitting and hand shape", "Before any notes. Worth a look every week, and again whenever something feels tight."),
        new("warm", "Warm-ups away from the keys", "A minute or two to get loose. Gentle, and never into pain."),
        new("arm", "Arm weight and rotation", "The arm plays the note and the forearm's turn helps each finger. Prima hears the notes; the movement is yours to feel."),
        new("five", "Five-finger patterns", "Five notes under five fingers, in every key: what scales and chords grow from."),
        new("scales", "Scales", "Plain scales are a drill of their own; these are the forms that go beside them."),
        new("broken", "Broken chords and left-hand patterns", "A chord a note at a time, and the patterns a left hand accompanies with."),
        new("chords", "Chords and cadences", "Three notes that land together, and the chords of a key played in turn."),
        new("jumps", "Jumps", "Get there early, land, and only then play. Slow first: the time to travel shrinks line by line."),
        new("thumb", "Thumb under", "The thumb passing under the hand, as it does in every scale."),
        new("trills", "Trills", "Two notes back and forth: even before quick."),
        new("hands", "Hands doing different things", "One hand holds, or moves at another speed, or with another touch."),
        new("touch", "Touch, loud and soft", "Joined and detached, and how fast the key goes down. Prima judges these from each key's press and release."),
        new("pedal", "Pedal", "The right pedal, changed cleanly."),
        new("ways", "Ways to practise anything", "Not exercises: how to work on any of them, or on a piece."),
    ];

    public static IReadOnlyList<Exercise> All { get; } =
    [
        // ——— Sitting and hand shape ———
        Away("bench", "setup", "Bench height and distance", "Set the bench so your forearms are level with the keys and your elbows hang a little in front of you.",
            ["Sit on the front half of the bench, opposite the middle of the keyboard.", "Rest your hands on the keys: forearms about level, elbows just ahead of your ribs and free to swing.", "Lean from the hips to touch the lowest key and the highest. If you can't, move the bench back."],
            "A wrist that sags, or shoulders that rise, means the bench is too low or too close."),
        Away("balance", "setup", "Sitting balanced", "Find the upright seat that takes no effort to hold.",
            ["Feet flat on the floor, the right one near the pedal.", "Rock gently forward and back on your sitting bones, and stop where you feel balanced.", "Let your head float up and your shoulders widen."],
            "Tall without being stiff. Come back to it whenever a passage gets hard."),
        Away("hand-shape", "setup", "Your hand's own shape", "Bring the curve your hand has when it hangs by your side onto the keys.",
            ["Let an arm hang and look at the hand: gently curved, a low dome at the knuckles.", "Lift it onto the keys without changing it. The thumb rests on its outer corner.", "Play one note with each finger, slowly: the joint nearest the nail stays firm and doesn't cave in."],
            "Neither flat fingers nor a claw. The little finger and the thumb are the ones that collapse or stiffen first."),
        Away("shoulders", "setup", "Shoulders down", "Tense them on purpose, then let them go, so you know the difference.",
            ["Lift both shoulders towards your ears for two counts.", "Let them drop for four.", "Three times; then play a few notes and feel that they stay down."],
            "They creep up in hard passages. That's the moment to do this again."),
        Away("tension-check", "setup", "Tension check", "A ten-second check between runs.",
            ["Breathe out.", "Go through them one at a time: jaw, shoulders, elbows, wrists, thumbs.", "Let go of anything that's holding on."],
            "Holding your breath is the usual first sign."),
        Away("side-video", "setup", "Film yourself from the side", "A weekly look at what you can't feel from the inside.",
            ["Prop the phone level with the keys, side on to you.", "Play a scale, then a few bars of a piece.", "Watch the wrist, the shoulders, and how high the fingers lift."],
            "A wrist in line with the forearm and free to move, and fingers that stay close to the keys."),

        // ——— Warm-ups away from the keys ———
        Away("swings", "warm", "Arm swings and shoulder rolls", "Loosen the big joints first.",
            ["Standing, swing both arms loosely forward and back, ten times.", "Roll the shoulders backward five times, then forward five."],
            "Loose and unhurried."),
        Away("wrists", "warm", "Wrist circles and doorknob turns", "Loosen the wrists, and rehearse the forearm's turn you'll use at the keys.",
            ["Elbows by your sides, circle both wrists slowly, five times each way.", "Then turn the forearms as if turning a doorknob, palm up to palm down, ten times."],
            "The doorknob turn is rotation: the same movement, later, behind each note."),
        Away("shake", "warm", "Shake out", "Ten seconds to let everything go.",
            ["Let both arms hang and shake the hands loosely for ten seconds.", "Use it between exercises too."],
            "Do it the moment anything feels tight."),
        Away("fan", "warm", "Fan and close", "Open the hand gently and let it close.",
            ["Spread the fingers apart, without pushing.", "Let them fall closed again. Five times, each hand."],
            "This wakes the hand up. It isn't a stretch: never push to reach further."),
        Away("taps", "warm", "Finger taps on a table", "Each finger moves a little by itself, the others staying at ease.",
            ["Rest a hand on a table in its own curve.", "Lift and drop one finger about a centimetre, five times, the others relaxed.", "Each finger in turn, then the other hand."],
            "The other fingers rest; they aren't pinned down. Stop if the forearm tightens."),
        Away("stretches", "warm", "Gentle forearm stretches", "A light stretch along the forearm, for ten to fifteen seconds.",
            ["Arm out in front, palm up; with the other hand ease the fingers back until you feel a light stretch.", "Then palm down, easing the hand downward.", "Both arms. After playing suits it as well as before."],
            "Light, and never into pain."),
        Away("arm-drop", "warm", "Arm drop onto your knee", "Feel the weight of a loose arm.",
            ["Lift a forearm a hand's height above your knee.", "Let it fall with all its weight, wrist loose.", "Five times, each arm."],
            "This weight, not pressing, is what plays a note."),

        // ——— Arm weight and rotation ———
        Played("drop", "arm", "Drop onto one note", ExerciseLevel.Start, "Let the arm's weight play a note, then let go of all but the little that holds the key.",
            ["From just above the key, let the arm fall onto the finger shown, wrist loose.", "Once the note sounds, ease off until you're only just holding the key down.", "A whole bar on each note. Breathe out as you land."],
            "The sound comes from how fast the key goes down, not from pressing once it's there. Prima hears the note; the letting go is yours to feel.",
            ExerciseMusic.Drop, ExerciseKeys.One, Either),
        Played("doorknob", "arm", "Doorknob: thumb to little finger", ExerciseLevel.Start, "Rock between the thumb and the little finger by turning the forearm.",
            ["Keep the fingers quiet and let the forearm turn, as it does on a doorknob.", "Big, slow turns in the quarter notes; smaller ones in the eighths.", "Stay close to the keys."],
            "The two notes equally loud. In time the turn shrinks until it barely shows.",
            (key, hands) => ExerciseMusic.Rock(key, hands, (1, 5)), ExerciseKeys.Majors, Either),
        Played("small-turns", "arm", "Smaller turns", ExerciseLevel.Beginner, "The same turn between fingers a third apart: 1 and 3, 2 and 4, 3 and 5.",
            ["A bar on each pair.", "The turn is small now: the forearm still does it, and the fingers ride on it.", "Light and even."],
            "The 3 and 5 pair is the hard one. Let the arm lean towards the little finger instead of the finger reaching.",
            ExerciseMusic.SmallTurns, ExerciseKeys.Majors, Either),
        Played("wrist-circles", "arm", "Wrist circles on a held note", ExerciseLevel.Start, "Hold a key down and circle the wrist, to feel that it's free.",
            ["Play the note and hold it for its two bars.", "While it's held, draw a slow circle with the wrist: one way, then the other.", "Keep the key down with only the weight it needs."],
            "The fingertip stays put while everything behind it moves.",
            ExerciseMusic.Held, ExerciseKeys.One, Either),

        // ——— Five-finger patterns ———
        Played("five", "five", "Five-finger pattern", ExerciseLevel.Start, "Five notes up and back, a finger to a key, in every key.",
            ["Each hand alone first, then together.", "Stay close to the keys and let the arm follow the hand along.", "The key's + and − go through the keys in order, easiest first."],
            "Even gaps and even loudness. In a new key, look at which keys are black before you start.",
            (key, hands) => ExerciseMusic.Five(key, hands), ExerciseKeys.All, Any, 72),
        Played("major-minor", "five", "Major, then minor", ExerciseLevel.Start, "The pattern in major, then with its middle note lowered.",
            ["Play the major pattern up and back.", "Lower the third note by one key and play it again: that one note is the whole difference."],
            "Listen for the change of colour. Four of the five keys stay the same.",
            ExerciseMusic.MajorMinor, ExerciseKeys.Majors, Any, 72),
        Played("mirror", "five", "Hands in mirror", ExerciseLevel.Beginner, "Both hands move away from each other and back, the same fingers playing together.",
            ["Thumbs start on the same note, an octave apart.", "Out for five notes, back for five."],
            "Mirror motion is the easy way to play hands together: matching fingers move at once.",
            ExerciseMusic.Mirror, ExerciseKeys.All, Both, 66),
        Played("long-short", "five", "Long, short", ExerciseLevel.Beginner, "The pattern in a long–short rhythm.",
            ["Lean on the long note and flick the short one into the next.", "Keep the long notes on the click."],
            "Each finger takes a turn at moving quickly, which plain playing never asks of it.",
            ExerciseMusic.LongShort, ExerciseKeys.All, Any),
        Played("five-chord", "five", "The pattern, then its chord", ExerciseLevel.Start, "Five notes up and back, then three of them as the chord: one at a time, then together.",
            ["The chord is the first, third and fifth notes of the pattern.", "Play it broken, then let all three keys go down as one."],
            "Three notes landing at the same instant.",
            ExerciseMusic.FiveAndChord, ExerciseKeys.All, Any, 72),
        Played("hanon", "five", "Hanon's first pattern, quietly", ExerciseLevel.Intermediate, "The best-known finger pattern, used the modern way: quiet, slow, with the arm helping.",
            ["Each hand alone. Ignore the old instruction to lift the fingers high: stay close to the keys.", "Give each run one thing to watch: a loose thumb, or the wrist gliding sideways so the forearm lines up behind each finger.", "Five minutes at most, and stop at the first ache."],
            "No study shows this pattern does more for playing than scales do. Its use is as plain notes to practise a movement on.",
            ExerciseMusic.Hanon, ExerciseKeys.One, Any),

        // ——— Scales ———
        Drill("scales", "scales", "Scales", "Every major and minor scale with its fingering, one to four octaves, with evenness measured after each run.", "drills/scales"),
        Played("contrary", "scales", "Scale in contrary motion", ExerciseLevel.Beginner, "A major scale with the hands moving apart and back, an octave each.",
            ["Thumbs start on the tonic, an octave apart.", "Each hand keeps the scale's own fingering, so the thumbs don't always pass together.", "Slowly: both hands reach their far notes at the same moment."],
            "In C the same fingers play together all the way. In other keys they don't, which is the test.",
            ExerciseMusic.ContraryScale, ExerciseKeys.Majors, Both),
        Played("chromatic", "scales", "Chromatic scale", ExerciseLevel.Beginner, "Every key in turn, black and white, up an octave and back.",
            ["The third finger takes every black key, the thumb most white ones.", "Where two white keys sit side by side, the second finger takes one of them.", "The hand moves in for the black keys and out for the white: let it."],
            "Even gaps, despite the thumb's short reach.",
            ExerciseMusic.Chromatic, ExerciseKeys.One, Any),

        // ——— Broken chords and left-hand patterns ———
        Drill("arpeggios", "broken", "Arpeggios", "A chord's notes one at a time over one to four octaves, with a close look at the thumb's crossings.", "drills/arpeggios"),
        Played("passed", "broken", "A chord passed between the hands", ExerciseLevel.Start, "The key's chord a note at a time: the left hand hands it up to the right, and the right hands it back.",
            ["Left hand up, right hand up; right hand down, left hand down.", "Make the join between the hands sound like one hand playing."],
            "No bump and no gap where the hands change over.",
            ExerciseMusic.Passed, ExerciseKeys.All, Both, 72),
        Played("broken-inversions", "broken", "Broken chords through the inversions", ExerciseLevel.Beginner, "The key's chord broken, starting from each of its notes in turn, up to the octave and back.",
            ["Three notes to a bar, the hand moving up one chord note each bar.", "The fingering changes with the shape: follow the numbers."],
            "The middle finger changes between 3 and 2. That is the thing to learn here.",
            ExerciseMusic.BrokenInversions, ExerciseKeys.All, Any, 72),
        Played("alberti", "broken", "Alberti bass", ExerciseLevel.Beginner, "The left hand's classic accompaniment: bottom, top, middle, top.",
            ["The hand stays in one place over the three chords.", "Let the forearm rock, as in the doorknob exercise: the thumb's notes are the lightest.", "Then play a tune you know over it."],
            "The bottom note a little fuller, the thumb's repeated note soft.",
            ExerciseMusic.Alberti, ExerciseKeys.All, LeftOnly),

        // ——— Chords and cadences ———
        Played("triad-walk", "chords", "One shape up the scale", ExerciseLevel.Start, "A three-note chord on each note of the scale, the hand keeping its shape.",
            ["Fingers 1, 3 and 5 every time (5, 3 and 1 in the left hand).", "Play, let go, move in the rest, and be over the next chord before its beat.", "In C the shape never changes. In other keys, see which chords pick up a black key."],
            "All three notes at once, and on the beat.",
            ExerciseMusic.TriadWalk, ExerciseKeys.Majors, Any),
        Played("inversions", "chords", "Up through the inversions", ExerciseLevel.Beginner, "The key's chord with each of its notes at the bottom in turn, up to the octave and back.",
            ["Root position, first inversion, second inversion, then root position an octave higher.", "The fingering changes with the shape: follow the numbers.", "Move in the rests."],
            "The same three note names every time, in a new order.",
            ExerciseMusic.Inversions, ExerciseKeys.All, Any),
        Played("chord-kinds", "chords", "Four kinds of chord on one note", ExerciseLevel.Beginner, "Major, minor, diminished and augmented from the same bottom note.",
            ["Major to minor: the middle note comes down a key.", "Minor to diminished: the top note comes down too.", "Augmented: back to major, with the top note up a key."],
            "One finger moves at a time. Hear how each changes the colour.",
            ExerciseMusic.ChordKinds, ExerciseKeys.Majors, Any),
        Played("cadence", "chords", "I–IV–I–V–I, hands close", ExerciseLevel.Beginner, "The three main chords of a key, voiced so the hand barely moves.",
            ["Notes the chords share stay under the same finger.", "With both hands, the left plays each chord's root.", "One key a day."],
            "Almost no movement in the right hand. This is how accompaniments stay smooth.",
            ExerciseMusic.Cadence, ExerciseKeys.All, Any),
        Played("four-chords", "chords", "Four chords round and round", ExerciseLevel.Beginner, "I, V, vi and IV, the loop under a great many songs, with the chords kept close.",
            ["Twice round, then home.", "With both hands, the left plays each chord's root.", "Then try the right hand's chords broken, a note at a time."],
            "Smooth changes, on the beat.",
            ExerciseMusic.FourChords, ExerciseKeys.Majors, Any),

        // ——— Jumps ———
        Played("octave-jump", "jumps", "Octave jump: get there, then play", ExerciseLevel.Start, "One finger from a note to the same note an octave away, with less time to travel on each line.",
            ["Play, let go, and move at once: be resting on the next key well before its beat.", "Look at the key you're going to, not the one you're leaving.", "If you land on the wrong key, hold it and see where you are. Don't slide to the right one."],
            "Arriving early and waiting. A fast move and a calm landing, not a throw.",
            ExerciseMusic.OctaveJump, ExerciseKeys.Majors, Either),
        Played("jump-ladder", "jumps", "Further each time", ExerciseLevel.Beginner, "From a home note to a third, a fifth, an octave and a tenth away, and home between each.",
            ["The same finger throughout.", "Move in the rests, and be over the key early.", "Then try it glancing only at the far key."],
            "Which distance starts to go wrong, and whether you fall short or go past.",
            ExerciseMusic.JumpLadder, ExerciseKeys.Majors, Either),
        Played("chord-jump", "jumps", "Chord jump, an octave away", ExerciseLevel.Beginner, "A chord, and the same chord an octave away, with less time to travel on each line.",
            ["The hand keeps its shape in the air.", "Land silently on all three keys first, then play them.", "Light: a jump played loudly is a less accurate one."],
            "All three notes landing together, on the beat.",
            ExerciseMusic.ChordJump, ExerciseKeys.All, Either),
        Played("cadence-far", "jumps", "Cadence chords, jumping", ExerciseLevel.Intermediate, "The three main chords of a key, each from its own bottom note, so the hand has to travel.",
            ["The same chords as the close cadence, further apart.", "A short chord, then move in the rest.", "Compare it with the close version: which lands more cleanly?"],
            "The landing after the longest move.",
            ExerciseMusic.CadenceFar, ExerciseKeys.All, Any),
        Played("waltz", "jumps", "Bass note, then chord", ExerciseLevel.Beginner, "A waltz's left hand: a low note on the first beat, its chord on the next two.",
            ["The little finger takes the bass note, then the hand moves up to the chord.", "Leave the bass note early enough to be over the chord in time.", "Chords alone first if the jump keeps missing."],
            "The bass note fuller, the chords lighter.",
            ExerciseMusic.Waltz, ExerciseKeys.All, LeftOnly),

        // ——— Thumb under ———
        Played("thumb-three", "thumb", "Thumb under three fingers", ExerciseLevel.Beginner, "Up three fingers, the thumb passes under to the fourth note, and back.",
            ["The thumb starts moving as soon as it has played, and stays loose.", "The wrist and elbow glide along so the hand arrives with it.", "Left hand: the same, going down from its thumb."],
            "No bump on the thumb's note and no dip of the elbow.",
            (key, hands) => ExerciseMusic.ThumbUnder(key, hands, 3), ExerciseKeys.One, Either),
        Played("thumb-four", "thumb", "Thumb under four fingers", ExerciseLevel.Beginner, "The longer pass: under four fingers to the fifth note.",
            ["As before, with further to go.", "Let the arm carry the hand along; the thumb shouldn't have to reach."],
            "The same evenness as the shorter pass.",
            (key, hands) => ExerciseMusic.ThumbUnder(key, hands, 4), ExerciseKeys.One, Either),

        // ——— Trills ———
        Drill("independence", "trills", "Finger independence", "Five-finger patterns, then trills on fingers 3–4 and 4–5, with evenness measured after each run.", "drills/independence"),
        Played("trill-2-3", "trills", "Slow trill, fingers 2 and 3", ExerciseLevel.Start, "Two neighbouring notes back and forth: a bar of quarter notes, then eighths, then sixteenths.",
            ["A small turn of the forearm does most of it; the fingers stay close to the keys.", "Keep the sixteenths as even as the quarter notes.", "Slow the click down if they aren't."],
            "Even, before quick.",
            (key, hands) => ExerciseMusic.Rock(key, hands, (2, 3), quick: true), ExerciseKeys.Majors, Either),
        Played("trill-1-2", "trills", "Slow trill, thumb and second finger", ExerciseLevel.Beginner, "The same, between the thumb and the finger beside it.",
            ["A bar of quarter notes, then eighths, then sixteenths.", "Keep the sixteenths as even as the quarter notes."],
            "The thumb plays from its base joint, not by the hand rocking.",
            (key, hands) => ExerciseMusic.Rock(key, hands, (1, 2), quick: true), ExerciseKeys.Majors, Either),
        Played("trill-1-3", "trills", "Slow trill, thumb and third finger", ExerciseLevel.Beginner, "The same, a third apart.",
            ["A bar of quarter notes, then eighths, then sixteenths.", "The turn of the forearm is wider here: let it be."],
            "Both notes equally loud.",
            (key, hands) => ExerciseMusic.Rock(key, hands, (1, 3), quick: true), ExerciseKeys.Majors, Either),

        // ——— Hands doing different things ———
        Played("hold-move", "hands", "One hand holds, the other moves", ExerciseLevel.Start, "A long note in one hand under five moving notes in the other, then the other way round.",
            ["The holding hand rests on its key with no more weight than it needs.", "Then the hands swap jobs."],
            "The holding hand staying quiet while the other plays.",
            ExerciseMusic.HoldAndMove, ExerciseKeys.All, Both, 66),
        Played("two-to-one", "hands", "Two notes against one", ExerciseLevel.Beginner, "One hand plays the pattern twice as fast as the other, then they swap.",
            ["Count the slower hand's notes; the quicker hand fits two to each.", "Both land together on every beat."],
            "The slower hand's notes held their full length.",
            ExerciseMusic.TwoToOne, ExerciseKeys.All, Both),
        Played("smooth-detached", "hands", "Smooth against detached", ExerciseLevel.Beginner, "One hand joins its notes while the other plays them short, then they swap.",
            ["Each hand alone first, to feel its touch.", "Together, slowly: the smooth hand must not go short too."],
            "Prima marks a detached note held long, and a gap in the smooth hand.",
            ExerciseMusic.SmoothDetached, ExerciseKeys.All, Both),
        Played("two-three", "hands", "Two against three", ExerciseLevel.Intermediate, "Three even notes in the right hand against two in the left.",
            ["Tap it on your knees first: together, right, left, right.", "The words “nice cup of tea” have the rhythm.", "Then on the keys, slowly."],
            "The hands together on every beat, and the left hand's second note falling between the right hand's second and third.",
            ExerciseMusic.TwoAgainstThree, ExerciseKeys.Majors, Both, 50),

        // ——— Touch, loud and soft ———
        Played("slurs", "touch", "Two-note slurs: down, then up", ExerciseLevel.Start, "Pairs of joined notes: the first with the arm's weight, the second lighter and let go.",
            ["Down on the first note, with a slightly lower wrist.", "Up and off on the second, the wrist rising as you let go.", "A tiny breath between pairs."],
            "The second note softer and shorter than the first.",
            ExerciseMusic.TwoNoteSlurs, ExerciseKeys.All, Either, 66),
        Played("detached", "touch", "Five fingers, detached", ExerciseLevel.Beginner, "The five-finger pattern with every note short.",
            ["Let each key come straight back up.", "Short, not hard: the same loudness as when it's smooth."],
            "Each note about half its length, and all equally short.",
            (key, hands) => ExerciseMusic.Five(key, hands, Articulation.Staccato), ExerciseKeys.All, Any, 66),
        Played("soft-loud", "touch", "One note, soft to loud", ExerciseLevel.Start, "The same key at five strengths: soft, medium, loud, medium, soft.",
            ["A bar at each.", "Louder comes from a faster key, not from pressing harder at the bottom.", "Soft is the hard one: find the slowest key that still sounds."],
            "Prima judges loud and soft against your own range, which Keyboard check in Tools measures.",
            ExerciseMusic.SoftToLoud, ExerciseKeys.One, Either),
        Played("swell", "touch", "Growing and fading", ExerciseLevel.Beginner, "The pattern growing louder to its top note and fading back.",
            ["Start soft enough to leave room to grow.", "Each note a little more than the last, then a little less."],
            "A steady rise and fall, with no sudden step at the top.",
            ExerciseMusic.Swell, ExerciseKeys.All, Either),

        // ——— Pedal ———
        Away("pedal-alone", "pedal", "The pedal by itself", "Press and release the right pedal in time, with no notes.",
            ["Heel on the floor, the ball of the foot on the pedal.", "Down on one, up on three, to a slow count.", "Quietly: no thump at the bottom, no clack at the top."],
            "The ankle does it; the heel never leaves the floor."),
        Played("pedal-changes", "pedal", "Change the pedal on each chord", ExerciseLevel.Beginner, "Four chords joined by the pedal, with a clean change at each.",
            ["Play the new chord, and as it sounds let the pedal up.", "Press it again at once, while the chord is still held.", "The foot follows the hands: up as they play, never before."],
            "No blur from the chord before, and no gap. Prima says when the pedal came late or stayed down.",
            ExerciseMusic.PedalChanges, ExerciseKeys.All, Either),

        // ——— Ways to practise anything ———
        Way("slow", "Slow enough to be right", "A tempo at which it comes out right the first time.",
            ["Find the speed at which you make no mistakes.", "Stay there until it's easy, then move up."],
            "How well you practise decides the result, not how long: in one study of pianists the share of correct run-throughs predicted the next day's playing, and practice time didn't."),
        Way("hands-apart", "Hands separately, then together", "Each hand alone, but not for too long.",
            ["Each hand alone until it's sure of its notes.", "Then together, sooner than feels comfortable, at a slower tempo."],
            "Hands separately teaches the notes. Playing them together is its own skill and only comes from doing it."),
        Way("small-bits", "Small sections", "A bar or two at a time.",
            ["Take the bit that goes wrong, with a note or two either side.", "Join it to its neighbours once it's sure."],
            "Playing the whole thing through again is the slow way."),
        Way("fix-now", "Stop and fix it at once", "A mistake is dealt with where it happens.",
            ["When something goes wrong, stop there.", "Find exactly which note or finger it was.", "Play that spot right, slowly, a few times before going on."],
            "A mistake repeated is a mistake learned. The best practisers in that study fixed theirs straight away."),
        Way("ladder", "The metronome ladder", "Speed built a notch at a time on clean runs.",
            ["Start where it's clean.", "After a few clean runs, a notch faster; after a messy one, a notch back.", "Tomorrow, start a couple of notches below where you finished."],
            "Speed that's built on clean runs stays."),
        Way("mix", "Mix things up", "Several things in turn instead of one for a long time.",
            ["Once two or three things are playable, take them in turn.", "It feels worse while you do it."],
            "It shows up as better playing a day or two later."),
        Way("sleep", "Sleep on it", "Short sessions on more days.",
            ["Short sessions on more days beat one long one.", "Judge an exercise by how the first try goes the next day."],
            "Much of the gain arrives overnight."),
        Way("record", "Record yourself", "A minute of sound, heard from outside.",
            ["Record a minute on the phone.", "Listen once without the music in front of you, once with."],
            "You hear things from outside that you can't while playing."),
        Way("hard-first", "The hard bit first", "Begin where you'd rather not.",
            ["Start the session on the spot you'd rather avoid, while you're fresh.", "Then the parts you like."],
            "The parts you like get played anyway."),
        Way("stop", "When to stop", "The signs that mean rest, not push on.",
            ["Stop at once for pain (not just tiredness), tingling, numbness, or a finger curling by itself.", "Rest. See a doctor if it stays.", "Take a short break every twenty minutes or so anyway."],
            "“No pain, no gain” is a risk factor in the studies of pianists' injuries, not a method."),
    ];

    public static Exercise? Find(string id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>The keys an exercise offers, easiest first, in the boards' order.</summary>
    public static IReadOnlyList<Key> KeysOf(Exercise exercise) => exercise.Keys switch
    {
        ExerciseKeys.All => Technique.Order,
        ExerciseKeys.Majors => [.. Technique.Order.Where(k => !k.Minor)],
        _ => [new Key(0)],
    };

    /// <summary>Whether every word of a search is somewhere in an exercise's name, its section's, or its words.</summary>
    public static bool Matches(Exercise exercise, string? search)
    {
        var text = string.Join(' ', [exercise.Name, Sections.First(s => s.Key == exercise.Section).Name, exercise.Does, .. exercise.How, exercise.Notice]);
        return (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static Exercise Away(string id, string section, string name, string does, string[] how, string notice) => new(id, section, name, ExerciseLevel.Start, ExerciseKind.Away, does, how, notice);

    private static Exercise Way(string id, string name, string does, string[] how, string notice) => new(id, "ways", name, ExerciseLevel.Start, ExerciseKind.Way, does, how, notice);

    private static Exercise Drill(string id, string section, string name, string does, string route) => new(id, section, name, ExerciseLevel.Beginner, ExerciseKind.Drill, does, [], "") { Route = route };

    private static Exercise Played(string id, string section, string name, ExerciseLevel level, string does, string[] how, string notice, Func<Key, HandUse, Score> music, ExerciseKeys keys, HandUse[] hands, double tempo = 60) =>
        new(id, section, name, level, ExerciseKind.Played, does, how, notice) { Music = music, Keys = keys, Hands = hands, Tempo = tempo };
}
