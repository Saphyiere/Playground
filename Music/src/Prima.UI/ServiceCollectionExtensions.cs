using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Playground.Kit;
using Prima.UI.Host;
using Prima.UI.Looks;
using Prima.UI.Storage;

namespace Prima.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Prima's storage, settings, look, click and way back. The host decides where the database file lives, and registers
    /// its own first: the piano (<see cref="IKeyboard"/>), the system clock (<see cref="IClock"/>), the speaker's click
    /// (<see cref="IClick"/>), keeping the screen on (<see cref="IScreen"/>), and a <see cref="LookSetting"/> if it has parts of
    /// its own to dress for the look. Without them there's no piano, a stopwatch clock, a silent click and no screen to keep on.
    /// </summary>
    public static IServiceCollection AddPrima(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new PrimaStore(databasePath));
        services.AddSingleton<ISettingStore>(sp => sp.GetRequiredService<PrimaStore>());
        services.AddSingleton(new PrimaSettings());
        services.TryAddSingleton<LookSetting>();
        services.TryAddSingleton<IClock, StopwatchClock>();
        services.TryAddSingleton<IKeyboard, NoKeyboard>();
        services.TryAddSingleton<IClick, GridClick>();
        services.TryAddSingleton<IScreen, NoScreen>();
        services.AddSingleton<Click>();
        services.AddScoped<AppNavigation>();
        return services;
    }
}
