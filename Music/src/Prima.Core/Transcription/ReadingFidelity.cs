namespace Prima.Core.Transcription;

public enum ReadingFidelityKind
{
    DanglingTie, UnclosedSlur, UnmatchedSlurEnd, UnclosedHairpin, UnmatchedHairpinEnd,
    UnclosedPedal, UnmatchedPedalUp, MarkDropped, MarkReanchored, BoundaryContinuation,
}

/// <summary>SystemIndex and EventIndex are zero-based; BarIndex is the reading's one-based local bar number.</summary>
public sealed record ReadingFidelityIssue(int SystemIndex, int BarIndex, int Staff, int Voice, int EventIndex,
    ReadingFidelityKind Kind, string Message);

/// <summary>Deterministic assembly/engraving concerns, separate from model doubts and human review flags.</summary>
public static class ReadingFidelity
{
    public static IReadOnlyList<ReadingFidelityIssue> Find(IReadOnlyList<SystemReading?> readings)
    {
        var issues = new List<ReadingFidelityIssue>();
        var ties = new Dictionary<(int Staff, int Voice), (ReadingFidelityIssue Origin, IReadOnlyList<ReadPitch> Pitches, int Ottava)>();
        var slurs = new List<ReadingFidelityIssue>();
        var hairpins = new List<ReadingFidelityIssue>();
        ReadingFidelityIssue? pedal = null;
        var unknownBefore = false;

        // A missing system is a boundary, not a join. Do not pair a mark across music that has not been read.
        void Finish(bool gap)
        {
            var open = ties.Values.Select(t => t.Origin).Concat(slurs).Concat(hairpins).Concat(pedal is null ? [] : new[] { pedal });
            issues.AddRange(gap ? open.Select(i => i with { Kind = ReadingFidelityKind.BoundaryContinuation,
                Message = $"The {i.Kind switch { ReadingFidelityKind.DanglingTie => "tie", ReadingFidelityKind.UnclosedSlur => "slur", ReadingFidelityKind.UnclosedHairpin => "hairpin", _ => "pedal" }} reaches an unread system; its continuation cannot be checked yet." }) : open);
            ties.Clear();
            slurs.Clear();
            hairpins.Clear();
            pedal = null;
        }

        for (var systemIndex = 0; systemIndex < readings.Count; systemIndex++)
        {
            if (readings[systemIndex] is not { } reading)
            {
                Finish(gap: true);
                unknownBefore = true;
                continue;
            }
            foreach (var bar in reading.Bars)
            {
                var marks = new List<(ReadEvent Event, ReadMark Mark, ReadingFidelityIssue Origin, int Tick)>();
                var lanes = bar.Staves.SelectMany(s => s.Voices.Select(v => (s.Staff, v.Voice))).ToHashSet();
                foreach (var absent in ties.Keys.Where(k => !lanes.Contains(k)).ToArray())
                {
                    issues.Add(ties[absent].Origin with { Message = "The forward tie has no matching voice in the next bar; engraving drops it." });
                    ties.Remove(absent);
                }
                foreach (var staff in bar.Staves)
                {
                    foreach (var voice in staff.Voices)
                    {
                        var lane = (staff.Staff, voice.Voice);
                        var tick = 0;
                        for (var eventIndex = 0; eventIndex < voice.Events.Count; eventIndex++)
                        {
                            var e = voice.Events[eventIndex];
                            var origin = new ReadingFidelityIssue(systemIndex, bar.Index, staff.Staff, voice.Voice, eventIndex,
                                ReadingFidelityKind.DanglingTie, "The forward tie has no following note in the available reading; engraving drops it.");
                            if (!e.Grace && e.Kind != ReadKind.Clef)
                            {
                                if (ties.Remove(lane, out var pending) && pending.Pitches.Any(p =>
                                    !e.Pitches.Any(q => q.Step == p.Step && q.Octave + e.Ottava == p.Octave + pending.Ottava)))
                                    issues.Add(pending.Origin with { Message = "The forward tie does not reach its pitch in the next event; engraving drops it." });
                                var outgoing = e.Pitches.Where(p => p.Tie).ToArray();
                                if (outgoing.Length > 0) ties[lane] = (origin, outgoing, e.Ottava);
                            }
                            else if (e.Grace && e.Pitches.Any(p => p.Tie))
                                issues.Add(origin with { Message = "Forward ties on grace notes are not preserved by the engraving conversion." });

                            foreach (var mark in e.Marks) marks.Add((e, mark, origin, tick));
                            if (!e.Grace && e.Kind != ReadKind.Clef)
                                tick += e.Kind == ReadKind.BarRest ? bar.MeterCount * Prima.Core.Music.Ticks.Whole / bar.MeterUnit : ReadingChecks.Length(e) ?? 0;
                        }
                    }
                }
                foreach (var item in marks.OrderBy(m => m.Tick).ThenBy(m => m.Event.Grace ? 0 : 1)
                    .ThenBy(m => m.Mark is ReadMark.SlurEnd or ReadMark.HairpinEnd or ReadMark.PedalUp ? 0 : 1))
                {
                    var (e, mark, origin, _) = item;
                    var needsAnchor = e.Kind != ReadKind.Note || e.Grace && mark is not (ReadMark.SlurStart or ReadMark.SlurEnd);
                    if (e.Kind == ReadKind.Clef || needsAnchor && !bar.Staves.SelectMany(s => s.Voices).SelectMany(v => v.Events).Any(n => n.Kind == ReadKind.Note && !n.Grace))
                        issues.Add(origin with { Kind = ReadingFidelityKind.MarkDropped, Message = $"The {mark} mark has no usable note anchor in this bar; engraving omits it." });
                    else if (needsAnchor && !bar.Staves.Single(s => s.Staff == origin.Staff).Voices.SelectMany(v => v.Events).Any(n => n.Kind == ReadKind.Note && !n.Grace))
                        issues.Add(origin with { Kind = ReadingFidelityKind.MarkReanchored, Message = $"The {mark} mark is attached to the other staff because its own staff has no note anchor." });

                    var boundary = unknownBefore ? ReadingFidelityKind.BoundaryContinuation : ReadingFidelityKind.UnmatchedSlurEnd;
                    switch (mark)
                    {
                        case ReadMark.SlurStart:
                            slurs.Add(origin with { Kind = ReadingFidelityKind.UnclosedSlur, Message = "The slur has no ending in the available reading; engraving omits it." });
                            break;
                        case ReadMark.SlurEnd:
                            var slur = slurs.FindLastIndex(s => s.Staff == origin.Staff && s.Voice == origin.Voice);
                            if (slur >= 0) slurs.RemoveAt(slur);
                            else issues.Add(origin with { Kind = boundary, Message = unknownBefore ? "The slur may start in unread music before this range." : "The slur ends without a start in this voice; engraving omits it." });
                            break;
                        case ReadMark.CrescendoStart or ReadMark.DiminuendoStart:
                            hairpins.Add(origin with { Kind = ReadingFidelityKind.UnclosedHairpin, Message = "The hairpin has no ending in the available reading; engraving omits it." });
                            break;
                        case ReadMark.HairpinEnd:
                            var hairpin = hairpins.FindLastIndex(h => h.Staff == origin.Staff);
                            if (hairpin >= 0) hairpins.RemoveAt(hairpin);
                            else issues.Add(origin with { Kind = unknownBefore ? ReadingFidelityKind.BoundaryContinuation : ReadingFidelityKind.UnmatchedHairpinEnd,
                                Message = unknownBefore ? "The hairpin may start in unread music before this range." : "The hairpin ends without a start on this staff; engraving omits it." });
                            break;
                        case ReadMark.PedalUp or ReadMark.PedalChange:
                            if (pedal is null) issues.Add(origin with { Kind = unknownBefore ? ReadingFidelityKind.BoundaryContinuation : ReadingFidelityKind.UnmatchedPedalUp,
                                Message = unknownBefore ? "The pedal may have been pressed in unread music before this range." : "The pedal releases without an earlier press in the available reading." });
                            pedal = mark == ReadMark.PedalChange ? origin with { Kind = ReadingFidelityKind.UnclosedPedal, Message = "The pedal has no later release in the available reading." } : null;
                            break;
                        case ReadMark.PedalDown:
                            if (pedal is not null) issues.Add(pedal with { Message = "The pedal is pressed again before a release; check whether a pedal change was intended." });
                            pedal = origin with { Kind = ReadingFidelityKind.UnclosedPedal, Message = "The pedal has no later release in the available reading." };
                            break;
                    }
                }
            }
        }
        Finish(gap: false);
        return issues.Distinct().OrderBy(i => i.SystemIndex).ThenBy(i => i.BarIndex).ThenBy(i => i.Staff).ThenBy(i => i.Voice).ThenBy(i => i.EventIndex).ToArray();
    }
}
