using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>What one monitor showed at one evaluation, with the context needed to log it.</summary>
/// <param name="Classification">The focus classification; pass Unknown outside focus phases.</param>
public sealed record ObservedActivity(
    ActivitySnapshot Snapshot,
    Classification Classification,
    Guid? ProfileId,
    bool InFocusSession);
