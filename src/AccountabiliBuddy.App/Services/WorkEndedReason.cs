namespace AccountabiliBuddy.App.Services;

/// <summary>Why work was ended early (ADR 65): when, and the reason sealed with DPAPI (base64). Never leaves this PC.</summary>
public sealed record WorkEndedReason(DateTimeOffset At, string ProtectedReason);
