using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Playground.AI.Assist;
using Playground.Kit;
using Prima.Core.Transcription;
using Prima.UI.Host;
using Prima.UI.Looks;
using Prima.UI.Storage;

namespace Prima.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Prima's storage, settings, look, click and way back. The host decides where the database file lives, and registers
    /// its own first: the piano (<see cref="IKeyboard"/>), the system clock (<see cref="IClock"/>), the speaker's click
    /// (<see cref="IClick"/>), files (<see cref="IFiles"/>), the Library's bundled music
    /// (<see cref="ILibraryBundle"/>), a <see cref="LookSetting"/> if it has parts of its own to dress for the look, and for Claude its
    /// key store (<see cref="IAssistKeyStore"/>), the <see cref="ClaudeKeyFile"/> a key arrives in, and an <see cref="IClaude"/> of its
    /// own if it answers without the API. Without them there's no piano, a stopwatch clock, a silent click, no files and no bundle, and
    /// a key kept in memory, looked for beside the data file.
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
        services.TryAddSingleton<IFiles, NoFiles>();
        services.TryAddSingleton<ILibraryBundle, NoBundle>();
        // Asking Claude: the key in the host's secure storage (in memory without one), the file a key arrives in, and each call
        // kept in Prima's data file with the piece it was for.
        services.TryAddSingleton<IAssistKeyStore, MemoryKeyStore>();
        services.TryAddSingleton(new ClaudeKeyFile(Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", AssistKey.FileName)));
        services.TryAddSingleton<IAssistLog, ClaudeLog>();
        services.AddAiAssist();
        services.TryAddSingleton<IConversionRenderer, NoConversionHost>();
        services.TryAddSingleton<IConversionHost, NoConversionHost>();
        services.AddSingleton<Transcription.ConversionJobs>();
        services.AddSingleton<Routines.RoutineService>();
        services.TryAddSingleton<IRoutineHost, LocalRoutineHost>();
        services.AddSingleton<PracticeLifecycle>();
        services.AddSingleton<Click>();
        services.AddSingleton<Practice.SessionRegistry>();
        services.AddScoped<AppNavigation>();
        services.AddScoped<Library.LibraryPrep>();
        return services;
    }
}
