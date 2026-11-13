using System.Text.Json;
using Dapper;
using Playground.AI.Assist;
using Prima.Core.Transcription;

namespace Prima.UI.Storage;

public sealed partial class PrimaStore
{
    private const string NotationReviewSchema = """
        CREATE TABLE IF NOT EXISTS NotationReviewSession (
            Piece TEXT NOT NULL REFERENCES Piece (Id) ON DELETE CASCADE,
            ReviewId TEXT NOT NULL,
            SourceRevision TEXT NOT NULL,
            Version INTEGER NOT NULL,
            Revision TEXT NOT NULL,
            Data TEXT NOT NULL,
            PRIMARY KEY (Piece, ReviewId)
        );
        """;

    public Task<NotationReview?> GetNotationReviewAsync(string piece, string reviewId) => RunAsync(async c =>
    {
        var row = await c.QuerySingleOrDefaultAsync<(int Version, string Data)?>(
            "SELECT Version, Data FROM NotationReviewSession WHERE Piece = @piece AND ReviewId = @reviewId", new { piece, reviewId });
        if (row is null) return null;
        if (row.Value.Version != 1) throw new InvalidDataException($"Unsupported notation review version {row.Value.Version}.");
        return NotationCorrections.Restore(JsonSerializer.Deserialize<NotationReview>(row.Value.Data, AssistSchema.Json) ?? throw new InvalidDataException("Missing notation review."));
    });

    /// <summary>Creates a review or atomically commits against the exact prior head. A late model patch cannot overwrite an edit.</summary>
    public Task<bool> SaveNotationReviewAsync(string piece, NotationReview review, string? expectedRevision, string? reviewId = null)
    {
        var frozen = NotationCorrections.Restore(review);
        var args = new { piece, reviewId = reviewId ?? frozen.Source.Revision, sourceRevision = frozen.Source.Revision, revision = frozen.Current.Revision, expectedRevision, data = JsonSerializer.Serialize(frozen, AssistSchema.Json) };
        return RunAsync(async c => expectedRevision is null
            ? await c.ExecuteAsync("INSERT OR IGNORE INTO NotationReviewSession (Piece, ReviewId, SourceRevision, Version, Revision, Data) VALUES (@piece, @reviewId, @sourceRevision, 1, @revision, @data)", args) == 1
            : await c.ExecuteAsync("UPDATE NotationReviewSession SET Revision = @revision, Data = @data WHERE Piece = @piece AND ReviewId = @reviewId AND SourceRevision = @sourceRevision AND Version = 1 AND Revision = @expectedRevision", args) == 1);
    }
}
