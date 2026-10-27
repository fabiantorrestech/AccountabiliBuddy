namespace AccountabiliBuddy.App.Services;

/// <summary>The start-of-day assistant's memory on this computer (document "startOfDay"; never synced).</summary>
/// <param name="ShownOn">The last date the assistant opened or a real session ran, so it comes once a day; null = never shown.</param>
public sealed record StartOfDayState(DateOnly? ShownOn = null);
