using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A label (ADR 76): the coloured dot a task has at most one of. Replaces the task category (<see cref="TaskCategory"/>) in the
/// UI; the old sync fields still carry it as a category by name and opaque colour. Its id is <see cref="IdFor"/> its name, so
/// every device that only knows the name (an old field) reaches the same label.
/// </summary>
/// <param name="Name">Unique among labels, case-insensitively.</param>
/// <param name="Color">"#RRGGBB", "#RRGGBBAA" or "rgba(r,g,b,a)" (<see cref="TaskLabelColors"/>).</param>
/// <param name="Position">Its place in the label list.</param>
/// <param name="EditedAt">Its newest change (name, colour, rings on, position); the newest version wins (rings on: see <paramref name="RingOnStamp"/>).</param>
/// <param name="RingOn">The phones its tasks' reminders ring on (device ids); null for every device. A PC always rings for its own reminders.</param>
/// <param name="DeletedAt">Set on a tombstone; its tasks become "No label".</param>
/// <param name="RingOnStamp">
/// When and on which device <paramref name="RingOn"/> was chosen; null when never. Rings on merges on its own, newest stamp first
/// (<see cref="Sync.TaskLabelMerge"/>), so another change of the label (an older phone's category colour read on a PC that hasn't
/// seen the choice yet) never takes it back.
/// </param>
public sealed record TaskLabel(
    Guid Id,
    string Name,
    string Color,
    double Position,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? RingOn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Sync.TaskFieldStamp? RingOnStamp = null)
{
    // A fixed namespace for the name-based ids (an RFC 4122 version 5 style id); the phone's twin uses the same bytes.
    private static readonly Guid NameSpace = new("6f1b9a4e-3c2d-4e8b-9a71-5d0c2b7e4f13");

    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    /// <summary>
    /// The label id for a name: SHA-1 over the namespace's bytes (RFC 4122 byte order) and the UTF-8 of the trimmed, invariant
    /// lower-cased name, as a version 5 id. The same on every device, so "Work" and "work" are one label.
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

    /// <summary>Whether two names are the same label (case-insensitive, trimmed).</summary>
    public static bool SameName(string first, string second)
    {
        return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The label as a tombstone: no name or colour left.</summary>
    public TaskLabel Deleted(DateTimeOffset at)
    {
        return new TaskLabel(Id, string.Empty, TaskLabelColors.Set[0], Position, at, DeletedAt: at);
    }
}
