using Bunit;
using Microsoft.Data.Sqlite;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Playground.AI.Assist;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.Core.Transcription;
using Prima.UI.Components;
using Prima.UI.Host;
using Prima.UI.Pages;
using Prima.UI.Transcription;

namespace Prima.UI.Tests;

public class ReadNotesTests
{
    [Fact]
    public async Task PdfToolbarOpensConversionAndBackReturnsDirectlyToTheSheet()
    {
        await using var host = new ComponentHost(new PdfBundle());
        var piece = await host.AddPiece(pdf: true, bundled: true);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));

        var open = view.WaitForElement("[data-action=convert-pdf]");
        view.WaitForAssertion(() => Assert.NotEmpty(host.Browser.ShownPdfs));
        var pdf = view.FindComponent<PdfPages>().Instance;
        var draws = host.Browser.ShownPdfs.Count;
        Assert.Equal("off", open.GetAttribute("data-piano"));
        Assert.Empty(view.FindComponents<ReadNotes>());
        await view.InvokeAsync(() => view.Find("[data-action=convert-pdf]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=convert-start]");
        Assert.NotNull(view.Find(".pdfread-plan [data-action=convert-start]"));
        Assert.Empty(view.FindAll(".pdfread-bar [data-action=convert-start]"));
        Assert.NotNull(view.Find(".pdfread-bar > [data-action=back-to-sheet]"));
        Assert.Equal("off", view.Find("[data-action=convert-start]").GetAttribute("data-piano"));
        Assert.Equal("off", view.Find("[data-action=back-to-sheet]").GetAttribute("data-piano"));
        view.Find("[data-action=back-to-sheet]").Click();

        view.WaitForAssertion(() => Assert.Empty(view.FindComponents<ReadNotes>()));
        Assert.NotNull(view.Find(".piece-stage:not(.piece-stage-gone) .piece-frame:not(.piece-frame-hidden)"));
        Assert.NotNull(view.Find("[data-action=convert-pdf]"));
        view.WaitForAssertion(() => Assert.True(host.Browser.ShownPdfs.Count > draws));
        Assert.Same(pdf, view.FindComponent<PdfPages>().Instance);
    }

    [Fact]
    public async Task ReturningFromReadyReviewRedrawsTheRetainedPdfAfterTheStageIsVisible()
    {
        await using var host = new ComponentHost(new PdfBundle());
        var piece = await host.AddPiece(pdf: true, bundled: true);
        await SaveReview(host, piece.Id);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForAssertion(() => Assert.NotEmpty(host.Browser.ShownPdfs));
        var pdf = view.FindComponent<PdfPages>().Instance;
        var draws = host.Browser.ShownPdfs.Count;
        await view.InvokeAsync(() => view.Find("[data-action=convert-pdf]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement(".pdfread-review");
        Assert.Equal(draws, host.Browser.ShownPdfs.Count);

        await view.InvokeAsync(() => view.Find("[data-action=back-to-sheet]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        view.WaitForAssertion(() => Assert.True(host.Browser.ShownPdfs.Count > draws));
        Assert.Same(pdf, view.FindComponent<PdfPages>().Instance);
        Assert.NotNull(view.Find(".piece-stage:not(.piece-stage-gone) .piece-frame:not(.piece-frame-hidden)"));
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task FailedConversionCanReturnToTheSheetWithoutNavigation()
    {
        await using var host = new ComponentHost(new PdfBundle());
        var piece = await host.AddPiece(pdf: true, bundled: true);
        host.Browser.Profile = new double[300];
        foreach (var row in new[] { 100, 110, 120, 130, 140, 200, 210, 220, 230, 240 }) host.Browser.Profile[row] = 0.8;
        host.Claude.Error = new InvalidOperationException("Conversion failed.");
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement("[data-action=convert-pdf]").Click();
        await ConfirmBoundaries(view);
        view.WaitForElement("[data-action=convert-start]:not([disabled])").Click();
        view.WaitForElement(".pdfread-error[role=alert]");

        Assert.All(view.FindComponent<ReadNotes>().FindAll(".mu-primary"), button => Assert.Equal("off", button.GetAttribute("data-piano")));
        view.Find("[data-action=back-to-sheet]").Click();
        view.WaitForAssertion(() => Assert.Empty(view.FindComponents<ReadNotes>()));
        Assert.NotNull(view.Find(".piece-stage:not(.piece-stage-gone) .piece-frame:not(.piece-frame-hidden)"));
        Assert.Equal(1, host.Claude.Calls);
    }

    [Fact]
    public async Task PreviewFrameFollowsSelectedPageShape()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Browser.PageRatios = [792.0 / 612, 210.0 / 297];
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(124.5, 210.75, 4) }, new List<PageSystem> { new(100, 200, 4) }]));

        view.WaitForElement("[data-action=edit-source]:not([disabled])");
        var style = view.Find(".pdfread-page").GetAttribute("style");
        Assert.NotNull(style);
        var ratio = double.Parse(style.Split(':')[1].TrimEnd(';'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(host.Browser.PageRatios[0], ratio, 8);
        var region = view.Find(".pdfread-box").GetAttribute("style")!;
        var top = double.Parse(region.Split(';')[0].Split(':')[1].TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(124.5 * 236 / 900, top / 100 * 236 / ratio, 1);

        view.FindAll(".pdfread-plan .tempo-control button")[1].Click();
        style = view.Find(".pdfread-page").GetAttribute("style");
        Assert.NotNull(style);
        ratio = double.Parse(style.Split(':')[1].TrimEnd(';'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(host.Browser.PageRatios[1], ratio, 8);
    }

    [Fact]
    public async Task LeavingActiveConversionKeepsTheRequestRunningAndOnlyCancelStopsIt()
    {
        await using var host = new ComponentHost(new PdfBundle());
        var piece = await host.AddPiece(pdf: true, bundled: true);
        host.Browser.Profile = new double[300];
        foreach (var row in new[] { 100, 110, 120, 130, 140, 200, 210, 220, 230, 240 }) host.Browser.Profile[row] = 0.8;
        host.Claude.WaitForCancellation = true;
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement("[data-action=convert-pdf]").Click();
        await ConfirmBoundaries(view);
        var reading = view.WaitForElement("[data-action=convert-start]:not([disabled])").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await host.Claude.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);
        await view.Find("[data-action=back-to-sheet]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await reading;

        Assert.False(host.Claude.WasCancelled);
        Assert.Empty(view.FindComponents<ReadNotes>());
        Assert.NotNull(view.Find(".piece-stage:not(.piece-stage-gone) .piece-frame:not(.piece-frame-hidden)"));
        Assert.True((await host.Jobs.LatestAsync(piece.Id))!.Active);
        view.Find("[data-action=convert-pdf]").Click();
        view.WaitForElement("[data-job-state=Running]");
        Assert.Equal(1, host.Claude.Calls);
        view.Find("[data-action=cancel-conversion]").Click();
        view.WaitForAssertion(() => Assert.True(host.Claude.WasCancelled));
        view.WaitForElement("[data-job-state=Cancelled]");
    }

    [Fact]
    public async Task EmptyCachedPageIsDetectedAgainAndCanBeRead()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Browser.Profile = new double[300];
        foreach (var row in new[] { 100, 110, 120, 130, 140, 200, 210, 220, 230, 240 }) host.Browser.Profile[row] = 0.8;
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem>()]));

        await ConfirmBoundaries(view);
        view.WaitForElement("[data-action=convert-start]:not([disabled])");
        Assert.Equal([1], host.Browser.ProfiledPages);
        Assert.Single(view.FindAll(".pdfread-box"));
        view.Find("[data-action=convert-start]").Click();
        view.WaitForElement("[data-job-state=Paused]");
        Assert.Equal(1, host.Claude.Calls);
    }

    [Fact]
    public async Task NonemptyCachedPageIsUsedWithoutDetectingItAgain()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));

        view.WaitForElement("[data-action=edit-source]:not([disabled])");
        Assert.Empty(host.Browser.ProfiledPages);
        Assert.Single(view.FindAll(".pdfread-box"));
    }

    [Fact]
    public async Task UndetectedMusicShowsAnErrorAndCannotStartReading()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem>()]));

        view.WaitForElement(".pdfread-plan .pdfread-error[role=alert]");
        Assert.Equal([1], host.Browser.ProfiledPages);
        Assert.NotNull(view.Find("[data-action=convert-start][disabled]"));
        Assert.Empty(view.FindAll(".pdfread-box"));
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task PendingPageDetectionDisablesReadingWithoutReportingAnError()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Browser.ProfileReady = ready.Task;
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem>()]));

        try
        {
            view.WaitForAssertion(() => Assert.Equal([1], host.Browser.ProfiledPages));
            Assert.NotNull(view.Find("[data-action=convert-start][disabled]"));
            Assert.Empty(view.FindAll(".pdfread-error"));
            Assert.Equal(0, host.Claude.Calls);
        }
        finally
        {
            ready.SetResult();
        }
        view.WaitForElement(".pdfread-plan .pdfread-error[role=alert]");
    }

    [Fact]
    public async Task ReaderInitializationFailureIsContainedAndCanReopen()
    {
        await using var host = new ComponentHost(new PdfBundle());
        var piece = await host.AddPiece(pdf: true, bundled: true);
        await host.Store.SaveReadingAsync(piece.Id, "invalid draft JSON");
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement("[data-action=convert-pdf]").Click();

        view.WaitForElement(".pdfread-error.pdfread-banner[role=alert] .mu-primary");
        Assert.Empty(view.FindComponents<ReadNotes>());
        await host.Store.DeleteReadingAsync(piece.Id);
        view.Find(".pdfread-error.pdfread-banner .mu-primary").Click();
        view.WaitForElement(".pdfread-estimate");
        Assert.Single(view.FindComponents<ReadNotes>());
        Assert.Empty(view.FindAll(".pdfread-error.pdfread-banner"));
    }

    [Fact]
    public async Task SavingFailureCanRetryWithoutCallingApiAgain()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Claude.SuccessfulReading = true;
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = host.DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync(Xunit.TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER FailReadingSave BEFORE INSERT ON PieceReading BEGIN SELECT RAISE(FAIL, 'Test save failure'); END;";
        await command.ExecuteNonQueryAsync(Xunit.TestContext.Current.CancellationToken);
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        view.WaitForElement("[data-action=convert-start]:not([disabled])").Click();

        view.WaitForElement(".pdfread-cutter + .pdfread-error[role=alert] .mu-primary");
        Assert.Null(await host.Store.GetReadingAsync(piece.Id));
        command.CommandText = "DROP TRIGGER FailReadingSave;";
        await command.ExecuteNonQueryAsync(Xunit.TestContext.Current.CancellationToken);
        view.Find(".pdfread-cutter + .pdfread-error .mu-primary").Click();
        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".pdfread-cutter + .pdfread-error")));
        Assert.NotNull(await host.Store.GetReadingAsync(piece.Id));
        Assert.Equal(1, host.Claude.Calls);
    }

    [Fact]
    public async Task CuttingFailureShowsLocalErrorAndCanBeRetried()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        host.Renderer.CropError = new JSException("Canvas failure");
        view.WaitForElement("[data-action=convert-start]:not([disabled])").Click();

        view.WaitForElement(".pdfread-error[role=alert]");
        Assert.Equal(0, host.Claude.Calls);
        await host.ConversionHost.Execution!;
        host.Renderer.CropError = null;
        view.Find("[data-action=resume-conversion]:not([disabled])").Click();
        view.WaitForAssertion(() => Assert.Equal(1, host.Claude.Calls));
        view.WaitForElement("[data-job-state=Paused]");
        Assert.Equal(1, host.Claude.Calls);
        Assert.NotNull(await host.Jobs.LatestAsync(piece.Id));
    }

    [Fact]
    public async Task UnexpectedApiFailureStaysInConversionAndAllowsRetry()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Claude.Error = new InvalidOperationException("Unexpected provider failure");
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        view.WaitForElement("[data-action=convert-start]:not([disabled])").Click();

        view.WaitForElement(".pdfread-error[role=alert]");
        Assert.NotNull(view.Find("[data-action=resume-conversion][disabled]"));
        Assert.NotNull(view.Find("[data-cost-kind=job][data-cost-unknown=true]"));
        view.Find("[data-action=acknowledge-resend]").Click();
        Assert.NotNull(view.Find("[data-action=resume-conversion]:not([disabled])"));
        Assert.Equal(1, host.Claude.Calls);
        Assert.Null(await host.Store.GetReadingAsync(piece.Id));
    }

    [Theory]
    [InlineData(AnswerKind.OutOfCredit)]
    [InlineData(AnswerKind.SpendLimit)]
    [InlineData(AnswerKind.Busy)]
    [InlineData(AnswerKind.Offline)]
    public async Task ExpectedApiFailuresKeepRetryableJob(AnswerKind kind)
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Claude.Kind = kind;
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        view.WaitForElement("[data-action=convert-start]:not([disabled])").Click();

        view.WaitForElement("[data-job-state=Paused]");
        Assert.All(view.FindAll(".mu-primary"), button => Assert.Equal("off", button.GetAttribute("data-piano")));
        Assert.NotNull(view.Find("[data-action=resume-conversion]:not([disabled])"));
        var saved = await host.Jobs.LatestAsync(piece.Id);
        Assert.NotNull(saved);
        Assert.Equal(kind, Assert.Single(saved.Attempts).Outcome);
        Assert.Equal(1, host.Claude.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryGeneratedBarIsSelectableByItsScoreIdIncludingPickups(bool pickup)
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var saved = await SaveReview(host, piece.Id, pickup);
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4), new(250, 450, 4) }]));

        view.WaitForAssertion(() => Assert.Equal(15, view.FindAll("[data-action=select-review-bar]").Count));
        var sheets = view.FindComponents<Sheet>();
        Assert.Equal(2, sheets.Count);
        Assert.Equal("1-13", sheets[0].Instance.Measures);
        Assert.Equal("14-15", sheets[1].Instance.Measures);
        Assert.Equal(sheets[0].Instance.Mei, sheets[1].Instance.Mei);
        Assert.Empty(host.Browser.Outlines);
        Assert.Empty(view.FindAll(".pdfread-tint"));
        Assert.Empty(view.FindAll(".pdfread-quote"));
        Assert.NotNull(view.Find("[data-flagged-bars='3']"));

        for (var index = 0; index < 15; index++)
        {
            Assert.Equal("off", view.FindAll("[data-action=select-review-bar]")[index].GetAttribute("data-piano"));
            await view.InvokeAsync(() => view.FindAll("[data-action=select-review-bar]")[index].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
            view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-action=select-review-bar][aria-pressed=true]")));
        }
        Assert.Equal(Enumerable.Range(pickup ? 0 : 1, 15).Select(n => $"m{n}"), host.Browser.Outlines.Where(o => o.Id is not null).Select(o => o.Id));
        Assert.Single(host.Browser.Outlines, o => o.Id is null);
        Assert.NotEqual(host.Browser.Outlines[0].Element, host.Browser.Outlines[^1].Element);

        // SVG taps use the exact generated id, not the measure's horizontal position or the card's local index.
        await view.InvokeAsync(() => sheets[0].Instance.BarTapped($"m{(pickup ? 11 : 12)}"));
        Assert.NotNull(view.Find("[data-system='1'] [data-bar='12'][aria-pressed=true]"));
        Assert.Equal("1", view.Find(".pdfread-quote[data-bar='12']").GetAttribute("data-staff"));
        Assert.Single(view.FindAll(".pdfread-quote"));

        var calls = host.Browser.Outlines.Count;
        await view.InvokeAsync(() => sheets[0].Instance.BarTapped("m999"));
        Assert.Equal(calls, host.Browser.Outlines.Count);
        await view.InvokeAsync(() => sheets[0].Instance.Painted(1));
        Assert.Equal(calls + 1, host.Browser.Outlines.Count);
        Assert.Equal($"m{(pickup ? 11 : 12)}", host.Browser.Outlines[^1].Id);
        Assert.Equal(0, host.Claude.Calls);
        Assert.Equal(saved, (await host.Store.GetReadingAsync(piece.Id))!.Result);
    }

    [Fact]
    public async Task SelectingOneBarShowsOnlyItsConcernsAndUnflaggedBarsRemainSelectable()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        await SaveReview(host, piece.Id);
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4), new(250, 450, 4) }]));

        view.WaitForElement("[data-system='1'] [data-bar='1'][data-issues='2']");
        await view.InvokeAsync(() => view.Find("[data-system='1'] [data-bar='1']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal(new[] { "1", "2" }, view.FindAll(".pdfread-quote[data-bar='1']").Select(q => q.GetAttribute("data-staff"))));
        await view.InvokeAsync(() => view.Find("[data-system='1'] [data-bar='7'][data-issues='1']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.Single(view.FindAll(".pdfread-quote[data-bar='7']"));
        Assert.Empty(view.FindAll(".pdfread-quote[data-bar='1']"));
        await view.InvokeAsync(() => view.Find("[data-system='1'] [data-bar='8'][data-issues='0']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.Empty(view.FindAll(".pdfread-quote"));
        Assert.Equal("m8", host.Browser.Outlines[^1].Id);
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task SourceConfirmationSavesWithoutCallingApiAndSurvivesReopening()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        view.WaitForElement("[data-action=edit-source]:not([disabled])");
        Assert.NotNull(view.Find("[data-action=convert-start][disabled]"));
        await ConfirmBoundaries(view);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1 }));
        var source = await host.Store.GetConversionSourceAsync(piece.Id, hash);
        Assert.NotNull(source);
        Assert.Equal(2, source.Bars.Count);
        Assert.Equal(0, host.Claude.Calls);
        view.Dispose();
        var reopened = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        reopened.WaitForElement("[data-action=convert-start]:not([disabled])");
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task MatchingCountsDoNotMapASavedReadingWithoutTheSeparateConfirmation()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        await SaveReview(host, piece.Id);
        host.Browser.CutResult = [
            new([1], "blob:first", Enumerable.Range(0, 14).Select(i => 0.1 + 0.8 * i / 13).ToArray(), new(0.1, 0.1, 0.8, 0.3)),
            new([1], "blob:second", [0.1, 0.5, 0.9], new(0.1, 0.6, 0.8, 0.3))
        ];
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4), new(250, 450, 4) }]));
        view.WaitForElement("[data-action=edit-source]:not([disabled])");
        await view.InvokeAsync(() => view.Find("[data-action=edit-source]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=confirm-reading-map]:not([disabled])");
        await view.InvokeAsync(() => view.Find("[data-action=confirm-source]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=select-review-bar]");
        await view.InvokeAsync(() => view.Find("[data-system='1'] [data-action=select-review-bar][data-bar='7']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.Empty(view.FindAll("[data-source-bar]"));

        await view.InvokeAsync(() => view.Find("[data-action=edit-source]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=confirm-reading-map]:not([disabled])");
        await view.InvokeAsync(() => view.Find("[data-action=confirm-reading-map]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-action=confirm-source]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-source-bar]");
        var draft = System.Text.Json.JsonSerializer.Deserialize<ReadingDraft>((await host.Store.GetReadingAsync(piece.Id))!.Result, AssistSchema.Json)!;
        Assert.NotNull(draft.SourceRevision);
        Assert.Single(view.FindAll("[data-source-bar]"));
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task CompletedJobReopensForReviewWithoutResendingOrResettingKeptState()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Claude.SuccessfulReading = true;
        host.Claude.CostKnown = false;
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        await view.InvokeAsync(() => view.Find("[data-action=convert-start]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=keep-reading]:not([disabled])");
        Assert.NotNull(view.Find("[data-cost-kind=reading][data-cost-unknown=true]"));
        await host.ConversionHost.Execution!;
        var job = await host.Jobs.LatestAsync(piece.Id);
        Assert.Equal(ConversionState.Ready, job!.State);
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-action=select-review-bar]").Count));
        await view.InvokeAsync(() => view.Find("[data-action=select-review-bar]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-source-bar]");
        view.WaitForElement("[data-action=edit-bar]").Click();
        Assert.NotNull(view.Find(".pdfread-card > .pdfread-review-controls > [data-review-bar]"));
        Assert.Empty(view.FindAll(".pdfread-card > .pdfread-side [data-review-bar]"));
        var review = await host.Store.GetNotationReviewAsync(piece.Id, job.Id);
        var written = review!.Current.Bars[0].Written!;
        var note = written.Staves[0].Voices[0].Events[0];
        var corrected = written with { Staves = [written.Staves[0] with { Voices = [new(1, [note with { Pitches = [note.Pitches[0] with { Octave = 6 }] }])] }, written.Staves[1]] };
        await view.InvokeAsync(() => view.FindComponent<BarEditor>().Instance.OnSave.InvokeAsync(corrected));
        view.WaitForAssertion(() => Assert.Contains(MeiReader.Read(view.FindComponents<Sheet>()[0].Instance.Mei).Expected(), n => n.Midi == 84));
        Assert.Single(view.FindAll("[data-source-bar]"));
        await host.Store.MarkReadingKeptAsync(piece.Id);
        var saved = await host.Store.GetReadingAsync(piece.Id);
        var calls = host.Claude.Calls;
        view.Dispose();

        var reopened = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        reopened.WaitForElement("[data-action=keep-reading]:not([disabled])");
        Assert.NotNull(reopened.Find("[data-cost-kind=reading][data-cost-unknown=true]"));
        var after = await host.Store.GetReadingAsync(piece.Id);
        Assert.Equal(saved!.Result, after!.Result);
        Assert.Equal(saved.Kept, after.Kept);
        Assert.Equal(calls, host.Claude.Calls);
    }

    [Fact]
    public async Task HostStartFailurePersistsAnInterruptedJobBeforeAnyApiCall()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.ConversionHost.StartError = new InvalidOperationException("Host could not start");
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        await view.InvokeAsync(() => view.Find("[data-action=convert-start]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-job-state=Interrupted]");
        Assert.NotNull(view.Find("[data-action=resume-conversion]:not([disabled])"));
        Assert.Empty(view.FindAll("[data-action=acknowledge-resend]"));
        Assert.Equal(ConversionState.Interrupted, (await host.Jobs.LatestAsync(piece.Id))!.State);
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task ConversionNotificationLinkOpensTheReaderDirectly()
    {
        await using var host = new ComponentHost(new PdfBundle());
        var piece = await host.AddPiece(pdf: true, bundled: true);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("/library/" + piece.Id + "?conversion=1");
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement("[data-action=back-to-sheet]");
        Assert.Single(view.FindComponents<ReadNotes>());
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task TargetedRecheckRequiresConfirmationAndItsProposalRequiresExplicitApplication()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Claude.SuccessfulReading = true;
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        await view.InvokeAsync(() => view.Find("[data-action=convert-start]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await host.ConversionHost.Execution!;
        view.WaitForElement("[data-action=keep-reading]");
        await view.InvokeAsync(() => view.Find("[data-action=select-review-bar][data-bar='1']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=recheck-bar]");
        var before = await host.Store.GetReadingAsync(piece.Id);
        var original = await host.Jobs.LatestAsync(piece.Id);
        var calls = host.Claude.Calls;
        host.Claude.RecheckOctave = 6;
        host.Claude.CostKnown = false;
        await view.InvokeAsync(() => view.Find("[data-action=recheck-bar]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.NotNull(view.Find(".pdfread-review-controls [data-action=confirm-recheck]"));
        Assert.Equal(calls, host.Claude.Calls);
        await view.InvokeAsync(() => view.Find("[data-action=confirm-recheck]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await host.ConversionHost.Execution!;
        view.WaitForElement("[data-action=apply-proposal]:not([disabled])");
        Assert.NotNull(view.Find("[data-cost-kind=recheck][data-cost-unknown=true]"));
        Assert.NotNull(view.Find("[data-cost-kind=reading][data-cost-unknown=false]"));
        var recheck = await host.Jobs.LatestAsync(piece.Id);
        Assert.True(recheck!.IsRecheck);
        Assert.Single(recheck.TargetBarIds!);
        Assert.Single(host.Claude.Requests[^1].OwnedSourceBars);
        Assert.NotNull(host.Claude.Requests[^1].CurrentBar);
        Assert.Equal(before!.Result, (await host.Store.GetReadingAsync(piece.Id))!.Result);

        await view.InvokeAsync(() => view.Find("[data-action=apply-proposal]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        var draft = System.Text.Json.JsonSerializer.Deserialize<ReadingDraft>((await host.Store.GetReadingAsync(piece.Id))!.Result, AssistSchema.Json)!;
        Assert.Equal(original!.Id, draft.ConversionJobId);
        Assert.True(draft.CostKnown);
        var score = MeiReader.Read(draft.Mei);
        Assert.Contains(score.Expected(), n => n.Bar == 1 && n.Midi == 84);
        Assert.Contains(score.Expected(), n => n.Bar == 2 && n.Midi == 60);
        Assert.NotNull(view.WaitForElement("[data-action=apply-proposal][disabled]"));
        Assert.Equal(calls + 1, host.Claude.Calls);
    }

    [Fact]
    public async Task ReconvertWithTheSameSourceMapStartsAFreshReviewAndPreservesThePreviousSession()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Claude.SuccessfulReading = true;
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        await view.InvokeAsync(() => view.Find("[data-action=convert-start]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=keep-reading]");
        await host.ConversionHost.Execution!;
        var first = await host.Jobs.LatestAsync(piece.Id);
        await view.InvokeAsync(() => view.Find("[data-action=select-review-bar][data-bar='1']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=accept-bar]");
        await view.InvokeAsync(() => view.Find("[data-action=accept-bar]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.True((await host.Store.GetNotationReviewAsync(piece.Id, first!.Id))!.Current.Bars[0].Accepted);

        host.Claude.FullReadingOctave = 5;
        await view.InvokeAsync(() => view.Find("[data-action=new-conversion]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-action=convert-start]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=keep-reading]");
        await host.ConversionHost.Execution!;
        var second = await host.Jobs.LatestAsync(piece.Id);
        Assert.NotEqual(first.Id, second!.Id);
        Assert.Equal(first.Source.Revision, second.Source.Revision);
        await view.InvokeAsync(() => view.Find("[data-action=select-review-bar][data-bar='1']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=accept-bar]");
        var secondReview = await host.Store.GetNotationReviewAsync(piece.Id, second.Id);
        Assert.False(secondReview!.Current.Bars[0].Accepted);
        Assert.Equal(5, secondReview.Current.Bars[0].Written!.Staves[0].Voices[0].Events[0].Pitches[0].Octave);
        Assert.True((await host.Store.GetNotationReviewAsync(piece.Id, first.Id))!.Current.Bars[0].Accepted);
        var draft = System.Text.Json.JsonSerializer.Deserialize<ReadingDraft>((await host.Store.GetReadingAsync(piece.Id))!.Result, AssistSchema.Json)!;
        Assert.Equal(second.Id, draft.ConversionJobId);
        Assert.Equal(5, draft.Readings[0]!.Bars[0].Staves[0].Voices[0].Events[0].Pitches[0].Octave);
    }

    [Fact]
    public async Task WrittenOnlyAccidentalEditUpdatesTheDraftEvenWhenItsMeiDoesNotChange()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Claude.SuccessfulReading = true;
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        await view.InvokeAsync(() => view.Find("[data-action=convert-start]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=keep-reading]");
        await host.ConversionHost.Execution!;
        var job = await host.Jobs.LatestAsync(piece.Id);
        await view.InvokeAsync(() => view.Find("[data-action=select-review-bar][data-bar='1']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=edit-bar]").Click();
        var before = System.Text.Json.JsonSerializer.Deserialize<ReadingDraft>((await host.Store.GetReadingAsync(piece.Id))!.Result, AssistSchema.Json)!;
        var written = (await host.Store.GetNotationReviewAsync(piece.Id, job!.Id))!.Current.Bars[0].Written!;
        var note = written.Staves[0].Voices[0].Events[0];
        var changed = written with { Staves = [written.Staves[0] with { Voices = [new(1, [note with { Pitches = [note.Pitches[0] with { Accidental = Accidental.Natural }] }])] }, written.Staves[1]] };
        await view.InvokeAsync(() => view.FindComponent<BarEditor>().Instance.OnSave.InvokeAsync(changed));

        var after = System.Text.Json.JsonSerializer.Deserialize<ReadingDraft>((await host.Store.GetReadingAsync(piece.Id))!.Result, AssistSchema.Json)!;
        Assert.Equal(before.Mei, after.Mei);
        Assert.Equal(Accidental.Natural, after.Readings[0]!.Bars[0].Staves[0].Voices[0].Events[0].Pitches[0].Accidental);
        Assert.Single(view.FindAll("[data-action=select-review-bar][aria-pressed=true]"));
    }

    [Fact]
    public async Task ExhaustedRequestAllowanceRequiresExplicitPermissionForExactlyOneMoreRequest()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Browser.CutResult = [new([1], "blob:single", [0.1, 0.9], new(0.1, 0.1, 0.8, 0.3))];
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        await view.InvokeAsync(() => view.Find("[data-action=convert-start]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-job-state=Paused]");
        await host.ConversionHost.Execution!;
        Assert.NotNull(view.Find("[data-action=resume-conversion][disabled]"));
        Assert.Equal(1, host.Claude.Calls);
        Assert.Equal(1, (await host.Jobs.LatestAsync(piece.Id))!.Options.MaxRequests);
        await view.InvokeAsync(() => view.Find("[data-action=allow-additional-request]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-action=resume-conversion]:not([disabled])").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await host.ConversionHost.Execution!;
        view.WaitForElement("[data-job-state=Paused]");
        Assert.Equal(2, host.Claude.Calls);
        Assert.Equal(2, (await host.Jobs.LatestAsync(piece.Id))!.Options.MaxRequests);
        Assert.NotNull(view.Find("[data-action=resume-conversion][disabled]"));
        Assert.Equal("false", view.Find("[data-action=allow-additional-request]").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task NotationIssuesStaySeparateAndMapToTheSameStoredBarAfterReopening()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var saved = await SaveReview(host, piece.Id, finalTie: true);
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4), new(250, 450, 4) }]));

        view.WaitForElement("[data-notation-issue-count='1']");
        Assert.NotNull(view.Find("[data-flagged-bars='3']"));
        Assert.NotNull(view.Find("[data-system='2'] [data-bar='2'][data-issues='0'][data-notation-issues='1'] .pdfread-notation-count"));
        Assert.Empty(view.FindAll("[data-notation-issue]"));
        await view.InvokeAsync(() => view.Find("[data-system='2'] [data-action=select-review-bar][data-bar='2']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        var issue = view.Find("[data-system='2'] [data-notation-bar='2'] [data-notation-issue='DanglingTie']");
        Assert.Equal("1", issue.GetAttribute("data-staff"));
        Assert.Equal("1", issue.GetAttribute("data-voice"));
        Assert.Equal("0", issue.GetAttribute("data-event"));
        Assert.Equal(saved, (await host.Store.GetReadingAsync(piece.Id))!.Result);
        view.Dispose();

        var reopened = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4), new(250, 450, 4) }]));
        reopened.WaitForElement("[data-system='2'] [data-bar='2'][data-notation-issues='1']");
        await reopened.InvokeAsync(() => reopened.Find("[data-system='2'] [data-action=select-review-bar][data-bar='2']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.Single(reopened.FindAll("[data-notation-issue='DanglingTie']"));
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task CorrectingWrittenNotationRecomputesItsIssuesWithoutChangingModelOrUserFlags()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        host.Claude.SuccessfulReading = true;
        host.Claude.FinalTie = true;
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await ConfirmBoundaries(view);
        await view.InvokeAsync(() => view.Find("[data-action=convert-start]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-notation-issue-count='1']");
        await host.ConversionHost.Execution!;
        Assert.NotNull(view.Find("[data-flagged-bars='0']"));
        await view.InvokeAsync(() => view.Find("[data-action=select-review-bar][data-bar='2']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=edit-bar]").Click();
        var job = await host.Jobs.LatestAsync(piece.Id);
        var review = await host.Store.GetNotationReviewAsync(piece.Id, job!.Id);
        var written = review!.Current.Bars[1].Written!;
        var note = written.Staves[0].Voices[0].Events[0];
        var corrected = written with { Staves = [written.Staves[0] with { Voices = [new(1, [note with { Pitches = [note.Pitches[0] with { Tie = false }] }])] }, written.Staves[1]] };
        await view.InvokeAsync(() => view.FindComponent<BarEditor>().Instance.OnSave.InvokeAsync(corrected));

        view.WaitForElement("[data-notation-issue-count='0']");
        Assert.Empty(view.FindAll("[data-notation-issue], .pdfread-notation-count"));
        var after = await host.Store.GetNotationReviewAsync(piece.Id, job.Id);
        Assert.All(after!.Current.Bars, bar => { Assert.Empty(bar.ModelDoubts); Assert.Empty(bar.UserFlags); });
        Assert.Equal(1, host.Claude.Calls);
        view.Dispose();
        var reopened = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        reopened.WaitForElement("[data-action=keep-reading]");
        Assert.NotNull(reopened.Find("[data-notation-issue-count='0']"));
    }

    private static async Task ConfirmBoundaries<T>(IRenderedComponent<T> view) where T : Microsoft.AspNetCore.Components.IComponent
    {
        view.WaitForElement("[data-action=edit-source]:not([disabled])");
        await view.InvokeAsync(() => view.Find("[data-action=edit-source]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=confirm-source]:not([disabled])");
        await view.InvokeAsync(() => view.Find("[data-action=confirm-source]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        view.WaitForElement("[data-action=convert-start]:not([disabled])");
    }

    private static async Task<string> SaveReview(ComponentHost host, string pieceId, bool pickup = false, bool finalTie = false)
    {
        var systems = new List<SystemReading>();
        foreach (var count in new[] { 13, 2 })
        {
            var bars = new List<ReadBar>();
            for (var i = 1; i <= count; i++)
            {
                var value = pickup && systems.Count == 0 && i == 1 ? NoteValue.Quarter : NoteValue.Whole;
                var staves = new[] { 1, 2 }.Select(staff => new ReadStaff(staff, staff == 1 ? ReadClef.Treble : ReadClef.Bass,
                    [new ReadVoice(1, [new ReadEvent(ReadKind.Note, [new ReadPitch(Step.C, Accidental.None, 5 - staff, finalTie && systems.Count == 1 && i == count && staff == 1, 0)],
                        value, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])])).ToList();
                bars.Add(new ReadBar(i, 0, 4, 4, false, BarEnd.Normal, 0, staves));
            }
            systems.Add(new SystemReading(bars, []));
        }
        var (score, _) = ReadingScore.Build(systems);
        var first = pickup ? 0 : 1;
        Doubt[] doubts = [new(1, 1, first, 1, "Check pitch") { BarInSystem = 1 }, new(1, 1, first, 2, "Check rhythm") { BarInSystem = 1 },
            new(1, 1, first + 6, 2, "Check duration") { BarInSystem = 7 }, new(1, 1, first + 11, 1, "Check chord") { BarInSystem = 12 }];
        var result = new ReadingResult(true, "", score, ReadingScore.Mei(score), doubts,
            [new(1, 1, SystemStep.Doubtful, ""), new(1, 2, SystemStep.Done, "")], 0.1m, TimeSpan.FromSeconds(1)) { Readings = systems };
        var json = System.Text.Json.JsonSerializer.Serialize(ReadingDraft.Of(result, 1, 1), AssistSchema.Json);
        await host.Store.SaveReadingAsync(pieceId, json);
        return json;
    }

    private sealed class PdfBundle : ILibraryBundle
    {
        public Task<LibraryManifest?> ManifestAsync() => Task.FromResult<LibraryManifest?>(null);
        public Task<byte[]?> ReadAsync(string path) => Task.FromResult<byte[]?>([1]);
    }
}
