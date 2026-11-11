using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The bins' names as the screens show them, and which bins are Today's and which the Shelf's (ADR 76).</summary>
public static class TaskBins
{
    public static IReadOnlyList<TaskBin> Today { get; } = [TaskBin.Session, TaskBin.Queued, TaskBin.Done];

    public static IReadOnlyList<TaskBin> Shelf { get; } = [TaskBin.Sort, TaskBin.Next, TaskBin.Later];

    public static bool IsShelf(TaskBin bin)
    {
        return bin is TaskBin.Sort or TaskBin.Next or TaskBin.Later;
    }

    public static string Name(TaskBin bin)
    {
        return bin switch
        {
            TaskBin.Session => "This session",
            TaskBin.Queued => "Queued",
            TaskBin.Done => "Finished today",
            TaskBin.Sort => "To sort",
            TaskBin.Next => "Next",
            TaskBin.Later => "Later",
            _ => bin.ToString(),
        };
    }
}
