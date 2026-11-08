using System.Net;

namespace Playground.Updates.Tests;

public class UpdaterTests
{
    private static readonly byte[] App = [.. Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251))];

    private static readonly UpdateOptions Prima = new("prima", "prima.apk") { DataFiles = ["/data/prima.db"], BackupFolder = "Prima backups" };

    private static (Updater Updater, FakeGitHub GitHub, FakeInstaller Device, Clock Clock) Make(string installed, UpdateOptions? options = null)
    {
        var github = new FakeGitHub();
        var device = new FakeInstaller(installed);
        var clock = new Clock();
        return (new Updater(options ?? Prima, device, clock, github), github, device, clock);
    }

    [Fact]
    public async Task CheckAsync_FindsThisAppsNewestRelease_AmongOtherAppsDraftsAndPrereleases()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Release("workout-1.0.9", App, asset: "workout.apk");
        github.Release("prima-1.0.8", App, draft: true);
        github.Release("prima-1.0.9", App, prerelease: true);
        github.Release("prima-1.0.7", App);
        github.Release("prima-1.0.6", App);
        github.Release("prima-1.0.10-test", App);

        await updater.CheckAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStep.Available, updater.Status.Step);
        Assert.Equal(7, updater.Status.Latest!.Build);
        Assert.Equal("Prima 1.0.7 is ready to install.", updater.Status.Message);
        Assert.Equal("https://api.github.com/repos/saphyiere/playground/releases?per_page=30", Assert.Single(github.Requests));
    }

    [Fact]
    public async Task CheckAsync_UpToDate_WhenTheNewestIsInstalled()
    {
        var (updater, github, device, _) = Make("1.0.7");
        using var _ = device;
        github.Release("prima-1.0.7", App);

        await updater.CheckAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStep.UpToDate, updater.Status.Step);
        Assert.Equal("Up to date: Prima 1.0.7 is the newest.", updater.Status.Message);
    }

    [Fact]
    public async Task CheckAsync_OffersTheNewestRelease_ToACopyBuiltOnThePc()
    {
        var (updater, github, device, _) = Make("1.0");
        using var _ = device;
        github.Release("prima-1.0.7", App);

        await updater.CheckAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(updater.Status.FromPc);
        Assert.Equal(UpdateStep.Available, updater.Status.Step);
        Assert.Equal("This copy was built on the PC. The newest release is Prima 1.0.7.", updater.Status.Message);
    }

    [Fact]
    public async Task CheckAsync_ReadsWhatsInstalledEachTime()
    {
        var (updater, github, device, _) = Make("1.0");
        using var _ = device;
        github.Release("prima-1.0.7", App);
        var ct = TestContext.Current.CancellationToken;
        await updater.CheckAsync(ct: ct);

        device.InstalledVersion = "1.0.7";
        await updater.CheckAsync(evenIfRecent: true, ct: ct);

        Assert.Equal(7, updater.Status.InstalledBuild);
        Assert.Equal(UpdateStep.UpToDate, updater.Status.Step);
    }

    [Fact]
    public async Task CheckAsync_SaysWhenThereIsNoReleaseYet()
    {
        var (updater, _, device, _) = Make("1.0");
        using var _ = device;

        await updater.CheckAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStep.UpToDate, updater.Status.Step);
        Assert.Null(updater.Status.Latest);
        Assert.Equal("There's no release yet.", updater.Status.Message);
    }

    [Fact]
    public async Task CheckAsync_AsksAtMostOnceIn20Hours_UnlessAskedAgain()
    {
        var (updater, github, device, clock) = Make("1.0.7");
        using var _ = device;
        github.Release("prima-1.0.7", App);
        var ct = TestContext.Current.CancellationToken;

        await updater.CheckAsync(ct: ct);
        clock.Now += TimeSpan.FromHours(19);
        await updater.CheckAsync(ct: ct);
        await updater.CheckAsync(evenIfRecent: true, ct: ct);
        clock.Now += TimeSpan.FromHours(21);
        await updater.CheckAsync(ct: ct);

        Assert.Equal(3, github.Requests.Count);
    }

    [Fact]
    public async Task CheckAsync_SaysWhenGitHubsLimitIsReached()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Next = () =>
        {
            var reply = new HttpResponseMessage(HttpStatusCode.Forbidden);
            reply.Headers.Add("X-RateLimit-Remaining", "0");
            return reply;
        };

        await updater.CheckAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStep.Failed, updater.Status.Step);
        Assert.Equal("GitHub's limit on checks from this connection is reached for now; try again in an hour.", updater.Status.Message);
    }

    [Fact]
    public async Task CheckAsync_SaysWhenGitHubCantBeReached()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Next = () => throw new HttpRequestException("No route to host");

        await updater.CheckAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStep.Failed, updater.Status.Step);
        Assert.Equal("GitHub couldn't be reached: check the connection.", updater.Status.Message);
    }

    [Fact]
    public async Task InstallAsync_ChecksTheDownload_KeepsACopyOfTheData_ThenHandsItOver()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Release("prima-1.0.7", App);
        var ct = TestContext.Current.CancellationToken;
        var progress = new List<double>();
        updater.Changed += s => progress.Add(s.Progress);
        await updater.CheckAsync(ct: ct);

        await updater.InstallAsync(ct);

        Assert.Equal(["signature", "copies of /data/prima.db", "install"], device.Calls);
        Assert.Equal(App, device.Handed);
        Assert.Equal(UpdateStep.Installing, updater.Status.Step);
        Assert.Equal("Confirm the update to Prima 1.0.7. A copy of your data is in Downloads/Prima backups.", updater.Status.Message);
        Assert.Equal(1, progress.Max());
    }

    [Fact]
    public async Task InstallAsync_InstallsNothing_SignedWithAnotherKey()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Release("prima-1.0.7", App);
        device.SameKey = false;
        var ct = TestContext.Current.CancellationToken;
        await updater.CheckAsync(ct: ct);

        await updater.InstallAsync(ct);

        Assert.Equal(["signature"], device.Calls);
        Assert.Equal(UpdateStep.Failed, updater.Status.Step);
        Assert.Equal("Prima 1.0.7 isn't signed with the same key as the app you have, so it can't install as an update. Nothing was installed.", updater.Status.Message);
    }

    [Fact]
    public async Task InstallAsync_InstallsNothing_WhenTheDownloadDoesntMatchGitHubsFingerprint()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Release("prima-1.0.7", App, digest: "sha256:" + new string('0', 64));
        var ct = TestContext.Current.CancellationToken;
        await updater.CheckAsync(ct: ct);

        await updater.InstallAsync(ct);

        Assert.Empty(device.Calls);
        Assert.Equal("The download doesn't match the fingerprint GitHub gives for it, so nothing was installed; try again.", updater.Status.Message);
    }

    [Fact]
    public async Task InstallAsync_InstallsNothing_WhenTheDownloadStopsShort()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Release("prima-1.0.7", App, size: App.Length + 1000);
        var ct = TestContext.Current.CancellationToken;
        await updater.CheckAsync(ct: ct);

        await updater.InstallAsync(ct);

        Assert.Empty(device.Calls);
        Assert.Equal("The download stopped at 0.3 MB of 0.3 MB, so nothing was installed; try again.", updater.Status.Message);
    }

    [Fact]
    public async Task InstallAsync_InstallsNothing_WhenACopyOfTheDataCantBeKept()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Release("prima-1.0.7", App);
        device.CopiesFail = new IOException("No space left");
        var ct = TestContext.Current.CancellationToken;
        await updater.CheckAsync(ct: ct);

        await updater.InstallAsync(ct);

        Assert.Equal(["signature", "copies of /data/prima.db"], device.Calls);
        Assert.Equal("A copy of your data couldn't be kept first (No space left), so nothing was installed.", updater.Status.Message);
    }

    [Fact]
    public async Task InstallAsync_WhenTheAppMayNotInstallAppsYet_SaysSo_AndCanTryAgain()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Release("prima-1.0.7", App);
        device.Outcome = new InstallOutcome(InstallResult.NeedsPermission, "Allow Prima to install apps, then tap Install again.");
        var ct = TestContext.Current.CancellationToken;
        await updater.CheckAsync(ct: ct);

        await updater.InstallAsync(ct);

        Assert.Equal(UpdateStep.Available, updater.Status.Step);
        Assert.Equal("Allow Prima to install apps, then tap Install again.", updater.Status.Message);
    }

    [Fact]
    public async Task InstallAsync_WhenThePersonCancelsTheDevicesInstaller_CanBeTriedAgain()
    {
        var (updater, github, device, _) = Make("1.0.5");
        using var _ = device;
        github.Release("prima-1.0.7", App);
        var ct = TestContext.Current.CancellationToken;
        await updater.CheckAsync(ct: ct);
        await updater.InstallAsync(ct);

        device.Stop("The update was cancelled.");

        Assert.Equal(UpdateStep.Available, updater.Status.Step);
        Assert.Equal("The update was cancelled.", updater.Status.Message);
    }

    [Theory]
    [InlineData("1.0.12", 12)]
    [InlineData("1.0", null)]
    [InlineData("1.0.x", null)]
    [InlineData("", null)]
    public void BuildOf_ReadsTheBuildFromAReleasesVersion(string version, int? build) => Assert.Equal(build, Updater.BuildOf(version));
}
