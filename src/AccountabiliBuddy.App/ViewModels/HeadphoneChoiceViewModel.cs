using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One paired Bluetooth device in Settings → Noise player, ticked when it starts the noise.</summary>
public sealed partial class HeadphoneChoiceViewModel : ObservableObject
{
    private readonly Action _changed;

    public HeadphoneChoiceViewModel(BluetoothAudioDevice device, bool isChosen, Action changed)
    {
        Device = device;
        _changed = changed;
        IsChosen = isChosen;
    }

    public BluetoothAudioDevice Device { get; }

    public string Name => Device.Name;

    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    partial void OnIsChosenChanged(bool value) => _changed();
}
