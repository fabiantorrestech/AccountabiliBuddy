namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A request to open Edit goal for <see cref="GoalId"/> at <see cref="Section"/>; <see cref="FocusActive"/> = from the goal menu's
/// Pause goal… / Resume goal…, so the footer's Active switch (the only place that pauses) takes the keyboard.
/// </summary>
public sealed record GoalEditRequest(Guid GoalId, GoalEditSection Section, bool FocusActive = false);
