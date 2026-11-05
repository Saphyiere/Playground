using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Playground.Kit;
using Prima.UI.Looks;
using Prima.UI.Storage;

namespace Prima.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Prima's storage, look and way back. The host decides where the database file lives, and registers its own
    /// <see cref="LookSetting"/> first if it has parts of its own to dress for the look.
    /// </summary>
    public static IServiceCollection AddPrima(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new PrimaStore(databasePath));
        services.AddSingleton<ISettingStore>(sp => sp.GetRequiredService<PrimaStore>());
        services.TryAddSingleton<LookSetting>();
        services.AddScoped<AppNavigation>();
        return services;
    }
}
