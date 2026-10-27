namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Where the phone is during a PC focus session, as the phone's sync file carries it (phone placement): a label, times and
/// counts only. Never sensor values, and never the reason for work use (it stays on the phone).
/// </summary>
/// <param name="State">One of <see cref="Bag"/>, <see cref="Desk"/>, <see cref="FaceUp"/>, <see cref="Pocket"/>, <see cref="InUse"/>, <see cref="Unknown"/>; any other text reads as unknown.</param>
/// <param name="Since">When the phone got into this state.</param>
/// <param name="WorkUseUntil">When an allowed "need it for work" stretch ends; null for none.</param>
/// <param name="OutCount">Stretches in use or face up (without work use) so far in the session.</param>
/// <param name="PocketMinutes">Minutes in a pocket so far in the session.</param>
/// <param name="SessionStart">The PC session's start this belongs to, which names it.</param>
public sealed record SyncPlacement(
    string State, DateTimeOffset Since, DateTimeOffset? WorkUseUntil, int OutCount, int PocketMinutes, DateTimeOffset SessionStart)
{
    public const string Bag = "bag";
    public const string Desk = "desk";
    public const string FaceUp = "faceUp";
    public const string Pocket = "pocket";
    public const string InUse = "inUse";
    public const string Unknown = "unknown";

    /// <summary>True when the phone is out in the open: in use or face up.</summary>
    public bool IsOut => State is InUse or FaceUp;
}
