using Microsoft.Extensions.Logging;
using Playground.AI.Assist;
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
        builder.Services.AddPrima(Path.Combine(FileSystem.AppDataDirectory, "prima.db"));

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
