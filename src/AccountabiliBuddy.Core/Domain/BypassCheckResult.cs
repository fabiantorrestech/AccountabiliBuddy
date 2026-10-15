namespace AccountabiliBuddy.Core.Domain;

public enum BypassCheckResult
{
    Allowed,
    WaitNotElapsed,
    ReasonTooShort,
}
