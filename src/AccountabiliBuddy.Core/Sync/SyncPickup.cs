namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// How often the phone was picked up (unlocked) during another device's counted session (ADR 26: times and counts only),
/// as the phone's sync file carries it.
/// </summary>
/// <param name="DeviceId">The device whose session it is.</param>
/// <param name="SessionStart">That session's start, which names it.</param>
/// <param name="Count">Unlocks from the session's start until <paramref name="CountedUntil"/>.</param>
/// <param name="LongestDownMinutes">The longest stretch in that time without an unlock.</param>
/// <param name="CountedUntil">The session's end, or when the phone last counted a running one.</param>
/// <param name="EmergencyMinutes">"I need my phone for a bit" time in the session: shown apart, never a pickup (0 for none).</param>
public sealed record SyncPickup(
    Guid DeviceId, DateTimeOffset SessionStart, int Count, int LongestDownMinutes, DateTimeOffset CountedUntil, int EmergencyMinutes = 0);
