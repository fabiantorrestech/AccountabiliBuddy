using AccountabiliBuddy.App.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Cold Turkey key window (ADR 39, 41), three modes: "Your spare key" shows a new key once and takes it typed back
/// (no pasting) before the app uses it; "Emergency release" takes the spare key typed and releases every held block
/// for today, then shows the new key; "Get a new spare key" takes a reason and a retyped code, waits 10 minutes with a
/// countdown and Cancel, then shows the new key like "Your spare key".
/// </summary>
public sealed partial class ColdTurkeyKeyViewModel : ObservableObject
{
    private readonly ColdTurkeyKeysService _keys;
    private readonly DispatcherTimer _countdown;

    public ColdTurkeyKeyViewModel(ColdTurkeyKeysService keys, ColdTurkeyKeyMode mode)
    {
        _keys = keys;
        Mode = mode;
        UnlockCode = PreWorkService.NewUnlockCode();
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) => _ = TickAsync();
        if (mode == ColdTurkeyKeyMode.SpareKey)
        {
            _ = ShowKeyAsync();
        }
        else if (mode == ColdTurkeyKeyMode.NewKey && _keys.NewKeyTimeLeft is null && _keys.PendingSpareKey is not null)
        {
            // The new key already came and isn't written down yet.
            ShowNewKeyIfMade();
        }
        else if (mode == ColdTurkeyKeyMode.NewKey)
        {
            RefreshNewKey();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpareKey), nameof(IsEmergency), nameof(IsNewKey), nameof(Title))]
    public partial ColdTurkeyKeyMode Mode { get; set; }

    public bool IsSpareKey => Mode == ColdTurkeyKeyMode.SpareKey;

    public bool IsEmergency => Mode == ColdTurkeyKeyMode.Emergency;

    public bool IsNewKey => Mode == ColdTurkeyKeyMode.NewKey;

    public string Title => Mode switch
    {
        ColdTurkeyKeyMode.Emergency => "Emergency release",
        ColdTurkeyKeyMode.NewKey => "Get a new spare key",
        _ => "Your spare key",
    };

    /// <summary>The key to write down (spare key mode).</summary>
    [ObservableProperty]
    public partial string? Key { get; set; }

    [ObservableProperty]
    public partial string TypedKey { get; set; } = "";

    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>New key: the code to retype by hand.</summary>
    public string UnlockCode { get; }

    [ObservableProperty]
    public partial string Reason { get; set; } = "";

    [ObservableProperty]
    public partial string TypedCode { get; set; } = "";

    /// <summary>New key: why it can't be asked for now (a focus session, the week's two used); null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    public partial string? Refusal { get; set; }

    /// <summary>New key: the 10-minute wait is under way.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    public partial bool IsWaiting { get; set; }

    /// <summary>New key: the reason and code form shows.</summary>
    public bool IsAsking => !IsWaiting && Refusal is null;

    /// <summary>New key: "9:41" left, or that a focus session holds it.</summary>
    [ObservableProperty]
    public partial string Countdown { get; set; } = "";

    public string NewKeysText => $"New spare keys in the last 7 days: {_keys.NewKeysThisWeek} of {ColdTurkeyKeysService.NewKeysPerWeek}.";

    /// <summary>Done: the window closes.</summary>
    public event EventHandler? Done;

    /// <summary>The window closed: the countdown stops (the wait itself goes on in the service).</summary>
    public void Detach() => _countdown.Stop();

    [RelayCommand]
    private async Task Confirm()
    {
        if (await _keys.ConfirmSpareKeyAsync(TypedKey))
        {
            Detach();
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
        Mode = ColdTurkeyKeyMode.SpareKey;
        Message = "Released for today: password locks are off now, timed locks end within 10 minutes. Write down your new spare key.";
        Key = _keys.PendingSpareKey;
    }

    [RelayCommand]
    private async Task RequestNewKey()
    {
        string? error = await _keys.RequestNewKeyAsync(Reason, TypedCode, UnlockCode);
        Message = error;
        RefreshNewKey();
    }

    [RelayCommand]
    private async Task CancelNewKey()
    {
        await _keys.CancelNewKeyAsync();
        Detach();
        Done?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshNewKey()
    {
        IsWaiting = _keys.NewKeyTimeLeft is not null;
        Refusal = IsWaiting ? null : _keys.NewKeyRefusal;
        OnPropertyChanged(nameof(NewKeysText));
        if (IsWaiting)
        {
            UpdateCountdown();
            _countdown.Start();
        }
        else
        {
            _countdown.Stop();
        }
    }

    private async Task TickAsync()
    {
        if (_keys.NewKeyTimeLeft is not TimeSpan left)
        {
            // Cancelled or made elsewhere (e.g. the service's own check): show the key if one is waiting.
            _countdown.Stop();
            ShowNewKeyIfMade();
            return;
        }

        if (left > TimeSpan.Zero)
        {
            UpdateCountdown();
            return;
        }

        if (await _keys.FinishNewKeyIfDueAsync())
        {
            _countdown.Stop();
            ShowNewKeyIfMade();
            return;
        }

        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        TimeSpan left = _keys.NewKeyTimeLeft ?? TimeSpan.Zero;
        Countdown = left > TimeSpan.Zero
            ? $"{(int)left.TotalMinutes}:{left.Seconds:00}"
            : "Ready when your focus session ends.";
    }

    private void ShowNewKeyIfMade()
    {
        IsWaiting = false;
        if (_keys.PendingSpareKey is string key)
        {
            Mode = ColdTurkeyKeyMode.SpareKey;
            Key = key;
            Message = "Your old key works until you type this new one back below; then it stops working.";
            return;
        }

        Detach();
        Done?.Invoke(this, EventArgs.Empty);
    }

    private async Task ShowKeyAsync()
    {
        Key = _keys.PendingSpareKey ?? await _keys.NewSpareKeyAsync();
    }
}
