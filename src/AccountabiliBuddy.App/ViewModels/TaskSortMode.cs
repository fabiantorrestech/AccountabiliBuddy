namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The filter's Sort section for the flat list. The due-date sorts use a Later item's due date (rows without one go last). Most
/// recent and Oldest use each row's own past-facing time: when it was parked, its session started, it was saved, done or (a Later
/// item) last edited.
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
