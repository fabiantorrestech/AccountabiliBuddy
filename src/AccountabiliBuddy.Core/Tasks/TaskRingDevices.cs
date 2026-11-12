namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// "Rings on" lists (Tasks redesign, spec 03 §C/§D): which of the user's devices ring for task reminders, as device ids. Two lists hold
/// one: the account's (Settings › Alerts &amp; on-screen › Task reminders ring on, <c>SyncTaskAccountSettings.RingOn</c>) and each
/// label's (<c>TaskLabel.RingOn</c>). Null means every device, also one added later; a list names the phones that ring (PCs never need
/// to be on it: a PC always rings for its own reminders whatever a list says). Phones honour both lists (a phone rings for a task's
/// reminder only when both name it).
/// </summary>
public static class TaskRingDevices
{
    /// <summary>The longest list taken from a file or a backup (a group has far fewer devices).</summary>
    public const int MaxDevices = 64;

    /// <summary>Whether <paramref name="device"/> is on the list (null: every device is).</summary>
    public static bool Rings(IReadOnlyList<Guid>? ringOn, Guid device)
    {
        return ringOn is null || ringOn.Contains(device);
    }

    /// <summary>
    /// Whether a device rings for a reminder of a task with the label's list: a PC always does; any other device when both the account's
    /// and the label's lists name it.
    /// </summary>
    public static bool RingsFor(IReadOnlyList<Guid>? account, IReadOnlyList<Guid>? label, Guid device, bool isPc)
    {
        return isPc || (Rings(account, device) && Rings(label, device));
    }

    /// <summary>The list in its stored form: no empty id, each once, in id order; null stays null.</summary>
    public static IReadOnlyList<Guid>? Normalized(IReadOnlyList<Guid>? ringOn)
    {
        return ringOn?.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToList();
    }

    /// <summary>Two lists ring on the same devices (null only equals null).</summary>
    public static bool Same(IReadOnlyList<Guid>? first, IReadOnlyList<Guid>? second)
    {
        return string.Equals(Text(first), Text(second), StringComparison.Ordinal);
    }

    /// <summary>
    /// The list with the phone <paramref name="device"/> turned on or off. <paramref name="phones"/> are the phones shown (never PCs: a PC
    /// always rings, so it is never on a list and can't keep one from meaning "every device"); a list that then names all of them is
    /// stored as null (every device, also one linked later). A device that isn't one of <paramref name="phones"/> (a PC) changes nothing.
    /// </summary>
    public static IReadOnlyList<Guid>? Toggled(IReadOnlyList<Guid>? ringOn, IReadOnlyList<Guid> phones, Guid device, bool on)
    {
        if (!phones.Contains(device))
        {
            return Normalized(ringOn);
        }

        var devices = new HashSet<Guid>(ringOn ?? phones);
        if (on)
        {
            devices.Add(device);
        }
        else
        {
            devices.Remove(device);
        }

        if (phones.Where(id => id != Guid.Empty).All(devices.Contains))
        {
            return null;
        }

        return Normalized(devices.ToList());
    }

    /// <summary>The list as one text, to compare and to break merge ties: "*" for every device.</summary>
    public static string Text(IReadOnlyList<Guid>? ringOn)
    {
        return ringOn is null ? "*" : string.Join(",", Normalized(ringOn)!.Select(id => id.ToString("D")));
    }
}
