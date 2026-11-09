namespace AccountabiliBuddy.App.Services;

/// <summary>
/// "Tomorrow's first task" typed on a shutdown card that closed by itself (the user never pressed Done), listed under the
/// Tasks page's "Auto-saved" for 30 days and shared with the user's other devices (ADR 72).
/// </summary>
/// <param name="WrapUpOf">The plan date the shutdown card wrapped up.</param>
/// <param name="SavedAt">When the card closed and saved it.</param>
/// <param name="Labels">Its labels (ADR 72); null for none.</param>
/// <param name="LocalTo">The device that keeps it as a local item; null = it syncs.</param>
public sealed record AutoSavedTask(Guid Id, string Title, DateOnly WrapUpOf, DateTimeOffset SavedAt, IReadOnlyList<string>? Labels = null, Guid? LocalTo = null);
