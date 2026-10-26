using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The start-nudge card (in front of everything, like the work-hours gate): "No session yet · 9:35 AM", Start (Today with
/// the task box), or Not now with a reason of at least one word.
/// </summary>
public sealed partial class StartNudgeCardViewModel : ObservableObject
{
    private readonly TimeZoneInfo _localZone;
    private readonly Action<StartNudgeCardViewModel> _start;
    private readonly Action<StartNudgeCardViewModel, string> _notNow;

    /// <param name="comesBack">False under "Only once": then the card doesn't say it comes back.</param>
    public StartNudgeCardViewModel(
        string untilText, DateTimeOffset now, TimeZoneInfo localZone, bool comesBack,
        Action<StartNudgeCardViewModel> onStart, Action<StartNudgeCardViewModel, string> onNotNow)
    {
        UntilText = untilText;
        _localZone = localZone;
        _start = onStart;
        _notNow = onNotNow;
        Question = comesBack
            ? "Start a session now, or say why not yet. This comes back until a session starts."
            : "Start a session now, or say why not yet.";
        Reason = string.Empty;
        Title = string.Empty;
        Refresh(now);
    }

    /// <summary>E.g. "Work until 12:00 PM".</summary>
    public string UntilText { get; }

    public string Question { get; }

    /// <summary>"No session yet · 9:35 AM".</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>Why not now; sealed on this computer, kept 14 days, never sent.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NotNowCommand))]
    public partial string Reason { get; set; }

    /// <summary>Keeps the time on the title current. Runs every second.</summary>
    public void Refresh(DateTimeOffset now)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, _localZone).DateTime;
        Title = $"No session yet · {local.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }

    /// <summary>At least one word: a letter or a digit somewhere in it.</summary>
    private bool HasReason() => Reason.Any(char.IsLetterOrDigit);

    [RelayCommand]
    private void Start()
    {
        _start(this);
    }

    [RelayCommand(CanExecute = nameof(HasReason))]
    private void NotNow()
    {
        _notNow(this, Reason.Trim());
    }
}
