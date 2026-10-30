namespace AccountabiliBuddy.Core.Sync;

/// <summary>A device that stays in the group when others are removed (ADR 54), with the public key its card published.</summary>
/// <param name="PublicKeyBase64">The card's <see cref="SyncDeviceCard.PublicKey"/>.</param>
public sealed record RekeyRecipient(Guid DeviceId, string PublicKeyBase64);
