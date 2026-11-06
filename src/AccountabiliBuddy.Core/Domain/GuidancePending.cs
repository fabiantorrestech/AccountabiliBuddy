namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A switch to <see cref="GuidanceMode.Companion"/> waiting out the looser-change delay (ADR 68): asked here, or received from
/// another device.
/// </summary>
/// <param name="EditedAt">When the switch was asked (on whichever device); the mode's edit time once it applies.</param>
/// <param name="ApplyAt">When it may apply: <paramref name="EditedAt"/> plus this device's looser-change delay.</param>
/// <param name="DuringSession">
/// "Now" during a focus session, after paying what ending it early costs (its wait and a reason): it applies at
/// <paramref name="ApplyAt"/> even if the session still runs. False for every other switch, which waits for no session to run.
/// Never travels: a device that receives the mode holds it until its own session ends.
/// </param>
public sealed record GuidancePending(DateTimeOffset EditedAt, DateTimeOffset ApplyAt, bool DuringSession = false);
