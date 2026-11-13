using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The id of a derived day value (ADR 79, GS1b): one entry per goal + source + measuring device + day, the same on every device and every
/// write, so a new reading updates it in place and a compact row can leave it out (<see cref="GoalEntryRows"/>). Only for the device-measured
/// sources (<see cref="GoalDerivedWriter.IsDerived"/>); Beeminder keeps its own id per goal + slug + day (the PC's Beeminder entry rule, <c>IdFor</c>,
/// no device), hand entries a random one. The phone's twin computes the same.
/// </summary>
public static class GoalDerivedIds
{
    private static readonly Guid NameSpace = new("7c1d2e94-3b5a-4f60-9e8d-0a7b6c5d4e31");

    /// <summary>
    /// SHA-1 over the namespace's bytes (RFC 4122 byte order) and the UTF-8 of
    /// <c>"&lt;goal id, D&gt;|&lt;source name&gt;|&lt;device id, D&gt;|yyyy-MM-dd"</c> (ids lower case, the source as its enum name, e.g.
    /// <c>ScreenTime</c>), as a version 5 id (the way <see cref="GoalReviewIds"/> makes a review's id).
    /// </summary>
    public static Guid For(Guid goalId, GoalEntrySource source, Guid deviceId, DateOnly day)
    {
        string text = string.Join('|',
            goalId.ToString("D"), source.ToString(), deviceId.ToString("D"), day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        byte[] space = NameSpace.ToByteArray(bigEndian: true);
        byte[] name = Encoding.UTF8.GetBytes(text);
        byte[] input = new byte[space.Length + name.Length];
        space.CopyTo(input, 0);
        name.CopyTo(input, space.Length);

        byte[] hash = SHA1.HashData(input);
        byte[] id = hash[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);
        return new Guid(id, bigEndian: true);
    }

    /// <summary>Whether <paramref name="entry"/> is a derived value under its own deterministic id (its source, device and day give its id).</summary>
    public static bool IsDerivedId(GoalEntry entry)
    {
        return GoalDerivedWriter.IsDerived(entry.Source) && entry.DeviceId is Guid device && entry.Id == For(entry.GoalId, entry.Source, device, entry.Day);
    }
}
