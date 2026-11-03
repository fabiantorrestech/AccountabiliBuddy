namespace AccountabiliBuddy.Core.Domain;

/// <summary>What Windows says ended its last run (ADR 60). Only words a notice; it never changes the rules.</summary>
public enum ShutdownCause
{
    Unknown,
    ShutDown,
    WindowsUpdate,
    Unexpected,
}
