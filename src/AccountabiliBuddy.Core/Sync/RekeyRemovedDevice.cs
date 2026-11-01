namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A device a rekey file (<see cref="RekeyCodec"/>) names (ADR 54): its id and the name it had. For a removed device,
/// <paramref name="Erase"/> is the remover's choice about its data (ADR 55): true = every staying device that applies the rekey
/// forgets what it received from it; false = they keep it, shown as removed. Always false for the maker entry, which is written
/// without the flag.
/// </summary>
public sealed record RekeyRemovedDevice(Guid DeviceId, string Name, bool Erase = false);
