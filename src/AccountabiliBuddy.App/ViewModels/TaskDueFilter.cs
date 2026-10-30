namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The filter's Due section: it applies to Later items (the only rows with a due date); other rows pass only with <see cref="Any"/>.</summary>
public enum TaskDueFilter
{
    Any,
    Overdue,
    Today,

    /// <summary>Today and the next six days.</summary>
    ThisWeek,
    NoDate,
}
