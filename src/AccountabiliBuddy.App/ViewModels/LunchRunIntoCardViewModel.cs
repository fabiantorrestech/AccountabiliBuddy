using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the run-into-lunch card's buttons do (set by <see cref="Services.LunchRunIntoService"/>).</summary>
/// <param name="Keep">Keep: the session stays as it is; lunch starts by itself once it has ended.</param>
/// <param name="Adjust">Adjust: a timed session that hasn't run yet (just started, or planning) is shortened to end when lunch starts.</param>
/// <param name="EndEarly">End early at lunch: a session that has run ends through the normal end-early flow (wait and reason).</param>
/// <param name="Flexible">Flexible this time: today's lunch doesn't start by itself; the user presses Start lunch.</param>
public sealed record LunchRunIntoActions(Action Keep, Action Adjust, Action EndEarly, Action Flexible);

/// <summary>
/// The run-into-lunch card (in front of everything, like the away card): the running session would overlap a fixed lunch, or end
/// within a minute of it. Keep, then either Adjust (only before any focus has run: it picks the session's length, so it costs no
/// focus time) or End early at lunch (a session that has run: the normal end-early flow with its wait and reason), and Flexible
/// this time. Nothing here is a way to cut focus that has run without that friction.
/// </summary>
public sealed partial class LunchRunIntoCardViewModel
{
    private readonly LunchRunIntoActions _actions;

    /// <param name="detail">When lunch starts and what the session would do, in words.</param>
    /// <param name="adjustText">The Adjust button's text ("Adjust: end at 12:00 PM"); null when the session can't be shortened that way.</param>
    /// <param name="canEndEarly">The "End early at lunch" button shows (a session that has run).</param>
    public LunchRunIntoCardViewModel(string detail, string? adjustText, bool canEndEarly, LunchRunIntoActions actions)
    {
        _actions = actions;
        Detail = detail;
        AdjustText = adjustText ?? string.Empty;
        CanAdjust = adjustText is not null;
        CanEndEarly = canEndEarly;
    }

    public string Title => "Your session runs into lunch";

    public string Detail { get; }

    public string AdjustText { get; }

    public bool CanAdjust { get; }

    public bool CanEndEarly { get; }

    [RelayCommand]
    private void Keep()
    {
        _actions.Keep();
    }

    [RelayCommand]
    private void Adjust()
    {
        _actions.Adjust();
    }

    [RelayCommand]
    private void EndEarly()
    {
        _actions.EndEarly();
    }

    [RelayCommand]
    private void Flexible()
    {
        _actions.Flexible();
    }
}
