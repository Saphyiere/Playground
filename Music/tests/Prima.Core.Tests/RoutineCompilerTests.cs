using Playground.AI.Assist;
using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Routines;

namespace Prima.Core.Tests;

public class RoutineCompilerTests
{
    private static readonly PracticeEvidenceSnapshot Evidence = new(DateTimeOffset.UnixEpoch, []);

    private static Score SourceScore()
    {
        static Notes Note(string id, int start, params int[] pitches) => new(id, start, Ticks.Quarter,
            pitches.Select((p, i) => new Tone($"{id}t{i}", new Key(0).Spell(p))).ToArray());
        return new Score(new Key(0), new Meter(4, 4),
        [
            new Bar(1, [new Voice(1, [Note("a", 0, 60, 64, 67), Note("b", 480, 69), new Rest("rr1", 960, 960)]), new Voice(2, [Note("c", 0, 48), Note("d", 480, 55), new Rest("lr1", 960, 960)])]),
            new Bar(2, [new Voice(1, [Note("e", 0, 65, 69, 72), Note("f", 480, 64), new Rest("rr2", 960, 960)]), new Voice(2, [Note("g", 0, 53), Note("h", 480, 60), new Rest("lr2", 960, 960)])]),
        ]);
    }

    private static RoutineSource Source(bool confirmed = true)
    {
        var mei = MeiWriter.Write(SourceScore());
        return new RoutineSource("sweden-test", "Source fixture", RoutineCompiler.SourceRevision(mei), mei, confirmed);
    }

    private static RoutineItem Item(string template) => new()
    {
        Id = "focus", Kind = template == "source" ? "excerpt" : "generated", TargetId = template, Title = "Exercise", Purpose = "Practise the selected transition",
        Minutes = 3, Hands = template == "coordination" ? HandUse.Together : HandUse.Right, Mode = template == "rhythm" ? "time" : "wait",
        SourceEventIds = template == "chord-shapes" ? ["b1s1t0"] : template is "jump" or "chord-changes" ? ["b1s1t0", "b1s1t480"] : [],
        FirstBar = template is "jump" or "chord-changes" or "chord-shapes" ? null : 1,
        LastBar = template is "jump" or "chord-changes" or "chord-shapes" ? null : 2,
    };

    [Fact]
    public void Planning_input_includes_tick_units_and_the_signature_at_the_selected_passage()
    {
        var initial = RoutineCompiler.Input(new RoutineRequest(10, "Practice", "sweden-test"), Evidence, Source());
        Assert.Equal(Ticks.Quarter, initial.TicksPerQuarter);
        Assert.Equal(new Key(0), initial.SourceKey);
        Assert.Equal(new Meter(4, 4), initial.SourceMeter);

        var score = SourceScore();
        score = score with { Bars = [score.Bars[0], score.Bars[1] with { NewKey = new Key(2), NewMeter = new Meter(2, 2) }] };
        var mei = MeiWriter.Write(score);
        var source = new RoutineSource("changing", "Source fixture", RoutineCompiler.SourceRevision(mei), mei, true);
        var selected = RoutineCompiler.Input(new RoutineRequest(10, "Practice", "changing", 2, 2), Evidence, source);
        Assert.Equal(new Key(2), selected.SourceKey);
        Assert.Equal(new Meter(2, 2), selected.SourceMeter);
        Assert.Equal(480, selected.TicksPerQuarter);
        Assert.All(selected.SourceEvents, e => Assert.Equal(2, e.Bar));

        var general = RoutineCompiler.Input(new RoutineRequest(10, "Practice"), Evidence);
        Assert.Null(general.SourceKey);
        Assert.Null(general.SourceMeter);
        Assert.Equal(Ticks.Quarter, general.TicksPerQuarter);
    }

    [Theory]
    [InlineData("jump")]
    [InlineData("chord-shapes")]
    [InlineData("chord-changes")]
    [InlineData("accompaniment")]
    [InlineData("rhythm")]
    [InlineData("coordination")]
    [InlineData("source")]
    public void Every_template_builds_deterministically_and_round_trips(string template)
    {
        var item = Item(template);
        var draft = new RoutineDraft("Routine", "A bounded plan", [item]);
        var request = new RoutineRequest(10, "Help with a transition", "sweden-test");
        var first = Assert.Single(RoutineCompiler.Prepare(draft, request, Source()));
        var second = Assert.Single(RoutineCompiler.Prepare(draft, request, Source()));
        Assert.Equal(first, second);
        Assert.Equal(Source().Revision, first.SourceRevision);
        var score = MeiReader.Read(first.Mei);
        var notes = score.Expected();
        Assert.NotEmpty(notes);
        if (template == "rhythm") Assert.All(notes, n => Assert.Equal(60, n.Midi));
        else Assert.All(notes, n => Assert.Contains(n.Midi, SourceScore().Expected().Select(s => s.Midi)));
        foreach (var (bar, _, meter) in score.WithSignatures())
        {
            foreach (var voice in bar.Voices) Assert.Equal(bar.Length ?? meter.BarTicks, voice.Events.Sum(e => e.Length));
        }
    }

    [Fact]
    public void The_departure_chord_and_landing_are_preserved_as_a_transition()
    {
        var score = RoutineMusic.Build(SourceScore(), Item("jump") with { Repetitions = 1, PreparationBeats = 2 });
        var groups = score.Expected().GroupBy(n => n.Tick).ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Equal(new[] { 60, 64, 67 }, groups[0].Select(n => n.Midi));
        Assert.Equal(69, Assert.Single(groups[1]).Midi);
        Assert.Equal(3 * Ticks.Quarter, groups[1].Key - groups[0].Key);
        Assert.All(score.Expected(), n => Assert.Equal(1, n.Staff));
    }

    [Fact]
    public void Coordination_contains_left_then_right_then_both_without_new_pitches()
    {
        var score = RoutineMusic.Build(SourceScore(), Item("coordination") with { Repetitions = 1 });
        Assert.Equal(6, score.Bars.Count);
        Assert.All(score.Expected().Where(n => n.Bar <= 2), n => Assert.Equal(2, n.Staff));
        Assert.All(score.Expected().Where(n => n.Bar is 3 or 4), n => Assert.Equal(1, n.Staff));
        Assert.Equal(new[] { 1, 2 }, score.Expected().Where(n => n.Bar >= 5).Select(n => n.Staff).Distinct().Order());
    }

    [Fact]
    public void Ambiguous_hands_or_suspect_notes_cannot_generate_confident_hand_practice()
    {
        var request = new RoutineRequest(10, "Jumps", "sweden-test");
        var draft = new RoutineDraft("Routine", "Practice", [Item("jump")]);
        Assert.NotEmpty(RoutineCompiler.Check(RoutineCompiler.Input(request, Evidence, Source(false)), draft));
        Assert.NotEmpty(RoutineCompiler.Check(RoutineCompiler.Input(request, Evidence, Source() with { HasUnresolvedErrors = true }), draft));
        Assert.Throws<ArgumentException>(() => RoutineCompiler.Prepare(draft, request, Source(false)));
    }

    [Fact]
    public void Source_revision_mismatch_and_out_of_selection_events_are_rejected()
    {
        var request = new RoutineRequest(10, "Jumps", "sweden-test", 2, 2);
        Assert.Throws<ArgumentException>(() => RoutineCompiler.Input(request, Evidence, Source() with { Revision = "old-revision" }));
        var input = RoutineCompiler.Input(request, Evidence, Source());
        Assert.NotEmpty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [Item("jump")])));
    }

    [Fact]
    public void Invented_ids_unsupported_parameters_and_overlong_plans_fail_validation()
    {
        var request = new RoutineRequest(5, "Jumps");
        var input = RoutineCompiler.Input(request, Evidence);
        var item = new RoutineItem { Id = "one", Kind = "exercise", TargetId = "octave-jump", Title = "Exercise", Purpose = "Landing", Minutes = 3 };
        Assert.Empty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [item])));
        Assert.NotEmpty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [item with { TargetId = "invented" }])));
        Assert.NotEmpty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [item with { Hands = HandUse.Together }])));
        Assert.NotEmpty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [item, item with { Id = "two" }])));
        Assert.NotEmpty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [item with { Tempo = double.NaN }])));
    }

    [Fact]
    public void Reading_quizzes_do_not_claim_timed_play_and_independence_is_major_only()
    {
        var input = RoutineCompiler.Input(new RoutineRequest(10, "Practice"), Evidence);
        var reading = new RoutineItem { Id = "read", Kind = "drill", TargetId = "geography", Title = "Exercise", Purpose = "Find notes", Hands = HandUse.Together };
        Assert.Empty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [reading])));
        Assert.NotEmpty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [reading with { Mode = "time" }])));
        Assert.Equal(new[] { "wait" }, input.Catalog.Single(c => c.Kind == "drill" && c.Id == "geography").Modes);
        var independence = reading with { TargetId = "independence", Hands = HandUse.Right };
        Assert.Empty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [independence])));
        Assert.NotEmpty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [independence with { Minor = true }])));
        Assert.False(input.Catalog.Single(c => c.Kind == "drill" && c.Id == "independence").AllowsMinor);
        Assert.NotEmpty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [reading with { TargetId = "rhythm", Mode = "wait" }])));
        Assert.Empty(RoutineCompiler.Check(input, new RoutineDraft("Routine", "Practice", [reading with { TargetId = "rhythm", Mode = "time" }])));
    }

    [Fact]
    public void Source_event_adjacency_is_per_hand_and_crosses_barlines()
    {
        var events = RoutineCompiler.SourceEvents(SourceScore());
        Assert.Equal("b1s1t480", events.Single(e => e.Id == "b2s1t0").PreviousId);
        Assert.Equal("b1s2t480", events.Single(e => e.Id == "b2s2t0").PreviousId);
        var item = Item("jump") with { SourceEventIds = ["b1s1t480", "b2s1t0"] };
        Assert.NotEmpty(RoutineMusic.Build(SourceScore(), item).Expected());
        Assert.Throws<ArgumentException>(() => RoutineMusic.Build(SourceScore(), item with { SourceEventIds = ["b1s1t0", "b2s1t0"] }));
    }

    [Fact]
    public void A_pickup_and_tied_boundary_keep_their_usable_excerpt_rhythm()
    {
        var pitch = new Spelled(Step.C, 0, 4);
        var source = new Score(new Key(0), new Meter(4, 4),
        [
            new Bar(0, [new Voice(1, [new Notes("pickup", 0, Ticks.Quarter, [new Tone("p", pitch) { TieToNext = true }])])]) { Length = Ticks.Quarter },
            new Bar(1, [new Voice(1, [new Notes("held", 0, Ticks.Whole, [new Tone("h", pitch) { TiedFromPrevious = true }])])]),
        ]);
        var full = RoutineMusic.Build(source, Item("source") with { FirstBar = 0, LastBar = 1 });
        var held = Assert.Single(full.Expected());
        Assert.Equal(-Ticks.Quarter, held.Tick);
        Assert.Equal(Ticks.Whole + Ticks.Quarter, held.Length);
        var cut = RoutineMusic.Build(source, Item("source") with { FirstBar = 1, LastBar = 1 });
        Assert.Equal(Ticks.Whole, Assert.Single(cut.Expected()).Length);
    }

    [Fact]
    public void Rhythm_reduction_retains_tuplet_onsets()
    {
        var triplet = new Tuplet(3, 2);
        var notes = Enumerable.Range(0, 3).Select(i => (Event)new Notes($"n{i}", i * 160, 160, [new Tone($"t{i}", new Key(0).Spell(60 + i * 2))]) { Tuplet = triplet }).ToList();
        notes.Add(new Rest("rest", 480, 1440));
        var source = new Score(new Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1, notes)])]);
        var rhythm = RoutineMusic.Build(source, Item("rhythm") with { FirstBar = 1, LastBar = 1, Repetitions = 1 });
        Assert.Equal(source.Expected().Select(n => (n.Tick, n.Length)), rhythm.Expected().Select(n => (n.Tick, n.Length)));
        Assert.All(rhythm.Expected(), n => Assert.Equal(60, n.Midi));
    }

    [Fact]
    public void Rhythm_retains_independent_voices_when_their_taps_coincide()
    {
        var source = new Score(new Key(2), new Meter(4, 4),
        [
            new Bar(1,
            [
                new Voice(1, [new Notes("upper", 0, Ticks.Whole, [new Tone("u", new Spelled(Step.F, 1, 5))])]),
                new Voice(1, [new Notes("lower", 0, Ticks.Half, [new Tone("l", new Spelled(Step.C, 1, 5))]), new Rest("silence", Ticks.Half, Ticks.Half)], 2),
            ]),
        ]);
        var rhythm = RoutineMusic.Build(source, Item("rhythm") with { FirstBar = 1, LastBar = 1, Repetitions = 1 });
        Assert.Equal(2, rhythm.Bars[0].Voices.Count);
        Assert.Equal(new[] { Ticks.Whole, Ticks.Half }, rhythm.Expected().Select(n => n.Length));
        Assert.All(rhythm.Expected(), n => Assert.Equal(60, n.Midi));
        Assert.Single(rhythm.Expected().Select(n => n.Tick).Distinct());
    }

    [Fact]
    public void Isolated_chord_practice_does_not_prescribe_unreviewed_wide_spans()
    {
        var source = SourceScore();
        var bars = source.Bars.ToArray();
        var voice = bars[0].Voices[0];
        var events = voice.Events.ToArray();
        var chord = (Notes)events[0];
        events[0] = chord with { Tones = [new Tone("wide1", new Key(0).Spell(60)), new Tone("wide2", new Key(0).Spell(74))] };
        bars[0] = bars[0] with { Voices = [voice with { Events = events }, bars[0].Voices[1]] };
        source = source with { Bars = bars };
        Assert.Throws<ArgumentException>(() => RoutineMusic.Build(source, Item("chord-shapes")));
    }

    [Fact]
    public void The_task_requires_explicit_model_and_has_a_supported_structured_schema()
    {
        Assert.Throws<ArgumentException>(() => new PlanRoutine(new InferenceSettings()));
        Assert.NotEmpty(AssistSchema.For(typeof(RoutineDraft)));
        var task = new PlanRoutine(new InferenceSettings("configured-model"));
        Assert.Equal("configured-model", task.Execution.Model);
        Assert.Equal(8_000, task.Execution.MaxOutputTokens);
    }
}
