using System.Security.Cryptography;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>Makes a rekey (ADR 54): a new link code, wrapped for every remaining device that has a public key.</summary>
public static class RekeyBuilder
{
    /// <param name="oldKey">The group's key now; it seals the file, so the removed devices can read that they were removed.</param>
    /// <param name="removed">The devices to remove.</param>
    /// <param name="recipients">The devices that stay (not the maker: it already has the new code).</param>
    public static RekeyBuild Build(SyncKey oldKey, IReadOnlyList<RekeyRemovedDevice> removed, IReadOnlyList<RekeyRecipient> recipients, DateTimeOffset now)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(SyncKey.SecretBytes);
        try
        {
            var wraps = new List<RekeyWrapRecord>();
            var notWrapped = new List<Guid>();
            foreach (RekeyRecipient recipient in recipients)
            {
                RekeyWrapRecord? wrap = TryWrap(secret, recipient, oldKey.GroupId);
                if (wrap is null)
                {
                    notWrapped.Add(recipient.DeviceId);
                }
                else
                {
                    wraps.Add(wrap);
                }
            }

            var payload = new RekeyPayload(now.ToUnixTimeSeconds(), removed, wraps);
            return new RekeyBuild(RekeyCodec.Seal(payload, oldKey), SyncKeyCode.FromSecret(secret), payload, notWrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static RekeyWrapRecord? TryWrap(byte[] secret, RekeyRecipient recipient, string oldGroupId)
    {
        try
        {
            return RekeyWrap.Create(secret, recipient.DeviceId, Convert.FromBase64String(recipient.PublicKeyBase64), oldGroupId);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
