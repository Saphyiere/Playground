using Microsoft.Extensions.Logging;
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
        builder.Services.AddSingleton<IScreen, AndroidScreen>();
        builder.Services.AddSingleton<IFiles, AndroidFiles>();
        builder.Services.AddSingleton<ILibraryBundle, AndroidBundle>();
        builder.Services.AddPrima(Path.Combine(FileSystem.AppDataDirectory, "prima.db"));

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
