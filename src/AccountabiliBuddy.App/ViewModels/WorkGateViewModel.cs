using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The work-hours gate (ADR 27): "Work until 5:00 PM · what were you about to do?", a line kept as a come-back, and Continue
/// after <see cref="Wait"/>.
/// </summary>
public sealed partial class WorkGateViewModel : ObservableObject
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly DateTimeOffset _openedAt;
    private readonly Action<WorkGateViewModel> _continue;

    public WorkGateViewModel(string untilText, DateTimeOffset openedAt, Action<WorkGateViewModel> onContinue)
    {
        UntilText = untilText;
        _openedAt = openedAt;
        _continue = onContinue;
        Text = string.Empty;
        WaitText = string.Empty;
    }

    /// <summary>E.g. "Work until 5:00 PM".</summary>
    public string UntilText { get; }

    public string Question => "What were you about to do? Park it here; it waits under Come back to.";

    /// <summary>The idea, saved as a come-back when the gate closes.</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    public partial bool CanContinue { get; set; }

    /// <summary>"Continue in 0:42", then empty.</summary>
    [ObservableProperty]
    public partial string WaitText { get; set; }

    /// <summary>Counts the wait down. Runs every second.</summary>
    public void Refresh(DateTimeOffset now)
    {
        TimeSpan left = _openedAt + Wait - now;
        CanContinue = left <= TimeSpan.Zero;
        WaitText = CanContinue ? string.Empty : $"Continue in {(int)left.TotalMinutes}:{Math.Max(0, left.Seconds):00}";
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        _continue(this);
    }
}
