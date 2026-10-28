using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Banner shown on the Profiles, Exceptions and Settings tabs while looser changes count down: what they are, when
/// they take effect, and Cancel. There is deliberately no "apply now". One instance is shared by the three tabs.
/// </summary>
public sealed partial class PendingChangesViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    public PendingChangesViewModel(ConfigurationService configuration, SessionController sessions)
    {
        _configuration = configuration;
        _sessions = sessions;

        Headline = string.Empty;
        Changes = Array.Empty<string>();

        _configuration.PendingChanged += (_, _) => Refresh();
        _sessions.Ticked += (_, _) => Refresh();
        _sessions.StateChanged += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty]
    public partial bool HasPending { get; set; }

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Changes { get; set; }

    /// <summary>"Applies at 1:48 PM · cancel any time before": the clock time the wait ends (or the session's end, when that is later).</summary>
    [ObservableProperty]
    public partial string AppliesText { get; set; } = string.Empty;

    [RelayCommand]
    private void Cancel()
    {
        _configuration.CancelPending();
    }

    private void Refresh()
    {
        PendingChanges? pending = _configuration.Pending;
        HasPending = pending is not null;
        if (pending is null)
        {
            return;
        }

        if (!ReferenceEquals(Changes, pending.Changes))
        {
            Changes = pending.Changes;
        }

        TimeSpan remaining = pending.ReadyAt - _sessions.Now;
        bool sessionRunning = _sessions.State.IsActive;
        Headline = remaining > TimeSpan.Zero
            ? $"Looser changes take effect in {Format(remaining)}" + (sessionRunning ? ", or when this session ends if later:" : ":")
            : "Looser changes take effect when this session ends:";
        string readyAt = TimeZoneInfo.ConvertTime(pending.ReadyAt, _sessions.TimeProvider.LocalTimeZone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        AppliesText = remaining > TimeSpan.Zero
            ? $"Applies at {readyAt}" + (sessionRunning ? ", or when this session ends if later" : string.Empty) + " · cancel any time before"
            : "Applies when this session ends · cancel any time before";
    }

    private static string Format(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }
}
