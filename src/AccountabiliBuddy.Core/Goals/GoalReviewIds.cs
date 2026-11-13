using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The weekly review's id from its week (ADR 79): the same on every device, so two devices reviewing the same week make one record.
/// Records stored before with a random id fold into it (<see cref="GoalSyncMerge"/>). The phone's twin computes the same.
/// </summary>
public static class GoalReviewIds
{
    private static readonly Guid NameSpace = new("5e7a0c2b-91d4-4f3e-8b6a-2c4d9e1f7a30");

    /// <summary>
    /// SHA-1 over the namespace's bytes (RFC 4122 byte order) and the UTF-8 of the week's first day as "yyyy-MM-dd", as a version 5 id
    /// (the way <see cref="Domain.TaskLabel.IdFor"/> makes a label's id).
    /// </summary>
    public static Guid For(DateOnly week)
    {
        byte[] space = NameSpace.ToByteArray(bigEndian: true);
        byte[] text = Encoding.UTF8.GetBytes(week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        byte[] input = new byte[space.Length + text.Length];
        space.CopyTo(input, 0);
        text.CopyTo(input, space.Length);

        byte[] hash = SHA1.HashData(input);
        byte[] id = hash[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);
        return new Guid(id, bigEndian: true);
    }
}
