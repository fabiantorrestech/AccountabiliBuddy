using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The questions the goal screens ask first (round 2 R5.4): Delete goal… (red, can't be undone), "Track time only, no target" for Deep work
/// (a lowering: it waits the looser-change delay and never starts during a session outside ProductaBuddy mode, through
/// <see cref="GoalsService"/>), and deleting an area (says where its goals go). The words never include a goal's notes.
/// </summary>
public static class GoalConfirmPrompts
{
    /// <summary>Delete goal…: its entries and reminders go with it. <paramref name="deleted"/> runs once it is gone.</summary>
    public static GoalConfirmRequest DeleteGoal(GoalsService goals, Guid goalId, Action? deleted = null)
    {
        Goal? goal = goals.Lists.Goals.FirstOrDefault(item => item.Id == goalId && item.IsLive);
        string name = goal?.Name ?? "this goal";
        bool can = goal is not null && GoalLowering.CanPauseOrDelete(goal);
        return new GoalConfirmRequest(
            $"Delete {name}?",
            can
                ? "Its history, entries and reminders go with it. This can't be undone. To stop it for a while instead, turn off Active in Edit goal."
                : "Deep work is built in: it can't be deleted. Use Track time only to keep counting with no target.",
            "Delete goal",
            IsDanger: true,
            CanRun: can,
            async () =>
            {
                GoalChangeResult result = await goals.DeleteGoalAsync(goalId);
                if (result == GoalChangeResult.Applied)
                {
                    deleted?.Invoke();
                    return null;
                }

                return "It couldn't be deleted now.";
            });
    }

    /// <summary>
    /// "Track time only, no target" (Deep work): no daily or weekly target on the Goals screens, still counting focus; the guards keep the
    /// daily goal's minutes (Q374). A lowering: waits, never during a session (outside ProductaBuddy mode).
    /// </summary>
    public static GoalConfirmRequest TrackTimeOnly(GoalsService goals)
    {
        int wait = (int)Math.Round(goals.LooserWait.TotalMinutes);
        bool refused = goals.LooserChangeRefusedNow;
        string rule = goals.IsProductaBuddy
            ? "In ProductaBuddy mode it applies at once (while today's work hours have work left, it waits for them)."
            : $"It loosens the goal, so it waits {wait} min and never happens during a session.";
        return new GoalConfirmRequest(
            "Track Deep work's time only?",
            refused
                ? "Not during a session: Track time only loosens Deep work, so it can't start until the session ends."
                : $"Deep work keeps counting your focus with no daily or weekly target, and its row reads \"tracking only\". The guards keep your daily goal. {rule} Setting a target again applies at once.",
            "Track time only",
            IsDanger: false,
            CanRun: !refused,
            async () =>
            {
                GoalChangeResult result = await goals.SetTrackTimeOnlyAsync(true);
                return result switch
                {
                    GoalChangeResult.Waiting when goals.DeepWorkSettings.PendingTrackTimeOnly is GoalPendingChange pending =>
                        $"Track time only waits until {GoalTexts.Time(pending.AppliesAt, TimeZoneInfo.Local)}. Cancel it on Deep work's Target tile.",
                    GoalChangeResult.Waiting => "Track time only waits like any looser change.",
                    GoalChangeResult.RefusedDuringSession => "Not during a session.",
                    GoalChangeResult.NotAvailable or GoalChangeResult.NotAllowed => "It couldn't be changed now.",
                    _ => null,
                };
            });
    }
}
