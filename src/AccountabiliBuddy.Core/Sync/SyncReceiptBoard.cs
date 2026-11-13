namespace AccountabiliBuddy.Core.Sync;

/// <summary>The colour of one dot on the sync troubleshooting board (ADR 86). Never the only signal: the UI pairs it with an icon and words.</summary>
public enum SyncDotColor
{
    Green,
    Yellow,
    Red,
    Grey,
}

/// <summary>Why a dot has its colour (the board's words; the UI picks the text).</summary>
public enum SyncDotReason
{
    /// <summary>The receiver confirms my newest file by this method (or this part's newest file was taken).</summary>
    Confirmed,

    /// <summary>The receiver's last file confirmed everything I had sent by then; my newest file is newer than that file, so it can't have said more yet.</summary>
    CaughtUp,

    /// <summary>The receiver has not written since my file reached this method: its next file will tell (normal).</summary>
    Waiting,

    /// <summary>The receiver has not sent a file at all yet.</summary>
    NoFileYet,

    /// <summary>The receiver confirms my newest file, but by another method.</summary>
    OtherMethod,

    /// <summary>The receiver is an older app: it says it holds my newest file, not how or which parts (or the receiver's file came by QR).</summary>
    OlderApp,

    /// <summary>The part is held back (a session, a wait, a clash).</summary>
    Held,

    /// <summary>The receiver wrote after my file reached this method and does not confirm my newest file.</summary>
    NotConfirmed,

    /// <summary>The receiver reports its last attempt to read my file by this method failed.</summary>
    Failed,

    /// <summary>The receiver refused the part (a clock too far ahead, a closed store).</summary>
    Refused,

    /// <summary>Files from the receiver stopped arriving by this method while it writes elsewhere.</summary>
    MethodQuiet,

    /// <summary>The receiver's receipt says nothing about this part.</summary>
    NoReport,

    /// <summary>The part is switched off on the receiver, or not carried by this method or file.</summary>
    NotTaken,

    /// <summary>A watch writes no file: it is reached through the phone.</summary>
    Watch,

    /// <summary>A removed device (ADR 54).</summary>
    Removed,

    /// <summary>No file from the receiver for a long time.</summary>
    Silent,

    /// <summary>My newest file has not been put on this method.</summary>
    NotSent,

    /// <summary>The receiver confirms my files by other methods only and has no record of this one (it does not use it): never red.</summary>
    MethodNotUsed,

    /// <summary>This device is alone in its group: nothing to confirm.</summary>
    NoOtherDevices,
}

/// <summary>One dot: a colour and why.</summary>
public sealed record SyncDot(SyncDotColor Color, SyncDotReason Reason);

/// <summary>What the sender knows about its own newest file (ADR 86).</summary>
/// <param name="Newest">The <c>WrittenAt</c> of this device's newest file.</param>
/// <param name="ReachedAt">Per method: when that file reached the method (a method it has not reached is missing).</param>
/// <param name="PreviousSentAt">
/// Per method: the newest <c>WrittenAt</c> of this device's files that had reached the method by the time of the receiver's latest file
/// (what that file could have confirmed); missing when nothing had.
/// </param>
public sealed record SyncSentFacts(
    DateTimeOffset Newest,
    IReadOnlyDictionary<string, DateTimeOffset> ReachedAt,
    IReadOnlyDictionary<string, DateTimeOffset>? PreviousSentAt = null);

/// <summary>What the sender knows about one other device (ADR 86).</summary>
/// <param name="Kind">The card's kind ("pc", "phone", "watch").</param>
/// <param name="Removed">Removed from the group (ADR 54).</param>
/// <param name="WrittenAt">The newest <c>WrittenAt</c> held from it, by any method; null when no file from it was ever read.</param>
/// <param name="WritesReceipts">Whether its latest file carries a <c>receipts</c> list (an app that predates ADR 86 does not, and neither does a QR file).</param>
/// <param name="Seen">Its card's <c>seen</c> value for this device (the newest file of this device it holds), for an older app.</param>
/// <param name="Receipt">Its receipt entry for this device; null when it has none.</param>
/// <param name="LastViaMethod">Per method: the newest <c>WrittenAt</c> of its files that this device read by that method.</param>
public sealed record SyncReceiverFacts(
    string Kind,
    bool Removed,
    DateTimeOffset? WrittenAt,
    bool WritesReceipts,
    DateTimeOffset? Seen,
    SyncReceipt? Receipt,
    IReadOnlyDictionary<string, DateTimeOffset>? LastViaMethod = null);

/// <summary>
/// The pure colour rule of the sync troubleshooting board (ADR 86), judged against the receiver's OWN latest file: a receiver that simply
/// has not written since my file reached the method is yellow, never red (receiving a file never triggers a write, so the receipt only
/// leaves with the receiver's next file). Red means the receiver wrote after and did not confirm, or reported a failure. Removed devices,
/// watches and long-silent devices are never red, and a device alone in its group has nothing to judge. The phone's twin is
/// <c>SyncReceiptBoard.kt</c>; the fixtures <c>sync1a/*.json</c> hold the cases.
/// </summary>
public static class SyncReceiptBoard
{
    /// <summary>A receiver's clock and the write itself may differ this much before "wrote after" holds.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    /// <summary>A device that sent no file for this long is "not seen since", grey.</summary>
    public static readonly TimeSpan Silent = TimeSpan.FromDays(30);

    /// <summary>A method is quiet after this many expected intervals.</summary>
    public const int QuietIntervals = 3;

    /// <summary>
    /// The dot of one sync method for one receiver; <paramref name="receiver"/> null = no other device in the group. A yellow dot turns red
    /// when files stopped arriving by this method while the receiver writes elsewhere (<see cref="Quiet"/>).
    /// </summary>
    /// <param name="expectedInterval">
    /// How often a receiver is expected to write when this method delivers by itself: the caller's real setting for the method (the folder's
    /// poll, Drive's, the relay's heartbeat). Null for a method used by hand (QR, file): never quiet.
    /// </param>
    public static SyncDot Method(string method, DateTimeOffset now, SyncSentFacts sent, SyncReceiverFacts? receiver, TimeSpan? expectedInterval)
    {
        SyncDot dot = Judge(method, now, sent, receiver);
        return dot.Color == SyncDotColor.Yellow ? Quiet(method, now, sent, receiver, expectedInterval) ?? dot : dot;
    }

    private static SyncDot Judge(string method, DateTimeOffset now, SyncSentFacts sent, SyncReceiverFacts? receiver)
    {
        if (receiver is null)
        {
            return new SyncDot(SyncDotColor.Grey, SyncDotReason.NoOtherDevices);
        }

        SyncDot? never = NeverRed(now, receiver);
        if (never is not null)
        {
            return never;
        }

        if (!sent.ReachedAt.TryGetValue(method, out DateTimeOffset reachedAt))
        {
            return new SyncDot(SyncDotColor.Grey, SyncDotReason.NotSent);
        }

        SyncReceipt? receipt = receiver.WritesReceipts ? receiver.Receipt : null;
        if (receipt?.ByMethod is not null && receipt.ByMethod.TryGetValue(method, out DateTimeOffset byMethod) && byMethod >= sent.Newest)
        {
            return new SyncDot(SyncDotColor.Green, SyncDotReason.Confirmed);
        }

        // A failure counts when it happened after my newest file reached the method (an old one-off, before that, is old news).
        if (receipt?.Failed is not null && receipt.Failed.ContainsKey(method)
            && !(receipt.FailedAt is not null && receipt.FailedAt.TryGetValue(method, out DateTimeOffset failedAt) && failedAt + Skew < reachedAt))
        {
            return new SyncDot(SyncDotColor.Red, SyncDotReason.Failed);
        }

        if (receiver.WrittenAt is not { } receiverWrote)
        {
            return new SyncDot(SyncDotColor.Yellow, SyncDotReason.NoFileYet);
        }

        // It confirms my files by other methods only and has no record of this one, either way: it does not use this method. Never red.
        if (receipt?.ByMethod is { Count: > 0 } used && !used.ContainsKey(method)
            && !(receiver.LastViaMethod is not null && receiver.LastViaMethod.ContainsKey(method)))
        {
            return new SyncDot(SyncDotColor.Grey, SyncDotReason.MethodNotUsed);
        }

        // Caught up as of its last file: it confirmed everything I had put on this method by then, and my newest file is newer than that.
        if (receipt?.ByMethod is not null && receipt.ByMethod.TryGetValue(method, out DateTimeOffset confirmed)
            && sent.PreviousSentAt is not null && sent.PreviousSentAt.TryGetValue(method, out DateTimeOffset previous) && confirmed >= previous
            && receiverWrote < reachedAt + Skew)
        {
            return new SyncDot(SyncDotColor.Green, SyncDotReason.CaughtUp);
        }

        DateTimeOffset held = receipt is not null ? receipt.Held : receiver.Seen ?? DateTimeOffset.MinValue;
        bool confirmsAny = held >= sent.Newest;
        if (!receiver.WritesReceipts)
        {
            // An older app (or a QR file): all it says is what it holds.
            if (confirmsAny || receiverWrote < reachedAt + Skew)
            {
                return new SyncDot(SyncDotColor.Yellow, SyncDotReason.OlderApp);
            }

            return new SyncDot(SyncDotColor.Red, SyncDotReason.NotConfirmed);
        }

        if (confirmsAny)
        {
            return new SyncDot(SyncDotColor.Yellow, SyncDotReason.OtherMethod);
        }

        if (receiverWrote < reachedAt + Skew)
        {
            return new SyncDot(SyncDotColor.Yellow, SyncDotReason.Waiting);
        }

        return new SyncDot(SyncDotColor.Red, SyncDotReason.NotConfirmed);
    }

    /// <summary>
    /// "Files stopped arriving by this method": the receiver's newest file is more than <see cref="QuietIntervals"/> expected intervals
    /// newer than the newest this device read from it by <paramref name="method"/> (or than my file reaching it), while it writes elsewhere.
    /// Only for methods that deliver by themselves (the caller's interval), and only when this device has read a file from the receiver by
    /// this method at least once (no record: nothing "stopped"); never for the cases <see cref="Method"/> keeps off red.
    /// </summary>
    private static SyncDot? Quiet(string method, DateTimeOffset now, SyncSentFacts sent, SyncReceiverFacts? receiver, TimeSpan? expectedInterval)
    {
        if (receiver is null || NeverRed(now, receiver) is not null || expectedInterval is not { } interval
            || !sent.ReachedAt.ContainsKey(method) || receiver.WrittenAt is not { } receiverWrote
            || receiver.LastViaMethod is null || !receiver.LastViaMethod.TryGetValue(method, out DateTimeOffset reference))
        {
            return null;
        }

        return receiverWrote - reference > interval * QuietIntervals ? new SyncDot(SyncDotColor.Red, SyncDotReason.MethodQuiet) : null;
    }

    /// <summary>The dot of one part for one receiver, from the receiver's receipt for this device.</summary>
    /// <param name="reachedAnyAt">When my newest file first reached any method (null: not sent anywhere).</param>
    public static SyncDot Part(string code, DateTimeOffset now, SyncSentFacts sent, DateTimeOffset? reachedAnyAt, SyncReceiverFacts? receiver)
    {
        if (receiver is null)
        {
            return new SyncDot(SyncDotColor.Grey, SyncDotReason.NoOtherDevices);
        }

        SyncDot? never = NeverRed(now, receiver);
        if (never is not null)
        {
            return never;
        }

        if (!receiver.WritesReceipts)
        {
            return new SyncDot(SyncDotColor.Yellow, SyncDotReason.OlderApp);
        }

        SyncPartReceipt? part = null;
        bool reported = receiver.Receipt?.Parts is not null && receiver.Receipt.Parts.TryGetValue(code, out part) && part is not null;
        if (!reported)
        {
            return new SyncDot(SyncDotColor.Yellow, SyncDotReason.NoReport);
        }

        switch (part!.State)
        {
            case SyncReceiptParts.Off:
            case SyncReceiptParts.Absent:
                return new SyncDot(SyncDotColor.Grey, SyncDotReason.NotTaken);
            case SyncReceiptParts.Refused when part.At >= sent.Newest:
                return new SyncDot(SyncDotColor.Red, SyncDotReason.Refused);
            case SyncReceiptParts.Held when part.At >= sent.Newest:
                return new SyncDot(SyncDotColor.Yellow, SyncDotReason.Held);
            case SyncReceiptParts.Taken when part.At >= sent.Newest:
                return new SyncDot(SyncDotColor.Green, SyncDotReason.Confirmed);
        }

        // It judged an older file of mine: wait for its next file, unless it wrote after my newest file went out.
        if (reachedAnyAt is not { } reached || receiver.WrittenAt is not { } receiverWrote || receiverWrote < reached + Skew)
        {
            return new SyncDot(SyncDotColor.Yellow, SyncDotReason.Waiting);
        }

        return new SyncDot(SyncDotColor.Red, SyncDotReason.NotConfirmed);
    }

    // Removed, watch and silent devices are grey, whatever else is true.
    private static SyncDot? NeverRed(DateTimeOffset now, SyncReceiverFacts receiver)
    {
        if (receiver.Removed)
        {
            return new SyncDot(SyncDotColor.Grey, SyncDotReason.Removed);
        }

        if (receiver.Kind == SyncDeviceCard.KindWatch)
        {
            return new SyncDot(SyncDotColor.Grey, SyncDotReason.Watch);
        }

        if (receiver.WrittenAt is { } wrote && now - wrote > Silent)
        {
            return new SyncDot(SyncDotColor.Grey, SyncDotReason.Silent);
        }

        return null;
    }
}
