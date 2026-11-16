using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One Android device's role in the group (ADR 90), as the sealed sync files carry it in <see cref="SyncFile.DeviceRoles"/>: ids, a role
/// word and a time, nothing else. JSON keys in this order: <c>id</c>, <c>role</c>, <c>at</c>, <c>by</c>. Merged one entry per id by
/// <see cref="SyncDeviceRoles"/> (the newest <paramref name="At"/> wins). Twin: Kotlin <c>SyncDeviceRole</c>.
/// </summary>
/// <param name="Id">The Android device the role is for (lower-case D text).</param>
/// <param name="Role">One of the <c>Role</c> words. A word this app doesn't know is kept and written back unchanged (at most 32 letters and hyphens), and it behaves as <see cref="RoleOther"/>.</param>
/// <param name="At">When the role was set, from the trusted clock (the request time, ADR 90); a received time more than 2 minutes ahead of the receiver's now is ignored until time catches up.</param>
/// <param name="By">The device that set it.</param>
public sealed record SyncDeviceRole(Guid Id, string Role, DateTimeOffset At, Guid By)
{
    public const string RoleMain = "main";
    public const string RoleTablet = "tablet";
    public const string RoleOther = "other";

    /// <summary>
    /// "No role": resolves exactly as no entry (Unassigned with no main in the group, Other with one). It exists so a Cancel can put back a
    /// device that had no entry (<see cref="SyncDeviceRoles.Restore"/>): an entry can only be replaced by a newer one, never removed.
    /// </summary>
    public const string RoleNone = "none";
}
