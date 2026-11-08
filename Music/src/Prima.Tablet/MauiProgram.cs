using Microsoft.Extensions.Logging;
using Playground.AI.Assist;
using Playground.Updates;
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
        builder.Services.AddSingleton<LookSetting, TabletLookSetting>();
        builder.Services.AddSingleton<IClock, AndroidClock>();
        builder.Services.AddSingleton<IKeyboard, AndroidKeyboard>();
        builder.Services.AddSingleton<IClick, AndroidClick>();
        builder.Services.AddSingleton<IFiles, AndroidFiles>();
        builder.Services.AddSingleton<ISystemBars, AndroidSystemBars>();
        builder.Services.AddSingleton<ILibraryBundle, AndroidBundle>();
        // Claude's key: kept in secure storage, and taken in from claude-key.txt in the folder a PC reaches over USB
        // (/sdcard/Android/data/com.saphyiere.prima/files).
        builder.Services.AddSingleton<IAssistKeyStore, SecureKeyStore>();
        builder.Services.AddSingleton(new ClaudeKeyFile(Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath, AssistKey.FileName)));
        var database = Path.Combine(FileSystem.AppDataDirectory, "prima.db");
        builder.Services.AddPrima(database);
        // Updates from the repo's GitHub releases (prima-1.0.N, with prima.apk); a copy of the data goes to Download/Prima backups first.
        builder.Services.AddAndroidUpdates(new UpdateOptions("prima", "prima.apk") { DataFiles = [database], BackupFolder = "Prima backups" });

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        // At most once in 20 hours; Settings › Updates shows what it found.
        _ = app.Services.GetRequiredService<Updater>().CheckAsync();
        return app;
    }
}
