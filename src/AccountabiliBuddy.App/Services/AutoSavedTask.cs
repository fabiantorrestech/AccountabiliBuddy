namespace AccountabiliBuddy.App.Services;

/// <summary>
/// "Tomorrow's first task" typed on a shutdown card that closed by itself (the user never pressed Done), kept on this PC
/// under the Tasks page's "Auto-saved" for 30 days.
/// </summary>
/// <param name="WrapUpOf">The plan date the shutdown card wrapped up.</param>
/// <param name="SavedAt">When the card closed and saved it.</param>
public sealed record AutoSavedTask(Guid Id, string Title, DateOnly WrapUpOf, DateTimeOffset SavedAt);
