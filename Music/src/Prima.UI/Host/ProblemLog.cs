using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Prima.UI.Host;

/// <summary>
/// A problem kept: when, how bad (Warning, Error, Critical), what said it (a logger's category: the screen, the job), the line it
/// said, and the rest (the stack, the page's own words).
/// </summary>
public sealed record Problem(DateTimeOffset At, string Level, string Source, string Message, string Detail)
{
    /// <summary>What said it, without its namespace: "Piece", "ScreenErrorBoundary".</summary>
    public string Short => Source[(Source.LastIndexOf('.') + 1)..];
}

/// <summary>
/// What went wrong in Prima, kept in a text file to read and send on: a screen that stopped, the page that stopped answering, a
/// job that failed. Settings › Problems shows the newest. On the tablet it's errors.log in the folder a PC reaches over USB
/// (Android/data/com.saphyiere.prima/files), so it can be copied off even when Prima won't open. Past half a megabyte the file
/// becomes errors.old.log, and a new one starts. Keeping a problem never throws: a log that fails is no reason to fail more.
/// </summary>
public sealed class ProblemLog(string path, TimeProvider? time = null)
{
    public const string FileName = "errors.log";
    public const long Largest = 512 * 1024;
    private const string Mark = "=== ";
    private const string When = "yyyy-MM-dd HH:mm:ss zzz";
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly Lock gate = new();

    public string Path => path;

    /// <summary>The file before this one, kept when this one grew past <see cref="Largest"/>.</summary>
    public string OldPath => System.IO.Path.ChangeExtension(path, ".old.log");

    /// <summary>Raised once a problem is kept, on the thread that kept it.</summary>
    public event Action<Problem>? Kept;

    /// <summary>Keeps a problem: its first line is what's shown, the rest (and <paramref name="detail"/>) is kept under it.</summary>
    public Problem Keep(string level, string source, string message, string? detail = null, DateTimeOffset? at = null)
    {
        var lines = (message ?? "").Replace("\r", "").Trim().Split('\n');
        var rest = string.Join('\n', lines.Skip(1).Append(detail ?? "").Where(l => l.Length > 0));
        var problem = new Problem(at ?? clock.GetLocalNow(), level, source, lines[0].Trim(), rest.Replace("\r", "").TrimEnd());
        // A line that looks like the start of an entry, the message's own or one of the detail's, is set in, so the file reads back
        // the same.
        string SetIn(string line) => line.StartsWith(Mark, StringComparison.Ordinal) ? " " + line : line;
        var entry = new StringBuilder()
            .Append(Mark).Append(problem.At.ToString(When, CultureInfo.InvariantCulture)).Append(" · ").Append(level).Append(" · ").Append(source).Append('\n')
            .Append(SetIn(problem.Message)).Append('\n');
        foreach (var line in problem.Detail.Split('\n').Where(l => l.Length > 0)) entry.Append(SetIn(line)).Append('\n');
        entry.Append('\n');
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
                if (File.Exists(path) && new FileInfo(path).Length > Largest) File.Move(path, OldPath, overwrite: true);
                File.AppendAllText(path, entry.ToString());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        try { Kept?.Invoke(problem); }
        catch (Exception)
        {
        }
        return problem;
    }

    /// <summary>The newest problems kept, newest first: from this file, and the one before when this one has too few.</summary>
    public IReadOnlyList<Problem> Recent(int count = 20)
    {
        List<Problem> found = [];
        try
        {
            lock (gate)
            {
                foreach (var file in new[] { path, OldPath })
                {
                    if (found.Count >= count) break;
                    if (File.Exists(file)) found.AddRange(Read(File.ReadAllText(file)).Reverse());
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return [.. found.Take(count)];
    }

    /// <summary>Everything kept, both files, gone.</summary>
    public void Clear()
    {
        try
        {
            lock (gate)
            {
                File.Delete(path);
                File.Delete(OldPath);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static IEnumerable<Problem> Read(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(Mark, StringComparison.Ordinal)) continue;

            var head = lines[i][Mark.Length..].Split(" · ", 3);
            if (head.Length < 3 || !DateTimeOffset.TryParseExact(head[0], When, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
            var body = new List<string>();
            while (i + 1 < lines.Length && !lines[i + 1].StartsWith(Mark, StringComparison.Ordinal)) body.Add(lines[++i]);
            // A message is kept trimmed, so a space before it is only ever the one that set it in.
            var said = (body.FirstOrDefault() ?? "").TrimStart();
            yield return new Problem(at, head[1], head[2], said, string.Join('\n', body.Skip(1)).TrimEnd());
        }
    }
}

/// <summary>
/// Warnings and errors logged by Prima's own code (Prima.*, Playground.*) and by Blazor (Microsoft.AspNetCore.Components.*), kept
/// in the problem log. A message with an "At" value (a <see cref="DateTimeOffset"/>) is kept as of then: the page's own problems
/// are logged when Prima next starts, and were had before.
/// </summary>
public sealed class ProblemLogProvider(ProblemLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Writer(log, categoryName);

    public void Dispose()
    {
    }

    private sealed class Writer(ProblemLog log, string category) : ILogger
    {
        private readonly bool ours = category.StartsWith("Prima.", StringComparison.Ordinal) || category.StartsWith("Playground.", StringComparison.Ordinal) ||
            category.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => ours && logLevel is >= LogLevel.Warning and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var at = state is IEnumerable<KeyValuePair<string, object?>> values ? values.FirstOrDefault(v => v.Key == "At").Value as DateTimeOffset? : null;
            log.Keep(logLevel.ToString(), category, formatter(state, exception), exception?.ToString(), at);
        }
    }
}
