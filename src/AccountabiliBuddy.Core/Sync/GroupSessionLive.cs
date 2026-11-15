namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A focus session that is live on another device of the group (ADR 85, rules R1 and R2), as <see cref="GroupSessions.LiveElsewhere"/>
/// reads it from that device's sync file.
/// </summary>
/// <param name="DeviceId">The device that runs it.</param>
/// <param name="Name">The device's own name, for "Running on Pixel · 32 min left".</param>
/// <param name="Kind"><see cref="SyncDeviceCard.KindPc"/> or <see cref="SyncDeviceCard.KindPhone"/>.</param>
/// <param name="EndsAt">When it ends (while away: moved on by the time away so far); null for a session with no set end.</param>
/// <param name="Paused">The timer is paused (the file says the user is away).</param>
/// <param name="NoSetEnd">The session has no set end.</param>
public sealed record GroupSessionLive(Guid DeviceId, string Name, string Kind, DateTimeOffset? EndsAt, bool Paused, bool NoSetEnd);
