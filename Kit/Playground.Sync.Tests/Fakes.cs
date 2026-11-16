using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Playground.Updates;

namespace Playground.Sync.Tests;

/// <summary>A folder of a test's own, deleted after.</summary>
sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "playground-sync-tests", Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string this[string name] => System.IO.Path.Combine(Path, name.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// An app's data on a device, as Prima keeps it: a database of pieces and runs, a library folder of imported files (one in a folder
/// of its own), a problem log, and a key file waiting to be taken in that's never copied. Its syncer works in a folder of its own.
/// </summary>
sealed class App : IDisposable
{
    public const string Key = "sk-ant-api03-ThisIsATestKeyThatMustNeverBeCopied-0123456789";

    private readonly TempFolder data = new();
    private readonly TempFolder work = new();

    public App(bool empty = false)
    {
        Directory.CreateDirectory(Library);
        Execute("CREATE TABLE IF NOT EXISTS Piece (Id TEXT PRIMARY KEY, Title TEXT NOT NULL); CREATE TABLE IF NOT EXISTS Attempt (Id INTEGER PRIMARY KEY, Piece TEXT, Notes TEXT)");
        if (empty) return;

        Execute("INSERT INTO Piece VALUES ('minuet', 'Minuet in G'), ('aria', 'Aria')");
        for (var i = 0; i < 40; i++) Execute($"INSERT INTO Attempt (Piece, Notes) VALUES ('minuet', '{new string('n', 200)}')");
        Write("library/minuet.pdf", Bytes(30_000, 1));
        Write("library/minuet.mid", Bytes(2_000, 2));
        Write("library/collection/aria.pdf", Bytes(50_000, 3));
        File.WriteAllText(Log, "=== 2026-11-15 10:00:00 +00:00 · Warning · Prima.Piece\nA page took long.\n\n");
        File.WriteAllText(KeyFile, Key);
    }

    public string Folder => data.Path;

    public string Database => data["prima.db"];

    public string Library => data["library"];

    public string Log => data["errors.log"];

    /// <summary>Where a key file sits before it's taken in: beside the data, as Prima's preview keeps it.</summary>
    public string KeyFile => data["claude-key.txt"];

    public string WorkFolder => work.Path;

    public SyncOptions Options(string version = "1.0.5") => new("Prima")
    {
        Items =
        [
            SyncItem.Database("prima.db", Database) with { RequiredTables = ["Piece", "Attempt"] },
            SyncItem.Folder("library", Library),
            SyncItem.File("errors.log", Log),
        ],
        Exclude = ["claude-key.txt"],
        Secrets = _ => Task.FromResult<IReadOnlyCollection<string>>([Key]),
        Version = version,
        WorkFolder = work.Path,
        StartDelay = TimeSpan.Zero,
        Describe = m => $"{m.Rows("prima.db", "Attempt") ?? 0} runs",
    };

    public Syncer Syncer(ISyncStore store, SyncOptions? options = null, TimeProvider? time = null) => new(options ?? Options(), [store], time);

    public void Write(string path, byte[] bytes)
    {
        var full = data[path];
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    public string this[string path] => data[path];

    public void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public long Count(string table) => Count(Database, table);

    public static long Count(string database, string table)
    {
        using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Every file under the data folder (the database's own journal aside), by its path there, with its bytes.</summary>
    public Dictionary<string, byte[]> Files() => Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)
        .ToDictionary(f => System.IO.Path.GetRelativePath(Folder, f).Replace('\\', '/'), File.ReadAllBytes);

    public static byte[] Bytes(int count, int seed)
    {
        var bytes = new byte[count];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public void Dispose()
    {
        data.Dispose();
        work.Dispose();
    }
}

/// <summary>A store that keeps each thing asked of it, and can stop at a given write, or do something just before one.</summary>
sealed class WatchedStore(ISyncStore inner) : ISyncStore
{
    public List<string> Writes { get; } = [];

    public List<string> Deletes { get; } = [];

    /// <summary>Throws instead of making this write (1 for the first).</summary>
    public int? FailAt { get; set; }

    /// <summary>Runs before each write, with its key.</summary>
    public Action<string>? BeforeWrite { get; set; }

    public bool Ready { get; set; } = true;

    public string Name => inner.Name;

    public bool SurvivesUninstall => inner.SurvivesUninstall;

    public Task<bool> ConnectAsync(CancellationToken ct) => Task.FromResult(Ready);

    public Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, CancellationToken ct) => inner.ListAsync(prefix, ct);

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);

    public Task WriteAsync(string key, Stream content, CancellationToken ct)
    {
        BeforeWrite?.Invoke(key);
        if (FailAt == Writes.Count + 1) throw new IOException("the store went away");
        Writes.Add(key);
        return inner.WriteAsync(key, content, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        Deletes.Add(key);
        return inner.DeleteAsync(key, ct);
    }

    public void Clear()
    {
        Writes.Clear();
        Deletes.Clear();
    }
}

/// <summary>What the Syncer logged, each line as it was written.</summary>
sealed class Logs : Microsoft.Extensions.Logging.ILogger<Syncer>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines) Lines.Add(formatter(state, exception));
    }
}

/// <summary>The same as <see cref="TimeProvider.System"/>, but for now, which a test sets.</summary>
sealed class Clock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Stands in for GitHub: one release of Prima, its file served at its address.</summary>
sealed class FakeGitHub : HttpMessageHandler
{
    public static readonly byte[] Apk = App.Bytes(100_000, 9);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        const string Url = "https://github.com/saphyiere/playground/releases/download/prima-1.0.7/prima.apk";
        if (request.RequestUri!.Host != "api.github.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Apk) });
        object[] releases =
        [
            new
            {
                tag_name = "prima-1.0.7",
                name = "Prima 1.0.7",
                body = "- Prima: something new",
                draft = false,
                prerelease = false,
                published_at = "2026-11-14T10:00:00Z",
                assets = new[] { new { name = "prima.apk", size = Apk.Length, browser_download_url = Url, digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Apk)) } },
            },
        ];
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases), Encoding.UTF8, "application/json") });
    }
}

/// <summary>Stands in for the device's installer: keeps the order of what's asked of it.</summary>
sealed class FakeInstaller(List<string> calls) : IInstaller
{
    public string InstalledVersion => "1.0.5";

    public string DownloadFolder { get; } = Path.Combine(Path.GetTempPath(), "playground-sync-tests", "downloads-" + Guid.NewGuid().ToString("N"));

    public event Action<string>? Stopped
    {
        add { }
        remove { }
    }

    public Task<bool> SignedLikeInstalledAsync(string file) => Task.FromResult(true);

    public Task<string> KeepCopiesAsync(IReadOnlyList<string> files, string folder, CancellationToken ct)
    {
        calls.Add("data files copied");
        return Task.FromResult(folder);
    }

    public Task<InstallOutcome> InstallAsync(string file, CancellationToken ct)
    {
        calls.Add("installed");
        return Task.FromResult(new InstallOutcome(InstallResult.Handed, ""));
    }
}
