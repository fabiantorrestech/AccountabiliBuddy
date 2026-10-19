namespace AccountabiliBuddy.App.Services;

/// <summary>When the setup assistant was last finished or closed, so it only opens by itself on the first launch.</summary>
public sealed record SetupAssistantRecord(DateTimeOffset ClosedAt);
