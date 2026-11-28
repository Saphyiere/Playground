using Prima.Core.Practice;
using Prima.UI.Practice;
using Prima.UI.Storage;

namespace Prima.UI.Pages;

// When I slip, on a piece's Practice: the way a run takes a slip, chosen for the piece under its bar's More (Keep going, Back a
// bar, Lock in), and what a slip does to a run then. A go is a run from Play to its end. A slip cuts it, and it counts in again
// where the way says; what it played up to each slip is a part of the go, kept with its end as one run once the go is over.
// Lock in's spots go round in between, each kept as a loop of its own. A loop of yours with Lock in, or the look back's Practice
// it (two, whatever the way), is counted to its clean times round in a row, and ends there with a notch faster or play on.
public partial class Piece
{
    private SlipWay slipWay;
    // The go's parts so far, each up to a slip.
    private readonly List<PlayOutcome> parts = [];
    // Lock in: the spot going round (its bars, streak and tempo), its times round so far (each played to its end, or cut short by
    // a slip), when it started, and your own tempo to go back to after it.
    private LockedSpot? spot;
    private readonly List<PiecePass> spotTries = [];
    private DateTime spotStarted;
    private double ownTempo;
    // A loop counted to its clean times round in a row (Lock in on your own loop, or the look back's Practice it): its streak, its
    // bars, whether a note off the beat spoils a time round, the review's first words once it's reached ("Locked in", "Done"),
    // and then the number, for the review.
    private CleanStreak? goal;
    private (int From, int To)? goalBars;
    private bool goalTiming;
    private string goalLead = "Locked in";
    private int? reached;
    // Practice it's loop, two clean in a row, while it's still those bars: and whether its place needs its timing.
    private (int From, int To, bool Timing)? practiceIt;
    // The run going's times round the page has been told of as they came.
    private int passesSeen;
    // Where the page's own cut goes on to: the music (none: the run ends there), its tempo, and whether the run cut was a spot's.
    private (PlayMusic? Music, double Tempo, bool OfSpot)? onward;
    // A line over the bar's end while a run plays: what Lock in is doing.
    private string? lockSaying;
    private int sayings;

    private string SlipKey => $"Library.Slip.{Id}";

    private static SlipWay SlipWayOf(string? kept) => kept switch { "back" => SlipWay.BackABar, "lock" => SlipWay.LockIn, _ => SlipWay.KeepGoing };

    private async Task SetSlipWay(SlipWay way)
    {
        slipWay = way;
        (goal, goalBars, reached) = (null, null, null);
        await Store.SetSettingAsync(SlipKey, way switch { SlipWay.BackABar => "back", SlipWay.LockIn => "lock", _ => null });
    }

    // A slip stops the run with Back a bar or Lock in.
    private Func<PlayOutcome, Task<PlayMusic?>>? SlipStops => slipWay == SlipWay.KeepGoing ? null : Slipped;

    // A slip, or the page's own cut: where the run counts in again. Back a bar: the bar before, inside the loop if one is set.
    // Lock in: the slip's bar with one either side (inside your loop) goes round, or, round its spot already, it again. Away
    // from Practice, or with nowhere to go on, nothing: the run ends there.
    private Task<PlayMusic?> Slipped(PlayOutcome part)
    {
        if (stage != Stage.Practice || score is null) return Task.FromResult<PlayMusic?>(null);

        // The page asked to move on (a spot done, let go or going slower; your loop's clean times round reached): on as it asked.
        if (onward is { } next)
        {
            onward = null;
            if (!next.OfSpot) parts.Add(part);
            RunTempo(next.Tempo);
            return Task.FromResult(next.Music);
        }
        if (slipWay != SlipWay.LockIn)
        {
            parts.Add(part);
            goal?.Add(false);
            return Task.FromResult(MusicAt(Slips.Back(part.Bar, loopTo is null ? FirstBar : from)));
        }
        if (spot is { } s)
        {
            // A time round the spot, cut short by the slip.
            if (part.CutShort is { } cut) spotTries.Add(cut);
            switch (s.Round(false))
            {
                case LockStep.Slower:
                    RunTempo(s.Tempo);
                    Say($"Two slips in a row: a notch slower, {QuickReview.Mark(s.Tempo)}");
                    return Task.FromResult(SpotMusic(s));
                case LockStep.LetGo:
                    return LetGo(s);
                default:
                    return Task.FromResult(SpotMusic(s));
            }
        }

        // Lock in on the slip's bar, with one either side, inside your loop. Your loop no bigger than that is the spot itself, and
        // goes a notch slower at every second slip in a row, as a spot does.
        parts.Add(part);
        var slower = goal?.Add(false) == true;
        var (first, last) = loopTo is { } to ? (from, to) : (FirstBar, LastBar);
        var (a, b) = Slips.Spot(part.Bar, first, last);
        if (loopTo is { } end && (a, b) == (from, end))
        {
            if (slower)
            {
                RunTempo(Prima.Core.Timing.TempoMarks.Down(tempo));
                Say($"Two slips in a row: a notch slower, {QuickReview.Mark(tempo)}");
            }
            return Task.FromResult(wholeLoop);
        }
        spot = new LockedSpot(a, b, Settings.LockClean, tempo);
        spotTries.Clear();
        (spotStarted, ownTempo) = (DateTime.Now, tempo);
        Say($"{BarWords.Capital(BarWords.Bars(a, b))} until {Settings.LockClean} clean in a row");
        return Task.FromResult(SpotMusic(spot));
    }

    // A bar tapped, or ‹ Bar, while a spot goes round: the spot kept as it was, and your tempo back for where the run goes now.
    private async Task LeaveSpot()
    {
        if (spot is not { } s) return;

        await EndSpot(s, "stopped");
        lockSaying = null;
        RunTempo(ownTempo);
    }

    // A spot spoiled too often at a slip: kept as it was, and the go on from the bar after it at your own tempo.
    private async Task<PlayMusic?> LetGo(LockedSpot s)
    {
        var on = await EndSpot(s, "let go");
        RunTempo(ownTempo);
        return on;
    }

    // Round a loop, each time round as it's scored: Lock in's spot counted (on, slower, done or let go), or your loop's clean
    // times round in a row.
    private async Task Passed(PiecePass pass)
    {
        passesSeen++;
        if (stage != Stage.Practice || play is null) return;

        if (spot is { } s)
        {
            spotTries.Add(pass);
            var step = s.Round(CleanStreak.IsClean(pass, Settings.SlipTiming));
            if (step == LockStep.Again) return;

            if (step == LockStep.Slower)
            {
                onward = (SpotMusic(s), s.Tempo, true);
                Say($"Two spoiled in a row: a notch slower, {QuickReview.Mark(s.Tempo)}");
            }
            else onward = (await EndSpot(s, step == LockStep.Done ? "done" : "let go"), ownTempo, true);
            await play.Cut();
            return;
        }
        if (Counted(pass))
        {
            // Your loop clean so many times in a row: it ends there, and the review says so, with a notch faster and play on.
            onward = (null, tempo, false);
            await play.Cut();
        }
    }

    // A time round your loop counted to its clean times round in a row: whether that's reached.
    private bool Counted(PiecePass pass)
    {
        if (goal is not { } g || loopTo is not { } to || goalBars != (from, to)) return false;

        g.Add(CleanStreak.IsClean(pass, goalTiming));
        if (!g.Done) return false;
        reached = g.Needed;
        return true;
    }

    // A spot done, let go or stopped: kept as a loop of its own, and where the go goes on from. Done, from its first bar; let go,
    // from the bar after it (round your loop, on round that from there); none past the piece's end.
    private async Task<PlayMusic?> EndSpot(LockedSpot s, string ended)
    {
        spot = null;
        await KeepSpot(s, [.. spotTries], ended);
        spotTries.Clear();
        if (ended == "done") Say($"{s.Streak.Needed} clean in a row: on from bar {s.From}");
        var at = ended == "let go" ? s.To + 1 : s.From;
        if (ended == "let go") Say(at > (loopTo ?? LastBar) && loopTo is null ? $"{BarWords.Capital(BarWords.Bars(s.From, s.To))} let go for now" : $"{BarWords.Capital(BarWords.Bars(s.From, s.To))} let go for now: on from bar {(at > (loopTo ?? LastBar) ? from : at)}");
        if (loopTo is { } end) return wholeLoop?.EnterLoopAt(at > end ? from : at);
        return at > LastBar ? null : MusicOf(at, null);
    }

    // A spot kept as a loop of its own: its bars, every time round, and how it ended.
    private async Task KeepSpot(LockedSpot s, IReadOnlyList<PiecePass> tries, string ended)
    {
        if (tries.Count == 0) return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var last = tries[^1];
        var measures = KeptRun.Lock(tries, (s.From, s.To), RunHands(), session?.Id, (DateTime.Now - spotStarted).TotalSeconds, new RunLock(s.Streak.Needed, ended), RunKey);
        await Store.AddAttemptAsync(new AttemptRecord(0, DateTimeOffset.Now, today, "piece", "in time", Id, LevelNow, s.Tempo, null, null, "[]", null, measures,
            last.Notes == 0 ? null : last.Right / (double)last.Notes, CleanStreak.IsClean(last, false), null));
    }

    // The spot's music: its bars round and round, without a pause between, whatever a loop of yours does.
    private PlayMusic? SpotMusic(LockedSpot s) => MusicOf(s.From, s.To, times: 50);

    // The tempo the run counts in at next (a spot a notch slower, or back to yours), not kept for the piece.
    private void RunTempo(double at)
    {
        tempo = at;
        StateHasChanged();
    }

    // A line over the bar's end for a few seconds.
    private void Say(string words)
    {
        lockSaying = words;
        var said = ++sayings;
        _ = InvokeAsync(async () =>
        {
            await Task.Delay(4000);
            if (said != sayings) return;
            lockSaying = null;
            StateHasChanged();
        });
    }

    // The clean times round in a row on the bar: the spot's while it goes round, or your loop's.
    private (int Clean, int Of)? StreakShown => spot is { } s ? (s.Streak.InARow, s.Streak.Needed)
        : goal is { } g && loopTo is { } to && goalBars == (from, to) ? (g.InARow, g.Needed) : null;

    // The run's music from a bar without moving the piece's own start: round its loop from there, or from there to the end.
    private PlayMusic? MusicAt(int bar) => loopTo is not null && wholeLoop is not null ? wholeLoop.EnterLoopAt(bar) : MusicOf(bar, null);

    // A run starting: moved, or counted in again after a slip, it's the same go; started anew, nothing of the last one is left,
    // and your loop with Lock in is counted afresh (where a loop goes a pass a run, its count carries on while it's the same loop).
    private void NewGo(bool continuing)
    {
        passesSeen = 0;
        if (continuing) return;

        parts.Clear();
        (spot, onward, reached) = (null, null, null);
        spotTries.Clear();
        var bars = loopTo is { } to ? (from, to) : ((int, int)?)null;
        if (practiceIt is { } p && bars != (p.From, p.To)) practiceIt = null;
        // Practice it's loop, two in a row, whatever the way; your own loop with Lock in, the number chosen.
        (int Needed, bool Timing, string Lead)? counted = bars is null ? null
            : practiceIt is { } asked ? (Slips.PracticeIt, asked.Timing || Settings.SlipTiming, "Done")
            : slipWay == SlipWay.LockIn ? (Settings.LockClean, Settings.SlipTiming, "Locked in") : null;
        if (counted is not { } count) (goal, goalBars) = (null, null);
        else if (goal is null || goalBars != bars || goal.Done || goal.Needed != count.Needed || !Settings.PassWaits)
            (goal, goalBars, goalTiming, goalLead) = (new CleanStreak(count.Needed), bars, count.Timing, count.Lead);
    }

    // Practice it's loop: counted to two clean times round in a row, as the look back's words say, from its next run.
    private void CountToTwo((int From, int To) bars, bool timing)
    {
        practiceIt = (bars.From, bars.To, timing);
        (goal, goalBars, reached) = (null, null, null);
    }

    // The go as one run: its parts up to each slip, and this, its end; on the music, the worst each note was played. Stopped in the
    // moment after a slip, the go's end is the part up to that slip, which it has already. Stopped round a spot: the spot kept as
    // it was and shown, and the go before it kept as a run stopped at its slip. Whether what's given back is still to be kept.
    private async Task<(PlayOutcome How, bool Keep)> GoEnded(PlayOutcome end)
    {
        // A move asked for that the run ended before (stopped, where slips don't stop it): nothing to go on to.
        onward = null;
        // Times round it played that weren't told as they came (scored as it ended, or a pass a run): counted now.
        foreach (var pass in end.Passes.Skip(passesSeen))
        {
            if (spot is not null) spotTries.Add(pass);
            else Counted(pass);
        }
        passesSeen = end.Passes.Count;
        if (parts.Count > 0 && ReferenceEquals(parts[^1], end)) parts.RemoveAt(parts.Count - 1);

        if (spot is { } s)
        {
            // Stopped in the moment after a slip round it, that time round is among its tries already.
            if (end.CutShort is { } cut && !(spotTries.Count > 0 && ReferenceEquals(spotTries[^1], cut))) spotTries.Add(cut);
            var tries = spotTries.ToList();
            lockSaying = null;
            RunTempo(ownTempo);
            if (parts.Count > 0)
            {
                var before = Go.Join([.. parts.SkipLast(1)], parts[^1]);
                parts.Clear();
                await Keep(before);
            }
            await EndSpot(s, "stopped");
            return (end with { Loop = (s.From, s.To), Passes = tries, CutShort = null }, false);
        }
        if (parts.Count == 0) return (end, true);

        var go = Go.Join(parts, end);
        parts.Clear();
        if (sheet is not null) await sheet.ShowMarks(go.Marks);
        return (go, true);
    }

    // The loop counted to its clean times round in a row, for Keep: how many, and how it ended.
    private RunLock? LoopLock(PlayOutcome how) => how.Loop is { } bars && goal is { } g && goalBars == bars ? new RunLock(g.Needed, g.Done ? "done" : "stopped") : null;

    // The review's words for your loop clean so many times in a row, and its keys.
    private Prima.Core.Practice.Sentence? LockedWords => reached is { } n ? QuickReview.Locked(n, tempo, goalLead) : null;

    // A notch faster: the same loop again, counted afresh.
    private async Task Faster()
    {
        await SetTempo(Prima.Core.Timing.TempoMarks.Up(tempo));
        (goal, goalBars, reached, outcome) = (null, null, null, null);
        startAfterRender = true;
    }

    // Play on: the loop off, from its first bar to the end.
    private void PlayOn()
    {
        (goal, goalBars, reached, outcome, practiceIt) = (null, null, null, null, null);
        SetLoop(null);
        startAfterRender = true;
    }
}
