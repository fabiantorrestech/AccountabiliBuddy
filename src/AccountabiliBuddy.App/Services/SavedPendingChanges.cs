namespace AccountabiliBuddy.App.Services;

/// <summary>
/// <see cref="PendingChanges"/> as kept on disk (documents key "config.pending"). The configuration is stored in the
/// versioned profile JSON format, so it is migrated like the current one if the app is updated meanwhile.
/// </summary>
public sealed record SavedPendingChanges(string Document, DateTimeOffset ReadyAt);
