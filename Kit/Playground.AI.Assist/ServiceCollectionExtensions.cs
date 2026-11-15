using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Playground.AI.Assist;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IClaude"/> and aliases it as <see cref="IInferenceProvider"/>. The host registers its <see cref="IAssistKeyStore"/> (the key, in the device's secure
    /// storage), and may register an <see cref="IAssistLog"/> to keep its calls; without one, they aren't kept. It may register an
    /// <see cref="IAssistKeepAlive"/> too, before or after this, to keep the app going while Claude answers (on Android,
    /// AddAndroidAskKeepAlive from Playground.AI.Assist.Android); without one, nothing is.
    /// </summary>
    public static IServiceCollection AddAiAssist(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAssistLog>(NoAssistLog.Instance);
        services.TryAddSingleton<IAssistKeepAlive>(NoKeepAlive.Instance);
        services.TryAddSingleton<IClaude>(provider => new Claude(provider.GetRequiredService<IAssistKeyStore>(),
            provider.GetRequiredService<IAssistLog>(), provider.GetRequiredService<TimeProvider>(),
            keepAlive: provider.GetRequiredService<IAssistKeepAlive>()));
        services.TryAddSingleton<IInferenceProvider>(provider => provider.GetRequiredService<IClaude>());
        return services;
    }
}
