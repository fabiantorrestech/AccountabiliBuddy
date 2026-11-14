using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "Shared" then "This device" groups of every label and tag menu (ADR 89). Shared = the Devices icon, This device = Laptop (the
/// "Keep on…" menu's meanings; the phone uses its twins). Without a sync group nothing can be device-only, so no headings.
/// </summary>
public static class ScopeMenu
{
    public static List<TaskMenuEntry> Group(bool linked, IReadOnlyList<(bool Shared, TaskMenuEntry Entry)> items)
    {
        if (!linked)
        {
            return items.Select(item => item.Entry).ToList();
        }

        var entries = new List<TaskMenuEntry>();
        foreach (bool shared in new[] { true, false })
        {
            List<TaskMenuEntry> of = items.Where(item => item.Shared == shared).Select(item => item.Entry with { Indented = true }).ToList();
            if (of.Count == 0 && !shared)
            {
                continue;
            }

            entries.Add(new TaskMenuEntry(shared ? "Shared" : "This device", IsHeading: true, Icon: shared ? MaterialIconKind.Devices : MaterialIconKind.Laptop));
            entries.AddRange(of);
        }

        return entries;
    }
}
