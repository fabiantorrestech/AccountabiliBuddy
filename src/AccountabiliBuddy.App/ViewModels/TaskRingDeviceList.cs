using AccountabiliBuddy.App.Services.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The devices a "Rings on" list shows, read from the Devices &amp; sync page's rows (<see cref="DevicesViewModel.Rows"/>): this PC first,
/// then the other PCs and phones still in the group. A phone's linked watch rings through its phone, and a removed device can't ring,
/// so neither is listed.
/// </summary>
public static class TaskRingDeviceList
{
    public static IReadOnlyList<TaskRingDevice> From(IEnumerable<DeviceRowViewModel> rows, Guid self)
    {
        var devices = new List<TaskRingDevice>();
        foreach (DeviceRowViewModel row in rows)
        {
            if (row.Kind == "This PC")
            {
                devices.Insert(0, new TaskRingDevice(self, row.Name, IsPc: true, IsThisPc: true, "This PC"));
                continue;
            }

            if (row.Indented || row.State == DeviceSyncState.Removed || row.Kind is not ("PC" or "Phone")
                || !Guid.TryParse(row.DeviceKey, out Guid id) || id == Guid.Empty)
            {
                continue;
            }

            devices.Add(new TaskRingDevice(id, row.Name, row.Kind == "PC", IsThisPc: false, StatusOf(row)));
        }

        return devices;
    }

    // "Synced 1 min ago", "Last synced 2 days ago", "Not synced yet".
    private static string StatusOf(DeviceRowViewModel row)
    {
        return row.State switch
        {
            DeviceSyncState.UpToDate => $"Synced {row.AgoText}",
            DeviceSyncState.Behind => $"Last synced {row.AgoText}",
            _ => "Not synced yet",
        };
    }
}
