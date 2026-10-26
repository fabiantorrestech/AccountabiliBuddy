using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The PC's answer to a phone's <see cref="SyncAction.KindEndEarlyBegin"/> (ADR 42), in its file's
/// <see cref="SyncFile.EndEarly"/>: the phone counts down to <paramref name="AllowedFrom"/> (the PC's clock), asks for a
/// reason of at least <paramref name="MinReasonLength"/> characters and sends <see cref="SyncAction.KindEndEarly"/>
/// with <paramref name="RequestId"/> and the reason's length only. Taken until <paramref name="AllowedFrom"/> plus
/// <see cref="ValidFor"/>, and only in the phase it was made in.
/// </summary>
/// <param name="RequestId">What the phone's end-early action names.</param>
/// <param name="PhaseStartedAt">The start of the focus phase it belongs to, as it was when offered.</param>
/// <param name="AllowedFrom">When the PC's wait is over.</param>
/// <param name="MinReasonLength">The reason's minimum length, by the PC's rules.</param>
/// <param name="Note">The PC's text for the phone to show; null for none.</param>
public sealed record SyncEndEarlyOffer(
    Guid RequestId,
    DateTimeOffset PhaseStartedAt,
    DateTimeOffset AllowedFrom,
    int MinReasonLength,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note = null)
{
    /// <summary>How long after <see cref="AllowedFrom"/> the phone's end-early action is still taken.</summary>
    public static readonly TimeSpan ValidFor = TimeSpan.FromMinutes(10);
}
