namespace AccountabiliBuddy.App.Services;

/// <summary>The shutdown card's memory (document "shutdown"): the last day it was shown, so it never shows twice a day.</summary>
public sealed record ShutdownState(DateOnly? LastShownOn);
