namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One choice in a device row's "Role" menu (ADR 90): "Main phone", "Tablet" or "Other device".</summary>
/// <param name="Header">The menu item's text; when the choice is refused (a session runs) the reason is part of it.</param>
/// <param name="IsCurrent">The role the merged list says the device has (what the change will apply, or already applied).</param>
/// <param name="Command">Asks for the role.</param>
/// <param name="IsEnabled">The choice can be made now.</param>
public sealed record DeviceRoleChoice(string Header, bool IsCurrent, System.Windows.Input.ICommand Command, bool IsEnabled);
