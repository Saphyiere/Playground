using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Playground.Updates;

namespace Playground.Sync;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The app's <see cref="Syncer"/>, from what it says it keeps (<paramref name="options"/>, made once from the app's services), and
    /// the safeguard its updates take a full copy through first. Copies go to each <see cref="ISyncStore"/> the host registers, the
    /// first being the app's own (on Android, AddAndroidSync from Playground.Sync.Android); without one, to a folder in
    /// <see cref="SyncOptions.WorkFolder"/>, which survives nothing. The host registers an <see cref="ISyncPicker"/> to restore from
    /// a folder or file the person picks; without one, only the app's own copies can be restored.
    /// </summary>
    public static IServiceCollection AddSync(this IServiceCollection services, Func<IServiceProvider, SyncOptions> options)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISyncPicker, NoPicker>();
        services.TryAddSingleton(options);
        services.TryAddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<SyncOptions>();
            List<ISyncStore> stores = [.. provider.GetServices<ISyncStore>()];
            if (stores.Count == 0) stores.Add(new FolderStore(Path.Combine(settings.WorkFolder, "copies"), "a temporary folder", survivesUninstall: false));
            return new Syncer(settings, stores, provider.GetRequiredService<TimeProvider>(), provider.GetService<ILogger<Syncer>>());
        });
        services.TryAddSingleton<IUpdateSafeguard>(provider => new SyncSafeguard(provider.GetRequiredService<Syncer>()));
        return services;
    }
}

/// <summary>An app's updates kept safe by its copies: a full one just before an update installs, checked the first time the app starts after.</summary>
internal sealed class SyncSafeguard(Syncer syncer) : IUpdateSafeguard
{
    public async Task<string> KeepBeforeUpdateAsync(Release release, IProgress<double>? progress, CancellationToken ct)
    {
        await syncer.CopyAsync(SyncReason.BeforeUpdate, release.Title, progress, ct);
        return syncer.Store.Name;
    }
}
