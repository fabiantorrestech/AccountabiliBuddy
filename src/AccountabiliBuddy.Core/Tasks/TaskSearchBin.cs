namespace AccountabiliBuddy.Core.Tasks;

/// <summary>Search filters' "Bins" (Tasks redesign 7.16; the Shelf only, the Archive uses its pills).</summary>
public enum TaskSearchBin
{
    Next,

    Sort,

    Later,

    /// <summary>Today: This session and Queued (and Finished today).</summary>
    Today,
}
