namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The shutdown routine's memory (document "shutdown", device-local, never synced). Everything is optional so a document written by
/// an older version, which held only <paramref name="LastShownOn"/>, still reads.
/// </summary>
/// <param name="LastShownOn">The last plan date whose card opened by itself, so it never opens twice a day.</param>
/// <param name="Settings">The routine's settings; null = the defaults (on).</param>
/// <param name="Days">The days the routine was finished, newest last.</param>
/// <param name="OpenFor">The plan date of a card that is open (kept so a restart brings it back); null = none open.</param>
/// <param name="OpenedAt">When that card opened.</param>
/// <param name="OpenedByHand">That card was started with "Start shutdown", not by the end of work.</param>
/// <param name="NotNowOn">The plan date whose card was put off with "Not now": it returns once, then stays closed.</param>
/// <param name="ReturnAt">When the put-off card comes back; null = nothing waiting.</param>
/// <param name="RemindedOn">The last plan date the "work ends soon" reminder came for.</param>
public sealed record ShutdownState(
    DateOnly? LastShownOn,
    ShutdownSettings? Settings = null,
    IReadOnlyList<ShutdownDay>? Days = null,
    DateOnly? OpenFor = null,
    DateTimeOffset? OpenedAt = null,
    bool OpenedByHand = false,
    DateOnly? NotNowOn = null,
    DateTimeOffset? ReturnAt = null,
    DateOnly? RemindedOn = null);
