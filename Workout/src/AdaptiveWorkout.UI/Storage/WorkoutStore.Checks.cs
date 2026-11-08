using System.Globalization;
using Dapper;

namespace AdaptiveWorkout.UI.Storage;

/// <summary>
/// What I said about entries that looked like slips (see <see cref="Core.Checks"/>): days logged far short to count at maintenance,
/// or that are right as they are, and weigh-ins far off the line to keep.
/// </summary>
public sealed record CheckAnswers(IReadOnlySet<DateOnly> AtMaintenance, IReadOnlySet<DateOnly> RightDays, IReadOnlySet<long> KeptWeighIns)
{
    public static readonly CheckAnswers None = new(new HashSet<DateOnly>(), new HashSet<DateOnly>(), new HashSet<long>());

    /// <summary>The days I've answered either way, so they're not asked about again.</summary>
    public IReadOnlySet<DateOnly> AnsweredDays => AtMaintenance.Union(RightDays).ToHashSet();
}

public sealed partial class WorkoutStore
{
    private const string DayCheck = "day", WeighInCheck = "weigh-in";
    private const string AtMaintenanceAnswer = "maintenance", RightAnswer = "right", KeepAnswer = "keep";

    public Task<CheckAnswers> GetCheckAnswersAsync() => RunAsync(async c =>
    {
        var rows = (await c.QueryAsync<(string Kind, string Item, string Answer)>("SELECT Kind, Item, Answer FROM DataCheck")).AsList();
        HashSet<DateOnly> Days(string answer) => [.. rows.Where(r => r.Kind == DayCheck && r.Answer == answer).Select(r => DateOnly.ParseExact(r.Item, "yyyy-MM-dd", CultureInfo.InvariantCulture))];
        return new CheckAnswers(Days(AtMaintenanceAnswer), Days(RightAnswer),
            rows.Where(r => r.Kind == WeighInCheck && r.Answer == KeepAnswer).Select(r => long.Parse(r.Item, CultureInfo.InvariantCulture)).ToHashSet());
    });

    /// <summary>Counts a day at maintenance whatever it has logged, like a day I forgot; or with false, as logged again.</summary>
    public Task CountAtMaintenanceAsync(DateOnly day, bool atMaintenance) => AnswerAsync(DayCheck, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), atMaintenance ? AtMaintenanceAnswer : null);

    /// <summary>A day logged short that's right as it is.</summary>
    public Task MarkDayRightAsync(DateOnly day) => AnswerAsync(DayCheck, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), RightAnswer);

    /// <summary>A weigh-in far off the line that's right as it is.</summary>
    public Task KeepWeighInAsync(long weighInId) => AnswerAsync(WeighInCheck, weighInId.ToString(CultureInfo.InvariantCulture), KeepAnswer);

    // An answer, in place of any before; none takes it away.
    private Task AnswerAsync(string kind, string item, string? answer) => RunAsync(c => answer is null
        ? c.ExecuteAsync("DELETE FROM DataCheck WHERE Kind = @kind AND Item = @item", new { kind, item })
        : c.ExecuteAsync("INSERT INTO DataCheck (Kind, Item, Answer) VALUES (@kind, @item, @answer) ON CONFLICT (Kind, Item) DO UPDATE SET Answer = excluded.Answer", new { kind, item, answer }));
}
