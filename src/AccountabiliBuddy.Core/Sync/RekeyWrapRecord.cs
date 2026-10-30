namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The new link code's secret wrapped for one remaining device (ADR 54), as a rekey file holds it.
/// </summary>
/// <param name="DeviceId">The device that can unwrap it.</param>
/// <param name="EphemeralPublicKey">The 65-byte public key of the one-time key pair the wrapper used (see <see cref="SyncDeviceKey"/>).</param>
/// <param name="Nonce">The AES-GCM nonce (12 bytes).</param>
/// <param name="Wrapped">The 25-byte secret encrypted, then the 16-byte tag (41 bytes).</param>
public sealed record RekeyWrapRecord(Guid DeviceId, byte[] EphemeralPublicKey, byte[] Nonce, byte[] Wrapped);
