using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Cold Turkey key window (ADR 39), two modes: "Your spare key" shows a new key once and takes it typed back
/// (no pasting) before the app uses it; "Emergency release" takes the spare key typed and releases every held block
/// for today, then shows the new key.
/// </summary>
public sealed partial class ColdTurkeyKeyViewModel : ObservableObject
{
    private readonly ColdTurkeyKeysService _keys;

    public ColdTurkeyKeyViewModel(ColdTurkeyKeysService keys, bool emergency)
    {
        _keys = keys;
        IsEmergency = emergency;
        if (!emergency)
        {
            _ = ShowKeyAsync();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpareKey), nameof(Title))]
    public partial bool IsEmergency { get; set; }

    public bool IsSpareKey => !IsEmergency;

    public string Title => IsEmergency ? "Emergency release" : "Your spare key";

    /// <summary>The key to write down (spare key mode).</summary>
    [ObservableProperty]
    public partial string? Key { get; set; }

    [ObservableProperty]
    public partial string TypedKey { get; set; } = "";

    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>Done: the window closes.</summary>
    public event EventHandler? Done;

    [RelayCommand]
    private async Task Confirm()
    {
        if (await _keys.ConfirmSpareKeyAsync(TypedKey))
        {
            Done?.Invoke(this, EventArgs.Empty);
            return;
        }

        Message = "That doesn't match the key above. Type it exactly as written (dashes are optional).";
    }

    [RelayCommand]
    private async Task Release()
    {
        if (!await _keys.EmergencyReleaseAsync(TypedKey))
        {
            Message = "That isn't your current spare key.";
            return;
        }

        TypedKey = "";
        IsEmergency = false;
        Message = "Released for today: password locks are off now, timed locks end within 10 minutes. Write down your new spare key.";
        Key = _keys.PendingSpareKey;
    }

    private async Task ShowKeyAsync()
    {
        Key = _keys.PendingSpareKey ?? await _keys.NewSpareKeyAsync();
    }
}
