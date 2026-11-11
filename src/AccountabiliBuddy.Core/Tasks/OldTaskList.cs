namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The old sync field a bin's tasks travel in for apps older than ADR 76.</summary>
public enum OldTaskList
{
    /// <summary><c>nextTasks</c>: This session, Queued, Finished today.</summary>
    NextTasks,

    /// <summary><c>later</c>: Next and Later.</summary>
    Later,

    /// <summary><c>parked</c> (and the legacy <c>comebacks</c>): To sort.</summary>
    Parked,
}
