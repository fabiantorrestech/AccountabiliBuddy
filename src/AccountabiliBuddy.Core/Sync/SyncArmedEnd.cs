namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A wait running in the background on a PC (ADR 66): an end confirmed early that applies itself at <paramref name="ReadyAt"/>.
/// Times only, never the reason. The phone shows it with Cancel, which reaches the PC as <see cref="SyncAction.KindArmedCancel"/>.
/// </summary>
/// <param name="Id">The armed end's id; a cancel names it.</param>
/// <param name="Kind">One of the <c>Kind…</c> constants; a reader shows a kind it doesn't know with a plain label.</param>
public sealed record SyncArmedEnd(Guid Id, string Kind, DateTimeOffset ReadyAt)
{
    public const string KindEndSession = "endSession";
    public const string KindEndWorkEarly = "endWorkEarly";
    public const string KindStopForToday = "stopForToday";
    public const string KindPreWorkDayOff = "preWorkDayOff";
}
