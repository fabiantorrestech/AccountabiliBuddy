namespace AccountabiliBuddy.App.Services;

/// <summary>How much of a classified event the overlay let into the plan the guards read (ADR 58).</summary>
public enum ForceLevel
{
    /// <summary>None of it: the plan stands over the event's time.</summary>
    No,

    /// <summary>Some of its time holds the choice and some doesn't (a stricter event or a lunch inside it, for instance).</summary>
    Partly,

    /// <summary>All of it.</summary>
    Yes,
}
