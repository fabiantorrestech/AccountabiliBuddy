namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Search filters' "When" (Tasks redesign 7.28, 7.31): on the Shelf it reads a task's created date, in the Archive its finished or
/// deleted time. "This week" starts on the system's first day of the week.
/// </summary>
public enum TaskSearchWhen
{
    AnyTime,

    Today,

    ThisWeek,

    ThisMonth,

    /// <summary>From and To dates, both days included (the same date in both = one day); a blank end is open.</summary>
    Custom,
}
