using Microsoft.Extensions.Logging;
using Prima.UI.Routines;

namespace Prima.UI.Host;

public interface IRoutineHost
{
    Task StartAsync(string jobId);
}

/// <summary>A desktop/preview lifetime; Android replaces it with a foreground service.</summary>
public sealed class LocalRoutineHost(RoutineService jobs, ILogger<LocalRoutineHost> logger) : IRoutineHost
{
    public Task StartAsync(string jobId)
    {
        _ = Task.Run(async () =>
        {
            try { await jobs.RunAsync(jobId); }
            catch (Exception error) { logger.LogError(error, "Routine generation {JobId} stopped.", jobId); }
        });
        return Task.CompletedTask;
    }
}
