namespace AccountabiliBuddy.Core.Domain;

public enum UnlockStatus
{
    /// <summary>No unlock, or it has relocked.</summary>
    None,

    /// <summary>Counting down; the blocks still apply.</summary>
    Waiting,

    /// <summary>The blocks are lifted: no scheduled blocks start and nothing is locked.</summary>
    Unlocked,
}
