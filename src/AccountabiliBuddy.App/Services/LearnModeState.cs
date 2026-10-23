namespace AccountabiliBuddy.App.Services;

/// <summary>Whether the next session is a fresh install's first one, which learns instead of asking (document "learn.firstSession").</summary>
public sealed record LearnModeState(bool Pending);
