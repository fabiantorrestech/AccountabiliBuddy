namespace AccountabiliBuddy.App.Services;

/// <summary>One "Not now" on the start-nudge card: when, and its reason sealed with DPAPI (base64).</summary>
public sealed record StartNudgeCardSkip(DateTimeOffset At, string ProtectedReason);
