using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Playground.Updates;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The app's <see cref="Updater"/>, for its screens. The host registers the device's <see cref="IInstaller"/> too: on Android,
    /// AddAndroidUpdates from Playground.Updates.Android; a preview host, a stand-in.
    /// </summary>
    public static IServiceCollection AddUpdates(this IServiceCollection services, UpdateOptions options)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider => new Updater(options, provider.GetRequiredService<IInstaller>(), provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}
