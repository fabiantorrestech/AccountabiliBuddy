using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the away card's buttons do (set by <see cref="Services.StepAwayService"/>).</summary>
/// <param name="StartSideTask">A length in minutes was chosen.</param>
/// <param name="BackOnTrack">Back on track: the countdown carries on.</param>
/// <param name="StillAway">Still away: the card comes back in 2 minutes.</param>
/// <param name="AddFive">+5 min with the typed reason, after the friction is met.</param>
/// <param name="AddFiveRules">The wait and the shortest reason for the next +5 min.</param>
/// <param name="EndSession">End session: the normal end-early flow, no shortcut.</param>
/// <param name="Close">The length question was cancelled.</param>
public sealed record AwayCardActions(
    Action<int> StartSideTask,
    Action BackOnTrack,
    Action StillAway,
    Action<string> AddFive,
    Func<BypassSettings> AddFiveRules,
    Action EndSession,
    Action Close);
