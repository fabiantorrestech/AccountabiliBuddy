using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The device-local document behind <see cref="DeviceRolesService"/> (key "sync.deviceRoles"; never in a backup, never sent as such): the merged
/// role list this device carries, the list it applied (what its readers use), and the change waiting to apply.
/// </summary>
/// <param name="Held">The merged list this device writes into its sync file (it may hold a request that is not applied yet).</param>
/// <param name="Applied">The list the readers use: "the phones" for steps, sleep and the PC's alarm, bedtime and wake estimate come from it.</param>
/// <param name="AppliedIds">The Android devices that were active when <paramref name="Applied"/> was taken, so a device that has left is still seen as having left.</param>
/// <param name="Pending">The change waiting to apply; null when the applied roles are what the list says.</param>
public sealed record DeviceRolesDocument(
    List<SyncDeviceRole> Held, List<SyncDeviceRole> Applied, List<Guid> AppliedIds, DeviceRolesPending? Pending)
{
    /// <summary>No roles at all: every Android device is "the phone", as before roles existed.</summary>
    public static DeviceRolesDocument Empty => new([], [], [], null);
}
