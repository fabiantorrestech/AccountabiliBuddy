using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One tag on the Labels screen (ADR 89): its name, "3 tasks" and the Shared / This device only switch, which works as a label's does
/// (device only to shared asks first; a device is never taken out of a list).
/// </summary>
public sealed partial class TagRowViewModel : ObservableObject
{
    private readonly TaskLabelService _labels;
    private readonly Action<string> _say;

    public TagRowViewModel(string name, TaskLabelService labels, Action<string> say)
    {
        _labels = labels;
        _say = say;
        Name = name;
        CountText = ScopeText = string.Empty;
        Update();
    }

    public string Name { get; }

    public string Text => "#" + Name;

    [ObservableProperty]
    public partial string CountText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeIcon))]
    public partial bool IsShared { get; set; }

    [ObservableProperty]
    public partial string ScopeText { get; set; }

    public MaterialIconKind ScopeIcon => IsShared ? MaterialIconKind.Devices : MaterialIconKind.Laptop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDevicesLine))]
    public partial string? DevicesLine { get; set; }

    public bool HasDevicesLine => DevicesLine is not null;

    public bool CanScope => _labels.CanScope;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotConfirmingShare))]
    public partial bool IsConfirmingShare { get; set; }

    public bool IsNotConfirmingShare => !IsConfirmingShare;

    public void Update()
    {
        int uses = _labels.TagUseCount(Name);
        CountText = uses == 1 ? "1 task" : $"{uses} tasks";
        IsShared = _labels.IsTagShared(Name);
        ScopeText = IsShared ? "Shared" : "This device";
        DevicesLine = IsShared ? null : _labels.TagDevicesLine(Name);
        OnPropertyChanged(nameof(CanScope));
    }

    [RelayCommand]
    private async Task ToggleScopeAsync()
    {
        if (IsShared)
        {
            await _labels.SetTagSharedAsync(Name, shared: false);
            _say($"#{Name} is only on this device now");
            return;
        }

        IsConfirmingShare = true;
    }

    [RelayCommand]
    private async Task MakeSharedAsync()
    {
        IsConfirmingShare = false;
        await _labels.SetTagSharedAsync(Name, shared: true);
        _say($"#{Name} is shared on all your devices");
    }

    [RelayCommand]
    private void KeepScope()
    {
        IsConfirmingShare = false;
    }
}
