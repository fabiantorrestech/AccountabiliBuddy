namespace AccountabiliBuddy.Core.Domain;

/// <summary>Why the quit safeguard is holding (ADR 59).</summary>
public enum QuitLockReason
{
    AllTheTime,
    Session,
    Category,
}
