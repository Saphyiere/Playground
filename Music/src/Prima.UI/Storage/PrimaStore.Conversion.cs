using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Playground.AI.Assist;
using Prima.Core.Transcription;

namespace Prima.UI.Storage;

public sealed partial class PrimaStore
{
    private const string ConversionSchema = """
        CREATE TABLE IF NOT EXISTS ConversionSource (
            Piece TEXT NOT NULL REFERENCES Piece (Id) ON DELETE CASCADE,
            Revision TEXT NOT NULL,
            SourceHash TEXT NOT NULL,
            Saved TEXT NOT NULL,
            Data TEXT NOT NULL,
            PRIMARY KEY (Piece, Revision)
        );
        CREATE TABLE IF NOT EXISTS ConversionJob (
            Id TEXT PRIMARY KEY,
            Piece TEXT NOT NULL REFERENCES Piece (Id) ON DELETE CASCADE,
            Revision INTEGER NOT NULL,
            State INTEGER NOT NULL,
            Updated TEXT NOT NULL,
            Data TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS OnePendingConversionPerPiece ON ConversionJob (Piece) WHERE State IN (0, 1, 2, 3);
        """;

    public async Task<string> SaveConversionArtifactAsync(byte[] data, CancellationToken ct = default)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(data));
        var folder = Path.ChangeExtension(databasePath, ".conversion");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, hash + ".bin");
        if (File.Exists(path)) return hash;
        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(data, ct);
                stream.Flush(true);
            }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { }
            return hash;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<byte[]> GetConversionArtifactAsync(string hash, CancellationToken ct = default)
    {
        if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid artifact hash.", nameof(hash));
        var path = Path.Combine(Path.ChangeExtension(databasePath, ".conversion"), hash.ToLowerInvariant() + ".bin");
        var data = await File.ReadAllBytesAsync(path, ct);
        if (!Convert.ToHexStringLower(SHA256.HashData(data)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The saved conversion artifact is damaged.");
        return data;
    }

    public Task<ConversionJob?> GetConversionJobAsync(string id) => RunAsync(async c =>
    {
        var data = await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM ConversionJob WHERE Id = @id", new { id });
        return data is null ? null : ReadJob(data);
    });

    public Task<ConversionJob?> GetLatestConversionJobAsync(string piece) => RunAsync(async c =>
    {
        var data = await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM ConversionJob WHERE Piece = @piece ORDER BY Updated DESC, rowid DESC LIMIT 1", new { piece });
        return data is null ? null : ReadJob(data);
    });

    public Task<List<ConversionJob>> GetRecentConversionJobsAsync() => RunAsync(async c =>
    {
        var rows = await c.QueryAsync<string>("""
            SELECT Data FROM (
                SELECT Data, Updated, rowid AS JobOrder,
                    ROW_NUMBER() OVER (PARTITION BY Piece ORDER BY Updated DESC, rowid DESC) AS Latest
                FROM ConversionJob
            ) WHERE Latest = 1 ORDER BY Updated DESC, JobOrder DESC LIMIT 20
            """);
        return rows.Select(ReadJob).ToList();
    });

    public Task<ConversionJob> CreateConversionJobAsync(ConversionJob job) => RunAsync(async c =>
    {
        var existing = await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM ConversionJob WHERE Piece = @Piece AND State IN (0, 1, 2, 3)", new { job.Piece });
        if (existing is not null) return ReadJob(existing);
        if (job.Version != 1 || job.Revision != 0 || job.State != ConversionState.Queued) throw new ArgumentException("Invalid initial conversion state.", nameof(job));
        await c.ExecuteAsync("INSERT INTO ConversionJob (Id, Piece, Revision, State, Updated, Data) VALUES (@Id, @Piece, @Revision, @state, @updated, @data)",
            new { job.Id, job.Piece, job.Revision, state = (int)job.State, updated = Iso(job.Updated), data = JsonSerializer.Serialize(job, AssistSchema.Json) });
        return job;
    });

    /// <summary>Revision fencing prevents late worker results from overwriting cancellation or a newer edit.</summary>
    public Task<bool> SaveConversionJobAsync(ConversionJob job, int expectedRevision) => RunAsync(async c =>
    {
        if (job.Version != 1 || job.Revision != expectedRevision + 1 || job.State == ConversionState.Cancelled) throw new ArgumentException("Invalid conversion commit.", nameof(job));
        return await c.ExecuteAsync("UPDATE ConversionJob SET Revision = @Revision, State = @state, Updated = @updated, Data = @data WHERE Id = @Id AND Revision = @expectedRevision AND State <> @cancelled",
            new { job.Id, job.Revision, state = (int)job.State, updated = Iso(job.Updated), data = JsonSerializer.Serialize(job, AssistSchema.Json), expectedRevision, cancelled = (int)ConversionState.Cancelled }) == 1;
    });

    public Task<ConversionJob?> CancelConversionJobAsync(string id) => StopConversionJobAsync(id, ConversionState.Cancelled, null);

    public Task<ConversionJob?> InterruptConversionJobAsync(string id, string reason) => StopConversionJobAsync(id, ConversionState.Interrupted, reason);

    private Task<ConversionJob?> StopConversionJobAsync(string id, ConversionState state, string? reason) => RunAsync(async c =>
    {
        while (true)
        {
            var data = await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM ConversionJob WHERE Id = @id", new { id });
            if (data is null) return null;
            var job = ReadJob(data);
            if (job.State is ConversionState.Cancelled or ConversionState.Ready) return job;
            var expected = job.Revision;
            job = job with
            {
                State = state, Revision = expected + 1, Updated = DateTimeOffset.UtcNow, Problem = reason ?? job.Problem,
                Attempts = job.Attempts.Select(a => a.State == ConversionAttemptState.Dispatched ? a with { State = ConversionAttemptState.Uncertain } : a).ToArray()
            };
            var changed = await c.ExecuteAsync("""
                UPDATE ConversionJob SET Revision = @Revision, State = @state, Updated = @updated, Data = @data
                WHERE Id = @Id AND Revision = @expected AND State NOT IN (@cancelled, @ready)
                """, new { job.Id, job.Revision, state = (int)job.State, updated = Iso(job.Updated), data = JsonSerializer.Serialize(job, AssistSchema.Json),
                    expected, cancelled = (int)ConversionState.Cancelled, ready = (int)ConversionState.Ready });
            if (changed == 1) return job;
        }
    });

    public Task RecoverConversionJobsAsync() => RunAsync(async c =>
    {
        var saved = await c.QueryAsync<string>("SELECT Data FROM ConversionJob WHERE State IN (0, 1)");
        foreach (var data in saved)
        {
            var job = ReadJob(data);
            job = job with
            {
                State = ConversionState.Interrupted, Revision = job.Revision + 1, Updated = DateTimeOffset.UtcNow,
                Problem = "The previous execution stopped. Completed stages are saved; resume when ready.",
                Attempts = job.Attempts.Select(a => a.State == ConversionAttemptState.Dispatched ? a with { State = ConversionAttemptState.Uncertain } : a).ToArray()
            };
            await c.ExecuteAsync("UPDATE ConversionJob SET Revision = @Revision, State = @state, Updated = @updated, Data = @data WHERE Id = @Id",
                new { job.Id, job.Revision, state = (int)job.State, updated = Iso(job.Updated), data = JsonSerializer.Serialize(job, AssistSchema.Json) });
        }
    });

    private static ConversionJob ReadJob(string data)
    {
        var job = JsonSerializer.Deserialize<ConversionJob>(data, AssistSchema.Json) ?? throw new InvalidDataException("Missing conversion state.");
        if (job.Version != 1) throw new InvalidDataException($"Unsupported conversion state version {job.Version}.");
        return job;
    }

    /// <summary>Retains each confirmed segmentation so an old result cannot silently attach to newly moved bar boundaries.</summary>
    public Task SaveConversionSourceAsync(string piece, ConversionSource source)
    {
        var problems = source.Problems();
        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(source));
        var data = JsonSerializer.Serialize(source, AssistSchema.Json);
        return RunAsync(async c =>
        {
            var existing = await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM ConversionSource WHERE Piece = @piece AND Revision = @Revision", new { piece, source.Revision });
            if (existing is not null)
            {
                if (existing != data) throw new InvalidOperationException("A saved source revision is immutable; changed boundaries need a new revision.");
                return;
            }
            await c.ExecuteAsync("INSERT INTO ConversionSource (Piece, Revision, SourceHash, Saved, Data) VALUES (@piece, @Revision, @Sha256, @saved, @data)",
                new { piece, source.Revision, source.Sha256, saved = Iso(DateTimeOffset.UtcNow), data });
        });
    }

    /// <summary>The latest confirmed map for these exact PDF bytes, or null before the owner confirms their bar boundaries.</summary>
    public Task<ConversionSource?> GetConversionSourceAsync(string piece, string sourceHash) => RunAsync(async c =>
    {
        var data = await c.QuerySingleOrDefaultAsync<string>(
            "SELECT Data FROM ConversionSource WHERE Piece = @piece AND SourceHash = @sourceHash ORDER BY Saved DESC, rowid DESC LIMIT 1", new { piece, sourceHash });
        return data is null ? null : JsonSerializer.Deserialize<ConversionSource>(data, AssistSchema.Json);
    });
}
