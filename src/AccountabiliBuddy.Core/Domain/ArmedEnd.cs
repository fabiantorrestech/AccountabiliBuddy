namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A waited end confirmed before its wait was over (ADR 66): it applies itself at <paramref name="ReadyAt"/> and can be
/// cancelled until then. The wait counts from <paramref name="RequestedAt"/>, when the window asking for it opened, so
/// confirming early never shortens it.
/// </summary>
/// <param name="Id">Stable id; a device cancels it by this id (sync action "armedCancel").</param>
/// <param name="SealedReason">The typed reason, DPAPI-protected and base64; empty for a kind that takes none. Never synced.</param>
/// <param name="ProfileId">For <see cref="ArmedEndKind.EndSession"/>: the profile of the session it ends; it is dropped if another runs.</param>
/// <param name="WorkEnd">For <see cref="ArmedEndKind.EndWorkEarly"/>: the work it ends.</param>
public sealed record ArmedEnd(
    Guid Id,
    ArmedEndKind Kind,
    DateTimeOffset RequestedAt,
    DateTimeOffset ReadyAt,
    string SealedReason,
    Guid? ProfileId = null,
    ArmedWorkEnd? WorkEnd = null);
