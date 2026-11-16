using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Sync;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant's "What is each device?" page (ADR 90; a new feature for groups with two or more Android devices): one row for each
/// Android device with Main phone, Tablet and Other device. A pick goes through the same wait as the Devices page: it is written at once, applies
/// once this PC's own upload reached Google Drive, and for the device the PC's sleep inputs follow also after the looser-change delay, never
/// during a session. Saying nothing keeps today's behaviour.
/// </summary>
public sealed partial class SetupAssistantViewModel
{
    /// <summary>The Android device roles (the same service as the Devices page); null where they aren't available.</summary>
    public DeviceRolesService? DeviceRoles { get; init; }

    public bool IsDeviceRoles => Step == AssistantStep.DeviceRoles;

    /// <summary>One row for each Android device of the group.</summary>
    public ObservableCollection<DeviceRolePageRowViewModel> RoleRows { get; } = [];

    /// <summary>Why a pick was refused (a session runs), else empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoleNotice))]
    public partial string RoleNotice { get; set; } = string.Empty;

    public bool HasRoleNotice => RoleNotice.Length > 0;

    private bool _rolesHooked;

    private void OnDeviceRolesStepShown(AssistantStep step)
    {
        if (step != AssistantStep.DeviceRoles || DeviceRoles is not { } roles)
        {
            return;
        }

        if (!_rolesHooked)
        {
            _rolesHooked = true;
            // A pick moves other rows too (one main phone only) and the status changes as the change waits: read the service again.
            roles.Changed += (_, _) => Dispatcher.UIThread.Post(ReloadRoleRows);
            roles.HeldChanged += (_, _) => Dispatcher.UIThread.Post(ReloadRoleRows);
        }

        RoleNotice = string.Empty;
        RoleRows.Clear();
        foreach (ReceivedDevice device in roles.AndroidDevices().OrderBy(device => device.File.Device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            RoleRows.Add(new DeviceRolePageRowViewModel(device.File.Device.DeviceId, device.File.Device.Name, SetRoleAsync));
        }

        ReloadRoleRows();
    }

    private void ReloadRoleRows()
    {
        if (DeviceRoles is not { } roles)
        {
            return;
        }

        foreach (DeviceRolePageRowViewModel row in RoleRows)
        {
            row.Load(roles.ChosenKindOf(row.Id), roles.StatusOf(row.Id));
        }
    }

    private async Task SetRoleAsync(Guid device, Core.Sync.SyncDeviceRoleKind kind)
    {
        if (DeviceRoles is not { } roles)
        {
            return;
        }

        RoleNotice = await roles.SetRoleAsync(device, DeviceRolesService.WordOf(kind)) ?? string.Empty;
        ReloadRoleRows();
    }
}
