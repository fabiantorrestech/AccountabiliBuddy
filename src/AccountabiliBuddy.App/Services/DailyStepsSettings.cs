namespace AccountabiliBuddy.App.Services;

/// <summary>This PC's display setting for the daily steps card (document "dailySteps"): hidden or shown. Never synced.</summary>
public sealed record DailyStepsSettings(bool Hidden = false);
