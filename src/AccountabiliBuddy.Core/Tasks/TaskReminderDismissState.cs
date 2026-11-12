namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Where a shown reminder's Dismiss stands (ADR 76, spec 05 §7): still waiting out dismiss protection, waiting for a hold (Can't
/// miss), or free. Only Dismiss waits; Snooze and Start never do.
/// </summary>
/// <param name="Left">How much of the dismiss wait is left (zero once it is over).</param>
/// <param name="Hold">How long Dismiss must be held once the wait is over; zero for a plain click.</param>
/// <param name="Text">The button's words: "Dismiss in 5 s", "Hold to dismiss" or "Dismiss".</param>
public sealed record TaskReminderDismissState(TimeSpan Left, TimeSpan Hold, string Text)
{
    /// <summary>Dismiss protection is still running: the button is off and fills up.</summary>
    public bool Waiting => Left > TimeSpan.Zero;
}
