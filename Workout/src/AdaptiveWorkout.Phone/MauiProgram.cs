using AdaptiveWorkout.UI;
using AdaptiveWorkout.UI.Assist;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Extensions.Logging;
using Playground.AI.Assist;
using Playground.Sync;
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
		// While Claude answers, a quiet notification keeps the app going, so leaving it (another app, the lock button, a swipe off the
		// recent apps) doesn't lose the answer. The askers live as long as the app then, not its screen: a screen opened after the app
		// was swiped away and opened again still hears an asking that's under way.
		builder.Services.AddAndroidAskKeepAlive(Resource.Drawable.notification_mark);
		// Copies of everything the app keeps but the key (Storage/WorkoutSync), in Documents/Workout backups, a folder for each install,
		// where uninstalling the app doesn't reach them; the phone's pickers, to restore one an install before kept; and its share sheet,
		// to send an export on.
		builder.Services.AddAndroidSync(WorkoutSync.Folder);
		builder.Services.AddSingleton<ShareSheet, PhoneShareSheet>();
		var database = Path.Combine(FileSystem.AppDataDirectory, "adaptiveworkout.db");
		builder.Services.AddAdaptiveWorkout(database, askers: ServiceLifetime.Singleton);
		// Updates from the repo's GitHub releases (workout-1.0.N, with workout.apk); a full copy of the data is taken first.
		builder.Services.AddAndroidUpdates(new UpdateOptions("workout", "workout.apk"));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();
		// At most once in 20 hours; Settings › Updates shows what it found.
		_ = app.Services.GetRequiredService<Updater>().CheckAsync();
		// In the background: the first start after an update checks at once, before a screen can change anything, that the data holds
		// all the owner's the copy from before it did, then copies it afresh once the first screen is up; otherwise a copy is taken when
		// the last is a day old (the very first, on a phone with none yet), and after a workout the same way, never while one is open.
		// Settings › Your data shows it.
		_ = app.Services.GetRequiredService<Syncer>().StartAsync();
		return app;
	}
}
