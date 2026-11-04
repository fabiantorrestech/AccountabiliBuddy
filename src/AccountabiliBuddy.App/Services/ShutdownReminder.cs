namespace AccountabiliBuddy.App.Services;

/// <summary>The "work ends soon" reminder: the plan date and the local time work (or a Budget day's window) ends.</summary>
public sealed record ShutdownReminder(DateOnly Date, DateTime WorkEndsAt);
