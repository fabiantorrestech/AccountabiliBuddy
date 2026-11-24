using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A label group (ADR 93; "category" in the UI, "label group" in code and in the sync files, since "category" already names the label in
/// the old fields): a named, iconed heading the labels sit under. Always shared (no device list). Its id is <see cref="IdFor"/> its name,
/// so two devices that make "Work" offline make one group. A label names its group (<see cref="TaskLabel.GroupId"/>); a label whose group
/// is missing or deleted shows as uncategorised, and nothing rewrites the label for it.
/// </summary>
/// <param name="Name">Unique among live groups, case-insensitively. The user's text: sealed, never logged.</param>
/// <param name="Icon">A neutral icon key (<see cref="TaskLabelGroupIcons"/>); an unknown key shows the default icon.</param>
/// <param name="Position">Its place in the group list.</param>
/// <param name="EditedAt">Its newest change (name, icon, position, delete); the newest version wins (<see cref="Sync.SharedListMerge"/>).</param>
/// <param name="DeletedAt">Set on a tombstone; its labels then show uncategorised. Group tombstones are kept for good, like label tombstones.</param>
public sealed record TaskLabelGroup(
    Guid Id,
    string Name,
    string Icon,
    double Position,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    // A fixed namespace for the groups' name-based ids (an RFC 4122 version 5 style id), not the labels'; the phone's twin uses the same bytes.
    private static readonly Guid NameSpace = new("b3d6e1a2-7c94-4f05-8e2b-19a4c6d0f871");

    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    /// <summary>
    /// The group id for a name: SHA-1 over the namespace's bytes (RFC 4122 byte order) and the UTF-8 of the trimmed, invariant lower-cased
    /// name, as a version 5 id (as <see cref="TaskLabel.IdFor"/>, in a namespace of its own). The same on every device.
    /// </summary>
    public static Guid IdFor(string name)
    {
        byte[] space = NameSpace.ToByteArray(bigEndian: true);
        byte[] text = Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant());
        byte[] input = new byte[space.Length + text.Length];
        space.CopyTo(input, 0);
        text.CopyTo(input, space.Length);

        byte[] hash = SHA1.HashData(input);
        byte[] id = hash[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);
        return new Guid(id, bigEndian: true);
    }

    /// <summary>The group as a tombstone: its id, position and stamps only (no name, the default icon).</summary>
    public TaskLabelGroup Deleted(DateTimeOffset at)
    {
        return new TaskLabelGroup(Id, string.Empty, TaskLabelGroupIcons.Default, Position, at, DeletedAt: at);
    }
}
