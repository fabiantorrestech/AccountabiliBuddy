namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The Archive sheet's filter pills (Tasks redesign 6.09): which of its columns show.</summary>
public enum ArchivePill
{
    /// <summary>Unfinished and Done open, Recently deleted folded to its rail.</summary>
    Everything,

    /// <summary>Only Unfinished, open.</summary>
    Unfinished,

    /// <summary>Only Done, open.</summary>
    Done,

    /// <summary>Only Recently deleted, open.</summary>
    Deleted,
}
