using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Playground.Kit;
using Prima.Core.Timing;

namespace Prima.UI.Storage;

/// <summary>
/// Prima's data, in one SQLite file on the tablet. Every piece of work runs in turn on one lane, each on a background thread with
/// its own connection, so a read started after a write sees it.
/// </summary>
public sealed class PrimaStore(string databasePath) : ISettingStore
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Setting (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """;

    private const string OffsetKey = "Timing.OffsetMs";
    private const string SpreadKey = "Timing.SpreadMs";
    private const string CalibratedKey = "Timing.CalibratedOn";
    private const string ClickOnPianoKey = "Click.OnPiano";
    private const string ClickSoundKey = "Click.Sound";
    private const string ClickVolumeKey = "Click.Volume";
    private const string SendToPianoKey = "Keyboard.SendToPiano";
    private const string MusicFontKey = "Notation.Font";
    private const string SheetSizeKey = "Notation.SheetSize";

    private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString();
    private readonly Lock laneGate = new();
    private Task lane = Task.CompletedTask;
    private bool schemaReady;

    public Task<Dictionary<string, string>> GetSettingsAsync() => RunAsync(async c =>
        (await c.QueryAsync<(string Key, string Value)>("SELECT Key, Value FROM Setting")).ToDictionary(s => s.Key, s => s.Value));

    /// <summary>Saves a setting; null removes it.</summary>
    public Task SetSettingAsync(string key, string? value) => RunAsync(c => value is null
        ? c.ExecuteAsync("DELETE FROM Setting WHERE Key = @key", new { key })
        : c.ExecuteAsync("INSERT INTO Setting (Key, Value) VALUES (@key, @value) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value", new { key, value }));

    /// <summary>Reads the saved settings into <paramref name="settings"/>; any not saved keep their defaults.</summary>
    public async Task LoadAsync(PrimaSettings settings)
    {
        var saved = await GetSettingsAsync();
        double? Number(string key) => double.TryParse(saved.GetValueOrDefault(key), CultureInfo.InvariantCulture, out var n) ? n : null;
        settings.TimingOffsetMs = Number(OffsetKey) ?? 0;
        settings.TappingSpreadMs = Number(SpreadKey);
        settings.CalibratedOn = DateOnly.TryParse(saved.GetValueOrDefault(CalibratedKey), CultureInfo.InvariantCulture, out var day) ? day : null;
        settings.ClickOnPiano = saved.GetValueOrDefault(ClickOnPianoKey) == "true";
        settings.ClickSound = Enum.TryParse<ClickSoundKind>(saved.GetValueOrDefault(ClickSoundKey), out var sound) ? sound : settings.ClickSound;
        settings.ClickVolume = (int?)Number(ClickVolumeKey) ?? settings.ClickVolume;
        settings.SendToPiano = saved.GetValueOrDefault(SendToPianoKey) != "false";
        settings.MusicFont = saved.GetValueOrDefault(MusicFontKey) ?? settings.MusicFont;
        settings.SheetSize = (int?)Number(SheetSizeKey) ?? settings.SheetSize;
    }

    /// <summary>Saves every setting in <paramref name="settings"/>, as one piece of work.</summary>
    public Task SaveAsync(PrimaSettings settings) => RunAsync(async c =>
    {
        (string Key, string? Value)[] values =
        [
            (OffsetKey, settings.TimingOffsetMs.ToString("R", CultureInfo.InvariantCulture)),
            (SpreadKey, settings.TappingSpreadMs?.ToString("R", CultureInfo.InvariantCulture)),
            (CalibratedKey, settings.CalibratedOn?.ToString("O", CultureInfo.InvariantCulture)),
            (ClickOnPianoKey, settings.ClickOnPiano ? "true" : "false"),
            (ClickSoundKey, settings.ClickSound.ToString()),
            (ClickVolumeKey, settings.ClickVolume.ToString(CultureInfo.InvariantCulture)),
            (SendToPianoKey, settings.SendToPiano ? "true" : "false"),
            (MusicFontKey, settings.MusicFont),
            (SheetSizeKey, settings.SheetSize.ToString(CultureInfo.InvariantCulture)),
        ];
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        foreach (var (key, value) in values)
        {
            if (value is null) await c.ExecuteAsync("DELETE FROM Setting WHERE Key = @key", new { key }, transaction);
            else await c.ExecuteAsync("INSERT INTO Setting (Key, Value) VALUES (@key, @value) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value", new { key, value }, transaction);
        }
        await transaction.CommitAsync();
    });

    /// <summary>Queues work behind everything started before it and runs it on a background thread with its own connection.</summary>
    private Task<T> RunAsync<T>(Func<SqliteConnection, Task<T>> work)
    {
        lock (laneGate)
        {
            // ContinueWith runs whether the previous piece succeeded or failed, so one failure doesn't block the lane.
            var next = lane.ContinueWith(async _ =>
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                if (!schemaReady)
                {
                    await connection.ExecuteAsync(Schema);
                    schemaReady = true;
                }

                return await work(connection);
            }, CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
            lane = next;
            return next;
        }
    }

    private Task RunAsync(Func<SqliteConnection, Task> work) => RunAsync(async c => { await work(c); return true; });
}
