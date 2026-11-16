using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Assist;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Playground.AI.Assist;
using Playground.Kit;
using Playground.Kit.Themes;
using Playground.Sync;

namespace AdaptiveWorkout.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app's storage and its copies, theme, workout flow, watch link, notice, launch timing, way back, and the ways it
    /// asks Claude. The host decides where the database file lives, and registers its own <see cref="WatchLink"/>,
    /// <see cref="WorkoutNotice"/>, <see cref="ThemeSetting"/>, <see cref="AppLaunch"/> and <see cref="ShareSheet"/> first if it can
    /// reach a watch, show notifications, has parts of its own to dress for the theme, times its own start, or can send a file on;
    /// for the copies, the stores they go to and its pickers (<see cref="ISyncStore"/>, <see cref="ISyncPicker"/>: on the phone,
    /// AddAndroidSync); and for Claude its key store (<see cref="IAssistKeyStore"/>, in the device's secure storage), the
    /// <see cref="ClaudeKeyFile"/> a key arrives in, and an <see cref="IClaude"/> of its own if it shouldn't call the API.
    /// </summary>
    /// <param name="askers">
    /// How long the ones that ask Claude live (the trainer, the coach, the week's look back, the muscle tagger and the facts they
    /// gather): as long as the screens (scoped, the default: the preview has a page of its own per tab), or as long as the app
    /// (singleton: the phone, which is kept going while Claude answers, so a screen opened again after the app was swiped away
    /// still hears an asking under way).
    /// </param>
    public static IServiceCollection AddAdaptiveWorkout(this IServiceCollection services, string databasePath, ServiceLifetime askers = ServiceLifetime.Scoped)
    {
        services.AddSingleton(new AppSettings());
        services.AddSingleton(new WorkoutStore(databasePath));
        services.AddSingleton<ISettingStore>(sp => sp.GetRequiredService<WorkoutStore>());
        services.TryAddSingleton<WatchLink>();
        services.TryAddSingleton<WorkoutNotice>();
        services.TryAddSingleton<ThemeSetting>();
        services.TryAddSingleton<AppLaunch>();
        services.TryAddSingleton<ShareSheet>();
        services.AddSingleton<WorkoutFlow>();
        services.AddScoped<AppNavigation>();

        // Copies of everything the app keeps but Claude's key (Storage/WorkoutSync), through the Kit's sync, which the updater takes a
        // full one through before it installs: into the stores the host registers first (the phone's Documents/Workout backups), and
        // restored from what its pickers find.
        services.AddSync(sp => WorkoutSync.Options(sp, databasePath));

        // Asking Claude: the key in the host's secure storage (in memory without one), the file a key arrives in, and each call kept in
        // the app's data file. The trainer, the muscle tagger, the coach and the week's look back outlive the screen that asks, at the
        // least, so an asking keeps going when its screen closes.
        services.TryAddSingleton<IAssistKeyStore, MemoryKeyStore>();
        services.TryAddSingleton(new ClaudeKeyFile(Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", AssistKey.FileName)));
        services.TryAddSingleton<IAssistLog, ClaudeLog>();
        services.AddAiAssist();
        services.Add(new ServiceDescriptor(typeof(MuscleTagger), typeof(MuscleTagger), askers));
        services.Add(new ServiceDescriptor(typeof(TrainingFacts), typeof(TrainingFacts), askers));
        services.Add(new ServiceDescriptor(typeof(Trainer), typeof(Trainer), askers));
        services.Add(new ServiceDescriptor(typeof(Coach), typeof(Coach), askers));
        services.Add(new ServiceDescriptor(typeof(WeekReview), typeof(WeekReview), askers));
        return services;
    }
}
