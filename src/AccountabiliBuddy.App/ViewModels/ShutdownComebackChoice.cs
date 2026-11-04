namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the routine's first step does with a parked thought. The order is the order of the choice box.</summary>
public enum ShutdownComebackChoice
{
    /// <summary>It stays under Come back to.</summary>
    Keep,

    /// <summary>It is ticked off.</summary>
    Done,

    /// <summary>It becomes a task on the next session's list (on Later instead when that list is full).</summary>
    Task,

    /// <summary>It goes to Later, with a due date when one is picked.</summary>
    Later,
}
