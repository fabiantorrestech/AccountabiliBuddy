using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the run-into-lunch card's buttons do (set by <see cref="Services.LunchRunIntoService"/>).</summary>
/// <param name="Keep">Keep: the session stays as it is; lunch starts by itself and holds it.</param>
/// <param name="Adjust">Adjust: a timed session is shortened to end when lunch starts.</param>
/// <param name="Flexible">Flexible this time: today's lunch doesn't start by itself; the user presses Start lunch.</param>
public sealed record LunchRunIntoActions(Action Keep, Action Adjust, Action Flexible);

/// <summary>
/// The run-into-lunch card (in front of everything, like the away card): the running session would overlap a fixed lunch, or end
/// within a minute of it. Keep, Adjust (a timed session only) or Flexible this time. Nothing here ends the session early or
/// shortens a wait: Adjust only moves a timed session's end to where lunch would hold it anyway.
/// </summary>
public sealed partial class LunchRunIntoCardViewModel
{
    private readonly LunchRunIntoActions _actions;

    /// <param name="detail">When lunch starts and what the session would do, in words.</param>
    /// <param name="adjustText">The Adjust button's text ("Adjust: end at 12:00 PM"); null when the session can't be shortened.</param>
    public LunchRunIntoCardViewModel(string detail, string? adjustText, LunchRunIntoActions actions)
    {
        _actions = actions;
        Detail = detail;
        AdjustText = adjustText ?? string.Empty;
        CanAdjust = adjustText is not null;
    }

    public string Title => "Your session runs into lunch";

    public string Detail { get; }

    public string AdjustText { get; }

    public bool CanAdjust { get; }

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
    private void Flexible()
    {
        _actions.Flexible();
    }
}
