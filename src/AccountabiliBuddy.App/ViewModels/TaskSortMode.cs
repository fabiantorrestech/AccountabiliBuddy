namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The filter's Sort section for the flat list. A row's "date" is its own: its due date if it has one, else when it was parked,
/// started, saved, done or (a Later item) last edited. Rows without a due date go last under the due-date sorts.
/// </summary>
public enum TaskSortMode
{
    /// <summary>Come back, Later (due first), Unfinished, Auto-saved, Done.</summary>
    Default,
    DueAscending,
    DueDescending,
    MostRecent,
    Oldest,
}
