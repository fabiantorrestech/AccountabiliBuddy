using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device in "Plan day(s)": its name, the time its blocks come back on, and its blocks.</summary>
public sealed partial class PlanDayDeviceGroup : ObservableObject
{
    public PlanDayDeviceGroup(string device, string name, bool isPhone, TimeSpan agreed)
    {
        Device = device;
        Name = name.ToUpperInvariant();
        IsPhone = isPhone;
        AgreedTime = agreed;
    }

    /// <summary>"phone" or "pc:&lt;id&gt;".</summary>
    public string Device { get; }

    public string Name { get; }

    public bool IsPhone { get; }

    public bool IsPc => !IsPhone;

    [ObservableProperty]
    public partial TimeSpan? AgreedTime { get; set; }

    public ObservableCollection<PlanDayBlockRow> Rows { get; } = new();
}
