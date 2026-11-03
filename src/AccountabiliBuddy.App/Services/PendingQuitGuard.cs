using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>A looser change of the quit safeguard, waiting: it replaces what is in force at <paramref name="ReadyAt"/>, once no session is running.</summary>
public sealed record PendingQuitGuard(QuitGuardSettings Settings, DateTimeOffset ReadyAt);
