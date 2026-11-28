using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Music;
using Key = Prima.Core.Music.Key;
using Prima.Core.Scoring;
using Prima.UI.Components;
using Prima.UI.Practice;

namespace Prima.UI.Tests;

/// <summary>
/// A piece played in another key keeps one history: each run says which key it was in, the marks shown on a bar are a run's in
/// the key on the stand, and the history shows a run's bar in the key it was played in, its wrong keys named in it.
/// </summary>
public class TransposedRunsTests
{
    // A run in Wait for me with a wrong key on a note, as a run keeps it: the key played named as that key spells it.
    private static string Measures(string noteId, string played, (Transposition, string)? key, long session) =>
        KeptRun.Measures(new PlayOutcome(PlayMode.WaitForMe, null, new WaitResult(1, 1, 0, 1, 2), [], false, 1)
        {
            Marks = [new SheetMark(noteId, "wrong", played, 1)],
        }, 1, "both", session, false, new Dictionary<string, int> { [noteId] = 1 }, key: key);

    [Fact]
    public async Task ARunInAnotherKeyIsKeptAsOneAndShownInIt()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(bars: 2);
        var at = DateTimeOffset.Now.AddDays(-1);
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", at);
        var written = MeiReader.Read((await host.Store.GetMeiAsync(piece.Id))!);
        var expected = written.Expected();
        var up = Transposition.To(written, new Key(2));
        // As written, a D played for bar 1's C; then up in D major, an E played for its D.
        await host.Store.AddAttemptAsync(new(0, at.AddMinutes(1), DateOnly.FromDateTime(at.LocalDateTime), "piece", "wait", piece.Id, 1, null, null, null, "[]", null,
            Measures("t1", "D4", null, session), 0, false, null));
        await host.Store.AddAttemptAsync(new(0, at.AddMinutes(2), DateOnly.FromDateTime(at.LocalDateTime), "piece", "wait", piece.Id, 1, null, null, null, "[]", null,
            Measures("t1", "E4", (up, "D major"), session), 0, false, null));
        await host.Store.SaveSessionAsync(new(session, piece.Id, at, TimeSpan.FromMinutes(2), TimeSpan.Zero, true, at.AddMinutes(2), "standard", at.AddMinutes(2)));

        var runs = (await host.Store.GetPieceRunsAsync(piece.Id)).Select(r => r.Run).OrderBy(r => r.At).ToList();
        Assert.Equal([(Transposition.None, (string?)null), (new Transposition(1, 2), "D major")], runs.Select(r => (r.Transposed, r.KeyName)));
        Assert.Equal("Played through the piece in D major at your own pace", PieceWords.Short(runs[1]));
        // On the stand as written, bar 1 shows the run as written; in D major, the run in D major.
        var kept = await host.Store.GetRunMarksAsync([.. runs.Select(r => r.Id)]);
        Assert.Equal("D4", Assert.Single(KeptRun.MarksForBar(1, expected, runs, kept)).Played);
        Assert.Equal("E4", Assert.Single(KeptRun.MarksForBar(1, expected, runs, kept, up)).Played);

        // The history shows bar 1 as the latest run with marks there played it: in D major, its music moved there.
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"library/{piece.Id}/history?session={session}");
        var history = host.Context.Render<Pages.PracticeHistory>(p => p.Add(c => c.Id, piece.Id));
        await history.WaitForElement("[data-action=review-details]").ClickAsync(new MouseEventArgs());
        await history.InvokeAsync(() => history.FindComponent<DeepReview>().Instance.OnPick.InvokeAsync(1));
        history.WaitForAssertion(() => Assert.Contains(history.FindAll(".section-title"), title => title.TextContent == "Bar 1 · in D major"));
        var shown = MeiReader.Read(history.FindComponent<Sheet>().Instance.Mei);
        Assert.Equal(new Key(2), shown.Key);
        Assert.Equal(62, shown.Expected().First(n => n.Id == "t1").Midi);
    }
}
