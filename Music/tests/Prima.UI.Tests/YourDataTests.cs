using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Playground.AI.Assist;
using Playground.Sync;
using Prima.Core.Timing;
using Prima.UI.Library;
using Prima.UI.Looks;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>
/// Settings › Your data's three keys, Start over, Remove library and Reset settings, as the owner decided them on 15 November (the
/// review of main at a7ab94d, part 6, rules 1-4). Start over's own test is with the others of it, in SettingsTests; a piece of
/// Prima's own removed by hand, and putting them back, are in OwnPiecesTests.
/// </summary>
public class YourDataTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    // Rule 1: Settings › Your data has three keys, each asked twice and each saying just what goes and what stays: Start over,
    // Remove library, Reset settings. Each keeps a copy first and holds it, named for the key. If that copy can't be taken, it says
    // so, nothing is done, and a second, explicit "… without a copy" does it.
    [Theory]
    [InlineData("Start over", "Clear my progress", "Clear it without a copy", "Keep it", "This clears everything Prima keeps about your playing", "The settings and the Library stay as they are", "cleared")]
    [InlineData("Remove library", "Remove every piece", "Remove them without a copy", "Keep them", "This takes every piece out of the Library, Prima's own too", "Your progress stays", "removed")]
    [InlineData("Reset settings", "Reset the settings", "Reset them without a copy", "Keep them", "This puts everything on this screen back as a new install has it", "Claude's key and the copies in Your data stay", "reset")]
    public async Task YourDataHasThreeKeysEachAskedTwiceSayingWhatGoesAndWhatStays_EachKeepingACopyFirstHeldUnderItsName(
        string key, string does, string without, string keep, string goes, string stays, string past)
    {
        await using var host = new ComponentHost();
        var sync = host.Context.Services.GetRequiredService<Syncer>();
        await Seed(host);
        var view = AppAndData(host);
        Assert.Equal(["Start over…", "Remove library…", "Reset settings…"], Keys(view));

        // Asked: what goes, what stays, and the copy kept first. Left as it is, nothing's done.
        await Tap(view, key + "…");
        Assert.Equal([does, keep], Keys(view));
        Assert.StartsWith(goes, Said(view), StringComparison.Ordinal);
        Assert.Contains(stays, Said(view), StringComparison.Ordinal);
        Assert.EndsWith("A copy of everything is kept first, held in Your data until you restore it or let it go.", Said(view), StringComparison.Ordinal);
        await Tap(view, keep);
        Assert.Equal(["Start over…", "Remove library…", "Reset settings…"], Keys(view));
        Assert.False(await Done(host, key));

        // Asked twice, it's done, a copy of everything kept first and held under its name.
        await Tap(view, key + "…");
        await Tap(view, does);
        Assert.True(await Done(host, key));
        var held = Assert.Single(sync.Status.Holding);
        Assert.Equal((SyncHeldFrom.Change, key), (held.Held!.From, held.Held.Why));

        // With no copy to be had (a problem kept with Claude's key in it), it says so and nothing's done, until it's asked for
        // without one.
        await Seed(host);
        host.Problems.Keep("Error", "Prima.UI.Pages.Settings", "The key test-key was refused");
        var again = AppAndData(host);
        await Tap(again, key + "…");
        await Tap(again, does);
        Assert.Equal($"A copy couldn't be kept first (errors.log holds a secret, which copies never take), so nothing was {past}.", Said(again));
        Assert.Equal([does, without, keep], Keys(again));
        Assert.False(await Done(host, key));

        await Tap(again, without);

        Assert.True(await Done(host, key));
        Assert.Equal(held.Made, Assert.Single(sync.Status.Holding).Made);
        Directory.Delete(sync.Options.WorkFolder, recursive: true);
    }

    // Rule 3: Remove library takes every piece out, the ones that come with Prima too, with their files, readings and excerpts, and
    // they aren't brought back: Prima stops putting its own pieces in by itself. The progress isn't touched: the runs kept on
    // pieces that have gone stay counted, and the Library opens without them. (One of Prima's own removed by hand stays out the
    // same way: OwnPiecesTests.)
    [Fact]
    public async Task RemoveLibraryTakesEveryPieceOutPrimasOwnTooAndNoneComesBack_TheRunsOnThemStillCounted()
    {
        var bundle = new OwnPieces();
        await using var host = new ComponentHost(bundle);
        // Prima's own, stored as the Library first opens.
        await new LibraryPrep(host.Store, bundle, host.Browser).EnsureAsync();
        Assert.Equal(["own-minuet", "own-musette"], await OwnPiecesTests.Ids(host));
        // An import: a PDF with its MIDI beside the data file, a reading of it and what the reading made. A run and a day on Prima's own.
        var name = $"remove-library-{Guid.NewGuid():N}";
        var (pdf, midi) = (Path.Combine(host.Store.FilesPath, name + ".pdf"), Path.Combine(host.Store.FilesPath, name + ".mid"));
        Directory.CreateDirectory(host.Store.FilesPath);
        File.WriteAllBytes(pdf, "%PDF-1.4 "u8.ToArray());
        File.WriteAllBytes(midi, "MThd"u8.ToArray());
        await host.Store.SavePieceAsync(new PieceRecord(name, "import", "A scan", "", null, "pdf", name + ".pdf", name + ".mid", null, null, null, null, null, null, null, null, null, null, null, null, null,
            DateTimeOffset.Now, null), null, []);
        await host.Store.SaveReadingAsync(name, "{}");
        var made = Path.Combine(Path.ChangeExtension(host.DatabasePath, ".conversion"), await host.Store.SaveConversionArtifactAsync([1, 2, 3], Ct) + ".bin");
        await host.Store.AddAttemptAsync(new AttemptRecord(0, DateTimeOffset.Now, Today, "piece", "in time", "own-minuet", 4, 72, null, null, "[]", null,
            JsonSerializer.Serialize(new { inTime = 0.9, right = 9, wrong = 1, missed = 0, from = 1, hands = "both" }), 0.9, false, null));
        await host.Store.AddMinutesAsync(Today, 6);
        var view = AppAndData(host);

        await Tap(view, "Remove library…");
        await Tap(view, "Remove every piece");

        Assert.Equal("Took 3 pieces out of the Library. Prima won't put its own back by itself: the Library has a key for that. Your runs on them still count.", Said(view));
        Assert.Empty(await OwnPiecesTests.Ids(host));
        Assert.False(File.Exists(pdf));
        Assert.False(File.Exists(midi));
        Assert.Null(await host.Store.GetReadingAsync(name));
        Assert.False(File.Exists(made));
        Assert.Empty(await host.Store.GetExcerptsAsync(4, 1, Today));
        // The progress stays: the run on a piece that's gone, and its day.
        Assert.Single(await host.Store.GetPieceRunsAsync("own-minuet"));
        Assert.Single(await host.Store.GetDaysAsync(Today));

        // Not brought back: the Library opening again stores none of Prima's own, and opens saying so, its runs read without them.
        await new LibraryPrep(host.Store, bundle, host.Browser).EnsureAsync();
        Assert.Empty(await OwnPiecesTests.Ids(host));
        var library = host.Context.Render<Pages.Library>();
        library.WaitForElement(".library-put-back");
        Assert.StartsWith("Prima's own pieces are out of the Library: Remove library took them out", library.Find(".page-note").TextContent, StringComparison.Ordinal);
        Directory.Delete(host.Context.Services.GetRequiredService<Syncer>().Options.WorkFolder, recursive: true);
        Directory.Delete(Path.GetDirectoryName(made)!, recursive: true);
    }

    // Rule 4: Reset settings puts everything on the Settings screen back to how a new install has it, the timing too; Claude's key
    // and the copies stay, and so do the progress and the Library. Then the welcome screen, which asks for the timing again.
    [Fact]
    public async Task ResetSettingsPutsEverythingOnTheSettingsScreenBackAsANewInstallHasIt_ClaudesKeyAndTheCopiesStaying_ThenOpensTheWelcome()
    {
        await using var host = new ComponentHost();
        var sync = host.Context.Services.GetRequiredService<Syncer>();
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        var options = host.Context.Services.GetRequiredService<PrimaSettings>();
        var looks = host.Context.Services.GetRequiredService<LookSetting>();
        await host.AddPiece();
        await host.Store.AddMinutesAsync(Today, 7);
        // Every setting on the screen changed, the timing among them, and the welcome done; the touch Keyboard check measured too.
        (options.Name, options.DailyMinutes, options.Cue, options.Clefs) = ("Ana", 45, "After dinner", "bass");
        (options.CountInBars, options.LookSeconds, options.Cursor, options.LiveMarks, options.LetterNames) = (2, 0, false, false, false);
        options.UseWay("hands-on");
        (options.SendToPiano, options.PianoButtons) = (false, false);
        (options.TimingOffsetMs, options.TappingSpreadMs, options.CalibratedOn) = (18, 14, Today.AddDays(-20));
        (options.ClickOnPiano, options.ClickSound, options.ClickVolume, options.PianoClick) = (true, ClickSoundKind.Beep, 50, Prima.UI.Host.PianoClickSound.WoodBlock);
        (options.MusicFont, options.SheetSize, options.Lyrics, options.Welcomed) = ("Bravura", 1, false, true);
        (options.SoftVelocity, options.LoudVelocity) = (30, 110);
        await host.Store.SaveAsync(options);
        looks.Choose(Look.Vinyl);
        var copy = await sync.CopyAsync(ct: Ct);
        var view = AppAndData(host);

        await Tap(view, "Reset settings…");
        await Tap(view, "Reset the settings");

        // As a new install has them, the timing not set, and as every screen has them now, the look too.
        var reset = new PrimaSettings();
        await host.Store.LoadAsync(reset);
        Assert.Equal(OnTheScreen(new PrimaSettings()), OnTheScreen(reset));
        Assert.Equal(OnTheScreen(new PrimaSettings()), OnTheScreen(options));
        Assert.Null(reset.CalibratedOn);
        Assert.False(reset.Welcomed);
        Assert.Equal(Look.Studio, looks.Current);
        Assert.False((await host.Store.GetSettingsAsync()).ContainsKey("Look"));
        // What's measured elsewhere stays: the touch, from Keyboard check.
        Assert.Equal(((int?)30, (int?)110), (reset.SoftVelocity, reset.LoudVelocity));
        // Claude's key, the copies, the progress and the Library stay.
        Assert.Equal("test-key", await host.Context.Services.GetRequiredService<IAssistKeyStore>().GetAsync());
        Assert.Contains(await sync.FindCopiesAsync(sync.Store, Ct), c => c.Made == copy.Made);
        Assert.Single(await host.Store.GetDaysAsync(Today));
        Assert.Single(await host.Store.GetPieceIdsAsync());
        // Then the welcome.
        Assert.Equal("http://localhost/welcome", nav.Uri);
        Directory.Delete(sync.Options.WorkFolder, recursive: true);
    }

    // What's on the Settings screen, and the welcome done or not.
    private static object OnTheScreen(PrimaSettings s) => (s.Name, s.DailyMinutes, s.Cue, s.Clefs, s.CountInBars, s.LookSeconds, s.Cursor, s.LiveMarks, s.AfterRun, s.PassFigure, s.PassWaits,
        s.RunAgain, s.LetterNames, s.SendToPiano, s.PianoButtons, s.TimingOffsetMs, s.TappingSpreadMs, s.CalibratedOn, s.ClickOnPiano, s.ClickSound, s.PianoClick, s.ClickVolume, s.MusicFont,
        s.SheetSize, s.Lyrics, s.Welcomed);

    // Something for each key to do: a practice day for Start over, a piece for Remove library, a setting for Reset settings.
    private static async Task Seed(ComponentHost host)
    {
        await host.AddPiece();
        await host.Store.AddMinutesAsync(Today, 5);
        var options = host.Context.Services.GetRequiredService<PrimaSettings>();
        options.CountInBars = 2;
        await host.Store.SaveAsync(options);
    }

    // Whether the key has done what it does.
    private static async Task<bool> Done(ComponentHost host, string key)
    {
        switch (key)
        {
            case "Start over":
                return (await host.Store.GetDaysAsync(DateOnly.MinValue)).Count == 0;
            case "Remove library":
                return (await host.Store.GetPieceIdsAsync()).Count == 0;
            default:
                var saved = new PrimaSettings();
                await host.Store.LoadAsync(saved);
                return saved.CountInBars == 1;
        }
    }

    private static IRenderedComponent<Settings> AppAndData(ComponentHost host)
    {
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("settings?tab=app");
        return host.Context.Render<Settings>();
    }

    // Your data's keys, by their words.
    private static string[] Keys(IRenderedComponent<Settings> view) => [.. view.FindAll(".data-keys button").Select(b => b.TextContent)];

    private static Task Tap(IRenderedComponent<Settings> view, string words) =>
        view.InvokeAsync(() => view.FindAll(".data-keys button").Single(b => b.TextContent == words).ClickAsync(new MouseEventArgs()));

    // What the keys say, under them.
    private static string Said(IRenderedComponent<Settings> view) => view.Find(".data-keys-note").TextContent;
}
