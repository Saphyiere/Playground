using Microsoft.Data.Sqlite;

namespace Playground.Sync;

/// <summary>
/// SQLite databases, for copies: a snapshot taken while the app has the database open, with SQLite's online backup (never a copy
/// of the file, which could catch a write half done), each table's rows, and whether a database opens cleanly. Every connection here
/// is its own and closed after, so nothing is left holding the file.
/// </summary>
internal static class SqliteFiles
{
    private static readonly byte[] Header = "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// Copies <paramref name="database"/> to <paramref name="snapshot"/>, whole and consistent, whatever the app is doing with it. A
    /// write the app is committing holds the file for a moment: the backup waits for it (SQLite's busy timeout, on the source's
    /// connection, which the backup's read takes its lock through), and tries again a few times if the app keeps writing.
    /// </summary>
    public static void Snapshot(string database, string snapshot)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var source = Open(database, SqliteOpenMode.ReadOnly);
                using (var wait = source.CreateCommand())
                {
                    wait.CommandText = "PRAGMA busy_timeout = 10000";
                    wait.ExecuteNonQuery();
                }
                using var target = Open(snapshot, SqliteOpenMode.ReadWriteCreate);
                source.BackupDatabase(target);
                return;
            }
            catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6 && attempt < 3)
            {
                // SQLITE_BUSY or SQLITE_LOCKED: the app is still writing. Again, from the start.
                if (File.Exists(snapshot)) File.Delete(snapshot);
            }
        }
    }

    /// <summary>Each table (SQLite's own aside) with how many rows it has.</summary>
    public static Dictionary<string, long> Tables(string database)
    {
        using var connection = Open(database, SqliteOpenMode.ReadOnly);
        var names = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' ORDER BY name";
            using var reader = list.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
        }

        var tables = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            tables[name] = (long)count.ExecuteScalar()!;
        }
        return tables;
    }

    /// <summary>What's wrong with a database, in SQLite's words, or null when it opens and its pages check out.</summary>
    public static string? Problem(string database)
    {
        try
        {
            using var connection = Open(database, SqliteOpenMode.ReadOnly);
            using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check";
            var said = check.ExecuteScalar() as string;
            return said == "ok" ? null : said ?? "it didn't answer its check";
        }
        catch (SqliteException e)
        {
            return e.Message;
        }
    }

    /// <summary>Whether a file starts the way every SQLite database does.</summary>
    public static bool IsDatabase(ReadOnlySpan<byte> start) => start.StartsWith(Header);

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
