namespace AccountabiliBuddy.Relay;

internal enum PutStatus
{
    Stored,

    /// <summary>The same bytes were already stored under that name; nothing changed and nothing is pushed.</summary>
    Unchanged,

    NotOwner,
    TooManyFiles,
    TooManyGroups,
}
