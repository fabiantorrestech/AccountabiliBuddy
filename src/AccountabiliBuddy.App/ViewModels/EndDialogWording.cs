namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The words of the end-early dialog (wait, then a reason). Ending a session uses <see cref="EndSession"/>; switching to
/// ProductaBuddy during a session costs the same and says so (<see cref="Companion"/>, ADR 68). "{time}" stands for the time left.
/// </summary>
/// <param name="Heading">The window title and heading.</param>
/// <param name="ReasonQuestion">Above the reason box; "{min}" stands for the minimum length.</param>
/// <param name="Waiting">While the wait runs.</param>
/// <param name="Ready">Once it is over and the reason is long enough.</param>
/// <param name="Armable">While the wait runs and the reason is long enough: what confirming now does.</param>
/// <param name="ArmButton">The button during the wait.</param>
/// <param name="ConfirmButton">The button after it.</param>
/// <param name="CancelButton">The button that keeps things as they are.</param>
public sealed record EndDialogWording(
    string Heading,
    string ReasonQuestion,
    string Waiting,
    string Ready,
    string Armable,
    string ArmButton,
    string ConfirmButton,
    string CancelButton)
{
    public static EndDialogWording EndSession { get; } = new(
        "End focus session early?",
        "Why are you ending this focus session early? (at least {min} characters)",
        "Take a breath. You can end the session in {time}.",
        "You can end the session now.",
        "Confirm to keep working: the session ends by itself in {time}, and you can cancel until then.",
        "Confirm · ends in {time}",
        "End session",
        "Keep focusing");

    /// <summary>ProductaBuddy now during a session; <paramref name="companionWait"/> is the looser-change wait that follows.</summary>
    public static EndDialogWording Companion(string companionName, string companionWait) => new(
        $"Switch to {companionName} during this session?",
        $"It costs what ending this session early costs. Why switch now? (at least {{min}} characters)",
        $"Take a breath. You can switch in {{time}}; {companionName} then takes effect after {companionWait}.",
        $"You can switch now. {companionName} takes effect after {companionWait}, with this session still running.",
        $"Confirm to keep working: the {companionWait} wait starts by itself in {{time}}, and you can cancel until then.",
        "Confirm · switch in {time}",
        "Switch",
        "Keep guarding");
}
