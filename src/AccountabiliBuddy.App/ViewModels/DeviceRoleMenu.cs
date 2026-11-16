namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What an Android device's row on the Devices &amp; sync page adds for roles (ADR 90): the three choices of its "Role" menu, and the status of a
/// change waiting to apply ("Waiting to reach Google Drive…", "On Google Drive · applies at 4:15 PM", "Applied"). It adds no column: the role
/// is in the row's subtitle and the status is one line under it.
/// </summary>
/// <param name="Main">"Main phone".</param>
/// <param name="Tablet">"Tablet".</param>
/// <param name="Other">"Other device".</param>
/// <param name="Status">The status line; null when nothing waits for this device.</param>
/// <param name="CancelCommand">Cancels the change this PC asked for; null when there is none to cancel.</param>
public sealed record DeviceRoleMenu(DeviceRoleChoice Main, DeviceRoleChoice Tablet, DeviceRoleChoice Other, string? Status, System.Windows.Input.ICommand? CancelCommand)
{
    public bool HasStatus => Status is not null;

    public bool CanCancel => CancelCommand is not null;
}
