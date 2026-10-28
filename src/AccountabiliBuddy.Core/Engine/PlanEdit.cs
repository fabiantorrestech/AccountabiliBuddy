using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// A change to the day plan a Plan helper suggestion proposes: one date gets its own plan (a one-off day) with this template.
/// It is only a proposal; the app applies it through the normal day plan edit path, with its lock and its wait.
/// </summary>
/// <param name="Date">The date that gets its own plan.</param>
/// <param name="Template">The date's template with the change made.</param>
/// <param name="Note">The one-off day's note (the existing one is kept).</param>
public sealed record PlanEdit(DateOnly Date, DayTemplate Template, string? Note)
{
    /// <summary>The plan with <see cref="Date"/> planned as <see cref="Template"/>, replacing any one-off day there, in date order.</summary>
    public DayPlan ApplyTo(DayPlan plan)
    {
        Guid id = plan.OneOffDays.FirstOrDefault(day => day.Date == Date)?.Id ?? Guid.NewGuid();
        var day = new OneOffDay(id, Date, Template, Note);
        return plan with
        {
            OneOffDays = plan.OneOffDays.Where(other => other.Date != Date).Append(day).OrderBy(other => other.Date).ToList(),
        };
    }
}
