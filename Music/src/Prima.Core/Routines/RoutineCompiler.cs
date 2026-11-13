using System.Security.Cryptography;
using System.Text;
using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Routines;

public static class RoutineCompiler
{
    public static IReadOnlyList<string> Templates { get; } = ["jump", "chord-shapes", "chord-changes", "accompaniment", "rhythm", "coordination"];

    public static string SourceRevision(string mei) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(mei)));

    public static RoutinePlanningInput Input(RoutineRequest request, PracticeEvidenceSnapshot evidence, RoutineSource? source = null)
    {
        if (request.Minutes is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(request), "Choose between 1 and 120 minutes.");
        if (request.Goal.Length > 2000) throw new ArgumentException("Keep the request within 2000 characters.", nameof(request));
        if ((request.FirstBar is null) != (request.LastBar is null) || request.FirstBar > request.LastBar) throw new ArgumentException("Choose a complete, ordered bar range.", nameof(request));
        if (source is null && request.PieceId is not null) throw new ArgumentException("The selected piece has no retained musical source.", nameof(source));
        if (source is not null && (source.PieceId != request.PieceId || source.Revision != SourceRevision(source.Mei))) throw new ArgumentException("The source does not match the selected piece revision.", nameof(source));

        var catalog = Exercises.All.Where(e => e.Kind == ExerciseKind.Played).Select(e => new RoutineCatalogEntry("exercise", e.Id, e.Name, e.Does,
            e.Hands, Exercises.KeysOf(e).Select(k => k.Fifths).Distinct().ToArray(), Exercises.KeysOf(e).Any(k => k.Minor), 1)).ToList();
        foreach (var (id, name, top) in new (string, string, int)[] { ("notes", "Note reading", 1), ("ledger", "Ledger lines", 3), ("intervals", "Intervals and shapes", 4), ("chords", "Chords", 4), ("keys", "Key signatures", 6), ("geography", "Keyboard geography", 4), ("scales", "Scales", 1), ("arpeggios", "Arpeggios", 1), ("independence", "Finger independence", 3), ("rhythm", "Rhythm", 12) })
        {
            var technique = id is "scales" or "arpeggios" or "independence";
            catalog.Add(new RoutineCatalogEntry("drill", id, name, name, technique ? [HandUse.Right, HandUse.Left, HandUse.Together] : [HandUse.Together],
                technique ? Technique.Order.Select(k => k.Fifths).Distinct().ToArray() : [0], technique && id != "independence", top)
            { Modes = technique ? ["wait", "time"] : id == "rhythm" ? ["time"] : ["wait"] });
        }

        IReadOnlyList<RoutineSourceEvent> events = [];
        Key? sourceKey = null;
        Meter? sourceMeter = null;
        if (source is not null)
        {
            var score = MeiReader.Read(source.Mei);
            if (request.FirstBar is { } first && (!score.Bars.Any(b => b.Number == first) || !score.Bars.Any(b => b.Number == request.LastBar))) throw new ArgumentException("The selected bars are not in this source.", nameof(request));
            sourceKey = score.Key;
            sourceMeter = score.Meter;
            if (request.FirstBar is { } selected)
            {
                var signature = score.WithSignatures().First(s => s.Bar.Number == selected);
                sourceKey = signature.Key;
                sourceMeter = signature.Meter;
            }
            var all = SourceEvents(score).Where(e => request.FirstBar is null || e.Bar >= request.FirstBar && e.Bar <= request.LastBar).ToList();
            if (request.FirstBar is not null && all.Count > 240) throw new ArgumentException("Choose a smaller passage for focused practice (at most 240 note groups).", nameof(request));
            if (all.Count <= 240) events = all;
            else
            {
                // Whole small windows keep both hands and neighbouring events available. Always include the opening.
                var bars = all.GroupBy(e => e.Bar).OrderByDescending(g => g.Max(e => e.MoveFromPrevious + e.Span + e.Pitches.Count * 2) + g.Count()).Take(12).Select(g => g.Key).ToHashSet();
                bars.Add(all[0].Bar);
                var windows = score.Bars.Select(b => b.Number).Where(b => bars.Contains(b) || bars.Contains(b + 1) || bars.Contains(b - 1)).ToHashSet();
                var bounded = new List<RoutineSourceEvent>();
                foreach (var group in all.Where(e => windows.Contains(e.Bar)).GroupBy(e => e.Bar))
                {
                    if (bounded.Count + group.Count() <= 240) bounded.AddRange(group);
                }
                events = bounded;
            }
        }
        return new RoutinePlanningInput(request, evidence, catalog, source?.Title, source?.Revision, source?.HandsConfirmed ?? false,
            source?.HasUnresolvedErrors ?? false, events) { SourceKey = sourceKey, SourceMeter = sourceMeter };
    }

    public static IReadOnlyList<RoutineSourceEvent> SourceEvents(Score score)
    {
        var events = new List<RoutineSourceEvent>();
        var previous = new Dictionary<int, RoutineSourceEvent>();
        foreach (var bar in score.Bars)
        {
            var starts = bar.Voices.Where(v => v.Staff is 1 or 2).SelectMany(v => v.Events.OfType<Notes>().Where(n => !n.Grace).Select(n => (v.Staff, Note: n)))
                .GroupBy(n => (n.Staff, n.Note.Start)).OrderBy(g => g.Key.Start).ThenBy(g => g.Key.Staff);
            foreach (var group in starts)
            {
                var tones = group.SelectMany(n => n.Note.Tones).Where(t => !t.TiedFromPrevious).DistinctBy(t => t.Pitch.Midi).OrderBy(t => t.Pitch.Midi).ToList();
                if (tones.Count == 0) continue;
                var pitches = tones.Select(t => t.Pitch.Midi).ToArray();
                var before = previous.GetValueOrDefault(group.Key.Staff);
                var movement = before is null ? 0 : Math.Max(Math.Abs(pitches[0] - before.Pitches[0]), Math.Abs(pitches[^1] - before.Pitches[^1]));
                var frame = new RoutineSourceEvent($"b{bar.Number}s{group.Key.Staff}t{group.Key.Start}", bar.Number, group.Key.Staff, group.Key.Start,
                    group.Max(n => n.Note.Length), pitches, tones.Select(t => t.Id).ToArray(), pitches[^1] - pitches[0], movement, before?.Id);
                events.Add(frame);
                previous[group.Key.Staff] = frame;
            }
        }
        return events;
    }

    public static IReadOnlyList<string> Check(RoutinePlanningInput input, RoutineDraft draft)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Title) || draft.Title.Length > 160 || draft.Summary.Length > 2000) problems.Add("The routine needs a short title and summary.");
        if (draft.Items.Count is < 1 or > 20) problems.Add("A routine needs between 1 and 20 activities.");
        if (draft.Items.Sum(i => (long)i.Minutes) > input.Request.Minutes) problems.Add("The activities exceed the requested practice time.");
        if (draft.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != draft.Items.Count) problems.Add("Each activity needs a distinct ID.");
        var frames = input.SourceEvents.ToDictionary(e => e.Id);
        foreach (var item in draft.Items)
        {
            var prefix = $"Activity {item.Id}: ";
            if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 80 || item.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) problems.Add(prefix + "use a short letter/number ID.");
            if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 160 || string.IsNullOrWhiteSpace(item.Purpose) || item.Purpose.Length > 1000) problems.Add(prefix + "supply a concise title and purpose.");
            if (item.Minutes is < 1 or > 30 || !double.IsFinite(item.Tempo) || item.Tempo is < 20 or > 160) problems.Add(prefix + "unsupported time or tempo.");
            if (item.Mode is not ("wait" or "time") || !Enum.IsDefined(item.Hands) || item.Fifths is < -7 or > 7) problems.Add(prefix + "unsupported musical settings.");
            if (item.PreparationBeats is < 0 or > 4 || item.Repetitions is < 1 or > 8) problems.Add(prefix + "unsupported preparation time or repeat count.");
            if (item.SourceEventIds.Count > 32 || item.SourceEventIds.Distinct().Count() != item.SourceEventIds.Count) problems.Add(prefix + "source event references must be distinct and bounded.");
            if (item.Kind is "exercise" or "drill")
            {
                var entry = input.Catalog.FirstOrDefault(c => c.Kind == item.Kind && c.Id == item.TargetId);
                if (entry is null || !entry.Hands.Contains(item.Hands) || !entry.Fifths.Contains(item.Fifths) || !entry.Modes.Contains(item.Mode) || item.Minor && !entry.AllowsMinor || item.Step < 1 || item.Step > entry.MaxStep) problems.Add(prefix + "the catalogue does not support these parameters.");
                if (item.Kind == "exercise" && Exercises.Find(item.TargetId) is { } exercise && !Exercises.KeysOf(exercise).Contains(new Key(item.Fifths, item.Minor))) problems.Add(prefix + "this exercise does not support the selected key.");
                if (item.SourceEventIds.Count != 0 || item.FirstBar is not null || item.LastBar is not null) problems.Add(prefix + "catalogue activities cannot claim source notes.");
                if (item.TargetId == "rhythm" && item.Mode != "time") problems.Add(prefix + "rhythm practice uses timed mode.");
                continue;
            }
            if (item.Kind is not ("generated" or "excerpt")) { problems.Add(prefix + "unknown activity kind."); continue; }
            if (input.SourceRevision is null || input.SourceEvents.Count == 0 || input.HasUnresolvedErrors) problems.Add(prefix + "a reviewed musical source is required.");
            if (!input.HandsConfirmed && (item.Kind == "generated" || item.Hands != HandUse.Together)) problems.Add(prefix + "confirm the source's hand assignments first.");
            if (item.Kind == "generated" && !Templates.Contains(item.TargetId) || item.Kind == "excerpt" && item.TargetId != "source") problems.Add(prefix + "unknown generation template.");
            var selected = item.SourceEventIds.Where(frames.ContainsKey).Select(id => frames[id]).ToList();
            if (selected.Count != item.SourceEventIds.Count) problems.Add(prefix + "a source event is absent from the supplied source.");
            if (item.TargetId is "jump" or "chord-changes" or "chord-shapes")
            {
                var count = item.TargetId == "chord-shapes" ? 1 : 2;
                if (selected.Count != count || item.Hands == HandUse.Together || selected.Any(e => e.Staff != (item.Hands == HandUse.Right ? 1 : 2))) problems.Add(prefix + "select the required events from one matching hand.");
                if (selected.Count == 2 && selected[1].PreviousId != selected[0].Id) problems.Add(prefix + "a transition must use consecutive source events.");
                if (item.TargetId == "chord-shapes" && selected.Count == 1 && selected[0].Pitches.Count < 2) problems.Add(prefix + "choose a chord for shape practice.");
                if (item.TargetId == "chord-changes" && selected.Count == 2 && selected.All(e => e.Pitches.Count < 2)) problems.Add(prefix + "choose a transition involving a chord.");
                if (selected.Any(e => e.Span > 12 || e.Pitches.Count > 5)) problems.Add(prefix + "this chord needs individual review before isolated hand practice.");
            }
            else
            {
                if (item.FirstBar is null || item.LastBar is null || item.LastBar < item.FirstBar || item.LastBar - item.FirstBar > 7 || !input.SourceEvents.Any(e => e.Bar >= item.FirstBar && e.Bar <= item.LastBar)) problems.Add(prefix + "choose one to eight source bars containing notes.");
                if (item.FirstBar < input.Request.FirstBar || item.LastBar > input.Request.LastBar) problems.Add(prefix + "the passage exceeds the requested selection.");
                if (item.TargetId is "rhythm" or "accompaniment" && item.Hands == HandUse.Together) problems.Add(prefix + "choose one hand's pattern.");
                if (item.TargetId == "coordination" && item.Hands != HandUse.Together) problems.Add(prefix + "coordination includes both hands.");
                if (item.TargetId == "rhythm" && item.Mode != "time") problems.Add(prefix + "rhythm practice uses timed mode.");
            }
        }
        return problems;
    }

    public static IReadOnlyList<PreparedRoutineItem> Prepare(RoutineDraft draft, RoutineRequest request, RoutineSource? source = null)
    {
        var input = Input(request, new PracticeEvidenceSnapshot(DateTimeOffset.UtcNow, []), source);
        var problems = Check(input, draft);
        if (problems.Count > 0) throw new ArgumentException(string.Join("\n", problems), nameof(draft));
        var score = source is null ? null : MeiReader.Read(source.Mei);
        return draft.Items.Select(item => item.Kind is "exercise" or "drill" ? new PreparedRoutineItem(item, "", null)
            : new PreparedRoutineItem(item, MeiWriter.Write(RoutineMusic.Build(score!, item), item.Tempo), source!.Revision)).ToArray();
    }
}
