using System.Security.Cryptography;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>Reads a rekey for this device (ADR 54): removed, given the new key, or cut off.</summary>
public static class RekeyReader
{
    public static RekeyReading Read(RekeyPayload payload, Guid ownId, SyncDeviceKey ownKey, string oldGroupId)
    {
        if (payload.Removed.Any(removed => removed.DeviceId == ownId))
        {
            return new RekeyReading(RekeyReadingKind.Removed, null);
        }

        foreach (RekeyWrapRecord wrap in payload.Wraps.Where(wrap => wrap.DeviceId == ownId))
        {
            byte[]? secret = RekeyWrap.TryUnwrap(wrap, ownKey, oldGroupId);
            if (secret is null)
            {
                continue;
            }

            try
            {
                return new RekeyReading(RekeyReadingKind.NewKey, SyncKeyCode.FromSecret(secret));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        return new RekeyReading(RekeyReadingKind.CutOff, null);
    }
}
