namespace AccountabiliBuddy.Core.Sync;

/// <summary>What <see cref="SyncFileCodec.TryOpen"/> found: the file, or why it did not open.</summary>
public sealed record SyncFileOpenResult(SyncFile? File, SyncFileOpenFailure? Failure)
{
    /// <summary>The reason as a receipt code (<see cref="SyncReceiptParts.Failures"/>); null when the file opened.</summary>
    public string? Code => Failure switch
    {
        SyncFileOpenFailure.Damaged => "damaged",
        SyncFileOpenFailure.OtherGroup => "otherGroup",
        SyncFileOpenFailure.TooBig => "tooBig",
        SyncFileOpenFailure.NewerFormat => "newerFormat",
        _ => null,
    };
}
