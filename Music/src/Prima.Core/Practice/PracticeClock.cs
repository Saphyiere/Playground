namespace Prima.Core.Practice;

/// <summary>
/// A practice session's clock. Its time is playing or idle: playing while a run goes, and for <see cref="Keep"/> after each key
/// played, so going over a bar on your own with the music stopped counts; idle the rest (looking at the music, working out a
/// fingering, a drink of water). After <see cref="PauseAfter"/> with nothing played it pauses, counting up to then, as it does
/// while you're away; paused it counts nothing, until you carry on or play again.
/// </summary>
public sealed class PracticeClock
{
    /// <summary>How long a key keeps you playing: the gaps of playing a bar over on your own (ours).</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromSeconds(10);

    /// <summary>How long with nothing played before the session pauses to ask whether you're still practising.</summary>
    public static readonly TimeSpan PauseAfter = TimeSpan.FromMinutes(15);

    // The time counted up to; until when a key keeps you playing; whether a run is going; and the last thing done, played or a
    // carrying on, from which the pause is counted.
    private DateTimeOffset counted;
    private DateTimeOffset playingUntil;
    private bool running;
    private DateTimeOffset active;

    public PracticeClock(DateTimeOffset start, TimeSpan playing = default, TimeSpan idle = default, bool paused = false, DateTimeOffset? lastPlayed = null)
    {
        (counted, playingUntil, active) = (start, start, start);
        (Playing, Idle, Paused, LastPlayed) = (playing, idle, paused, lastPlayed ?? start);
    }

    public TimeSpan Playing { get; private set; }

    public TimeSpan Idle { get; private set; }

    public bool Paused { get; private set; }

    /// <summary>When something was last played: a key, or a run's end. A session finished from a pause ends then.</summary>
    public DateTimeOffset LastPlayed { get; private set; }

    /// <summary>Counts the time up to <paramref name="now"/>, and pauses once nothing's been played for <see cref="PauseAfter"/>, counting up to then. True when it paused now.</summary>
    public bool Advance(DateTimeOffset now)
    {
        if (now <= counted) return false;
        if (Paused)
        {
            counted = now;
            return false;
        }

        var limit = running || now < active + PauseAfter ? now : active + PauseAfter;
        var span = limit > counted ? limit - counted : TimeSpan.Zero;
        var playing = running ? span : TimeSpan.FromTicks(Math.Clamp((playingUntil - counted).Ticks, 0, span.Ticks));
        (Playing, Idle, counted) = (Playing + playing, Idle + span - playing, now);
        if (limit == now) return false;

        Paused = true;
        return true;
    }

    /// <summary>A key played: playing for <see cref="Keep"/> from now. Played while paused, the session carries on.</summary>
    public void Key(DateTimeOffset at)
    {
        Played(at);
        playingUntil = at + Keep;
    }

    /// <summary>A run started: playing until it ends, however long. Started while paused, the session carries on.</summary>
    public void RunStarted(DateTimeOffset at)
    {
        Played(at);
        running = true;
    }

    /// <summary>A run ended: playing for <see cref="Keep"/> more, as after a key.</summary>
    public void RunEnded(DateTimeOffset at)
    {
        Advance(at);
        running = false;
        (LastPlayed, active, playingUntil) = (at, at, at + Keep);
    }

    /// <summary>Paused now: leaving the piece, or the tablet sleeping.</summary>
    public void Pause(DateTimeOffset at)
    {
        Advance(at);
        (Paused, running) = (true, false);
    }

    /// <summary>Carrying on after a pause: the time counts again from now, and the pause is counted afresh.</summary>
    public void Resume(DateTimeOffset at)
    {
        if (!Paused) return;

        (Paused, counted, active, playingUntil) = (false, at, at, at);
    }

    private void Played(DateTimeOffset at)
    {
        Advance(at);
        Resume(at);
        (LastPlayed, active) = (at, at);
    }
}
