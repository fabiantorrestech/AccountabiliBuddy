namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A phone's session that would be live but for its file going quiet (ADR 85, rule R3): Start comes back with the muted line
/// "&lt;name&gt;'s session last heard from 2:05 PM".
/// </summary>
/// <param name="DeviceId">The phone.</param>
/// <param name="Name">The phone's own name.</param>
/// <param name="LastHeardAt">When its file was written, as the file says (not clamped).</param>
public sealed record GroupSessionStale(Guid DeviceId, string Name, DateTimeOffset LastHeardAt);
