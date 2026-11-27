using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Generation;
using Key = Prima.Core.Music.Key;
using Prima.Core.Routines;
using Prima.UI.Practice;
using Prima.UI.Components;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

public class HanonCourseTests
{
    [Fact]
    public async Task A_completed_pass_starts_the_next_key_and_stopping_that_pass_keeps_its_bookmark()
    {
        var clock = new SetClock();
        var piano = new TestPiano(plugged: true);
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        await host.Store.SaveTrackAsync("hanon:keys", new HanonPlace(41, Cycle: true));
        var view = host.Context.Render<HanonCourse>(p => p.Add(c => c.Mode, "keys"));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        var player = view.FindComponent<PlayAlong>().Instance;
        await view.InvokeAsync(() => player.Start());
        var sessions = host.Context.Services.GetRequiredService<SessionRegistry>();
        var firstRun = sessions.Current!;
        var notes = player.Music!.Notes;
        var first = notes[0];
        clock.NowNs = firstRun.TimeAt(0);
        piano.Play(first.Midi, clock.NowNs);
        piano.Release(first.Midi, clock.NowNs + 100_000_000);
        clock.NowNs = firstRun.TimeAt(notes.Max(n => n.Tick + n.Length) / (double)player.Music.BeatTicks + 2);
        view.WaitForAssertion(() =>
        {
            Assert.Equal(new Key(0,true), player.Music.Score.Key);
            Assert.NotNull(sessions.Current);
            Assert.NotSame(firstRun, sessions.Current);
        }, TimeSpan.FromSeconds(5));
        await view.InvokeAsync(player.StopAndSaveAsync);
        Assert.Equal(new Key(0,true), (await host.Store.GetTrackAsync<HanonPlace>("hanon:keys"))!.Key);
    }

    [Fact]
    public async Task Changing_the_study_and_key_resumes_in_the_player_without_moving_the_book()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var book = new HanonPlace(39, 0, true);
        await host.Store.SaveTrackAsync("hanon:book", book);
        var view = host.Context.Render<HanonCourse>(p => p.Add(c => c.Mode, "keys"));
        view.WaitForElement("#hanon-study").Change("6");
        view.WaitForAssertion(() => Assert.Equal("hanon-06", view.FindComponent<ExercisePractice>().Instance.Id));
        view.WaitForElement("[data-action='hanon-next']").Click();
        var next = Hanon.Keys(6)[1];
        view.WaitForAssertion(() => Assert.Equal(next, view.FindComponent<PlayAlong>().Instance.Music!.Score.Key));
        Assert.True((await host.Store.GetTrackAsync<HanonPlace>("hanon:keys"))!.Cycle);
        view.Dispose();
        var reopened = host.Context.Render<HanonCourse>(p => p.Add(c => c.Mode, "keys"));
        reopened.WaitForAssertion(() => Assert.Equal(next, reopened.FindComponent<PlayAlong>().Instance.Music!.Score.Key));
        Assert.Equal("hanon-06", reopened.FindComponent<ExercisePractice>().Instance.Id);
        Assert.Equal(book, await host.Store.GetTrackAsync<HanonPlace>("hanon:book"));
    }

    [Fact]
    public async Task Book_resume_uses_the_saved_minor_and_finishing_the_last_study_stays_finished()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        await host.Store.SaveTrackAsync("hanon:book", new HanonPlace(39, 0, true));
        var view = host.Context.Render<HanonCourse>(p => p.Add(c => c.Mode, "book"));
        view.WaitForAssertion(() => Assert.Equal(20, view.FindComponent<PlayAlong>().Instance.Music!.Score.Bars.Count));
        Assert.Equal(new Key(0, true), view.FindComponent<PlayAlong>().Instance.Music!.Score.Key);
        view.Find("#hanon-study").Change("60");
        view.WaitForAssertion(() => Assert.Equal("hanon-60", view.FindComponent<ExercisePractice>().Instance.Id));
        view.WaitForElement("[data-action='hanon-next']").Click();
        view.WaitForAssertion(() => Assert.Empty(view.FindComponents<PlayAlong>()));
        Assert.True((await host.Store.GetTrackAsync<HanonPlace>("hanon:book"))!.Complete);
        view.Dispose();
        var reopened = host.Context.Render<HanonCourse>(p => p.Add(c => c.Mode, "book"));
        reopened.WaitForElement("button.mu-primary").Click();
        reopened.WaitForAssertion(() => Assert.Equal("hanon-01", reopened.FindComponent<ExercisePractice>().Instance.Id));
    }
}
