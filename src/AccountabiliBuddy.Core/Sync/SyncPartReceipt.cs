namespace AccountabiliBuddy.Core.Sync;

/// <summary>The outcome of the last accept of one part of a sender's file (ADR 86).</summary>
/// <param name="At">The <c>WrittenAt</c> of the sender's file that was judged.</param>
/// <param name="State">One of <see cref="SyncReceiptParts.States"/>: taken, held, off, absent or refused.</param>
public sealed record SyncPartReceipt(DateTimeOffset At, string State);
