namespace AccountabiliBuddy.Core.Tasks;

/// <summary>How long a toast stays (spec 05 section 14): the kind decides, <see cref="TaskToastTiming.For"/> gives the time.</summary>
public enum TaskToastLength
{
    /// <summary>A plain message: 4 seconds.</summary>
    Plain,

    /// <summary>A message with Undo or an action button: 6 seconds.</summary>
    WithUndo,

    /// <summary>An explanation (what a swap or an undo did, why something wasn't done): 7 seconds.</summary>
    Explanation,

    /// <summary>"N more reminders waiting": 8 seconds.</summary>
    Waiting,
}
