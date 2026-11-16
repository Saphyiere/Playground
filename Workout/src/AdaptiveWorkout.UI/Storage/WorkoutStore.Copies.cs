using Dapper;
using Microsoft.Data.Sqlite;

namespace AdaptiveWorkout.UI.Storage;

// The store's side of the copies of its data (WorkoutSync, through the Kit's sync): what's queued written before a copy, whether a
// workout is open (a copy the app takes on its own waits until none is, and so does a restore), and the data file left alone while a
// restore puts a copy in its place, after which everything read from it before is read again.
public sealed partial class WorkoutStore
{
    private int restores;

    /// <summary>
    /// Raised once a workout is finished and what Finish saved is written, on the store's thread: a copy is taken then, when one is
    /// due (WorkoutSync).
    /// </summary>
    public event Action? WorkoutFinished;

    /// <summary>
    /// How many times a restore has put a copy in place of the data file since the app started: what was read from the store before
    /// the last one is out of date (WorkoutFlow reads it again).
    /// </summary>
    public int Restores => Volatile.Read(ref restores);

    /// <summary>Waits for every piece of work started so far, so the file has it all (before a copy).</summary>
    public Task FlushAsync() => RunAsync(_ => Task.CompletedTask);

    /// <summary>Whether a workout is open: started and not finished (or cancelled) yet.</summary>
    public Task<bool> HasOpenWorkoutAsync() => RunAsync(async c => await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM WorkoutSession WHERE FinishedAt IS NULL") > 0);

    /// <summary>Says a workout was just finished, once everything Finish saved before it is written (<see cref="WorkoutFinished"/>).</summary>
    internal Task FinishedAsync() => RunAsync(_ =>
    {
        WorkoutFinished?.Invoke();
        return Task.CompletedTask;
    });

    /// <summary>
    /// Runs <paramref name="work"/> with nothing of the app's holding the data file open, so a restore can put a copy's in its place
    /// (the Kit's sync does, WorkoutSync): its own place on the lane, after everything before it and before anything after, with the
    /// pooled connections closed. What comes after opens the file afresh, bringing a copy kept by an older version up to date, and
    /// what was read from it before is out of date (<see cref="Restores"/>).
    /// </summary>
    public async Task WhileClosedAsync(Func<Task> work)
    {
        Task closed;
        lock (laneGate)
        {
            closed = lane.ContinueWith(async _ =>
            {
                SqliteConnection.ClearAllPools();
                try
                {
                    await work();
                }
                finally
                {
                    schemaReady = false;
                    Interlocked.Increment(ref restores);
                }
            }, CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
            lane = closed;
        }
        await closed;
    }

    /// <summary>
    /// Whether the data file at <paramref name="path"/> has a workout open, read straight from it: for the moment it's held still for a
    /// restore, when the lane can't be asked. A file without workouts has none.
    /// </summary>
    internal static bool HasOpenWorkout(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            return connection.ExecuteScalar<long>("SELECT COUNT(*) FROM WorkoutSession WHERE FinishedAt IS NULL") > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }
}
