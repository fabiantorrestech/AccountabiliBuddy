using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Unlock card on the Session tab. During a session it offers the session profile's unlock options; otherwise
/// every profile's (an unlock then just pauses scheduled blocks). Asking needs a reason; then the option's wait
/// counts down, after which the session ends and the blocks stay lifted until the relock.
/// </summary>
public sealed partial class UnlockViewModel : ObservableObject
{
    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly UnlockService _unlocks;

    public UnlockViewModel(SessionController sessions, ConfigurationService configuration, UnlockService unlocks)
    {
        _sessions = sessions;
        _configuration = configuration;
        _unlocks = unlocks;
        Reason = RelockTimeText = StatusText = string.Empty;
        SelectedRelock = DefaultRelock();

        _sessions.StateChanged += (_, _) => RefreshOptions();
        _sessions.Ticked += (_, _) => RefreshStatus();
        _configuration.Changed += (_, _) =>
        {
            SelectedRelock = DefaultRelock();
            RefreshOptions();
        };
        _unlocks.Changed += (_, _) => RefreshStatus();

        RefreshOptions();
        RefreshStatus();
    }

    public ObservableCollection<UnlockChoice> Options { get; } = new();

    public IReadOnlyList<RelockChoice> RelockChoices => RelockChoice.All;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand))]
    public partial UnlockChoice? SelectedOption { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRelockTimeVisible))]
    public partial RelockChoice SelectedRelock { get; set; }

    /// <summary>Typed relock time when <see cref="RelockChoice.Kind.AtTime"/> is chosen, e.g. "6pm".</summary>
    [ObservableProperty]
    public partial string RelockTimeText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand))]
    [NotifyPropertyChangedFor(nameof(ReasonHint))]
    public partial string Reason { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormVisible), nameof(IsVisible), nameof(IsWaiting), nameof(IsUnlocked))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(LockAgainCommand), nameof(RequestCommand))]
    public partial UnlockStatus Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial bool HasOptions { get; set; }

    public bool IsVisible => HasOptions || Status != UnlockStatus.None;

    public bool IsFormVisible => Status == UnlockStatus.None && HasOptions;

    public bool IsWaiting => Status == UnlockStatus.Waiting;

    public bool IsUnlocked => Status == UnlockStatus.Unlocked;

    public bool IsRelockTimeVisible => SelectedRelock.When == RelockChoice.Kind.AtTime;

    public string ReasonHint
    {
        get
        {
            int missing = MinReasonLength - Reason.Trim().Length;
            return missing > 0 ? $"Why? (at least {missing} more characters)" : "Why?";
        }
    }

    private int MinReasonLength => _configuration.Current.Bypass.MinReasonLength;

    private bool CanRequest => Status == UnlockStatus.None && SelectedOption is not null
                               && Reason.Trim().Length >= MinReasonLength;

    private bool CanCancel => Status == UnlockStatus.Waiting;

    private bool CanLockAgain => Status == UnlockStatus.Unlocked;

    [RelayCommand(CanExecute = nameof(CanRequest))]
    private void Request()
    {
        UnlockOption option = SelectedOption!.Option;
        DateTimeOffset readyAt = _sessions.Now + option.Wait;
        if (!TryGetRelock(readyAt, out DateTimeOffset? relockAt, out string? error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        if (_unlocks.Request(option, relockAt, Reason.Trim()))
        {
            Reason = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _unlocks.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanLockAgain))]
    private void LockAgain()
    {
        _unlocks.LockAgain();
    }

    private bool TryGetRelock(DateTimeOffset readyAt, out DateTimeOffset? relockAt, out string? error)
    {
        TimeZoneInfo timeZone = _sessions.TimeProvider.LocalTimeZone;
        relockAt = null;
        error = null;

        switch (SelectedRelock.When)
        {
            case RelockChoice.Kind.Midnight:
                relockAt = RelockClock.NextMidnight(readyAt, timeZone);
                return true;

            case RelockChoice.Kind.AtTime:
                DateTime localReady = TimeZoneInfo.ConvertTime(readyAt, timeZone).DateTime;
                if (!ClockTimeParser.TryParseUpcoming(RelockTimeText, localReady, out DateTime localRelock, out error))
                {
                    error = $"Relock time: {error}";
                    return false;
                }

                relockAt = new DateTimeOffset(localRelock, timeZone.GetUtcOffset(localRelock));
                return true;

            default:
                return true;
        }
    }

    private RelockChoice DefaultRelock()
    {
        RelockChoice.Kind kind = _configuration.Current.Locks.RelockAtMidnight
            ? RelockChoice.Kind.Midnight
            : RelockChoice.Kind.Never;
        return RelockChoice.All.First(choice => choice.When == kind);
    }

    private void RefreshOptions()
    {
        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        Guid? sessionProfile = _sessions.State.IsActive ? _sessions.State.ProfileId : null;

        IEnumerable<UnlockChoice> choices = sessionProfile is Guid profileId
            ? profiles.Where(profile => profile.Id == profileId)
                .SelectMany(profile => profile.UnlockOptions)
                .Select(option => new UnlockChoice(option, $"{option.Name} · wait {FormatWait(option.Wait)}"))
            : profiles.SelectMany(profile => profile.UnlockOptions
                .Select(option => new UnlockChoice(option, $"{option.Name} · wait {FormatWait(option.Wait)} · {profile.Name}")));

        Guid? selectedId = SelectedOption?.Option.Id;
        Options.Clear();
        foreach (UnlockChoice choice in choices)
        {
            Options.Add(choice);
        }

        SelectedOption = Options.FirstOrDefault(choice => choice.Option.Id == selectedId) ?? Options.FirstOrDefault();
        HasOptions = Options.Count > 0;
        OnPropertyChanged(nameof(ReasonHint));
        RequestCommand.NotifyCanExecuteChanged();
    }

    private void RefreshStatus()
    {
        Status = _unlocks.Status;
        DateTimeOffset now = _sessions.Now;
        TimeZoneInfo timeZone = _sessions.TimeProvider.LocalTimeZone;

        StatusText = _unlocks.Current switch
        {
            { } unlock when Status == UnlockStatus.Waiting =>
                $"Unlocking “{unlock.OptionName}” in {FormatCountdown(unlock.ReadyAt - now)}. "
                + "Everything stays locked until then.",
            { RelockAt: DateTimeOffset relock } unlock when Status == UnlockStatus.Unlocked =>
                $"Unlocked (“{unlock.OptionName}”) until {DescribeRelock(relock, now, timeZone)}. "
                + "Scheduled blocks won't start before then.",
            { } unlock when Status == UnlockStatus.Unlocked =>
                $"Unlocked (“{unlock.OptionName}”) until you lock again. Scheduled blocks won't start.",
            _ => "Lifts the blocks after a wait: when it ends, this session ends and scheduled blocks pause until it relocks.",
        };
    }

    private static string DescribeRelock(DateTimeOffset relock, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(relock, timeZone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, timeZone).Date;
        if (local.TimeOfDay == TimeSpan.Zero && local.Date == today.AddDays(1))
        {
            return "midnight";
        }

        return local.Date == today ? local.ToString("t")
            : local.Date == today.AddDays(1) ? $"tomorrow {local:t}"
            : local.ToString("ddd d MMM, t");
    }

    private static string FormatWait(TimeSpan wait)
    {
        return wait.TotalMinutes < 1 ? "none"
            : wait.TotalHours < 1 ? $"{(int)wait.TotalMinutes} min"
            : $"{(int)wait.TotalHours} h {wait.Minutes} min";
    }

    private static string FormatCountdown(TimeSpan left)
    {
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        return left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"m\:ss");
    }
}
