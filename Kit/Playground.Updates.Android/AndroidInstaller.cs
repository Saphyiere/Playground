using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Microsoft.Extensions.DependencyInjection;

[assembly: UsesPermission(Android.Manifest.Permission.RequestInstallPackages)]

namespace Playground.Updates;

/// <summary>
/// Android's side of an app's updates.
/// <list type="bullet">
/// <item>The installed version is the app's version name: 1.0.N from a release, 1.0 from a build on the PC.</item>
/// <item>A download installs only when Android's package manager finds it's this app, signed by the same key as the installed copy:
/// then Android replaces the app and keeps its data.</item>
/// <item>The app's data files are copied to Downloads, through MediaStore, where an uninstall wouldn't reach them.</item>
/// <item>The file goes to Android's package installer in a session; <see cref="InstallStatusReceiver"/> shows its confirmation and
/// hears how it ended. On success Android stops the app, replaced; the person opens it again.</item>
/// </list>
/// </summary>
public sealed class AndroidInstaller : IInstaller
{
    private readonly Context context;

    public AndroidInstaller(Context context)
    {
        this.context = context;
        InstallStatusReceiver.Stopped += why => Stopped?.Invoke(why);
    }

    public event Action<string>? Stopped;

    public string InstalledVersion => Info(context.PackageName!, 0)?.VersionName ?? "";

    public string DownloadFolder => Path.Combine(context.CacheDir!.AbsolutePath, "updates");

    private string Label => context.ApplicationInfo?.LoadLabel(context.PackageManager!) ?? "the app";

    public Task<bool> SignedLikeInstalledAsync(string file)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) throw new UpdateException("Updating from the app needs Android 9 or later; install from the PC instead.");
        var archive = ArchiveInfo(file, (long)PackageInfoFlags.SigningCertificates);
        var installed = Info(context.PackageName!, (long)PackageInfoFlags.SigningCertificates);
        if (archive is null || installed is null || archive.PackageName != context.PackageName) return Task.FromResult(false);
        var theirs = Signers(archive);
        return Task.FromResult(theirs.Count > 0 && theirs.SetEquals(Signers(installed)));
    }

    public async Task<string> KeepCopiesAsync(IReadOnlyList<string> files, string folder, CancellationToken ct)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) throw new IOException("keeping a copy in Downloads needs Android 10 or later");
        var resolver = context.ContentResolver!;
        var place = global::Android.OS.Environment.DirectoryDownloads + "/" + folder;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
        foreach (var path in files.Where(File.Exists))
        {
            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, $"{stamp} {Path.GetFileName(path)}");
            values.Put(MediaStore.IMediaColumns.RelativePath, place);
            var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values) ?? throw new IOException("Downloads didn't take the copy");
            await using var output = resolver.OpenOutputStream(uri) ?? throw new IOException("Downloads didn't open the copy");
            await using var input = File.OpenRead(path);
            await input.CopyToAsync(output, ct);
        }
        return place;
    }

    public async Task<InstallOutcome> InstallAsync(string file, CancellationToken ct)
    {
        var packages = context.PackageManager!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !packages.CanRequestPackageInstalls())
        {
            try
            {
                var setting = new Intent(Settings.ActionManageUnknownAppSources, global::Android.Net.Uri.Parse($"package:{context.PackageName}"));
                setting.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(setting);
            }
            catch (ActivityNotFoundException)
            {
                return new InstallOutcome(InstallResult.Failed, $"This device has no setting to let {Label} install apps; install the update from the PC instead.");
            }
            return new InstallOutcome(InstallResult.NeedsPermission, $"Allow {Label} to install apps, in the setting just opened, then tap Install again.");
        }

        var installer = packages.PackageInstaller;
        var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        parameters.SetAppPackageName(context.PackageName);
        var id = installer.CreateSession(parameters);
        using var session = installer.OpenSession(id);
        await using (var input = File.OpenRead(file))
        await using (var output = session.OpenWrite("app.apk", 0, input.Length))
        {
            await input.CopyToAsync(output, ct);
            session.Fsync(output);
        }

        // The installer fills in how it went, so the intent it's handed back has to be mutable: explicit, so Android allows that.
        var flags = PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0);
        var reply = PendingIntent.GetBroadcast(context, id, new Intent(context, typeof(InstallStatusReceiver)), flags)!;
        session.Commit(reply.IntentSender);
        return new InstallOutcome(InstallResult.Handed, "");
    }

    private PackageInfo? Info(string package, long flags) => OperatingSystem.IsAndroidVersionAtLeast(33)
        ? context.PackageManager!.GetPackageInfo(package, PackageManager.PackageInfoFlags.Of(flags))
        : context.PackageManager!.GetPackageInfo(package, (PackageInfoFlags)flags);

    private PackageInfo? ArchiveInfo(string file, long flags) => OperatingSystem.IsAndroidVersionAtLeast(33)
        ? context.PackageManager!.GetPackageArchiveInfo(file, PackageManager.PackageInfoFlags.Of(flags))
        : context.PackageManager!.GetPackageArchiveInfo(file, (PackageInfoFlags)flags);

    private static HashSet<string> Signers(PackageInfo info) => OperatingSystem.IsAndroidVersionAtLeast(28) && info.SigningInfo?.GetApkContentsSigners() is { } signers
        ? [.. signers.Select(s => Convert.ToBase64String(s.ToByteArray() ?? []))]
        : [];
}

/// <summary>Hears from Android's package installer: shows its confirmation, and passes on an install that stopped.</summary>
[BroadcastReceiver(Exported = false)]
public sealed class InstallStatusReceiver : BroadcastReceiver
{
    /// <summary>The installer stopped without installing, with a sentence on why.</summary>
    public static event Action<string>? Stopped;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        switch ((PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure))
        {
            case PackageInstallStatus.PendingUserAction:
                var confirm = OperatingSystem.IsAndroidVersionAtLeast(33)
                    ? intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent
                    : intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
                if (confirm is null) break;
                confirm.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(confirm);
                break;
            case PackageInstallStatus.Success:
                break;
            case PackageInstallStatus.FailureAborted:
                Stopped?.Invoke("The update was cancelled: nothing was installed.");
                break;
            default:
                Stopped?.Invoke($"Android's installer stopped: {intent.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? "it gave no reason"}. Nothing was installed.");
                break;
        }
    }
}

public static class AndroidUpdatesExtensions
{
    /// <summary>The app's <see cref="Updater"/>, installing through Android's package installer.</summary>
    public static IServiceCollection AddAndroidUpdates(this IServiceCollection services, UpdateOptions options)
    {
        services.AddSingleton<IInstaller>(_ => new AndroidInstaller(Application.Context));
        return services.AddUpdates(options);
    }
}
