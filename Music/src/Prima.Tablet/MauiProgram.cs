using Microsoft.Extensions.Logging;
using Playground.AI.Assist;
using Playground.Updates;
using Prima.Core.Transcription;
using Prima.UI;
using Prima.UI.Host;
using Prima.UI.Looks;

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
        var database = Path.Combine(FileSystem.AppDataDirectory, "prima.db");
        builder.Services.AddPrima(database);
        // Updates from the repo's GitHub releases (prima-1.0.N, with prima.apk); a copy of the data goes to Download/Prima backups first.
        builder.Services.AddAndroidUpdates(new UpdateOptions("prima", "prima.apk") { DataFiles = [database], BackupFolder = "Prima backups" });

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        KeepWhatNothingCaught(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Prima.Tablet"));
        // At most once in 20 hours; Settings › Updates shows what it found.
        _ = app.Services.GetRequiredService<Updater>().CheckAsync();
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
