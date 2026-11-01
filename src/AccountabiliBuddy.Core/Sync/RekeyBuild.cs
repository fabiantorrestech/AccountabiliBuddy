namespace AccountabiliBuddy.Core.Sync;

/// <summary>A rekey made by <see cref="RekeyBuilder"/> (ADR 54).</summary>
/// <param name="SealedFile">The rekey file's bytes, sealed with the old key; written to every method (<see cref="RekeyCodec.FileNameFor"/>).</param>
/// <param name="NewCode">The new link code (40 characters); the maker switches to it and keeps it only under DPAPI.</param>
/// <param name="Payload">What the file says.</param>
/// <param name="NotWrapped">The remaining devices that got no wrap because they had no valid public key; they are cut off like the removed ones.</param>
public sealed record RekeyBuild(byte[] SealedFile, string NewCode, RekeyPayload Payload, IReadOnlyList<Guid> NotWrapped);
