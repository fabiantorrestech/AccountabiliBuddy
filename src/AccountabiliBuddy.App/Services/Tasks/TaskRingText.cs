using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>The words for a "Rings on" list (<see cref="TaskRingDevices"/>) over the devices shown: "All devices", "Work PC only", "Work PC, Pixel 8".</summary>
public static class TaskRingText
{
    /// <summary>Which of <paramref name="devices"/> ring with <paramref name="ringOn"/> (a PC always does).</summary>
    public static IReadOnlyList<TaskRingDevice> Ringing(IReadOnlyList<Guid>? ringOn, IReadOnlyList<TaskRingDevice> devices)
    {
        return devices.Where(device => device.IsPc || TaskRingDevices.Rings(ringOn, device.Id)).ToList();
    }

    /// <summary>"All devices" when every device shown rings; "Work PC only" for one; else their names.</summary>
    public static string For(IReadOnlyList<Guid>? ringOn, IReadOnlyList<TaskRingDevice> devices)
    {
        IReadOnlyList<TaskRingDevice> ringing = Ringing(ringOn, devices);
        if (ringOn is null || ringing.Count == devices.Count)
        {
            return "All devices";
        }

        return ringing.Count == 1 ? $"{ringing[0].Name} only" : string.Join(", ", ringing.Select(device => device.Name));
    }

    /// <summary>
    /// "Per-label rings": each label that rings on fewer devices than shown ("Bugs: Work PC only · Home: Work PC, Pixel 8"), or "None:
    /// every label rings on the devices above".
    /// </summary>
    public static string PerLabel(IReadOnlyList<TaskLabel> labels, IReadOnlyList<TaskRingDevice> devices)
    {
        List<string> limited = labels
            .Where(label => label.RingOn is not null && Ringing(label.RingOn, devices).Count < devices.Count)
            .Select(label => $"{label.Name}: {For(label.RingOn, devices)}")
            .ToList();
        return limited.Count == 0 ? "None: every label rings on the devices above" : string.Join(" · ", limited);
    }
}
