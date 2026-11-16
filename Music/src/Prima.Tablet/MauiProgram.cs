using Microsoft.Extensions.Logging;
using Playground.AI.Assist;
using Playground.Sync;
using Playground.Updates;
using Prima.Core.Transcription;
using Prima.UI;
using Prima.UI.Host;
using Prima.UI.Looks;
using Prima.UI.Storage;

namespace Prima.Tablet;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
        // Keep conversion and Blazor failures visible to adb logcat in the installed Release app too.
        builder.Logging.AddProvider(new AndroidErrorLog());
        builder.Services.AddSingleton<LookSetting, TabletLookSetting>();
        builder.Services.AddSingleton<IClock, AndroidClock>();
        builder.Services.AddSingleton<IKeyboard, AndroidKeyboard>();
        builder.Services.AddSingleton<IClick, AndroidClick>();
        builder.Services.AddSingleton<IFiles, AndroidFiles>();
        builder.Services.AddSingleton<ISystemBars, AndroidSystemBars>();
        builder.Services.AddSingleton<ILibraryBundle, AndroidBundle>();
        builder.Services.AddSingleton<ISpeaker, AndroidSpeaker>();
        builder.Services.AddSingleton<IConversionRenderer, AndroidConversionRenderer>();
        builder.Services.AddSingleton<IConversionHost, AndroidConversionHost>();
        builder.Services.AddSingleton<IRoutineHost, AndroidRoutineHost>();
        // Claude's key: kept in secure storage, and taken in from claude-key.txt in the folder a PC reaches over USB
        // (/sdcard/Android/data/com.saphyiere.prima/files). What goes wrong is kept in the same folder, in errors.log, so it can be
        // copied off even when Prima won't open (Settings › Problems shows it too).
        var shared = Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath;
        builder.Services.AddSingleton<IAssistKeyStore, SecureKeyStore>();
        builder.Services.AddSingleton(new ClaudeKeyFile(Path.Combine(shared, AssistKey.FileName)));
        builder.Services.AddSingleton(new ProblemLog(Path.Combine(shared, ProblemLog.FileName)));
        // Copies of everything Prima keeps but the key (Storage/PrimaSync), in Documents/Prima backups, a folder for each install,
        // where uninstalling Prima doesn't reach them; and the tablet's pickers, to restore one an install before kept.
        builder.Services.AddAndroidSync(PrimaSync.Folder);
        var database = Path.Combine(FileSystem.AppDataDirectory, "prima.db");
        builder.Services.AddPrima(database);
        // Updates from the repo's GitHub releases (prima-1.0.N, with prima.apk); a full copy of the data is taken first.
        builder.Services.AddAndroidUpdates(new UpdateOptions("prima", "prima.apk"));

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        KeepWhatNothingCaught(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Prima.Tablet"));
        // At most once in 20 hours; Settings › Updates shows what it found.
        _ = app.Services.GetRequiredService<Updater>().CheckAsync();
        // In the background: the first start after an update checks at once, before a screen can change anything, that the data
        // holds all the owner's the copy from before it did, then copies it afresh once the first screen is up; otherwise a copy is
        // taken when the last is a day old, and after practice the same way. Settings › Your data shows it.
        _ = app.Services.GetRequiredService<Syncer>().StartAsync();
        return app;
    }

    // Thrown with nothing to catch it: kept in the problem log as Android closes Prima. Work nobody waited for that failed is kept
    // when it's found (the page's failing renders end up there too), unless it was only stopped, or its page had gone.
    private static void KeepWhatNothingCaught(ILogger logger)
    {
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => logger.LogCritical(e.Exception, "Prima stopped: {Message}", e.Exception.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error) logger.LogCritical(error, "Prima stopped: {Message}", error.Message);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            if (e.Exception.InnerExceptions.All(i => i is OperationCanceledException or Microsoft.JSInterop.JSDisconnectedException)) return;
            logger.LogWarning(e.Exception, "Work nobody waited for failed: {Message}", e.Exception.GetBaseException().Message);
        };
    }
}
