using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS.Storage;
using Android.Provider;
using Microsoft.Extensions.DependencyInjection;
using AndroidEnvironment = Android.OS.Environment;

namespace Playground.Sync;

public static class AndroidSyncExtensions
{
    /// <summary>
    /// The app's copies in the device's shared storage, in <c>Documents/{folder}</c> (<see cref="DeviceStore"/>), where uninstalling
    /// the app doesn't reach them, and the system's pickers to restore one this install didn't make. The host registers it before
    /// the app's AddSync, with its other services.
    /// </summary>
    public static IServiceCollection AddAndroidSync(this IServiceCollection services, string folder)
    {
        services.AddSingleton<ISyncStore>(_ => DeviceStore(Application.Context, folder));
        services.AddSingleton<ISyncPicker>(_ => new AndroidSyncPicker(Application.Context, folder));
        return services;
    }

    /// <summary>
    /// This install's folder of copies: <c>Documents/{folder}/Installed {when}</c> in the device's shared storage, named for when the
    /// app was installed (UTC). Android 11 and later let an app write there by path without asking, and read back what it wrote; an
    /// app installed again isn't the one that wrote the files before, can't read or replace them, and so keeps its copies in a folder
    /// of its own, while the person opens the ones before with the folder picker. Android 10 and earlier keep them in the app's own
    /// folder, which an uninstall deletes.
    /// </summary>
    public static FolderStore DeviceStore(Context context, string folder)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && context.GetSystemService(Context.StorageService) is StorageManager storage && storage.PrimaryStorageVolume.Directory is { } volume)
        {
            var installed = DateTimeOffset.FromUnixTimeMilliseconds(Package(context).FirstInstallTime).UtcDateTime;
            var install = "Installed " + installed.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
            return new FolderStore(Path.Combine(volume.AbsolutePath, AndroidEnvironment.DirectoryDocuments!, folder, install), $"Documents/{folder}");
        }

        var own = context.GetExternalFilesDir(null)!.AbsolutePath;
        return new FolderStore(Path.Combine(own, "Backups"), $"Android/data/{context.PackageName}/files/Backups", survivesUninstall: false);
    }

    private static PackageInfo Package(Context context) => OperatingSystem.IsAndroidVersionAtLeast(33)
        ? context.PackageManager!.GetPackageInfo(context.PackageName!, PackageManager.PackageInfoFlags.Of(0L))!
        : context.PackageManager!.GetPackageInfo(context.PackageName!, 0)!;
}

/// <summary>
/// The system's folder and file pickers, for restoring: a folder of copies (opening on <c>Documents/{folder}</c>), read through
/// Android's documents provider (<see cref="DocumentTreeStore"/>), or one file (an export, or a database on its own), copied in.
/// </summary>
public sealed class AndroidSyncPicker(Context context, string folder) : ISyncPicker
{
    public async Task<IReadOnlyList<ISyncStore>> PickFolderAsync(CancellationToken ct)
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            intent.PutExtra(DocumentsContract.ExtraInitialUri, DocumentsContract.BuildDocumentUri("com.android.externalstorage.documents", $"primary:{AndroidEnvironment.DirectoryDocuments}/{folder}"));
        }
        var tree = await SyncPickerActivity.PickAsync(context, intent);
        return tree is null ? [] : await Task.Run(() => DocumentTreeStore.Find(context, tree), ct);
    }

    public async Task<string?> PickFileAsync(string into, CancellationToken ct)
    {
        var intent = new Intent(Intent.ActionOpenDocument).AddCategory(Intent.CategoryOpenable).SetType("*/*");
        if (await SyncPickerActivity.PickAsync(context, intent) is not { } picked) return null;

        return await Task.Run(async () =>
        {
            Directory.CreateDirectory(into);
            var path = Path.Combine(into, Path.GetFileName(DocumentTreeStore.DisplayName(context, picked) ?? "picked"));
            await using (var input = context.ContentResolver!.OpenInputStream(picked) ?? throw new IOException("the file picked couldn't be opened"))
            await using (var output = File.Create(path))
            {
                await input.CopyToAsync(output, ct);
            }
            // The picked file's own time, so a database on its own is dated when it was kept, not when it was picked.
            if (DocumentTreeStore.LastModified(context, picked) is { } modified) File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
            return path;
        }, ct);
    }
}
