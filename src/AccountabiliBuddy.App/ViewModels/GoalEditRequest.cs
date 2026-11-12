namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A request to open Edit goal for <see cref="GoalId"/> at <see cref="Section"/> (an R5b hook: the dialog isn't built yet).</summary>
public sealed record GoalEditRequest(Guid GoalId, GoalEditSection Section);
