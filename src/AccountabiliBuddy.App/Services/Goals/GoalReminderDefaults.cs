namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Settings › Goals › Default reminders on this PC (round 2 R5.6, brief §5.10; part of "goals.settings", device-local): the rule switches a
/// new goal reminder starts with. "Nudge when the week falls behind" has one default per mode (on in AccountabiliBuddy mode, off in
/// ProductaBuddy mode, ADR 69); the user changes either here and per goal in Edit goal › Reminders. Suggestions only.
/// </summary>
/// <param name="SkipWhenDone">"Skip when the goal is already done" (on by default).</param>
/// <param name="NudgeWhenBehindAccountabiliBuddy">"Nudge when the week falls behind" in AccountabiliBuddy mode (on by default).</param>
/// <param name="NudgeWhenBehindProductaBuddy">"Nudge when the week falls behind" in ProductaBuddy mode (off by default).</param>
public sealed record GoalReminderDefaults(bool SkipWhenDone, bool NudgeWhenBehindAccountabiliBuddy, bool NudgeWhenBehindProductaBuddy)
{
    public static GoalReminderDefaults Default { get; } = new(true, true, false);

    /// <summary>The nudge default in the mode the app is in.</summary>
    public bool NudgeFor(bool productaBuddy) => productaBuddy ? NudgeWhenBehindProductaBuddy : NudgeWhenBehindAccountabiliBuddy;
}
