using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Playground.AI.Assist;
using Prima.Core.Library;
using Prima.UI.Host;
using Prima.UI.Transcription;

namespace Prima.UI.Tests;

/// <summary>
/// From the review of main at a7ab94d: Settings in tabs, and the problem log. Each fails on that code; the place in it is given
/// with each.
/// </summary>
public class SettingsEdgeTests
{
    // ReadNotes.razor:167 (and :255, for a key Claude turned down) still link to plain "settings", which since 14e66e4 opens on
    // the Practice tab, with nothing about the key on it. The routine screens' links were moved to settings?tab=claude
    // (RoutineAsk.razor:119, RoutineReview.razor:134); these two weren't.
    [Fact]
    public async Task OpenSettingsForAMissingKeyOpensTheClaudeTab()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        await host.Context.Services.GetRequiredService<IAssistKeyStore>().RemoveAsync();

        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));

        Assert.Equal("settings?tab=claude", view.WaitForElement(".pdfread-error a.mu-primary").GetAttribute("href"));
    }

    // ProblemLog.cs:45-50 sets in a line of the detail that starts like an entry ("=== "), but not the message's own first line,
    // and :113-118 reads an entry up to the next line that starts so. A problem whose message starts "=== " reads back with no
    // message and no detail.
    [Fact]
    public void AProblemWhoseMessageLooksLikeAnEntryReadsBackWhole()
    {
        var log = new ProblemLog(Path.Combine(Path.GetTempPath(), $"prima-problems-{Guid.NewGuid():N}.log"));
        try
        {
            log.Keep("Error", "Prima.Test", "=== boom", "detail");

            var only = Assert.Single(log.Recent());
            Assert.Equal("=== boom", only.Message.Trim());
            Assert.Equal("detail", only.Detail);
        }
        finally
        {
            log.Clear();
        }
    }
}
