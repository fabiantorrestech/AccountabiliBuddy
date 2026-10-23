namespace AccountabiliBuddy.Core.Domain;

public static class CategoryBehaviorExtensions
{
    /// <summary>Planned work: Watch and Comms parts (the day bar's planned work, the work-hours guard).</summary>
    public static bool IsWork(this CategoryBehavior behavior)
    {
        return behavior is CategoryBehavior.Watch or CategoryBehavior.Comms;
    }
}
