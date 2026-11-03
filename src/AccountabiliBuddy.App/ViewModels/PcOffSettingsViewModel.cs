using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The "When this PC goes off during a session" settings (ADR 60), on the Focus sessions page. Use from the UI thread only.</summary>
public sealed partial class PcOffSettingsViewModel : ObservableObject
{
    private const int PauseChoice = 0;
    private const int KeepEndChoice = 1;

    private readonly PcOffSettingsService _service;
    private bool _loading;

    public PcOffSettingsViewModel(PcOffSettingsService service, SessionController sessions)
    {
        _service = service;
        InFocus = service.InFocus;
        sessions.StateChanged += (_, _) => InFocus = service.InFocus;
        service.Changed += (_, _) => Load();
        Load();
    }

    public static IReadOnlyList<string> ChoiceNames { get; } = ["Pause the timer", "Keep the original end time"];

    [ObservableProperty]
    public partial int ChoiceIndex { get; set; }

    [ObservableProperty]
    public partial bool TellMe { get; set; }

    /// <summary>A focus session is running: the looser choice can't be picked now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool InFocus { get; set; }

    public bool CanEdit => !InFocus;

    /// <summary>"Takes effect at 4:05 PM…"; empty when nothing waits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    public partial string PendingText { get; set; } = string.Empty;

    public bool HasPending => PendingText.Length > 0;

    partial void OnChoiceIndexChanged(int value)
    {
        if (!_loading && value >= 0)
        {
            _ = ChooseAsync(value == KeepEndChoice);
        }
    }

    partial void OnTellMeChanged(bool value)
    {
        if (!_loading)
        {
            _ = _service.SetTellMeAsync(value);
        }
    }

    private async Task ChooseAsync(bool keep)
    {
        if (!await _service.SetKeepOriginalEndAsync(keep))
        {
            Load();
        }
    }

    private void Load()
    {
        _loading = true;
        PcOffSettings settings = _service.Current;
        ChoiceIndex = settings.KeepOriginalEnd || _service.KeepEndPendingAt is not null ? KeepEndChoice : PauseChoice;
        TellMe = settings.TellMe;
        PendingText = _service.KeepEndPendingAt is DateTimeOffset at
            ? $"Keeping the original end time takes effect at {TimeZoneInfo.ConvertTime(at, TimeZoneInfo.Local):t}, or after a session running then. Until then the timer pauses."
            : string.Empty;
        _loading = false;
    }
}
