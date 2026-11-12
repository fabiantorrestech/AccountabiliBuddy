namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A question the Goals page asks before something it can't take back or that waits (round 2 R5.4): Delete goal…, "Track time only, no
/// target", deleting an area. The view shows it as a small dialog; <see cref="Run"/> does it and returns a note to show, or null.
/// </summary>
/// <param name="ActionText">The button that does it ("Delete goal").</param>
/// <param name="IsDanger">The button is red.</param>
/// <param name="CanRun">False when it can't happen now (the text says why); the dialog offers only Close.</param>
public sealed record GoalConfirmRequest(string Title, string Text, string ActionText, bool IsDanger, bool CanRun, Func<Task<string?>> Run);
