namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>The outcome of <see cref="SyncRemovalService.RemoveAsync"/> (ADR 54).</summary>
/// <param name="Done">True when the rekey was written and this PC switched to the new key.</param>
/// <param name="Problem">In plain words why not, when <paramref name="Done"/> is false; nothing changed then.</param>
/// <param name="NotWrapped">Remaining devices that had no public key and so can't receive the new key: they are cut off like the removed ones.</param>
/// <param name="FailedMethods">How many sync methods failed to take the rekey file (others did): devices that sync only through those won't hear of it.</param>
public sealed record RemovalResult(bool Done, string? Problem, IReadOnlyList<Guid> NotWrapped, int FailedMethods)
{
    public static RemovalResult Failed(string problem) => new(false, problem, [], 0);
}
