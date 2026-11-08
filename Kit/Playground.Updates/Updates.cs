namespace Playground.Updates;

/// <summary>
/// Where an app's updates come from: the tag its releases go by (<c>prima</c>, for <c>prima-1.0.N</c>), the file it installs from
/// each (<c>prima.apk</c>), and the repo. <see cref="DataFiles"/> are copied to <see cref="BackupFolder"/>, somewhere an uninstall
/// wouldn't reach, before an update installs.
/// </summary>
public sealed record UpdateOptions(string Tag, string Asset)
{
    public string Repository { get; init; } = "saphyiere/playground";

    /// <summary>The app's data, its database say, copied before an update installs.</summary>
    public IReadOnlyList<string> DataFiles { get; init; } = [];

    /// <summary>The folder the copies go in, under Downloads on Android: "Prima backups".</summary>
    public string BackupFolder { get; init; } = "Backups";
}

/// <summary>A release of the app: its build (N in 1.0.N), when it was published, what changed, and the file to install.</summary>
public sealed record Release(string Tag, int Build, string Title, string Notes, DateTimeOffset Published, Uri Download, long Size, string? Sha256);

public enum UpdateStep
{
    /// <summary>Not checked yet.</summary>
    Unchecked,
    Checking,
    /// <summary>The newest release is the one installed, or there's no release yet.</summary>
    UpToDate,
    /// <summary>A newer release than the installed copy, or the installed copy came from the PC.</summary>
    Available,
    Downloading,
    /// <summary>Handed to the device's installer, which asks the person to confirm and then replaces the app.</summary>
    Installing,
    /// <summary>Something went wrong, and nothing was installed: the message says what.</summary>
    Failed,
}

/// <summary>Where the updater has got to, for the screen: what's installed, the newest release, and a sentence on how it stands.</summary>
public sealed record UpdateStatus(UpdateStep Step, string Installed, int? InstalledBuild, Release? Latest, double Progress, string Message, DateTimeOffset? CheckedAt)
{
    /// <summary>The installed copy was built on the PC (version 1.0, without a build number) rather than taken from a release.</summary>
    public bool FromPc => InstalledBuild is null;
}

public enum InstallResult
{
    /// <summary>The device's installer has the file and is asking the person to confirm.</summary>
    Handed,
    /// <summary>The person has to allow the app to install apps first; the installer has opened that setting.</summary>
    NeedsPermission,
    Failed,
}

public sealed record InstallOutcome(InstallResult Result, string Message);

/// <summary>The device's side of an update. Android's is <c>AndroidInstaller</c>, in Playground.Updates.Android.</summary>
public interface IInstaller
{
    /// <summary>The installed app's version name: 1.0.N from a release, 1.0 from a build on the PC.</summary>
    string InstalledVersion { get; }

    /// <summary>A folder for downloads, which the updater empties of old ones.</summary>
    string DownloadFolder { get; }

    /// <summary>Whether the file is this app signed with the installed copy's key: only then does it install as an update and keep the app's data.</summary>
    Task<bool> SignedLikeInstalledAsync(string file);

    /// <summary>Copies the files somewhere an uninstall wouldn't reach, and says where, for the screen.</summary>
    Task<string> KeepCopiesAsync(IReadOnlyList<string> files, string folder, CancellationToken ct);

    /// <summary>Hands the file to the device's installer, which asks the person to confirm.</summary>
    Task<InstallOutcome> InstallAsync(string file, CancellationToken ct);

    /// <summary>Raised when the device's installer stops without installing (the person cancelled, say), with a sentence on why.</summary>
    event Action<string>? Stopped;
}

/// <summary>A step of an update that failed in a way the person should hear about, in a sentence for the screen.</summary>
public sealed class UpdateException(string message) : Exception(message);
