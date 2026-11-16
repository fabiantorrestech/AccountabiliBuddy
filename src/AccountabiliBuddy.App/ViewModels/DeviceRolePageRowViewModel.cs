using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One Android device on the setup assistant's "What is each device?" page (ADR 90): its name, the three choices, and what a waiting change says.</summary>
public sealed partial class DeviceRolePageRowViewModel : ObservableObject
{
    /// <summary>The choices, in the order of the page's list.</summary>
    public static readonly IReadOnlyList<string> ChoiceWords = ["Main phone", "Tablet", "Other device"];

    private static readonly SyncDeviceRoleKind[] ChoiceKinds = [SyncDeviceRoleKind.Main, SyncDeviceRoleKind.Tablet, SyncDeviceRoleKind.Other];

    private readonly Func<Guid, SyncDeviceRoleKind, Task> _set;
    private bool _loading;

    public DeviceRolePageRowViewModel(Guid id, string name, Func<Guid, SyncDeviceRoleKind, Task> set)
    {
        Id = id;
        Name = name;
        _set = set;
    }

    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<string> Choices => ChoiceWords;

    /// <summary>The device's role icon: a phone, a tablet, or another device.</summary>
    [ObservableProperty]
    public partial MaterialIconKind Icon { get; set; } = MaterialIconKind.Cellphone;

    /// <summary>Index into <see cref="Choices"/>; -1 while the device has no role.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>What a waiting change says ("Waiting to reach Google Drive…"); empty when nothing waits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = string.Empty;

    public bool HasStatus => Status.Length > 0;

    /// <summary>Takes the roles as the service says them now, without asking for anything.</summary>
    public void Load(SyncDeviceRoleKind chosen, string? status)
    {
        _loading = true;
        SelectedIndex = Array.IndexOf(ChoiceKinds, chosen);
        Icon = chosen switch
        {
            SyncDeviceRoleKind.Tablet => MaterialIconKind.Tablet,
            SyncDeviceRoleKind.Other => MaterialIconKind.Devices,
            _ => MaterialIconKind.Cellphone,
        };
        Status = status ?? string.Empty;
        _loading = false;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < ChoiceKinds.Length)
        {
            _ = _set(Id, ChoiceKinds[value]);
        }
    }
}
