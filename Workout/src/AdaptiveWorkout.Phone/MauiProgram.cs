using AdaptiveWorkout.UI;
using AdaptiveWorkout.UI.Assist;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Extensions.Logging;
using Playground.AI.Assist;
using Playground.Kit.Themes;

namespace AdaptiveWorkout.Phone;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<WatchLink, PhoneWatchLink>();
		builder.Services.AddSingleton<WorkoutNotice, PhoneWorkoutNotice>();
		builder.Services.AddSingleton<ThemeSetting, PhoneThemeSetting>();
		builder.Services.AddSingleton<AppLaunch, PhoneAppLaunch>();
		// Claude's key: kept in secure storage, and taken in from claude-key.txt in the folder a PC reaches over USB
		// (/sdcard/Android/data/com.saphyiere.adaptiveworkout/files/).
		builder.Services.AddSingleton<IAssistKeyStore, SecureKeyStore>();
		builder.Services.AddSingleton(new ClaudeKeyFile(Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath, AssistKey.FileName)));
		builder.Services.AddAdaptiveWorkout(Path.Combine(FileSystem.AppDataDirectory, "adaptiveworkout.db"));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
