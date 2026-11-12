using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One device in a "Rings on" list (Alerts &amp; on-screen › Task reminders ring on, a label's Rings on ▾): its switch and status line.
/// A PC's switch stays on: turning it off is refused with the toast "This PC always rings for its own reminders".
/// </summary>
public sealed partial class RingDeviceChoiceViewModel : ObservableObject
{
    private readonly Action<TaskRingDevice, bool> _toggle;
    private bool _setting;

    /// <param name="toggle">The switch was turned (the device, on or off); for a PC it is the refusal.</param>
    public RingDeviceChoiceViewModel(TaskRingDevice device, bool isOn, Action<TaskRingDevice, bool> toggle)
    {
        Device = device;
        _toggle = toggle;
        _setting = true;
        IsOn = device.IsPc || isOn;
        _setting = false;
    }

    public TaskRingDevice Device { get; }

    /// <summary>"Work PC (this PC)" for this computer, else its name.</summary>
    public string Name => Device.IsThisPc ? $"{Device.Name} (this PC)" : Device.Name;

    public string Status => Device.IsPc ? $"{Device.Status} · always rings for its own reminders" : Device.Status;

    public MaterialIconKind Icon => Device.IsPc ? MaterialIconKind.Laptop : MaterialIconKind.Cellphone;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Shows a change made elsewhere (another device, the list rebuilt) without turning the switch.</summary>
    public void Show(bool isOn)
    {
        _setting = true;
        IsOn = Device.IsPc || isOn;
        _setting = false;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (_setting)
        {
            return;
        }

        if (Device.IsPc && !value)
        {
            // A PC can't be turned off: back on (after the switch's own change has finished), and the toggle says why.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _setting = true;
                IsOn = true;
                _setting = false;
            });
        }

        _toggle(Device, value);
    }
}
