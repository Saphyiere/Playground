using AdaptiveWorkout.UI;
using AdaptiveWorkout.UI.Assist;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Extensions.Logging;
using Playground.AI.Assist;
using Playground.Updates;
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
		var database = Path.Combine(FileSystem.AppDataDirectory, "adaptiveworkout.db");
		builder.Services.AddAdaptiveWorkout(database);
		// Updates from the repo's GitHub releases (workout-1.0.N, with workout.apk); a copy of the data goes to Download/Workout backups first.
		builder.Services.AddAndroidUpdates(new UpdateOptions("workout", "workout.apk") { DataFiles = [database], BackupFolder = "Workout backups" });

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
