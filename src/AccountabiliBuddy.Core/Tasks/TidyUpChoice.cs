namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What the weekly tidy-up can do with a task (spec 05 section 9).</summary>
public enum TidyUpChoice
{
    /// <summary>"Still matters. Resets its age."</summary>
    Keep,

    /// <summary>"Do it soon."</summary>
    MoveToNext,

    /// <summary>"Out of the way. It stays in History."</summary>
    Archive,

    /// <summary>"Gone for good after 30 days in History."</summary>
    Delete,
}
