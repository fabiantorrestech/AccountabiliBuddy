namespace AccountabiliBuddy.Core.Sync;

/// <summary>A device a rekey file (<see cref="RekeyCodec"/>) removes from the group: its id and the name it had (ADR 54).</summary>
public sealed record RekeyRemovedDevice(Guid DeviceId, string Name);
