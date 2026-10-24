using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Settings → Work hours on this computer → Lights out: on/off and lock or sleep.</summary>
public sealed partial class LightsOutViewModel : ObservableObject
{
    private readonly LightsOutService _service;
    private bool _loading;

    public LightsOutViewModel(LightsOutService service)
    {
        _service = service;
        Show();
        _service.Changed += (_, _) => Show();
    }

    /// <summary>Not offered on a Local-only computer.</summary>
    public bool CanUse => _service.CanUse;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial bool UsesSleep { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    partial void OnIsOnChanged(bool value) => _ = SaveAsync();

    partial void OnUsesSleepChanged(bool value) => _ = SaveAsync();

    private async Task SaveAsync()
    {
        if (_loading)
        {
            return;
        }

        Message = await _service.SaveAsync(new LightsOutSettings(IsOn, UsesSleep));
        if (Message is not null)
        {
            Show();
        }
    }

    private void Show()
    {
        _loading = true;
        IsOn = _service.Settings.On;
        UsesSleep = _service.Settings.Sleep;
        _loading = false;
    }
}
