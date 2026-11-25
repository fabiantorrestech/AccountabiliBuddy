using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What a filtered search needs besides the query and the filters (<see cref="TaskSearch.Matcher"/>).</summary>
/// <param name="Labels">The labels "@label" words are looked up in (full name or short name).</param>
/// <param name="Groups">The label categories, for the short names ("@ai" finds "Work - AI" under Work); null: full names only.</param>
/// <param name="LabelShown">ADR 89: whether this device shows a label; a task whose label it doesn't show reads as No label. Null: every label.</param>
/// <param name="TagShown">ADR 89: whether this device shows a tag; a tag it doesn't show is never matched. Null: every tag.</param>
/// <param name="Now">The trusted time "When" counts from.</param>
/// <param name="Zone">The zone that says which day it is.</param>
/// <param name="FirstDayOfWeek">The system's first day of the week ("This week").</param>
public sealed record TaskSearchContext(
    IReadOnlyList<TaskLabel> Labels,
    IReadOnlyList<TaskLabelGroup>? Groups,
    Func<TaskLabel, bool>? LabelShown,
    Func<string, bool>? TagShown,
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    DayOfWeek FirstDayOfWeek);
