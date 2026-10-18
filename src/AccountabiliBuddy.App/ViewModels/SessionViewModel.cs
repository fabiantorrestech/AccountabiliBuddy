using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Session tab: current status with countdown, and controls to start manual or Pomodoro sessions.</summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SessionEndFlow _endFlow;
    private readonly Func<Window?> _ownerWindow;

    public SessionViewModel(
        SessionController sessions,
        ConfigurationService configuration,
        SessionEndFlow endFlow,
        Func<Window?> ownerWindow,
        UnlockViewModel unlock)
    {
        Unlock = unlock;
        _sessions = sessions;
        _configuration = configuration;
        _endFlow = endFlow;
        _ownerWindow = ownerWindow;

        ManualMinutes = "50";
        PomodoroCycles = "4";
        StatusTitle = string.Empty;
        StatusDetail = string.Empty;
        PomodoroSummary = string.Empty;

        _sessions.StateChanged += (_, _) => RefreshStatus();
        _sessions.Ticked += (_, _) => RefreshStatus();
        _configuration.Changed += (_, document) => RefreshFromConfiguration(document);

        RefreshFromConfiguration(_configuration.Current);
        RefreshStatus();
    }

    public ObservableCollection<ProfileOption> Profiles { get; } = new();

    /// <summary>The Unlock card.</summary>
    public UnlockViewModel Unlock { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartPomodoroCommand))]
    public partial ProfileOption? SelectedProfile { get; set; }

    /// <summary>Blank = runs until stopped.</summary>
    [ObservableProperty]
    public partial string ManualMinutes { get; set; }

    /// <summary>Blank = runs until stopped.</summary>
    [ObservableProperty]
    public partial string PomodoroCycles { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; }

    [ObservableProperty]
    public partial string StatusDetail { get; set; }

    [ObservableProperty]
    public partial string PomodoroSummary { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasNoProfiles { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartPomodoroCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndSessionCommand))]
    public partial bool IsSessionActive { get; set; }

    private bool CanStart => SelectedProfile is not null && !IsSessionActive;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartManual()
    {
        if (!TryParseOptionalPositive(ManualMinutes, out int? minutes))
        {
            ErrorMessage = "Minutes must be a whole number above 0, or blank to run until you stop it.";
            return;
        }

        ErrorMessage = null;
        TimeSpan? duration = minutes is int value ? TimeSpan.FromMinutes(value) : null;
        _sessions.StartManual(SelectedProfile!.Id, duration);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartPomodoro()
    {
        if (!TryParseOptionalPositive(PomodoroCycles, out int? cycles))
        {
            ErrorMessage = "Cycles must be a whole number above 0, or blank to run until you stop it.";
            return;
        }

        ErrorMessage = null;
        _sessions.StartPomodoro(SelectedProfile!.Id, cycles);
    }

    [RelayCommand(CanExecute = nameof(IsSessionActive))]
    private async Task EndSessionAsync()
    {
        await _endFlow.TryEndSessionAsync(_ownerWindow());
    }

    private void RefreshFromConfiguration(ProfileDocument document)
    {
        SelectedProfile = ProfileOptions.Refresh(Profiles, document.Profiles, SelectedProfile?.Id);
        HasNoProfiles = Profiles.Count == 0;

        PomodoroSettings pomodoro = document.PomodoroDefaults;
        PomodoroSummary =
            $"{pomodoro.FocusLength.TotalMinutes:0} min focus · {pomodoro.ShortBreakLength.TotalMinutes:0} min break · " +
            $"{pomodoro.LongBreakLength.TotalMinutes:0} min long break every {pomodoro.CyclesBeforeLongBreak} cycles";
    }

    private void RefreshStatus()
    {
        SessionState state = _sessions.State;
        string? profileName = ProfileOptions.NameOf(_configuration.Current.Profiles, state.ProfileId);

        IsSessionActive = state.IsActive;
        StatusTitle = SessionStatusFormatter.Title(state, profileName);
        StatusDetail = SessionStatusFormatter.Detail(state, _sessions.Now);
    }

    private static bool TryParseOptionalPositive(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text.Trim(), out int parsed) && parsed > 0)
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
