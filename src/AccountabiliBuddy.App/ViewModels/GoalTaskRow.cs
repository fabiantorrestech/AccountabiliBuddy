namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One task of the detail's Tasks card (GS5): a task that carries the goal, open or finished. The title is the user's private text.</summary>
public sealed record GoalTaskRow(string Title, bool IsDone);
