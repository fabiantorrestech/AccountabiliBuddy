namespace AccountabiliBuddy.Relay;

internal enum PutStatus
{
    Stored,

    /// <summary>The same bytes were already stored under that name; nothing changed and nothing is pushed.</summary>
    Unchanged,

    NotOwner,
    TooManyFiles,
    TooManyGroups,

    /// <summary>Storing it would take the stored files past the relay's disk budget (<c>--max-disk-mb</c>).</summary>
    DiskFull,
}
