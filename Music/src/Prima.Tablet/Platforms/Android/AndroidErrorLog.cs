using Microsoft.Extensions.Logging;

namespace Prima.Tablet;

/// <summary>Errors from Prima's screens in Android's device log, including installed Release builds.</summary>
public sealed class AndroidErrorLog : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ErrorLogger(categoryName);

    public void Dispose() { }

    private sealed class ErrorLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error && logLevel != LogLevel.None &&
            (category.StartsWith("Prima.UI.", StringComparison.Ordinal) || category.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            Android.Util.Log.Error("Prima", $"{category}: {formatter(state, exception)}\n{exception}");
        }
    }
}
