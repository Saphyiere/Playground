using System.Buffers.Binary;
using Playground.AI.Assist;
using Prima.Core.Music;

namespace Prima.Core.Transcription;

/// <summary>A system of the print, cut from its page by the screens (pdf.js), as PNG: up to 2,576 px wide, Claude's largest image unshrunk.</summary>
public sealed record SystemImage(int Page, int Index, byte[] Png);

/// <summary>Engraves MEI as a PNG of about the given width, for comparing with the print: Verovio, in the web view.</summary>
public interface IEngraver
{
    Task<byte[]> EngraveAsync(string mei, int widthPx, CancellationToken ct);
}

/// <summary>What reading a PDF should take, before starting: a rough cost in US dollars, and time.</summary>
public sealed record ReadingEstimate(int Pages, int Systems, decimal LowUsd, decimal HighUsd, TimeSpan About);

public enum SystemStep
{
    Waiting,
    Reading,
    Checking,
    Comparing,
    Done,
    Doubtful,
    Failed,
}

/// <summary>Where a system has got to, with a note for the screen: what went wrong, or what the comparison changed.</summary>
public sealed record SystemStatus(int Page, int Index, SystemStep Step, string Note);

public sealed record ReadingProgress(IReadOnlyList<SystemStatus> Systems, decimal CostSoFar, TimeSpan Elapsed);

/// <summary>A bar Claude still isn't sure of after comparing: where it is on the page and in the piece, its staff, and what's unclear.</summary>
public sealed record Doubt(int Page, int System, int Bar, int Staff, string Note);

/// <summary>
/// A PDF read into notation. <see cref="Ok"/> when every system was read, some bars perhaps still in doubt. A system that
/// couldn't be read is left out of the score and listed as failed, to read again. <see cref="Mei"/> is the notation to keep,
/// for the Library's MEI import.
/// </summary>
public sealed record ReadingResult(bool Ok, string Summary, Score? Score, string Mei, IReadOnlyList<Doubt> Doubts, IReadOnlyList<SystemStatus> Systems,
    decimal Cost, TimeSpan Took)
{
    /// <summary>Each system's reading after its comparison, in the order of <see cref="Systems"/>; null where it couldn't be read.</summary>
    public IReadOnlyList<SystemReading?> Readings { get; init; } = [];
}

/// <summary>
/// Reads a PDF into notation, a system at a time. The first system goes alone, since its time signature is printed only there.
/// The rest go a few at a time, each read, checked and engraved. Claude then compares each engraving with its print, side by
/// side, and reads again the bars that differ. A missing key, used-up credit, a spend limit, no connection, or Claude too busy
/// stops the rest.
/// </summary>
public sealed class PdfToNotation(IClaude claude, TimeProvider? clock = null)
{
    private const int AtOnce = 4;

    // Rough per-system costs at Opus 5.5's prices, reading and comparing: until the log has real ones.
    private const decimal LowPerSystem = 0.15m;
    private const decimal HighPerSystem = 0.60m;

    private static readonly AnswerKind[] Stopping = [AnswerKind.NoKey, AnswerKind.KeyRejected, AnswerKind.OutOfCredit, AnswerKind.SpendLimit, AnswerKind.Offline, AnswerKind.Busy];

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public ReadingEstimate Estimate(int pages, int systems)
    {
        // The first system is read alone, for its time signature, then the rest four at a time: about 2.5 minutes a round.
        var rounds = systems == 0 ? 0 : 1 + Math.Ceiling((systems - 1) / (double)AtOnce);
        return new(pages, systems, systems * LowPerSystem, systems * HighPerSystem, TimeSpan.FromMinutes(rounds * 2.5));
    }

    public async Task<ReadingResult> ReadAsync(IReadOnlyList<SystemImage> systems, IEngraver engraver, IProgress<ReadingProgress>? progress = null, string title = "",
        CancellationToken ct = default)
    {
        var images = systems.OrderBy(s => s.Page).ThenBy(s => s.Index).ToList();
        if (images.Count == 0) throw new ArgumentException("There are no systems to read.", nameof(systems));
        var run = new Run([.. images.Select(s => new SystemStatus(s.Page, s.Index, SystemStep.Waiting, ""))], new SystemReading?[images.Count], 0, progress, time);
        run.Report();
        await ReadOneAsync(run, images[0], 0, 0, 0, engraver, ct);
        var meter = run.Readings[0]?.Bars[^1] is { } last ? (last.MeterCount, last.MeterUnit) : (0, 0);
        await Parallel.ForEachAsync(Enumerable.Range(1, images.Count - 1), new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = ct },
            async (i, token) => await ReadOneAsync(run, images[i], i, meter.Item1, meter.Item2, engraver, token));
        return run.Finish(title);
    }

    /// <summary>Reads one system of an earlier result again, from its image, and builds the notation anew.</summary>
    public async Task<ReadingResult> RereadAsync(ReadingResult previous, SystemImage system, IEngraver engraver, IProgress<ReadingProgress>? progress = null,
        string title = "", CancellationToken ct = default)
    {
        var at = previous.Systems.ToList().FindIndex(s => s.Page == system.Page && s.Index == system.Index);
        if (at < 0) throw new ArgumentException($"The result has no system {system.Index} on page {system.Page}.", nameof(system));
        var readings = previous.Readings.Count == previous.Systems.Count ? previous.Readings.ToArray() : new SystemReading?[previous.Systems.Count];
        var run = new Run([.. previous.Systems], readings, previous.Cost, progress, time);
        var before = readings.Take(at).LastOrDefault(r => r is not null)?.Bars[^1];
        await ReadOneAsync(run, system, at, before?.MeterCount ?? 0, before?.MeterUnit ?? 0, engraver, ct);
        return run.Finish(title);
    }

    /// <summary>Reads a system, engraves its reading, has Claude compare the two, and keeps the reading with its corrections.</summary>
    private async Task ReadOneAsync(Run run, SystemImage image, int at, int meterCount, int meterUnit, IEngraver engraver, CancellationToken ct)
    {
        if (run.Stopped is { } why)
        {
            run.Set(at, SystemStep.Failed, why);
            return;
        }

        run.Set(at, SystemStep.Reading, "");
        var first = at == 0;
        var read = await claude.AskAsync(new ReadSystem(image.Png), new SystemToRead(image.Page, image.Index, first, meterCount, meterUnit), ct);
        run.Spend(read.Usage.Cost);
        if (!read.Ok)
        {
            if (Stopping.Contains(read.Kind)) run.Stop(read.Summary);
            run.Set(at, SystemStep.Failed, read.Summary);
            return;
        }

        var reading = read.Value;
        run.Set(at, SystemStep.Checking, "");
        byte[] engraving;
        try
        {
            var mei = MeiWriter.Write(ReadingScore.Build([reading]).Score);
            run.Set(at, SystemStep.Comparing, "");
            // The print's width, from its PNG header, so the engraving comes out at about the same size.
            var width = image.Png.Length > 24 ? BinaryPrimitives.ReadInt32BigEndian(image.Png.AsSpan(16, 4)) : 2400;
            engraving = await engraver.EngraveAsync(mei, width, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            run.Keep(at, reading, SystemStep.Doubtful, $"Read, but not compared: it couldn't be engraved ({e.Message}).");
            return;
        }

        var compare = await claude.AskAsync(new CompareSystem(image.Png, engraving), new SystemToCompare(image.Page, image.Index, first, reading), ct);
        run.Spend(compare.Usage.Cost);
        if (!compare.Ok)
        {
            if (Stopping.Contains(compare.Kind)) run.Stop(compare.Summary);
            run.Keep(at, reading, SystemStep.Doubtful, $"Read, but not compared: {compare.Summary}");
            return;
        }

        var corrections = compare.Value.Corrected;
        var corrected = reading with { Bars = [.. reading.Bars.Select(b => corrections.FirstOrDefault(c => c.Index == b.Index) ?? b)], Doubts = compare.Value.Doubts };
        var note = corrections.Count switch { 0 => "", 1 => "1 bar corrected.", _ => $"{corrections.Count} bars corrected." };
        run.Keep(at, corrected, corrected.Doubts.Count > 0 ? SystemStep.Doubtful : SystemStep.Done, note);
    }

    /// <summary>A reading's systems, where each has got to, and what it has cost, shared by the systems read at once.</summary>
    private sealed class Run(SystemStatus[] statuses, SystemReading?[] readings, decimal cost, IProgress<ReadingProgress>? progress, TimeProvider time)
    {
        private readonly Lock gate = new();
        private readonly long started = time.GetTimestamp();

        public SystemReading?[] Readings => readings;

        public string? Stopped { get; private set; }

        public void Report()
        {
            lock (gate) progress?.Report(new ReadingProgress([.. statuses], cost, time.GetElapsedTime(started)));
        }

        public void Set(int at, SystemStep step, string note)
        {
            lock (gate) statuses[at] = statuses[at] with { Step = step, Note = note };
            Report();
        }

        public void Keep(int at, SystemReading reading, SystemStep step, string note)
        {
            lock (gate) readings[at] = reading;
            Set(at, step, note);
        }

        public void Spend(decimal more)
        {
            lock (gate) cost += more;
        }

        public void Stop(string why)
        {
            lock (gate) Stopped ??= why;
        }

        /// <summary>The notation from every system read, its doubts by bar, and a sentence on how it went.</summary>
        public ReadingResult Finish(string title)
        {
            lock (gate)
            {
                var read = Enumerable.Range(0, readings.Length).Where(i => readings[i] is not null).ToList();
                Score? score = null;
                var mei = "";
                var doubts = new List<Doubt>();
                if (read.Count > 0)
                {
                    (score, var firstBars) = ReadingScore.Build([.. read.Select(i => readings[i]!)], title);
                    mei = MeiWriter.Write(score);
                    for (var k = 0; k < read.Count; k++)
                    {
                        var status = statuses[read[k]];
                        doubts.AddRange(readings[read[k]]!.Doubts.Select(d => new Doubt(status.Page, status.Index, firstBars[k] + d.Bar - 1, d.Staff, d.Note)));
                    }
                }

                var failed = statuses.Where(s => s.Step == SystemStep.Failed).ToList();
                var bars = score?.Bars.Count ?? 0;
                var doubtful = doubts.Select(d => d.Bar).Distinct().Count();
                var summary = Stopped ?? (failed.Count > 0
                    ? $"{failed.Count} of {statuses.Length} systems couldn't be read. Page {failed[0].Page}, system {failed[0].Index}: {failed[0].Note}"
                    : $"Read {bars} bars from {statuses.Length} systems.{(doubtful switch { 0 => "", 1 => " 1 bar to check.", _ => $" {doubtful} bars to check." })}");
                return new ReadingResult(failed.Count == 0 && Stopped is null, summary, score, mei, doubts, [.. statuses], cost, time.GetElapsedTime(started)) { Readings = [.. readings] };
            }
        }
    }
}
